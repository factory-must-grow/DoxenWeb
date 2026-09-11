namespace Doxen.Engine.Models;

// Переменная вида {{...}}, найденная в шаблоне. Path и FunctionName
// разобраны из Expression, но не проверены на существование значения —
// этим занимается ValueResolver.
public sealed class TemplateVariable
{
    public string Expression { get; init; } = "";
    public string Path { get; init; } = "";
    public string? FunctionName { get; init; }
    public int OccurrenceCount { get; set; }

    // Заполнено, если выражение не разобралось как путь или вызов
    // функции — переменная всё равно попадает в результат, а не
    // пропускается молча.
    public string? Error { get; init; }
}
