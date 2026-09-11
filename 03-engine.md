# 03 — Движок: разбор и генерация .docx

Самая ответственная часть. Проект `Doxen.Engine`, зависимости — только
`DocumentFormat.OpenXml` и `System.Text.Json`.

## Синтаксис переменных

```
{{org.name}}                  — путь к значению
{{org.bank.bankName}}         — вложенный путь
{{initialsEnd(org.director)}} — вызов функции от пути
```

Регулярные выражения:

```csharp
private static readonly Regex VariableRegex =
    new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

private static readonly Regex FunctionRegex =
    new(@"^(?<func>[A-Za-z][A-Za-z0-9_]*)\s*\(\s*(?<arg>[^()]+?)\s*\)$",
        RegexOptions.Compiled);
```

`VariableRegex` — нежадный, чтобы `{{a}} и {{b}}` дали два совпадения,
а не одно. Внутри `{{...}}` пробелы по краям обрезаются:
`{{ org.name }}` и `{{org.name}}` — одна и та же переменная.

## Критическая деталь: Word разрывает текст на фрагменты

**Это главная ловушка всей задачи.** Word хранит текст абзаца в
последовательности элементов `<w:r><w:t>`, и разрывает их произвольно —
после проверки орфографии, смены форматирования или просто по своей
логике. Переменная `{{org.name}}` в файле легко может лежать так:

```xml
<w:r><w:t>{{org.</w:t></w:r>
<w:r><w:t>na</w:t></w:r>
<w:r><w:t>me}}</w:t></w:r>
```

Поиск по каждому `<w:t>` по отдельности не найдёт ничего. Правильный
приём — работать с **абзацем целиком**:

```csharp
private static void ReplaceInParagraph(Paragraph paragraph, ...)
{
    List<Text> textNodes = paragraph.Descendants<Text>().ToList();
    if (textNodes.Count == 0) return;

    string original = string.Concat(textNodes.Select(t => t.Text));
    string replaced = ReplaceInText(original, ...);
    if (original == replaced) return;

    // Весь результат кладём в первый узел, остальные опустошаем.
    textNodes[0].Text = replaced;
    for (int i = 1; i < textNodes.Count; i++)
        textNodes[i].Text = "";
}
```

Побочный эффект, о котором нужно знать: если переменная была разорвана
между фрагментами с разным форматированием, весь текст абзаца примет
форматирование первого фрагмента. На практике это почти не встречается,
потому что переменную обычно набирают одним стилем. Не пытаться решать
это умнее — усложнение не оправдано.

## Где искать переменные

Обходить нужно **всё**, не только тело документа:

```csharp
IEnumerable<OpenXmlPart> GetAllParts(WordprocessingDocument doc)
{
    if (doc.MainDocumentPart is null) yield break;
    yield return doc.MainDocumentPart;
    foreach (var h in doc.MainDocumentPart.HeaderParts) yield return h;
    foreach (var f in doc.MainDocumentPart.FooterParts) yield return f;
    if (doc.MainDocumentPart.FootnotesPart is not null)
        yield return doc.MainDocumentPart.FootnotesPart;
    if (doc.MainDocumentPart.EndnotesPart is not null)
        yield return doc.MainDocumentPart.EndnotesPart;
}
```

Внутри каждой части обходить `RootElement.Descendants<Paragraph>()`.
Ячейки таблиц обходить **не отдельно** — `Descendants<Paragraph>()`
уже возвращает абзацы внутри ячеек. Обрабатывать их дважды нельзя.

> В десктопной версии абзацы и ячейки обходились по отдельности, что
> приводило к двойной обработке. Здесь так не делать.

## TemplateParser — разбор

```csharp
public sealed class TemplateVariable
{
    public string Expression { get; init; } = "";   // "initialsEnd(org.director)"
    public string Path { get; init; } = "";         // "org.director"
    public string? FunctionName { get; init; }      // "initialsEnd" или null
    public int OccurrenceCount { get; set; }        // сколько раз встречается в файле
}

public static IReadOnlyList<TemplateVariable> Parse(Stream docxStream)
```

