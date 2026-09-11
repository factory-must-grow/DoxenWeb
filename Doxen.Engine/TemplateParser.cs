using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Doxen.Engine.Models;

namespace Doxen.Engine;

// Поиск переменных {{...}} в .docx. Только чтение — ничего в документе
// не меняет. Подстановка значений — задача TemplateGenerator.
public static class TemplateParser
{
    private static readonly Regex VariableRegex =
        new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    private static readonly Regex FunctionRegex =
        new(@"^(?<func>[A-Za-z][A-Za-z0-9_]*)\s*\(\s*(?<arg>[^()]+?)\s*\)$",
            RegexOptions.Compiled);

    private static readonly Regex PathRegex =
        new(@"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*$", RegexOptions.Compiled);

    public static IReadOnlyList<TemplateVariable> Parse(Stream docxStream)
    {
        using var document = OpenReadOnly(docxStream);

        var variables = new List<TemplateVariable>();
        var indexByExpression = new Dictionary<string, int>();

        foreach (var part in GetAllParts(document))
        {
            var root = part.RootElement;
            if (root is null) continue;

            // Ячейки таблиц Descendants<Paragraph>() уже возвращает вместе
            // с обычными абзацами — обходить их отдельно нельзя, иначе
            // переменная в таблице засчитается дважды.
            foreach (var paragraph in root.Descendants<Paragraph>())
            {
                RegisterMatches(paragraph, variables, indexByExpression);
            }
        }

        return variables;
    }

    private static void RegisterMatches(Paragraph paragraph, List<TemplateVariable> variables,
        Dictionary<string, int> indexByExpression)
    {
        var textNodes = paragraph.Descendants<Text>().ToList();
        if (textNodes.Count == 0) return;

        // Word разрывает переменную на несколько <w:t> произвольно —
        // искать нужно по абзацу целиком, а не по отдельным фрагментам.
        var text = string.Concat(textNodes.Select(t => t.Text));

        foreach (Match match in VariableRegex.Matches(text))
        {
            var expression = match.Groups[1].Value;

            if (indexByExpression.TryGetValue(expression, out var existingIndex))
            {
                variables[existingIndex].OccurrenceCount++;
                continue;
            }

            variables.Add(BuildVariable(expression));
            indexByExpression[expression] = variables.Count - 1;
        }
    }

    private static TemplateVariable BuildVariable(string expression)
    {
        var functionMatch = FunctionRegex.Match(expression);
        if (functionMatch.Success)
        {
            var functionName = functionMatch.Groups["func"].Value;
            var path = functionMatch.Groups["arg"].Value;
            return new TemplateVariable
            {
                Expression = expression,
                Path = path,
                FunctionName = functionName,
                OccurrenceCount = 1,
                Error = PathRegex.IsMatch(path)
                    ? null
                    : $"Не удалось разобрать путь «{path}» в переменной «{{{{{expression}}}}}».",
            };
        }

        if (PathRegex.IsMatch(expression))
        {
            return new TemplateVariable
            {
                Expression = expression,
                Path = expression,
                OccurrenceCount = 1,
            };
        }

        return new TemplateVariable
        {
            Expression = expression,
            Path = expression,
            OccurrenceCount = 1,
            Error = $"Не удалось разобрать переменную «{{{{{expression}}}}}».",
        };
    }

    private static WordprocessingDocument OpenReadOnly(Stream docxStream)
    {
        try
        {
            return WordprocessingDocument.Open(docxStream, false);
        }
        catch (Exception ex)
        {
            throw new TemplateFormatException(
                "Не удалось открыть файл. Проверьте, что это .docx и что он открывается в Word.", ex);
        }
    }

    private static IEnumerable<OpenXmlPart> GetAllParts(WordprocessingDocument doc)
    {
        if (doc.MainDocumentPart is null) yield break;

        yield return doc.MainDocumentPart;
        foreach (var header in doc.MainDocumentPart.HeaderParts) yield return header;
        foreach (var footer in doc.MainDocumentPart.FooterParts) yield return footer;
        if (doc.MainDocumentPart.FootnotesPart is not null) yield return doc.MainDocumentPart.FootnotesPart;
        if (doc.MainDocumentPart.EndnotesPart is not null) yield return doc.MainDocumentPart.EndnotesPart;
    }
}
