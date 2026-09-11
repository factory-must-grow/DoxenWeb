using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Doxen.Engine.Tests;

// Программная сборка минимальных .docx для тестов движка — по правилу
// "не класть бинарные файлы в репозиторий" (07-build-plan.md, этап 2).
internal static class DocxBuilder
{
    // Один абзац из нескольких прогонов — так Word разрывает переменную
    // на несколько <w:t> по своей логике.
    public static MemoryStream WithParagraphRuns(params string[] runTexts)
    {
        return Build(mainPart =>
        {
            mainPart.Document.Body!.Append(ParagraphOf(runTexts));
        });
    }

    public static MemoryStream WithParagraphs(params string[][] paragraphsOfRunTexts)
    {
        return Build(mainPart =>
        {
            foreach (var runTexts in paragraphsOfRunTexts)
            {
                mainPart.Document.Body!.Append(ParagraphOf(runTexts));
            }
        });
    }

    public static MemoryStream WithHeader(string headerText, string? bodyText = null)
    {
        return Build(mainPart =>
        {
            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(ParagraphOf(headerText));
            headerPart.Header.Save();

            var sectionProperties = new SectionProperties(new HeaderReference
            {
                Type = HeaderFooterValues.Default,
                Id = mainPart.GetIdOfPart(headerPart),
            });

            if (bodyText is not null)
            {
                mainPart.Document.Body!.Append(ParagraphOf(bodyText));
            }

            mainPart.Document.Body!.Append(sectionProperties);
        });
    }

    public static MemoryStream WithTableCell(string cellText)
    {
        return Build(mainPart =>
        {
            var table = new Table(
                new TableRow(
                    new TableCell(ParagraphOf(cellText))));
            mainPart.Document.Body!.Append(table);
        });
    }

    // Байты, которые не являются ZIP-пакетом — имитация переименованного
    // не-.docx файла.
    public static MemoryStream NonDocxStream()
    {
        var bytes = "Это обычный текстовый файл, а не .docx."u8.ToArray();
        return new MemoryStream(bytes);
    }

    // Извлекает весь текст тела документа — для проверки результата
    // генерации в тестах.
    public static string ExtractText(byte[] docxBytes)
    {
        using var stream = new MemoryStream(docxBytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        return body is null ? "" : string.Concat(body.Descendants<Text>().Select(t => t.Text));
    }

    private static MemoryStream Build(Action<MainDocumentPart> configure)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            configure(mainPart);

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static Paragraph ParagraphOf(params string[] runTexts)
    {
        var paragraph = new Paragraph();
        foreach (var runText in runTexts)
        {
            paragraph.Append(new Run(CreateText(runText)));
        }

        return paragraph;
    }

    private static Text CreateText(string value)
    {
        var text = new Text(value);
        if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
        {
            text.Space = SpaceProcessingModeValues.Preserve;
        }

        return text;
    }
}
