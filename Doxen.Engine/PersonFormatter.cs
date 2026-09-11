using System.Text.Json;
using Doxen.Engine.Models;

namespace Doxen.Engine;

// Функции ФИО: initials, initialsStart, initialsEnd (03-engine.md).
// Аргумент должен разрешаться в объект с полями firstName/middleName/
// lastName — любое из них может отсутствовать.
public static class PersonFormatter
{
    private static readonly string[] KnownFunctions = { "initials", "initialsStart", "initialsEnd" };

    public static ResolveResult Format(string functionName, JsonElement root, string argumentPath)
    {
        if (Array.IndexOf(KnownFunctions, functionName) < 0)
        {
            return new ResolveResult
            {
                Status = ResolveStatus.WrongType,
                Message = $"Неизвестная функция «{functionName}». Доступны: {string.Join(", ", KnownFunctions)}.",
            };
        }

        var lookup = JsonPathWalker.Walk(root, argumentPath);

        if (lookup.Status == PathLookupStatus.Missing)
        {
            return new ResolveResult { Status = ResolveStatus.MissingValue };
        }

        if (lookup.Status == PathLookupStatus.WrongType)
        {
            return new ResolveResult { Status = ResolveStatus.WrongType, Message = lookup.Message };
        }

        if (lookup.Element.ValueKind != JsonValueKind.Object)
        {
            return new ResolveResult
            {
                Status = ResolveStatus.WrongType,
                Message = $"«{functionName}» ожидает группу с полями firstName/middleName/lastName, " +
                          $"а «{argumentPath}» — не группа.",
            };
        }

        var firstName = GetStringProperty(lookup.Element, "firstName");
        var middleName = GetStringProperty(lookup.Element, "middleName");
        var lastName = GetStringProperty(lookup.Element, "lastName");

        if (string.IsNullOrEmpty(firstName) && string.IsNullOrEmpty(middleName) && string.IsNullOrEmpty(lastName))
        {
            return new ResolveResult
            {
                Status = ResolveStatus.MissingValue,
                Message = $"Для «{argumentPath}» не заполнены ни имя, ни отчество, ни фамилия.",
            };
        }

        var formatted = functionName switch
        {
            "initials" => JoinNonEmpty(Initial(firstName), Initial(middleName)),
            "initialsStart" => JoinNonEmpty(Initial(firstName), Initial(middleName), lastName),
            "initialsEnd" => JoinNonEmpty(lastName, Initial(firstName), Initial(middleName)),
            _ => "",
        };

        return new ResolveResult { Status = ResolveStatus.Resolved, Value = formatted };
    }

    private static string? GetStringProperty(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Initial(string? value) =>
        string.IsNullOrEmpty(value) ? "" : char.ToUpperInvariant(value[0]) + ".";

    private static string JoinNonEmpty(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
