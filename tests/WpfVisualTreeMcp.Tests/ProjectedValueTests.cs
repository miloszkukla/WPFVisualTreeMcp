using System.Text.Json;
using FluentAssertions;
using WpfVisualTreeMcp.Server.Controls;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class ProjectedValueTests
{
    [Fact]
    public void StringAvailabilityDoesNotHideAnInvalidConvertedValue()
    {
        using var document = JsonDocument.Parse("{\"Pending\":{\"missing\":true},\"WrongType\":{\"value\":1},\"Invalid\":{\"value\":\"unexpected\"}}");
        var pending = ProjectedValue<string?>.Capture(document.RootElement, "Pending", value => value.GetString());
        var wrongType = ProjectedValue<string?>.Capture(document.RootElement, "WrongType", value => value.GetString());
        var invalid = ProjectedValue<int>.Capture(document.RootElement, "Invalid", _ => throw new InvalidOperationException("invalid"));
        pending.HasStringValue.Should().BeFalse();
        wrongType.HasStringValue.Should().BeFalse();
        invalid.HasStringValue.Should().BeTrue();
        FluentActions.Invoking(() => invalid.Value).Should().ThrowExactly<InvalidOperationException>().WithMessage("invalid");
    }

    [Fact]
    public void CaptureDecodesOnceAndDoesNotRetainTheDocument()
    {
        var calls = 0;
        ProjectedValue<int> captured;
        using (var document = JsonDocument.Parse("{\"Count\":{\"value\":42}}"))
            captured = ProjectedValue<int>.Capture(document.RootElement, "Count", value => { calls++; return value.GetInt32(); });

        captured.Value.Should().Be(42);
        captured.Value.Should().Be(42);
        calls.Should().Be(1);
    }

    [Fact]
    public void CapturePreservesMissingUnreadableAndNullValuesUntilAccess()
    {
        using var document = JsonDocument.Parse("{\"Error\":{\"error\":\"getter failed\"},\"Empty\":{\"value\":null}}");
        var missing = ProjectedValue<string?>.Capture(document.RootElement, "Absent", value => value.GetString());
        var unreadable = ProjectedValue<string?>.Capture(document.RootElement, "Error", value => value.GetString());
        var empty = ProjectedValue<string?>.Capture(document.RootElement, "Empty", value => value.GetString());

        FluentActions.Invoking(() => missing.Value).Should().ThrowExactly<KeyNotFoundException>();
        FluentActions.Invoking(() => unreadable.Value).Should().ThrowExactly<InvalidOperationException>().WithMessage("*Error*getter failed*");
        empty.Value.Should().BeNull();
    }

    [Fact]
    public void CapturePreservesConversionFailuresUntilAccess()
    {
        using var document = JsonDocument.Parse("{\"Count\":{\"value\":\"not a number\"}}");
        var captured = ProjectedValue<int>.Capture(document.RootElement, "Count", value => value.GetInt32());

        FluentActions.Invoking(() => captured.Value).Should().ThrowExactly<InvalidOperationException>();
    }

    [Theory]
    [InlineData("{\"missing\":\"unexpected\",\"error\":42,\"value\":7}")]
    [InlineData("{\"missing\":{\"unexpected\":true},\"error\":[1,2],\"value\":7}")]
    public void CaptureIgnoresInvalidMetadataWhenValueIsPresent(string field)
    {
        using var document = JsonDocument.Parse("{\"Count\":" + field + "}");
        var captured = ProjectedValue<int>.Capture(document.RootElement, "Count", value => value.GetInt32());
        captured.Value.Should().Be(7);
    }

    [Fact]
    public void CapturedJsonValueSurvivesDisposalOfSourceDocument()
    {
        ProjectedValue<JsonElement> captured;
        using (var document = JsonDocument.Parse("{\"Entry\":{\"value\":{\"name\":\"test\"}}}"))
            captured = ProjectedValue<JsonElement>.Capture(document.RootElement, "Entry", value => value);
        captured.Value.GetProperty("name").GetString().Should().Be("test");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[]")]
    public void NonObjectFieldRemainsAnErrorOnAccess(string field)
    {
        using var document = JsonDocument.Parse("{\"Count\":" + field + "}");
        var captured = ProjectedValue<int>.Capture(document.RootElement, "Count", value => value.GetInt32());
        FluentActions.Invoking(() => captured.Value).Should().ThrowExactly<InvalidOperationException>();
    }
}
