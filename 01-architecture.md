# 01 — Архитектура

## Стек

- **.NET 8**, ASP.NET Core, **Razor Pages** (не MVC, не Blazor, не SPA)
- **PostgreSQL** через **Npgsql** — запросы пишутся напрямую, ORM не используется
- **DocumentFormat.OpenXml** — чтение и запись `.docx`
- **Bootstrap 5.3** (CSS с CDN) + минимум ванильного JavaScript
- Аутентификация — **cookie-схема ASP.NET Core**, без ASP.NET Core Identity

## Решения и обоснования

### Почему Razor Pages, а не Blazor Server

Основной сценарий — пошаговое заполнение формы, которое может занять
несколько минут. Blazor Server держит постоянное SignalR-соединение, и
обрыв связи означает потерю состояния формы. Razor Pages с обычной
POST-отправкой переживает и обрыв, и обновление вкладки.

### Аутентификация: ASP.NET Core Identity, EF Core только под неё

Используется **ASP.NET Core Identity** со стандартным хранилищем на
**EF Core + Npgsql**. Это осознанный пересмотр первоначального решения:
самописная аутентификация требует вручную реализовать инвалидацию
сессий при смене пароля, блокировку перебора, токены сброса пароля с
корректными сроками — и каждый из этих пунктов легко упустить.
Identity даёт их готовыми и проверенными.

**Требование «без ORM» при этом не нарушается**, потому что EF Core
используется исключительно как хранилище для собственных таблиц
Identity. Ни одного EF-запроса писать не нужно — всё общение идёт
через `UserManager` и `SignInManager`:

```csharp
await userManager.CreateAsync(user, password);
await signInManager.PasswordSignInAsync(email, password, remember, lockoutOnFailure: true);
```

**Все бизнес-данные Doxen — тарифы, счётчики, журнал, выборки для
админки — только чистый Npgsql**, как описано в `02-database.md`.
`DbContext` для них не создаётся и не используется. Читать таблицу
`AspNetUsers` обычным SQL-запросом (например, для списка пользователей
в админке с джойнами к тарифам и счётчикам) — можно и нужно: это
обычная таблица. Запрещено обратное — писать в неё в обход
`UserManager`.

### Разделение владения таблицами

Два механизма миграций в одном приложении работают, если каждый владеет
своим непересекающимся набором таблиц:

