using System.Text.Json;
using FluentAssertions;
using Moq;
using WpfVisualTreeMcp.Server;
using WpfVisualTreeMcp.Server.Controls;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class TypedBoundaryTests
{
    [Fact]
    public void PropertyFilterConvertsValuesToTextAndIgnoresNonObjects()
    {
        var filter = JsonSerializer.Deserialize<PropertyFilter>("{\"Number\":3,\"Active\":true,\"Text\":\"hello\",\"Empty\":null,\"Array\":[1,2],\"Object\":{\"x\":1}}");
        filter!["Number"].Should().Be("3");
        filter["Active"].Should().Be("True");
        filter["Text"].Should().Be("hello");
        filter["Empty"].Should().Be("");
        filter["Array"].Should().Be("[1,2]");
        filter["Object"].Should().Be("{\"x\":1}");
        JsonSerializer.Deserialize<PropertyFilter>(JsonSerializer.Serialize(filter)).Should().BeEquivalentTo(filter);
        JsonSerializer.Deserialize<PropertyFilter>("[1,2]").Should().BeNull();
        JsonSerializer.Deserialize<PropertyFilter>("42").Should().BeNull();
    }

    [Fact]
    public void DiagnosticSectionsSerializeWithoutChangingTheirShape()
    {
        var result = new DiagnosticCaptureResult
        {
            Generation = "gen", ProcessId = 42,
            Tree = CaptureSection<VisualTreeResult>.Succeeded(new VisualTreeResult()),
            Properties = CaptureSection<ElementPropertiesResult>.Failed("not found"),
            Bindings = CaptureSection<BindingsResult>.Succeeded(new BindingsResult()),
            DataContext = CaptureSection<ClrReadResponse>.Succeeded(new ClrReadResponse()),
            Screenshot = CaptureSection<ScreenshotResult>.Succeeded(new ScreenshotResult())
        };
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("generation").GetString().Should().Be("gen");
        json.GetProperty("processId").GetInt32().Should().Be(42);
        json.GetProperty("properties").GetProperty("error").GetString().Should().Be("not found");
        json.GetProperty("tree").TryGetProperty("value", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TypedProjectionDistinguishesAbsentFromExplicitNull()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.SetupSequence(x => x.ReadTypedAsync("button", "", "Value"))
            .ReturnsAsync(new ClrReadEnvelope { Kind = ClrReadKind.Object })
            .ReturnsAsync(new ClrReadEnvelope { Kind = ClrReadKind.Object, Value = JsonSerializer.SerializeToElement<object?>(null) });
        var button = await new WpfControls(transport.Object).FindAsync<Button>();
        await FluentActions.Invoking(() => button.ReadProjectionAsync("", "Value", value => value.ValueKind)).Should().ThrowAsync<KeyNotFoundException>();
        (await button.ReadProjectionAsync("", "Value", value => value.ValueKind)).Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ProjectedWrappersRetainNullAndErrorShapes()
    {
        IpcSerializer.Serialize(new ClrProjectedValue { Value = null }).Should().Be("{\"value\":null}");
        IpcSerializer.Serialize(new ClrProjectedMissing { Missing = true, Error = "absent" }).Should().Be("{\"missing\":true,\"error\":\"absent\"}");
        IpcSerializer.Serialize(new ClrObjectSummary { Type = "Example", Count = null }).Should().Be("{\"type\":\"Example\",\"count\":null}");
    }
    [Fact]
    public void DiagnosticResponseErrorIsNotConfusedWithCaptureFailure()
    {
        var section = JsonSerializer.Deserialize<CaptureSection<ClrReadResponse>>("{\"success\":false,\"error\":\"read failed\"}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        section!.Error.Should().BeNull();
        section.Value!.Error.Should().Be("read failed");
        var failed = JsonSerializer.Deserialize<CaptureSection<ClrReadResponse>>("{\"error\":\"capture failed\"}");
        failed!.Error.Should().Be("capture failed");
    }

    [Fact]
    public void ResultCountsDistinguishAbsentFromExplicitZero()
    {
        var absent = IpcSerializer.Deserialize<FindElementsPayload>("{\"elements\":[]}")!;
        var zero = IpcSerializer.Deserialize<FindElementsPayload>("{\"elements\":[],\"count\":0}")!;
        absent.HasCount.Should().BeFalse();
        zero.HasCount.Should().BeTrue();
        zero.Count.Should().Be(0);
        IpcSerializer.Serialize(zero).Should().NotContain("hasCount");
    }

}
