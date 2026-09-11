using Doxen.Engine.Models;
using Doxen.Web.Services;

namespace Doxen.Web.Models;

// Одно поле формы на экране заполнения. Для FieldKind.Person значение
// хранится в трёх частях, для остальных — в Value.
public sealed class FieldViewModel
{
    public required TemplateVariable Variable { get; init; }
    public required FieldKind Kind { get; init; }
    public bool IsPerDocument { get; init; }
    public string? Value { get; init; }
    public bool NoDate { get; init; }
    public string? FirstName { get; init; }
    public string? MiddleName { get; init; }
    public string? LastName { get; init; }

    // Имя поля path.firstName и т.д. — как в шаблоне {{ }}, так и в
    // сериализованном JSON.
    public string FirstNamePath => Variable.Path + ".firstName";
    public string MiddleNamePath => Variable.Path + ".middleName";
    public string LastNamePath => Variable.Path + ".lastName";
}

public sealed class FieldGroupViewModel
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required List<FieldViewModel> Fields { get; init; }
}
