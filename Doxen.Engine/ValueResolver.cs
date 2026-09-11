using System.Globalization;
using System.Text.Json;
using Doxen.Engine.Models;

namespace Doxen.Engine;

// Разрешение пути переменной по JSON одного документа (03-engine.md,
// 04-answers-file.md). Массивы внутри пути намеренно не поддерживаются —
// см. 03-engine.md, раздел «Массивы внутри данных документа».
public static class ValueResolver
{
    public static ResolveResult Resolve(JsonElement root, string path)
    {
        var lookup = JsonPathWalker.Walk(root, path);

        return lookup.Status switch
        {
            PathLookupStatus.Missing => new ResolveResult { Status = ResolveStatus.MissingValue },
            PathLookupStatus.WrongType => new ResolveResult { Status = ResolveStatus.WrongType, Message = lookup.Message },
            _ => FromFoundElement(lookup.Element, path),
        };
    }

    private static ResolveResult FromFoundElement(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var keys = element.EnumerateObject().Select(p => p.Name).ToArray();
                var example = keys.Length > 0 ? $" Уточните путь, например {path}.{keys[0]}." : "";
                return new ResolveResult
                {
                    Status = ResolveStatus.PathIsObject,
                    AvailableKeys = keys,
                    Message = $"«{path}» — это группа значений, а не значение.{example}",
                };

            case JsonValueKind.String:
                return new ResolveResult { Status = ResolveStatus.Resolved, Value = element.GetString() ?? "" };

            case JsonValueKind.Number:
                return new ResolveResult { Status = ResolveStatus.Resolved, Value = FormatNumber(element) };

            case JsonValueKind.True:
                return new ResolveResult { Status = ResolveStatus.Resolved, Value = "Да" };

            case JsonValueKind.False:
                return new ResolveResult { Status = ResolveStatus.Resolved, Value = "Нет" };

            default:
                // Null уже отфильтрован в JsonPathWalker как MissingValue.
                return new ResolveResult { Status = ResolveStatus.MissingValue };
        }
    }

    private static string FormatNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var longValue))
        {
            return longValue.ToString(CultureInfo.InvariantCulture);
        }

        return element.GetDouble().ToString("0.####################", CultureInfo.InvariantCulture);
    }
}
