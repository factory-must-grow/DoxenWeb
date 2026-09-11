using System.Text.Json;
using Doxen.Engine;

namespace Doxen.Engine.Tests;

// Тесты 8, 10–13 из 03-engine.md, плюс тест полного цикла — критерий
// готовности этапа 3 из 07-build-plan.md.
public class TemplateGeneratorTests
{
    // Критерий готовности этапа 3: шаблон плюс JSON на входе, корректный
    // .docx на выходе, SubstitutionCount совпадает с ожидаемым.
    [Fact]
    public void Generate_FullCycle_ProducesValidDocumentWithExpectedSubstitutionCount()
    {
        using var template = DocxBuilder.WithParagraphs(
            new[] { "Уважаемый {{org.director.firstName}}!" },
            new[] { "Организация: {{org.name}}, ИНН {{org.inn}}." },
            new[] { "Представитель: {{initialsEnd(org.director)}}." });

        var answers = Parse("""
            {
                "org": {
                    "name": "ООО «Рога и копыта»",
                    "inn": "7701234567",
                    "director": { "firstName": "Иван", "middleName": "Петрович", "lastName": "Сидоров" }
                }
            }
            """);

        var result = TemplateGenerator.Generate(template, answers);

        Assert.Equal(4, result.SubstitutionCount);
        Assert.Empty(result.Warnings);

        // Результат должен открываться как корректный .docx — это же
        // делает и DocxBuilder.ExtractText, падая, если разметка битая.
        var text = DocxBuilder.ExtractText(result.Document);
        Assert.Contains("Иван", text);
        Assert.Contains("ООО «Рога и копыта»", text);
        Assert.Contains("7701234567", text);
        Assert.Contains("Сидоров И. П.", text);
        Assert.DoesNotContain("{{", text);
    }

    // Тест 8: отсутствующий путь → MissingValue, документ генерируется
    // с пустотой на этом месте и предупреждением.
    [Fact]
    public void Generate_MissingPath_ReplacesWithEmptyAndWarns()
    {
        using var template = DocxBuilder.WithParagraphRuns("Значение: {{org.missing}}.");
        var answers = Parse("""{"org":{"name":"ООО"}}""");

        var result = TemplateGenerator.Generate(template, answers);

        Assert.Equal(1, result.SubstitutionCount);
        Assert.Single(result.Warnings);

        var text = DocxBuilder.ExtractText(result.Document);
        Assert.Equal("Значение: .", text);
    }

    // Тест 13: путь упирается в массив → понятная ошибка (предупреждение),
    // а не исключение.
    [Fact]
    public void Generate_PathHitsArray_ProducesWarningNotException()
    {
        using var template = DocxBuilder.WithParagraphRuns("{{positions}}");
        var answers = Parse("""{"positions":[1,2,3]}""");

        var result = TemplateGenerator.Generate(template, answers);

        Assert.Equal(1, result.SubstitutionCount);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("список", warning);
    }

    // Тест 10: GenerateBatch с тремя наборами данных → три разных
    // документа; второй документ не содержит значений первого — это
    // поймает ошибку с переиспользованием потока шаблона.
    [Fact]
    public void GenerateBatch_ThreeDatasets_ProducesThreeDistinctDocuments()
    {
        using var template = DocxBuilder.WithParagraphRuns("Представитель: {{agent.fullName}}.");

        var answersRoot = Parse("""
            {
                "datasets": [
                    { "title": "Первый", "values": { "agent": { "fullName": "Кузнецова Мария Алексеевна" } } },
                    { "title": "Второй", "values": { "agent": { "fullName": "Петров Сергей Иванович" } } },
                    { "title": "Третий", "values": { "agent": { "fullName": "Смирнова Анна Викторовна" } } }
                ]
            }
            """);

        var results = TemplateGenerator.GenerateBatch(template, answersRoot);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Null(r.Error));

        var texts = results.Select(r => DocxBuilder.ExtractText(r.Document!)).ToList();

        Assert.Contains("Кузнецова Мария Алексеевна", texts[0]);
        Assert.DoesNotContain("Петров Сергей Иванович", texts[0]);
        Assert.DoesNotContain("Смирнова Анна Викторовна", texts[0]);

        Assert.Contains("Петров Сергей Иванович", texts[1]);
        Assert.DoesNotContain("Кузнецова Мария Алексеевна", texts[1]);

        Assert.Contains("Смирнова Анна Викторовна", texts[2]);
        Assert.DoesNotContain("Кузнецова Мария Алексеевна", texts[2]);
    }

    // Тест 11: GenerateBatch, где второй набор содержит ошибку → первый
    // и третий собраны, у второго заполнен Error, цикл не прерван.
    [Fact]
    public void GenerateBatch_SecondDatasetBroken_OthersStillSucceed()
    {
        using var template = DocxBuilder.WithParagraphRuns("Представитель: {{agent.fullName}}.");

        var answersRoot = Parse("""
            {
                "datasets": [
                    { "title": "Первый", "values": { "agent": { "fullName": "Кузнецова Мария Алексеевна" } } },
                    { "title": "Второй", "values": "это не объект" },
                    { "title": "Третий", "values": { "agent": { "fullName": "Смирнова Анна Викторовна" } } }
                ]
            }
            """);

        var results = TemplateGenerator.GenerateBatch(template, answersRoot);

        Assert.Equal(3, results.Count);

        Assert.Null(results[0].Error);
        Assert.NotNull(results[0].Document);

        Assert.NotNull(results[1].Error);
        Assert.Null(results[1].Document);

        Assert.Null(results[2].Error);
        Assert.NotNull(results[2].Document);
    }

    // Тест 12: два набора с одинаковым title → имена файлов различаются.
    [Fact]
    public void GenerateBatch_DuplicateTitles_FileNamesDiffer()
    {
        using var template = DocxBuilder.WithParagraphRuns("{{agent.fullName}}");

        var answersRoot = Parse("""
            {
                "datasets": [
                    { "title": "Иванов И. И.", "values": { "agent": { "fullName": "Иванов Иван Иванович" } } },
                    { "title": "Иванов И. И.", "values": { "agent": { "fullName": "Иванов Игорь Ильич" } } }
                ]
            }
            """);

        var results = TemplateGenerator.GenerateBatch(template, answersRoot);

        Assert.Equal(2, results.Count);
        Assert.NotEqual(results[0].Title, results[1].Title);
        Assert.Equal("Иванов И. И.", results[0].Title);
        Assert.StartsWith("Иванов И. И.", results[1].Title);
    }

    // Слияние shared и values набора — на уровне листьев, а не целых
    // веток (04-answers-file.md).
    [Fact]
    public void GenerateBatch_MergesSharedAndDatasetValuesAtLeafLevel()
    {
        using var template = DocxBuilder.WithParagraphRuns("{{org.name}}, ИНН {{org.inn}}.");

        var answersRoot = Parse("""
            {
                "shared": { "org": { "name": "ООО «Рога и копыта»" } },
                "datasets": [
                    { "values": { "org": { "inn": "7701234567" } } }
                ]
            }
            """);

        var results = TemplateGenerator.GenerateBatch(template, answersRoot);

        var text = DocxBuilder.ExtractText(results[0].Document!);
        Assert.Contains("ООО «Рога и копыта»", text);
        Assert.Contains("7701234567", text);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
