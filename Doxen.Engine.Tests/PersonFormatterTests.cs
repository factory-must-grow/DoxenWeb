using System.Text.Json;
using Doxen.Engine;
using Doxen.Engine.Models;

namespace Doxen.Engine.Tests;

// Тест 6: initialsEnd для полного ФИО, для одной фамилии, для пустого
// объекта. Плюс initials/initialsStart и разбор ошибок.
public class PersonFormatterTests
{
    private static readonly JsonElement FullName =
        Parse("""{"person":{"firstName":"Иван","middleName":"Петрович","lastName":"Сидоров"}}""");

    [Fact]
    public void InitialsEnd_FullName_ReturnsLastNameFirst()
    {
        var result = PersonFormatter.Format("initialsEnd", FullName, "person");

        Assert.Equal(ResolveStatus.Resolved, result.Status);
        Assert.Equal("Сидоров И. П.", result.Value);
    }

    [Fact]
    public void InitialsEnd_OnlyLastName_ReturnsJustLastName()
    {
        var root = Parse("""{"person":{"lastName":"Сидоров"}}""");

        var result = PersonFormatter.Format("initialsEnd", root, "person");

        Assert.Equal(ResolveStatus.Resolved, result.Status);
        Assert.Equal("Сидоров", result.Value);
    }

    [Fact]
    public void InitialsEnd_EmptyObject_ReturnsMissingValue()
    {
        var root = Parse("""{"person":{}}""");

        var result = PersonFormatter.Format("initialsEnd", root, "person");

        Assert.Equal(ResolveStatus.MissingValue, result.Status);
    }

    [Fact]
    public void Initials_ReturnsFirstAndMiddleOnly()
    {
        var result = PersonFormatter.Format("initials", FullName, "person");

        Assert.Equal(ResolveStatus.Resolved, result.Status);
        Assert.Equal("И. П.", result.Value);
    }

    [Fact]
    public void InitialsStart_ReturnsInitialsThenLastName()
    {
        var result = PersonFormatter.Format("initialsStart", FullName, "person");

        Assert.Equal(ResolveStatus.Resolved, result.Status);
        Assert.Equal("И. П. Сидоров", result.Value);
    }

    [Fact]
    public void ArgumentResolvesToScalar_ReturnsWrongType()
    {
        var root = Parse("""{"person":"Иван Сидоров"}""");

        var result = PersonFormatter.Format("initialsEnd", root, "person");

        Assert.Equal(ResolveStatus.WrongType, result.Status);
    }

    [Fact]
    public void UnknownFunction_ReturnsWrongTypeListingAvailable()
    {
        var result = PersonFormatter.Format("shout", FullName, "person");

        Assert.Equal(ResolveStatus.WrongType, result.Status);
        Assert.Contains("initials", result.Message);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
