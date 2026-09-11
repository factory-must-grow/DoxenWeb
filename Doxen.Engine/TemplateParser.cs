using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Wordprocessing;
using Doxen.Engine.Models;

namespace Doxen.Engine;

// Поиск переменных {{...}} в .docx. Только чтение — ничего в документе
// не меняет. Подстановка значений — задача TemplateGenerator.
public static class TemplateParser
{
    public static IReadOnlyList<TemplateVariable> Parse(Stream docxStream)
    {
        using var document = TemplateSyntax.OpenReadOnly(docxStream);

        var variables = new List<TemplateVariable>();
        var indexByExpression = new Dictionary<string, int>();

        foreach (var part in TemplateSyntax.GetAllParts(document))
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

        foreach (Match match in TemplateSyntax.VariableRegex.Matches(text))
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
        var parsed = TemplateSyntax.Parse(expression);
        return new TemplateVariable
        {
            Expression = expression,
            Path = parsed.Path,
            FunctionName = parsed.FunctionName,
            OccurrenceCount = 1,
            Error = parsed.Error,
        };
    }
}