| Владелец | Таблицы |
|---|---|
| EF Core (`context.Database.MigrateAsync()`) | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserTokens`, `__EFMigrationsHistory` |
| Самописные миграции (`Migrations.ApplyAsync()`) | `plans`, `user_profiles`, `usage_counters`, `generation_log`, `schema_version` |

Порядок при старте: сначала EF, потом самописные.

**Внешних ключей между этими группами не создавать.** Связь
`user_profiles.user_id → AspNetUsers.Id` поддерживается кодом, а не
ограничением БД — иначе два механизма миграций начнут зависеть от
порядка друг друга и сломаются при первом же обновлении Identity.

### Настройка Identity

```csharp
builder.Services.AddIdentity<DoxenUser, IdentityRole<long>>(o =>
{
    o.Password.RequiredLength = 10;
    o.Password.RequireNonAlphanumeric = false;   // длина важнее классов символов
    o.User.RequireUniqueEmail = true;
    o.SignIn.RequireConfirmedEmail = false;      // почта не подключена, см. 07
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<AuthDbContext>()
.AddDefaultTokenProviders();
```

`DoxenUser : IdentityUser<long>` — без дополнительных свойств.
Тариф и прочее лежат в `user_profiles`, чтобы бизнес-данные не
смешивались с таблицей Identity.

Обязательно включить проверку security stamp — она не работает по
умолчанию во всех сценариях:

```csharp
builder.Services.Configure<SecurityStampValidatorOptions>(
    o => o.ValidationInterval = TimeSpan.FromMinutes(5));
```

Без неё украденная cookie продолжает работать после смены пароля.

### Что осталось за нами

Identity закрывает почти всё, но не всё:

1. **Cookie:** `HttpOnly`, `SecurePolicy = Always`, `SameSite = Lax`,
   скользящий срок 14 дней.
2. **Anti-forgery токен** на всех POST-формах.
3. **HTTPS обязателен**, `UseHsts()` в продакшене.
4. Ограничение частоты запросов на **анонимный разбор шаблона**
   (Identity тут не помогает — пользователя нет). См. `05-screens.md`.

### Где живёт загруженный шаблон между шагами

Между «загрузил файл» и «нажал Собрать» проходит несколько минут, и
файл всё это время где-то должен быть. Решение: **в оперативной памяти
сервера, никогда на диске.**

`IMemoryCache` с абсолютным сроком жизни и лимитом по размеру:

```csharp
builder.Services.AddMemoryCache(o => o.SizeLimit = 2_000_000_000); // ~2 ГБ

// при загрузке шаблона
var entryKey = "tpl:" + Guid.NewGuid().ToString("N");
cache.Set(entryKey, new UploadedTemplate(bytes, fileName), new MemoryCacheEntryOptions
{
    Size = bytes.Length,
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
    SlidingExpiration = TimeSpan.FromMinutes(30)
});
```

В сессии пользователя хранится **только ключ** `entryKey`, а не сам файл.

Почему именно так, а не на диск:

- **Уборка не нужна.** Истёк срок — запись вытеснена самим кешем.
  Нет фонового сборщика, который может сломаться и забить диск.
- **Файл не попадает в резервные копии и снапшоты диска.**
- **Персональные данные не хранятся при хранении.** Доверенности и
  заявления содержат паспортные данные и адреса. Пока файл живёт
  только в памяти процесса — это обработка. Как только он лёг на диск,
  это хранение персональных данных со всеми вытекающими обязанностями
  по 152-ФЗ.
- Утверждение «на сервере ничего не остаётся» остаётся честным.

Срок жизни — **2 часа**, не сутки: заполнение формы занимает минуты,
а каждый лишний час — это окно, в течение которого чужие паспортные
данные лежат в памяти сервера. Скользящий срок продлевает запись при
каждом действии пользователя, так что медленное заполнение не обрывается.

Имя файла шаблона хранится там же, в записи кеша. **В БД оно не
пишется** — см. `02-database.md`.

Готовый документ никуда не кладётся вообще: собрали в `MemoryStream` и
сразу отдали через `File(bytes, contentType, fileName)`.

Никаких `Path.GetTempFileName()`, никакой папки `uploads`, никакого
`wwwroot/generated`. Если понадобится масштабирование на несколько
серверов — та же схема переносится на Redis (`IDistributedCache`) без
изменений в логике.

### Бюджет памяти и допуск при нехватке

Общий бюджет на хранение шаблонов задаётся в конфигурации, исходя из
памяти сервера. `IMemoryCache` **не показывает текущий занятый размер**,
поэтому его считаем сами: `Interlocked.Add` при добавлении записи и
`Interlocked.Add` с минусом в `PostEvictionCallback`.

Два порога от бюджета:

| Занято | Поведение |
|---|---|
| до 75% | пускаем всех |
| 75–90% | платные проходят сразу; бесплатные ждут освобождения места, но не дольше 20 секунд |
| выше 90% | отказ всем сразу, без ожидания |

Ожидание реализуется через `SemaphoreSlim` с таймаутом и **ограничением
числа ждущих** (например, 50). Настоящая неограниченная очередь
недопустима: она удерживает HTTP-соединения открытыми, и они кончатся
раньше памяти — очередь сама станет причиной отказа.

Не дождался или отказ сразу — понятное сообщение и HTTP 503 с
заголовком `Retry-After`:

> «Сервер сейчас загружен. Попробуйте через минуту — или перейдите на
> «Pro», там сборка идёт без очереди.»

Всё настраивается:

```
Doxen__TemplateCacheSizeBytes=2000000000
Doxen__SoftLimitPercent=75
Doxen__HardLimitPercent=90
Doxen__FreeUserWaitSeconds=20
Doxen__MaxQueuedFreeUsers=50
Doxen__TemplateCacheLifetimeHours=2
```

Метрику «занято сейчас / бюджет» вывести в админку — без неё пороги
подбирать вслепую.

## Структура решения

```
Doxen.sln
├── Doxen.Engine/                  # библиотека классов, без зависимостей от ASP.NET
│   ├── TemplateParser.cs          # поиск переменных в .docx
│   ├── TemplateGenerator.cs       # подстановка значений
│   ├── ValueResolver.cs           # разрешение пути по JSON
│   ├── PersonFormatter.cs         # initials / initialsStart / initialsEnd
│   ├── AnswersFile.cs             # чтение и запись файла ответов
│   └── Models/                    # TemplateVariable, ResolveResult, GenerationResult
├── Doxen.Data/                    # доступ к PostgreSQL, чистый Npgsql
│   ├── Db.cs                      # фабрика подключений, ExecuteAsync-хелперы
│   ├── Migrations.cs              # версионирование схемы
│   ├── UserRepository.cs
│   ├── PlanRepository.cs
│   └── UsageRepository.cs
├── Doxen.Web/                     # Razor Pages
│   ├── Pages/
│   ├── Services/                  # PasswordHasher, LimitChecker, CurrentUser
│   ├── wwwroot/css/doxen.css      # переопределения Bootstrap из макета
│   └── Program.cs
└── Doxen.Engine.Tests/            # xUnit, тесты движка
```

**`Doxen.Engine` не должен ссылаться ни на ASP.NET Core, ни на
Npgsql.** Он получает на вход поток `.docx` и JSON, отдаёт результат.
Это позволяет покрыть его тестами без поднятия веб-приложения и БД —
и именно так его и нужно тестировать.

## Конфигурация

`appsettings.json` — только несекретное. Строка подключения к БД и
ключ защиты cookie — через переменные окружения или user-secrets в
разработке. В репозиторий не коммитить.

```
ConnectionStrings__Doxen=Host=...;Database=doxen;Username=...;Password=...
DOXEN_ADMIN_EMAIL=...
DOXEN_ADMIN_PASSWORD=...
```

Настройки бюджета памяти — см. раздел «Бюджет памяти и допуск при
нехватке» выше, там же их значения по умолчанию.

Одна строка подключения используется и EF Core (для таблиц Identity),
и Npgsql напрямую (для бизнес-таблиц) — база одна.
```

## Обработка ошибок

Пользователь никогда не должен видеть stack trace. На каждый ожидаемый
сбой — понятное сообщение о том, что произошло и что делать:

- файл не `.docx` → «Doxen понимает только файлы .docx. Если у вас .doc,
  пересохраните его в Word как .docx.»
- файл повреждён → «Не удалось открыть файл. Проверьте, что он
  открывается в Word.»
- в шаблоне нет переменных → «В этом шаблоне нет ни одной переменной
  вида {{...}}. Проверьте, что вы загрузили нужный файл.»
- превышен лимит → см. `05-screens.md`, раздел про лимиты

Непредвиденные исключения логировать целиком на сервере, пользователю
показывать общую страницу ошибки без деталей.
