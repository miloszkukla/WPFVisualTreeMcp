using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class ClrReaderTests
{
    private sealed class Model
    {
        public List<Entry> Entries { get; } = new() { new("A", "Accepted"), new("B", "Rejected"), new("C", "ToBeReviewed") };
        public Model? Child => null;
        public string Throws => throw new InvalidOperationException("getter failed");
        public int Calls;
        public void Mutate() { Calls++; }
    }
    private sealed class Entry
    {
        public Entry(string code, string status) { Code = code; CodeStatus = status; }
        public string Code { get; }
        public string CodeStatus { get; }
    }

    [Fact]
    public void ReadCollectionProjectsRequestedFieldsAndReportsPaging()
    {
        using var result = JsonDocument.Parse(new ClrReader().Read(new Model(), "Entries", new[] { "Code", "CodeStatus" }, 1, 1));
        var root = result.RootElement;
        Assert.Equal(ClrReadKind.Collection, root.GetProperty("kind").Deserialize<ClrReadKind>());
        Assert.Equal(3, root.GetProperty("totalCount").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.Equal("B", root.GetProperty("items")[0].GetProperty("Code").GetProperty("value").GetString());
        Assert.Equal("Rejected", root.GetProperty("items")[0].GetProperty("CodeStatus").GetProperty("value").GetString());
    }

    [Fact]
    public void NestedIndexedPathReadsScalarWithoutTraversingObjectGraph()
    {
        using var result = JsonDocument.Parse(new ClrReader().Read(new Model(), "Entries[2].Code", Array.Empty<string>(), 0, 1));
        Assert.Equal(ClrReadKind.Value, result.RootElement.GetProperty("kind").Deserialize<ClrReadKind>());
        Assert.Equal("C", result.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public void PointerProjectionRejectsNonUiElementsWithoutCallingModelMethods()
    {
        var model = new Model();
        using var result = JsonDocument.Parse(new ClrReader().Read(model, "", new[] { "$pointer" }, 0, 1));

        Assert.True(result.RootElement.GetProperty("value").GetProperty("$pointer").GetProperty("missing").GetBoolean());
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public void NullMissingAndThrowingMembersRemainDistinct()
    {
        using var result = JsonDocument.Parse(new ClrReader().Read(new Model(), "", new[] { "Child", "Missing", "Throws", "Entries" }, 0, 1));
        Assert.Equal(ClrReadKind.Object, result.RootElement.GetProperty("kind").Deserialize<ClrReadKind>());
        var root = result.RootElement.GetProperty("value");
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Child").GetProperty("value").ValueKind);
        Assert.True(root.GetProperty("Missing").GetProperty("missing").GetBoolean());
        Assert.Equal("getter failed", root.GetProperty("Throws").GetProperty("error").GetString());
        Assert.Equal(3, root.GetProperty("Entries").GetProperty("value").GetProperty("count").GetInt32());
    }

    [Fact]
    public void MembersListSignaturesWithoutCallingGettersOrMethods()
    {
        var model = new Model();
        using var result = JsonDocument.Parse(new ClrReader().Read(model, "", Array.Empty<string>(), 0, 100, true));
        Assert.Equal(ClrReadKind.Members, result.RootElement.GetProperty("kind").Deserialize<ClrReadKind>());
        var member = result.RootElement.GetProperty("members").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "Mutate");
        var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(null, false) } };
        Assert.Equal(System.Reflection.MemberTypes.Method, member.GetProperty("kind").Deserialize<System.Reflection.MemberTypes>(options));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public void MembersOnNullReportNullKind()
    {
        using var result = JsonDocument.Parse(new ClrReader().Read(new Model(), "Child", Array.Empty<string>(), 0, 1, true));
        Assert.Equal(ClrReadKind.Null, result.RootElement.GetProperty("kind").Deserialize<ClrReadKind>());
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(10001, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 201)]
    public void InvalidPagingBoundsAreRejected(int skip, int take) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClrReader().Read(new Model(), "Entries", Array.Empty<string>(), skip, take));

    [Theory]
    [InlineData("Mutate()")]
    [InlineData("Entries[-1]")]
    [InlineData("Entries[0];Mutate")]
    public void MethodsAndExpressionsAreRejectedAsPaths(string path) =>
        Assert.Throws<ArgumentException>(() => ClrReader.Resolve(new Model(), path));

    [Fact]
    public void OutOfRangeIndexReportsAnErrorInsteadOfReadingAnotherEntry() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ClrReader.Resolve(new Model(), "Entries[3]"));

    [Fact]
    public void EndOfCollectionHasNoFalseTruncation()
    {
        using var result = JsonDocument.Parse(new ClrReader().Read(new Model(), "Entries", new[] { "Code" }, 3, 1));
        Assert.Equal(0, result.RootElement.GetProperty("returned").GetInt32());
        Assert.False(result.RootElement.GetProperty("truncated").GetBoolean());
    }
}
