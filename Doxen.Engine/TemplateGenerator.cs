using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Doxen.Engine.Models;

namespace Doxen.Engine;

// Подстановка значений в шаблон и пакетная генерация нескольких
// документов из одного шаблона (03-engine.md).
public static class TemplateGenerator
{
    public static GenerationResult Generate(Stream templateStream, JsonElement answers)
    {
        // Исходный поток не модифицируется — пользователь может
        // загрузить тот же файл ещё раз.
        using var buffer = new MemoryStream();
        templateStream.CopyTo(buffer);
        buffer.Position = 0;

        var warnings = new List<string>();
        var substitutionCount = 0;

        using (var document = TemplateSyntax.OpenReadWrite(buffer))
        {
            foreach (var part in TemplateSyntax.GetAllParts(document))
            {
                var root = part.RootElement;
                if (root is null) continue;

                var partSubstitutions = 0;
                foreach (var paragraph in root.Descendants<Paragraph>())
                {
                    partSubstitutions += ReplaceInParagraph(paragraph, answers, warnings);
                }

                if (partSubstitutions > 0)
                {
                    root.Save();
                }

                substitutionCount += partSubstitutions;
            }
        }

        return new GenerationResult
        {
            Document = buffer.ToArray(),
            SubstitutionCount = substitutionCount,
            Warnings = warnings,
        };
    }

    public static IReadOnlyList<BatchItemResult> GenerateBatch(Stream templateStream, JsonElement answersRoot)
    {
        // Шаблон читается один раз, дальше на каждой итерации
        // оборачивается в новый MemoryStream — Generate модифицирует
        // переданный ему поток.
        var templateBytes = ReadAllBytes(templateStream);

        var shared = answersRoot.TryGetProperty("shared", out var sharedEl) &&
                     sharedEl.ValueKind == JsonValueKind.Object
            ? sharedEl
            : (JsonElement?)null;

        var datasets = answersRoot.TryGetProperty("datasets", out var datasetsEl) &&
                       datasetsEl.ValueKind == JsonValueKind.Array
            ? datasetsEl.EnumerateArray().ToList()
            : new List<JsonElement>();

        var results = new List<BatchItemResult>();
        var usedTitles = new HashSet<string>();

        for (var i = 0; i < datasets.Count; i++)
        {
            var item = datasets[i];
            var rawTitle = item.ValueKind == JsonValueKind.Object &&
                           item.TryGetProperty("title", out var titleEl) &&
                           titleEl.ValueKind == JsonValueKind.String
                ? titleEl.GetString()
                : null;

            var values = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("values", out var valuesEl)
                ? valuesEl
                : item;

            var title = MakeUniqueTitle(rawTitle, i, usedTitles);

            try
            {
                if (values.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException(
                        $"Набор данных «{title}» повреждён: values должно быть объектом, а не {values.ValueKind}.");
                }

                var merged = MergeLeaves(shared, values);
                using var templateMs = new MemoryStream(templateBytes, 0, templateBytes.Length, writable: false);
                var generated = Generate(templateMs, merged);

                results.Add(new BatchItemResult
                {
                    Title = title,
                    Document = generated.Document,
                    SubstitutionCount = generated.SubstitutionCount,
                    Warnings = generated.Warnings,
                });
            }
            catch (Exception ex)
            {
                // Ошибка на одном наборе не должна прерывать остальные —
                // пользователь получает документы по всем остальным
                // наборам плюс внятное сообщение по этому.
                results.Add(new BatchItemResult
                {
                    Title = title,
                    Document = null,
                    Error = ex.Message,
                });
            }
        }

        return results;
    }

    private static int ReplaceInParagraph(Paragraph paragraph, JsonElement answers, List<string> warnings)
    {
        var textNodes = paragraph.Descendants<Text>().ToList();
        if (textNodes.Count == 0) return 0;

        // Word разрывает переменную на несколько <w:t> произвольно —
        // заменять нужно по абзацу целиком, а не по отдельным фрагментам.
        var original = string.Concat(textNodes.Select(t => t.Text));
        var substitutions = 0;

        var replaced = TemplateSyntax.VariableRegex.Replace(original, match =>
        {
            substitutions++;
            var expression = match.Groups[1].Value;
            var result = ResolveExpression(expression, answers);

            if (result.Status == ResolveStatus.Resolved)
            {
                return result.Value ?? "";
            }

            // Неразрешённая переменная превращается в пустую строку, а не
            // остаётся как {{...}} в документе — пользователь уже видел
            // список недостающих значений на экране заполнения.
            warnings.Add(BuildWarning(expression, result));
            return "";
        });

        if (substitutions == 0) return 0;

        // Весь результат — в первый узел, остальные опустошаем.
        textNodes[0].Text = replaced;
        textNodes[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < textNodes.Count; i++)
        {
            textNodes[i].Text = "";
        }

        return substitutions;
    }

    private static ResolveResult ResolveExpression(string expression, JsonElement answers)
    {
        var parsed = TemplateSyntax.Parse(expression);
        if (parsed.Error is not null)
        {
            return new ResolveResult { Status = ResolveStatus.WrongType, Message = parsed.Error };
        }

        return parsed.FunctionName is null
            ? ValueResolver.Resolve(answers, parsed.Path)
            : PersonFormatter.Format(parsed.FunctionName, answers, parsed.Path);
    }

    private static string BuildWarning(string expression, ResolveResult result) =>
        result.Message is not null
            ? $"«{{{{{expression}}}}}»: {result.Message}"
            : $"«{{{{{expression}}}}}»: значение не найдено, оставлено пустым.";

    private static string MakeUniqueTitle(string? rawTitle, int index, HashSet<string> used)
    {
        var baseTitle = string.IsNullOrWhiteSpace(rawTitle) ? $"Документ {index + 1}" : rawTitle!;
        var candidate = baseTitle;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{baseTitle} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // Значение для переменной ищется сначала в values набора, потом в
    // shared (04-answers-file.md). Слияние — на уровне листьев, а не
    // целых веток: shared.org.name и values.org.inn разрешатся оба.
    private static JsonElement MergeLeaves(JsonElement? baseElement, JsonElement overlay)
    {
        JsonNode? baseNode = baseElement is { } b ? JsonNode.Parse(b.GetRawText()) : new JsonObject();
        var overlayNode = JsonNode.Parse(overlay.GetRawText());

        var merged = MergeNodes(baseNode, overlayNode);
        return JsonDocument.Parse(merged?.ToJsonString() ?? "{}").RootElement;
    }

    private static JsonNode? MergeNodes(JsonNode? baseNode, JsonNode? overlayNode)
    {
        if (overlayNode is null) return baseNode?.DeepClone();

        if (baseNode is JsonObject baseObj && overlayNode is JsonObject overlayObj)
        {
            var result = new JsonObject();
            foreach (var property in baseObj)
            {
                result[property.Key] = property.Value?.DeepClone();
            }

            foreach (var property in overlayObj)
            {
                var baseChild = baseObj.TryGetPropertyValue(property.Key, out var existing) ? existing : null;
                result[property.Key] = MergeNodes(baseChild, property.Value);
            }

            return result;
        }

        return overlayNode.DeepClone();
    }
}
