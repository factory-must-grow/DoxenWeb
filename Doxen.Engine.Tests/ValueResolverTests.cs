using System.Text.Json;
using Doxen.Engine;
using Doxen.Engine.Models;

namespace Doxen.Engine.Tests;

public class ValueResolverTests
{
    // Тест 7: путь в объект ({{org.bank}} при вложенном объекте) →
    // PathIsObject с перечислением ключей.
    [Fact]
    public void PathToObject_ReturnsPathIsObjectWithAvailableKeys()
    {
        var root = Parse("""{"org":{"bank":{"bankName":"ПАО «Сбербанк»","bik":"044525225"}}}""");

        var result = ValueResolver.Resolve(root, "org.bank");

        Assert.Equal(ResolveStatus.PathIsObject, result.Status);
        Assert.NotNull(result.AvailableKeys);
        Assert.Equal(new[] { "bankName", "bik" }, result.AvailableKeys);
        Assert.Contains("org.bank", result.Message);
    }

    [Fact]
    public void MissingSegment_ReturnsMissingValue()
    {
        var root = Parse("""{"org":{"name":"ООО «Рога и копыта»"}}""");

        var result = ValueResolver.Resolve(root, "org.inn");

        Assert.Equal(ResolveStatus.MissingValue, result.Status);
    }

    [Fact]
    public void NullValue_ReturnsMissingValue()
    {
        var root = Parse("""{"org":{"inn":null}}""");

        var result = ValueResolver.Resolve(root, "org.inn");

        Assert.Equal(ResolveStatus.MissingValue, result.Status);
    }

    [Fact]
    public void PathContinuesPastScalar_ReturnsWrongType()
    {
        var root = Parse("""{"org":{"name":"ООО «Рога и копыта»"}}""");

        var result = ValueResolver.Resolve(root, "org.name.unexpected");

        Assert.Equal(ResolveStatus.WrongType, result.Status);
        Assert.Contains("org.name", result.Message);
    }

    // Тест 13: путь упирается в массив → понятная ошибка, а не исключение.
    [Fact]
    public void PathHitsArray_ReturnsWrongTypeWithClearMessage()
    {
        var root = Parse("""{"positions":[1,2,3]}""");

        var result = ValueResolver.Resolve(root, "positions");

        Assert.Equal(ResolveStatus.WrongType, result.Status);
        Assert.Contains("positions", result.Message);
        Assert.Contains("список", result.Message);
    }

    [Fact]
    public void ArrayInTheMiddleOfPath_ReturnsWrongType()
    {
        var root = Parse("""{"positions":[{"name":"первая"}]}""");

        var result = ValueResolver.Resolve(root, "positions.name");

        Assert.Equal(ResolveStatus.WrongType, result.Status);
    }

    [Theory]
    [InlineData("""{"count":3}""", "3")]
    [InlineData("""{"count":3.5}""", "3.5")]
    [InlineData("""{"count":3.0}""", "3")]
    public void Number_FormattedWithoutExponentOrTrailingZeros(string json, string expected)
    {
        var root = Parse(json);

        var result = ValueResolver.Resolve(root, "count");

        Assert.Equal(ResolveStatus.Resolved, result.Status);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("""{"flag":true}""", "Да")]
    [InlineData("""{"flag":false}""", "Нет")]
    public void Bool_FormattedAsRussianYesNo(string json, string expected)
    {
        var root = Parse(json);

        var result = ValueResolver.Resolve(root, "flag");

        Assert.Equal(ResolveStatus.Resolved, result.Status);
        Assert.Equal(expected, result.Value);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
