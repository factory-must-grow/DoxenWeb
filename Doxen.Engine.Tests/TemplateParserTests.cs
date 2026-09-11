using Doxen.Engine;

namespace Doxen.Engine.Tests;

// Тесты 1–5 и 9 из 03-engine.md — обязательный минимум для этапа 2
// («Движок: разбор», см. 07-build-plan.md). На этом этапе есть только
// TemplateParser, поэтому проверяется нахождение переменных и подсчёт
// вхождений; подстановка значений (TemplateGenerator) и связанные
// с ней тесты 6–13 — задача этапа 3.
public class TemplateParserTests
{
    // Тест 1: переменная, разорванная Word'ом на три <w:t>, — находится.
    [Fact]
    public void SplitAcrossThreeTextRuns_IsFoundAsSingleVariable()
    {
        using var stream = DocxBuilder.WithParagraphRuns("{{org.", "na", "me}}");

        var variables = TemplateParser.Parse(stream);

        var variable = Assert.Single(variables);
        Assert.Equal("org.name", variable.Expression);
        Assert.Equal("org.name", variable.Path);
        Assert.Null(variable.FunctionName);
        Assert.Null(variable.Error);
        Assert.Equal(1, variable.OccurrenceCount);
    }

    // Тест 2: переменная в колонтитуле — находится.
    [Fact]
    public void VariableInHeader_IsFound()
    {
        using var stream = DocxBuilder.WithHeader("{{org.name}}");

        var variables = TemplateParser.Parse(stream);

        var variable = Assert.Single(variables);
        Assert.Equal("org.name", variable.Expression);
    }

    // Тест 3: переменная в ячейке таблицы — считается ровно один раз
    // (регрессия на двойной обход тела документа и ячеек по отдельности).
    [Fact]
    public void VariableInTableCell_CountedExactlyOnce()
    {
        using var stream = DocxBuilder.WithTableCell("{{org.name}}");

        var variables = TemplateParser.Parse(stream);

        var variable = Assert.Single(variables);
        Assert.Equal(1, variable.OccurrenceCount);
    }

    // Тест 4 (часть про разбор): одна и та же переменная трижды в
    // документе — OccurrenceCount = 3.
    [Fact]
    public void SameVariableThreeTimes_OccurrenceCountIsThree()
    {
        using var stream = DocxBuilder.WithParagraphs(
            new[] { "{{org.name}}" },
            new[] { "{{org.name}}" },
            new[] { "{{org.name}}" });

        var variables = TemplateParser.Parse(stream);

        var variable = Assert.Single(variables);
        Assert.Equal(3, variable.OccurrenceCount);
    }

    // Тест 5: "{{ org.name }}" с пробелами и "{{org.name}}" — одна
    // и та же переменная.
    [Fact]
    public void WhitespaceInsideBraces_IsSameVariableAsWithout()
    {
        using var stream = DocxBuilder.WithParagraphs(
            new[] { "{{ org.name }}" },
            new[] { "{{org.name}}" });

        var variables = TemplateParser.Parse(stream);

        var variable = Assert.Single(variables);
        Assert.Equal("org.name", variable.Expression);
        Assert.Equal(2, variable.OccurrenceCount);
    }

    // Тест 9: файл не .docx (например, переименованный .txt) — внятное
    // исключение, а не NullReferenceException.
    [Fact]
    public void NonDocxFile_ThrowsTemplateFormatException()
    {
        using var stream = DocxBuilder.NonDocxStream();

        Assert.Throws<TemplateFormatException>(() => TemplateParser.Parse(stream));
    }

    [Fact]
    public void FunctionCall_ParsesFunctionNameAndPath()
    {
        using var stream = DocxBuilder.WithParagraphRuns("{{initialsEnd(org.director)}}");

        var variables = TemplateParser.Parse(stream);

        var variable = Assert.Single(variables);
        Assert.Equal("initialsEnd", variable.FunctionName);
        Assert.Equal("org.director", variable.Path);
        Assert.Null(variable.Error);
    }

    [Fact]
    public void EmptyPathSegment_IsMarkedAsError()
    {
        using var stream = DocxBuilder.WithParagraphRuns("{{org..name}}");

        var variables = TemplateParser.Parse(stream);

        var variable = Assert.Single(variables);
        Assert.NotNull(variable.Error);
    }

    [Fact]
    public void NoVariables_ReturnsEmptyList()
    {
        using var stream = DocxBuilder.WithParagraphRuns("Обычный текст без переменных.");

        var variables = TemplateParser.Parse(stream);

        Assert.Empty(variables);
    }
}
