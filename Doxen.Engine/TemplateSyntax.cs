using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;

namespace Doxen.Engine;

internal sealed class ParsedExpression
{
    public string Path { get; init; } = "";
    public string? FunctionName { get; init; }
    public string? Error { get; init; }
}

// Общее между TemplateParser и TemplateGenerator: синтаксис {{...}},
// обход частей .docx и открытие файла с понятной ошибкой вместо трассы
// OpenXml или NullReferenceException.
internal static class TemplateSyntax
{
    public static readonly Regex VariableRegex =
        new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    private static readonly Regex FunctionRegex =
        new(@"^(?<func>[A-Za-z][A-Za-z0-9_]*)\s*\(\s*(?<arg>[^()]+?)\s*\)$",
            RegexOptions.Compiled);

    private static readonly Regex PathRegex =
        new(@"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*$", RegexOptions.Compiled);

    public static ParsedExpression Parse(string expression)
    {
        var functionMatch = FunctionRegex.Match(expression);
        if (functionMatch.Success)
        {
            var functionName = functionMatch.Groups["func"].Value;
            var path = functionMatch.Groups["arg"].Value;
            return new ParsedExpression
            {
                Path = path,
                FunctionName = functionName,
                Error = PathRegex.IsMatch(path)
                    ? null
                    : $"Не удалось разобрать путь «{path}» в переменной «{{{{{expression}}}}}».",
            };
        }

        if (PathRegex.IsMatch(expression))
        {
            return new ParsedExpression { Path = expression };
        }

        return new ParsedExpression
        {
            Path = expression,
            Error = $"Не удалось разобрать переменную «{{{{{expression}}}}}».",
        };
    }

    public static WordprocessingDocument OpenReadOnly(Stream stream) => Open(stream, false);

    public static WordprocessingDocument OpenReadWrite(Stream stream) => Open(stream, true);

    private static WordprocessingDocument Open(Stream stream, bool editable)
    {
        try
        {
            return WordprocessingDocument.Open(stream, editable);
        }
        catch (Exception ex)
        {
            throw new TemplateFormatException(
                "Не удалось открыть файл. Проверьте, что это .docx и что он открывается в Word.", ex);
        }
    }

    public static IEnumerable<OpenXmlPart> GetAllParts(WordprocessingDocument doc)
    {
        if (doc.MainDocumentPart is null) yield break;

        yield return doc.MainDocumentPart;
        foreach (var header in doc.MainDocumentPart.HeaderParts) yield return header;
        foreach (var footer in doc.MainDocumentPart.FooterParts) yield return footer;
        if (doc.MainDocumentPart.FootnotesPart is not null) yield return doc.MainDocumentPart.FootnotesPart;
        if (doc.MainDocumentPart.EndnotesPart is not null) yield return doc.MainDocumentPart.EndnotesPart;
    }
}