- Открывать поток **только на чтение**: `WordprocessingDocument.Open(stream, false)`.
- Дедуплицировать по `Expression`, считая вхождения в `OccurrenceCount`.
- Сохранять порядок первого появления — форма заполнения должна идти
  в том же порядке, что и документ.
- Если выражение не разбирается как путь или вызов функции — включить
  его в результат с пометкой ошибки, а не молча пропустить.

Допустимый путь: сегменты из букв, цифр и `_`, разделённые точками.
Пустые сегменты (`org..name`) — ошибка.

## ValueResolver — разрешение пути по JSON

Работает над `JsonElement` одного документа (см. `04-answers-file.md`).

```csharp
public enum ResolveStatus { Resolved, MissingValue, PathIsObject, WrongType }

public sealed class ResolveResult
{
    public ResolveStatus Status { get; init; }
    public string? Value { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string>? AvailableKeys { get; init; }
}
```

Правила:

1. Идём по сегментам пути от корня объекта документа.
2. Сегмент не найден или значение `null` → `MissingValue`.
3. Дошли до конца, значение — строка/число/bool → `Resolved`,
   значение приводится к строке. Числа выводить без экспоненты
   и без лишних нулей, bool — как `Да`/`Нет`.
4. Дошли до конца, значение — объект → `PathIsObject`, в
   `AvailableKeys` положить его ключи. Сообщение:
   «`org.bank` — это группа значений, а не значение. Уточните путь,
   например `org.bank.bankName`.»
5. Путь продолжается, а текущее значение не объект → `WrongType`.
   Сообщение должно называть, на каком сегменте сломалось.
6. Массивы внутри документа на этом этапе не поддерживаются —
   встретили массив в середине пути → `WrongType` с понятным текстом.

## PersonFormatter — функции ФИО

Три функции, аргумент должен разрешаться в **объект** с ключами
`firstName`, `middleName`, `lastName` (любой может отсутствовать):

| Функция | Результат для Иван Петрович Сидоров |
|---|---|
| `initials` | `И. П.` |
| `initialsStart` | `И. П. Сидоров` |
| `initialsEnd` | `Сидоров И. П.` |

Правила сборки:

- Инициал — первая буква, приведённая к верхнему регистру, точка.
- Отсутствующие части просто пропускаются, лишние пробелы схлопываются.
- Если **все три** части пусты → `MissingValue` с путём аргумента и
  пометкой, что нужны имя, отчество и фамилия.
- Если аргумент разрешился не в объект → понятная ошибка о том, что
  функция ожидает группу с полями `firstName`/`middleName`/`lastName`.

Неизвестное имя функции → ошибка с перечислением доступных.

## TemplateGenerator — генерация

```csharp
public sealed class GenerationResult
{
    public byte[] Document { get; init; } = Array.Empty<byte>();
    public int SubstitutionCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public static GenerationResult Generate(Stream templateStream, JsonElement answers)
```

Порядок работы:

1. Скопировать входной поток в `MemoryStream` — **исходный поток не
   модифицировать**, пользователь может загрузить тот же файл ещё раз.
2. Открыть копию на запись: `WordprocessingDocument.Open(ms, true)`.
3. Пройти по всем частям и абзацам, заменяя переменные.
4. `SubstitutionCount` — количество фактически выполненных замен
   (каждое вхождение считается отдельно). Это число уходит в
   `usage_counters.substitutions_total`.
5. Сохранить, вернуть `ms.ToArray()`.

**Неразрешённые переменные заменяются на пустую строку**, а не остаются
как `{{...}}` в документе. Каждая такая замена добавляет предупреждение
в `Warnings`. Логика в том, что до генерации пользователь уже видел
список недостающих значений и осознанно нажал «Собрать» — значит, эти
места он оставил пустыми намеренно.

## Пакетная генерация

Один шаблон плюс несколько наборов данных — несколько отдельных
документов. Это основной сценарий «одинаковая доверенность на десять
человек»: каждому человеку свой файл.

