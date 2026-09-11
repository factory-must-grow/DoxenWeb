using System.Text.Json;
using Doxen.Engine;

namespace Doxen.Engine.Tests;

public class AnswersFileTests
{
    [Fact]
    public void Read_WellFormedFile_ReturnsSharedAndDatasets()
    {
        var json = """
            {
                "doxenVersion": 1,
                "shared": { "org": { "name": "ООО «Рога и копыта»" } },
                "datasets": [
                    { "title": "Кузнецова М. А.", "values": { "agent": { "fullName": "Кузнецова Мария Алексеевна" } } }
                ]
            }
            """;

        var result = AnswersFile.Read(json);

        Assert.True(result.Success);
        Assert.False(result.IsNewerVersion);

        var shared = result.Root.GetProperty("shared");
        Assert.Equal("ООО «Рога и копыта»", shared.GetProperty("org").GetProperty("name").GetString());

        var datasets = result.Root.GetProperty("datasets");
        Assert.Equal(1, datasets.GetArrayLength());
        Assert.Equal("Кузнецова М. А.", datasets[0].GetProperty("title").GetString());
    }

    // "documents" — устаревшее имя того же поля, читается как datasets.
    [Fact]
    public void Read_LegacyDocumentsField_IsReadAsDatasets()
    {
        var json = """{"documents":[{"values":{"a":1}}]}""";

        var result = AnswersFile.Read(json);

        Assert.True(result.Success);
        Assert.Equal(1, result.Root.GetProperty("datasets").GetArrayLength());
    }

    // Массива нет вовсе, значения лежат прямо в корне — считаем одним набором.
    [Fact]
    public void Read_BareValuesAtRoot_IsTreatedAsSingleDataset()
    {
        var json = """{"org":{"name":"ООО"}}""";

        var result = AnswersFile.Read(json);

        Assert.True(result.Success);
        var datasets = result.Root.GetProperty("datasets");
        Assert.Equal(1, datasets.GetArrayLength());
        Assert.Equal("ООО", datasets[0].GetProperty("values").GetProperty("org").GetProperty("name").GetString());
    }

    [Fact]
    public void Read_EmptyDatasets_SucceedsAsIfNoFile()
    {
        var json = """{"shared":{},"datasets":[]}""";

        var result = AnswersFile.Read(json);

        Assert.True(result.Success);
        Assert.Equal(0, result.Root.GetProperty("datasets").GetArrayLength());
    }

    [Fact]
    public void Read_MalformedJson_ReturnsErrorWithLineNumber()
    {
        var json = "{\n  \"shared\": {\n";

        var result = AnswersFile.Read(json);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("строке", result.Error);
    }

    [Fact]
    public void Read_JsonArray_IsNotAnAnswersFile()
    {
        var result = AnswersFile.Read("[1,2,3]");

        Assert.False(result.Success);
        Assert.Contains("не похоже на файл ответов", result.Error);
    }

    [Fact]
    public void Read_NewerVersion_IsFlagged()
    {
        var json = """{"doxenVersion":99,"shared":{},"datasets":[]}""";

        var result = AnswersFile.Read(json);

        Assert.True(result.Success);
        Assert.True(result.IsNewerVersion);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsSharedAndDatasets()
    {
        var shared = JsonDocument.Parse("""{"org":{"name":"ООО «Рога и копыта»"}}""").RootElement;
        var values = JsonDocument.Parse("""{"agent":{"fullName":"Кузнецова Мария Алексеевна"}}""").RootElement;

        var json = AnswersFile.Write(shared, new[] { ((string?)"Кузнецова М. А.", values) });

        // Кириллица не экранируется — файл предназначен для правки руками.
        Assert.Contains("Кузнецова", json);
        Assert.DoesNotContain("\\u", json);

        var result = AnswersFile.Read(json);
        Assert.True(result.Success);
        Assert.Equal("Кузнецова М. А.", result.Root.GetProperty("datasets")[0].GetProperty("title").GetString());
    }
}
