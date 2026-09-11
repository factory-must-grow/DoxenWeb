using Doxen.Engine.Models;

namespace Doxen.Web.Services;

// Тип поля ввода — только подсказка интерфейса, на результат в
// документе не влияет (04-answers-file.md). Список признаков — здесь,
// в одном месте, чтобы его можно было дополнять.
public enum FieldKind
{
    Text,
    Date,
    Number,
    Person,
}

public static class FieldKindDetector
{
    private static readonly string[] DateMarkers = { "date", "дата", "until", "from", "till" };
    private static readonly string[] NumberMarkers = { "inn", "kpp", "ogrn", "bik", "count", "number", "sum", "amount" };

    public static FieldKind Detect(TemplateVariable variable)
    {
        if (variable.FunctionName is not null)
        {
            // Путь — аргумент функции ФИО: три поля вместо одного.
            return FieldKind.Person;
        }

        var lastSegment = variable.Path.Split('.')[^1].ToLowerInvariant();

        if (Array.Exists(DateMarkers, m => lastSegment.Contains(m)))
        {
            return FieldKind.Date;
        }

        if (Array.Exists(NumberMarkers, m => lastSegment.Contains(m)))
        {
            return FieldKind.Number;
        }

        return FieldKind.Text;
    }
}
