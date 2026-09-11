namespace Doxen.Engine.Models;

public enum ResolveStatus
{
    Resolved,
    MissingValue,
    PathIsObject,
    WrongType,
}

// Результат разрешения пути переменной по JSON одного документа.
public sealed class ResolveResult
{
    public ResolveStatus Status { get; init; }
    public string? Value { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string>? AvailableKeys { get; init; }
}