```csharp
public sealed class BatchItemResult
{
    public string Title { get; init; } = "";      // из datasets[i].title
    public byte[]? Document { get; init; }        // null, если сборка не удалась
    public int SubstitutionCount { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public static IReadOnlyList<BatchItemResult> GenerateBatch(
    Stream templateStream, JsonElement answersRoot)
```

Правила:

1. Цикл по `datasets[]` из файла ответов. Значение для документа `i`
   ищется сначала в `datasets[i].values`, потом в `shared` —
   слияние на уровне листьев, а не целых веток, см. `04-answers-file.md`.
2. **Шаблон читается в `byte[]` один раз** до цикла, дальше на каждой
   итерации оборачивается в новый `MemoryStream`. Не открывать файл
   заново на каждой итерации и не переиспользовать один поток —
   `WordprocessingDocument.Open(stream, true)` модифицирует его.
3. Ошибка на одном элементе **не прерывает остальные**: записывается
   в `Error` этого элемента, цикл продолжается. Пользователь получает
   девять документов из десяти плюс внятное сообщение по десятому.
4. Имя файла документа — из `title`, очищенное от символов, недопустимых
   в имени файла. Пустой или повторяющийся `title` → `Документ 1`,
   `Документ 2`. Повторы разрешать суффиксом, а не молча перезаписывать.
5. Упаковка в ZIP — **не** задача движка. `GenerateBatch` возвращает
   массив результатов, ZIP собирает веб-слой (`ZipArchive` в памяти).
6. Имени шаблона в файле ответов нет — выбор шаблонов целиком на
   стороне интерфейса, движок получает уже выбранный поток
   (см. `04-answers-file.md`, «Имя шаблона в файле ответов не хранится»).

Учёт: `documents_count` = число успешных элементов, каждый считается
отдельным документом в месячном лимите, `substitutions_total` — сумма
по всем.

## Массивы внутри данных документа

Пакетная генерация выше закрывает основной сценарий: «одинаковая
доверенность на десять человек» — это десять наборов в `datasets[]`
и десять отдельных файлов. Специального синтаксиса в шаблоне для
этого не нужно.

Отдельный и гораздо более редкий случай — **список внутри одного
документа**: акт, где в таблице должно быть N строк по числу позиций.
Это потребовало бы циклов в шаблоне и клонирования строк таблицы в
OpenXml. **Сейчас не делается.**

Если в пути встречается массив, движок отвечает понятной ошибкой
(«`positions` — это список; вывод списков внутри одного документа пока
не поддерживается»), а не молчанием и не пустотой.

## Обязательные тесты (`Doxen.Engine.Tests`)

Движок покрывается тестами **до** того, как подключается к вебу.
Минимальный набор:

1. Переменная, разорванная Word'ом на три `<w:t>` — находится и
   заменяется. Тестовый файл собрать программно, вручную создав
   такую разбивку.
2. Переменная в колонтитуле — находится и заменяется.
3. Переменная в ячейке таблицы — заменяется **ровно один раз**
   (регрессия на двойной обход).
4. Одна и та же переменная трижды в документе — `OccurrenceCount` = 3,
   `SubstitutionCount` после генерации = 3.
5. `{{ org.name }}` с пробелами и `{{org.name}}` — одна переменная.
6. `initialsEnd` для полного ФИО, для одной фамилии, для пустого объекта.
7. Путь в объект (`{{org.bank}}` при вложенном объекте) → `PathIsObject`
   с перечислением ключей.
8. Отсутствующий путь → `MissingValue`, документ генерируется с пустотой
   на этом месте и предупреждением.
9. Файл не `.docx` (например, переименованный `.txt`) → внятное
   исключение, а не `NullReferenceException`.
10. `GenerateBatch` с тремя наборами данных → три разных документа,
    в каждом свои значения. Проверить, что второй документ не содержит
    значений первого — это поймает ошибку с переиспользованием потока.
11. `GenerateBatch`, где второй набор содержит ошибку → первый и третий
    собраны, у второго заполнен `Error`, цикл не прерван.
12. Два набора с одинаковым `title` → имена файлов различаются.
13. Путь упирается в массив → понятная ошибка, а не исключение.
