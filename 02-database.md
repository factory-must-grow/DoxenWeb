# 02 — База данных

PostgreSQL, доступ через Npgsql напрямую. ORM не используется.

## Правила доступа к данным

1. **Только параметризованные запросы.** Ни одной строки, собранной
   конкатенацией с пользовательским вводом. Даже там, где значение
   «точно безопасное».
2. Каждый репозиторий открывает `NpgsqlConnection`, использует и
   закрывает через `await using`. Пул соединений Npgsql делает это
   дешёвым — держать одно общее открытое соединение не нужно.
3. Все методы асинхронные (`ExecuteNonQueryAsync`, `ExecuteReaderAsync`).
4. Схема применяется при старте приложения через `Migrations.ApplyAsync()`.

## Механизм миграций

Тот же принцип, что в десктопной версии: таблица с номером текущей
версии, массив миграций, применение недостающих по порядку в транзакции.

```sql
CREATE TABLE IF NOT EXISTS schema_version (
    version integer NOT NULL
);
```

`Migrations.cs` содержит `string[] Migrations` — индекс в массиве плюс
единица равен номеру версии. При старте: прочитать текущую версию
(если таблицы нет — считать 0), применить все миграции с номером выше,
каждую в отдельной транзакции, обновить версию.

**Существующие миграции никогда не редактируются** — только добавляются
новые в конец массива.

## Схема

### plans — тарифы

```sql
CREATE TABLE plans (
    id                          serial PRIMARY KEY,
    code                        text NOT NULL UNIQUE,
    name                        text NOT NULL,
    price_rub                   integer NOT NULL DEFAULT 0,
    max_documents_per_month     integer NOT NULL,
    max_variables_per_template  integer NOT NULL,
    max_file_size_mb            integer NOT NULL,
    allow_batch                 boolean NOT NULL DEFAULT false,
    is_active                   boolean NOT NULL DEFAULT true,
    sort_order                  integer NOT NULL DEFAULT 0
);
```

Начальные данные — один тариф:

```sql
INSERT INTO plans (code, name, price_rub, max_documents_per_month,
                   max_variables_per_template, max_file_size_mb, allow_batch, sort_order)
VALUES ('free', 'Free', 0, 1000000, 1000000, 50, false, 0);
```

Обрати внимание на числа: на время разработки и тестирования Free —
**без практических ограничений** (по решению заказчика). Значения
правятся в админке без деплоя. Логика проверки лимитов при этом должна
быть написана и работать с самого начала — просто не срабатывать при
таких значениях.

`allow_batch` заложен на будущее (пакетная генерация для платных
тарифов). Проверять его в коде уже сейчас, при том что сама пакетная
генерация ещё не реализована.

### user_profiles — бизнес-данные пользователя

Сама учётная запись (почта, пароль, блокировка, подтверждение почты)
живёт в таблице `AspNetUsers`, которой владеет Identity через EF Core —
см. `01-architecture.md`, раздел про разделение владения таблицами.
**Эту таблицу самописные миграции не создают и не изменяют.**

Здесь — только то, что относится к Doxen, а не к аутентификации:

```sql
CREATE TABLE user_profiles (
    user_id     bigint PRIMARY KEY,
    plan_id     integer NOT NULL REFERENCES plans(id),
    created_at  timestamptz NOT NULL DEFAULT now()
);
```

`user_id` соответствует `AspNetUsers.Id`, но **внешнего ключа нет
намеренно**: два механизма миграций не должны зависеть от порядка
работы друг друга. Целостность поддерживается кодом — профиль
создаётся в той же операции, что и учётная запись.

Блокировка пользователя — не отдельная колонка, а штатный механизм
Identity (`LockoutEnd` в далёком будущем через
`userManager.SetLockoutEndDateAsync`). Не заводить своё поле
`is_blocked`: два источника правды разойдутся.

Признак администратора — роль Identity `Admin`, а не колонка.

Список пользователей для админки собирается **обычным SQL-запросом**
с джойном к таблице Identity — читать её напрямую можно:

```sql
SELECT u."Email", u."LockoutEnd", p.plan_id, pl.name AS plan_name,
       COALESCE(c.documents_generated, 0) AS docs_this_month
FROM user_profiles p
JOIN "AspNetUsers" u ON u."Id" = p.user_id
JOIN plans pl ON pl.id = p.plan_id
LEFT JOIN usage_counters c
       ON c.user_id = p.user_id
      AND c.period_year = @year AND c.period_month = @month
ORDER BY p.created_at DESC
LIMIT 50 OFFSET @offset;
```

Обрати внимание на кавычки: EF Core создаёт таблицы Identity с именами
в PascalCase, и в PostgreSQL к ним нужно обращаться в двойных кавычках.

**Писать** в `AspNetUsers` напрямую запрещено — только через
`UserManager`, иначе рассогласуются security stamp и счётчики попыток.

### usage_counters — счётчики за период

```sql
CREATE TABLE usage_counters (
    user_id             bigint NOT NULL REFERENCES user_profiles(user_id) ON DELETE CASCADE,
    period_year         integer NOT NULL,
    period_month        integer NOT NULL,
    documents_generated integer NOT NULL DEFAULT 0,
    substitutions_total bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, period_year, period_month)
);
```

Инкремент — атомарный upsert, без чтения-изменения-записи:

```sql
INSERT INTO usage_counters (user_id, period_year, period_month,
                            documents_generated, substitutions_total)
VALUES (@userId, @year, @month, 1, @substitutions)
ON CONFLICT (user_id, period_year, period_month) DO UPDATE
SET documents_generated = usage_counters.documents_generated + 1,
    substitutions_total = usage_counters.substitutions_total + @substitutions;
```

### generation_log — журнал операций

```sql
CREATE TABLE generation_log (
    id                  bigserial PRIMARY KEY,
    user_id             bigint REFERENCES user_profiles(user_id) ON DELETE SET NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    template_size_bytes integer NOT NULL DEFAULT 0,
    documents_count     integer NOT NULL DEFAULT 1,
    variables_count     integer NOT NULL DEFAULT 0,
    substitutions_count integer NOT NULL DEFAULT 0,
    succeeded           boolean NOT NULL,
    error_message       text
);

CREATE INDEX ix_generation_log_user_created ON generation_log (user_id, created_at DESC);
```

**В журнал не пишется ничего, что относится к содержимому.** Ни текста
документа, ни значений переменных, ни путей переменных, **ни имени
файла**: «Иск к Иванову.docx» само по себе раскрывает и предмет спора,
и фамилию. Имя файла живёт только в кеше вместе с самим файлом и
исчезает вместе с ним.

Для диагностики достаточно размера, количеств и результата.

`documents_count` — сколько документов собрано за одну операцию
(при пакетной генерации больше единицы, см. `03-engine.md`).

`error_message` — только текст исключения. Проверить, что в него не
попадают значения переменных: сообщения движка о неразрешённых путях
содержат сам путь (`org.director.lastName`), а путь — это уже
структура чужих данных. В журнал писать обобщённо
(«не разрешено N переменных»), подробности показывать пользователю
в интерфейсе, но не сохранять.

## Что специально отсутствует

Нет таблиц для шаблонов, документов и файлов ответов. Это не упущение —
см. требование в `00-README.md`. Если по ходу работы появляется желание
их добавить, значит, задача решается неверно.
