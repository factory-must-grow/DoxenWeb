using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Doxen.Engine;

// Чтение и запись файла ответов (04-answers-file.md). Файл описывает
// клиента: shared — общие значения, datasets — наборы данных, из
// каждого получается свой документ. Файл на сервере не хранится —
// движок только разбирает и собирает его в памяти.
public static class AnswersFile
{
    public const int CurrentVersion = 1;

    public sealed class ReadResult
    {
        public bool Success { get; init; }

        // Канонический вид: { doxenVersion, shared, datasets: [{ title?, values }] }.
        // Заполнен только при Success == true.
        public JsonElement Root { get; init; }

        public string? Error { get; init; }
        public bool IsNewerVersion { get; init; }
    }

    public static ReadResult Read(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new ReadResult
            {
                Success = false,
                Error = $"Не удалось прочитать файл ответов: ошибка в строке {ex.LineNumber + 1}. " +
                        "Проверьте файл в текстовом редакторе.",
            };
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ReadResult { Success = false, Error = "Это не похоже на файл ответов Doxen." };
            }

            var version = root.TryGetProperty("doxenVersion", out var versionEl) &&
                          versionEl.ValueKind == JsonValueKind.Number
                ? versionEl.GetInt32()
                : 1;

            var shared = root.TryGetProperty("shared", out var sharedEl) && sharedEl.ValueKind == JsonValueKind.Object
                ? sharedEl
                : (JsonElement?)null;

            // "documents" — устаревшее имя того же поля, принимаем как datasets.
            var datasetsArray = GetArray(root, "datasets") ?? GetArray(root, "documents");

            var looksLikeAnswersFile = shared is not null || datasetsArray is not null ||
                                        root.TryGetProperty("doxenVersion", out _);

            List<(string? Title, JsonElement Values)> datasets;
            if (datasetsArray is { } array)
            {
                datasets = ReadDatasetItems(array);
            }
            else if (looksLikeAnswersFile)
            {
                // Явно файл Doxen (есть shared/doxenVersion), но без
                // наборов — работаем как будто файла нет, без ошибки.
                datasets = new List<(string?, JsonElement)>();
            }
            else if (root.EnumerateObject().Any())
            {
                // Массива нет вовсе, значения лежат прямо в корне —
                // считаем это одним набором.
                datasets = new List<(string?, JsonElement)> { (null, root) };
            }
            else
            {
                return new ReadResult { Success = false, Error = "Это не похоже на файл ответов Doxen." };
            }

            var canonicalRoot = BuildCanonicalRoot(version, shared, datasets);

            return new ReadResult
            {
                Success = true,
                Root = canonicalRoot,
                IsNewerVersion = version > CurrentVersion,
            };
        }
    }

    // Форматируется с отступами и без экранирования кириллицы — файл
    // предназначен для правки руками.
    public static string Write(JsonElement shared, IReadOnlyList<(string? Title, JsonElement Values)> datasets)
    {
        var root = BuildCanonicalRoot(CurrentVersion, shared, datasets.ToList());

        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static List<(string? Title, JsonElement Values)> ReadDatasetItems(JsonElement array)
    {
        var result = new List<(string?, JsonElement)>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                result.Add((null, item));
                continue;
            }

            var title = item.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                ? titleEl.GetString()
                : null;

            var values = item.TryGetProperty("values", out var valuesEl) ? valuesEl : item;

            result.Add((title, values));
        }

        return result;
    }

    private static JsonElement BuildCanonicalRoot(int version, JsonElement? shared,
        List<(string? Title, JsonElement Values)> datasets)
    {
        var root = new JsonObject
        {
            ["doxenVersion"] = JsonValue.Create(version),
            ["shared"] = shared is { } s ? JsonNode.Parse(s.GetRawText()) : new JsonObject(),
        };

        var datasetsNode = new JsonArray();
        foreach (var (title, values) in datasets)
        {
            var item = new JsonObject
            {
                ["values"] = JsonNode.Parse(values.GetRawText()),
            };
            if (!string.IsNullOrEmpty(title))
            {
                item["title"] = JsonValue.Create(title);
            }

            datasetsNode.Add(item);
        }

        root["datasets"] = datasetsNode;

        return JsonDocument.Parse(root.ToJsonString()).RootElement;
    }

    private static JsonElement? GetArray(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array ? el : null;
}
