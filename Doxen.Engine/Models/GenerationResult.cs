namespace Doxen.Engine.Models;

public sealed class GenerationResult
{
    public byte[] Document { get; init; } = Array.Empty<byte>();
    public int SubstitutionCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

// Результат сборки одного документа внутри пакетной генерации.
// Document == null означает, что сборка этого набора не удалась —
// подробности в Error, остальные наборы пакета при этом не прерываются.
public sealed class BatchItemResult
{
    public string Title { get; init; } = "";
    public byte[]? Document { get; init; }
    public int SubstitutionCount { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
