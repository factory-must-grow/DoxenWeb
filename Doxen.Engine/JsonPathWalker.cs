using System.Text.Json;

namespace Doxen.Engine;

internal enum PathLookupStatus
{
    Found,
    Missing,
    WrongType,
}

internal readonly struct PathLookupResult
{
    public PathLookupStatus Status { get; init; }
    public JsonElement Element { get; init; }
    public string? Message { get; init; }
}

// Проход по сегментам пути внутри JSON. Общее для ValueResolver (простые
// пути, должны привести к значению) и PersonFormatter (путь-аргумент
// функции ФИО, должен привести к объекту) — оба спотыкаются об одни и
// те же случаи: сегмент не найден, путь упёрся в список, путь продолжается
// после не-объекта.
internal static class JsonPathWalker
{
    public static PathLookupResult Walk(JsonElement root, string path)
    {
        var segments = path.Split('.');
        var current = root;

        for (var i = 0; i < segments.Length; i++)
        {
            if (current.ValueKind == JsonValueKind.Array)
            {
                return WrongType(ListMessage(segments, i));
            }

            if (current.ValueKind != JsonValueKind.Object)
            {
                return WrongType(NotGroupMessage(segments, i));
            }

            if (!current.TryGetProperty(segments[i], out var next) || next.ValueKind == JsonValueKind.Null)
            {
                return new PathLookupResult { Status = PathLookupStatus.Missing };
            }

            current = next;
        }

        if (current.ValueKind == JsonValueKind.Array)
        {
            return WrongType(ListMessage(segments, segments.Length));
        }

        return new PathLookupResult { Status = PathLookupStatus.Found, Element = current };
    }

    private static PathLookupResult WrongType(string message) =>
        new() { Status = PathLookupStatus.WrongType, Message = message };

    private static string ListMessage(string[] segments, int upTo) =>
        $"«{PathPrefix(segments, upTo)}» — это список; вывод списков внутри одного документа пока не поддерживается.";

    private static string NotGroupMessage(string[] segments, int upTo) =>
        $"«{PathPrefix(segments, upTo)}» — не группа значений, дальше пути быть не может.";

    private static string PathPrefix(string[] segments, int upTo) =>
        upTo == 0 ? "(корень)" : string.Join('.', segments[..upTo]);
}
