using System.Text.Json;
using FluentAssertions;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

/// <summary>
/// Regression tests for the IPC request envelope. SerializeRequest historically
/// serialized the payload by its declared base type (IpcRequest), silently dropping
/// every derived-class property — filters never reached the Inspector and all
/// handle-based operations failed with "ElementHandle required".
/// </summary>
public class IpcSerializerTests
{
    [Fact]
    public void NotificationsAndInteractionMethodsSerializeAsTypedStrings()
    {
        using var notification = JsonDocument.Parse(IpcSerializer.Serialize(new PropertyChangedNotification()));
        notification.RootElement.GetProperty("notificationType").Deserialize<NotificationType>().Should().Be(NotificationType.PropertyChanged);
        using var bindingError = JsonDocument.Parse(IpcSerializer.Serialize(new BindingErrorNotification()));
        bindingError.RootElement.GetProperty("notificationType").Deserialize<NotificationType>().Should().Be(NotificationType.BindingError);

        var response = IpcSerializer.DeserializeResponse<ClickElementResponse>("{\"success\":true,\"method\":\"physicaldoubleclick\"}");
        response!.Method.Should().Be(InteractionMethod.PhysicalDoubleClick);
        Action numeric = () => IpcSerializer.DeserializeResponse<ClickElementResponse>("{\"method\":1}");
        Action unknown = () => IpcSerializer.DeserializeResponse<ClickElementResponse>("{\"method\":\"other\"}");
        numeric.Should().Throw<JsonException>();
        unknown.Should().Throw<JsonException>();
    }

    [Fact]
    public void RequestTypeRoundTripsAndRejectsUnknownOrNumericDiscriminators()
    {
        var request = new ClrReadRequest { ElementHandle = "element" };
        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);
        envelope!.Value.type.Should().Be(IpcRequestType.ReadClr);
        IpcSerializer.DeserializeRequestData<ClrReadRequest>(envelope.Value.data)!.ElementHandle.Should().Be("element");
        IpcSerializer.DeserializeRequest("{\"type\":\"readclr\",\"data\":{}}")!.Value.type.Should().Be(IpcRequestType.ReadClr);
        IpcSerializer.DeserializeRequest("{\"type\":\"other\",\"data\":{}}").Should().BeNull();
        IpcSerializer.DeserializeRequest("{\"type\":1,\"data\":{}}").Should().BeNull();
    }

    [Fact]
    public void ResourceAndExportOptionsRoundTripAndRejectUnknownValues()
    {
        var resource = IpcSerializer.DeserializeRequestData<GetResourcesRequest>(JsonDocument.Parse("{\"scope\":\"all\"}").RootElement);
        var export = IpcSerializer.DeserializeRequestData<ExportTreeRequest>(JsonDocument.Parse("{\"format\":\"xaml\"}").RootElement);
        resource!.Scope.Should().Be(ResourceScope.All);
        export!.Format.Should().Be(TreeExportFormat.Xaml);
        Action invalidScope = () => IpcSerializer.DeserializeRequestData<GetResourcesRequest>(JsonDocument.Parse("{\"scope\":\"unknown\"}").RootElement);
        Action invalidFormat = () => IpcSerializer.DeserializeRequestData<ExportTreeRequest>(JsonDocument.Parse("{\"format\":1}").RootElement);
        invalidScope.Should().Throw<JsonException>();
        invalidFormat.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(PreviousPropertySource.Binding)]
    [InlineData(PreviousPropertySource.Local)]
    [InlineData(PreviousPropertySource.Unset)]
    public void PreviousPropertySourceRoundTripsThroughIpc(PreviousPropertySource source)
    {
        var response = new SetPropertyResponse { PreviousSource = source };
        var json = IpcSerializer.SerializeResponse(response);
        IpcSerializer.DeserializeResponse<SetPropertyResponse>(json)!.PreviousSource.Should().Be(source);
    }

    [Fact]
    public void InspectionEnumsReadExistingStringValuesAndRejectInvalidValues()
    {
        var click = IpcSerializer.DeserializeRequestData<ClickElementRequest>(JsonDocument.Parse("{\"clickType\":\"right\"}").RootElement);
        var screenshot = IpcSerializer.DeserializeRequestData<CaptureScreenshotRequest>(JsonDocument.Parse("{\"mode\":\"screen\"}").RootElement);
        var wait = IpcSerializer.DeserializeRequestData<WaitForElementRequest>(JsonDocument.Parse("{\"condition\":\"hidden\"}").RootElement);

        click!.ClickType.Should().Be(ClickType.Right);
        screenshot!.Mode.Should().Be(ScreenshotMode.Screen);
        wait!.Condition.Should().Be(WaitCondition.Hidden);
        Action unknown = () => IpcSerializer.DeserializeRequestData<ClickElementRequest>(JsonDocument.Parse("{\"clickType\":\"other\"}").RootElement);
        Action number = () => IpcSerializer.DeserializeRequestData<CaptureScreenshotRequest>(JsonDocument.Parse("{\"mode\":1}").RootElement);
        unknown.Should().Throw<JsonException>();
        number.Should().Throw<JsonException>();
    }

    [Fact]
    public void SerializeRequest_IncludesDerivedClassProperties()
    {
        var request = new FindElementsRequest
        {
            TypeName = "Button",
            ElementName = "SubmitButton",
            Text = "Save",
            VisibleOnly = true,
            MaxResults = 25
        };

        var json = IpcSerializer.SerializeRequest(request);

        json.Should().Contain("\"typeName\":\"Button\"");
        json.Should().Contain("\"elementName\":\"SubmitButton\"");
        json.Should().Contain("\"text\":\"Save\"");
        json.Should().Contain("\"visibleOnly\":true");
        json.Should().Contain("\"maxResults\":25");
    }

    [Fact]
    public void SerializeRequest_IncludesElementHandle()
    {
        var request = new ClickElementRequest
        {
            ElementHandle = "elem_00000042",
            Physical = true
        };

        var json = IpcSerializer.SerializeRequest(request);

        json.Should().Contain("\"elementHandle\":\"elem_00000042\"");
        json.Should().Contain("\"physical\":true");
    }

    [Fact]
    public void SerializeRequest_RoundTripsThroughInspectorDeserialization()
    {
        var request = new FindElementsRequest
        {
            TypeName = "Button",
            Text = "Save",
            PropertyFilter = new Dictionary<string, string> { ["IsEnabled"] = "True" },
            VisibleOnly = true,
            MaxResults = 10
        };

        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);

        envelope.Should().NotBeNull();
        envelope!.Value.type.Should().Be(IpcRequestType.FindElements);

        var roundTripped = IpcSerializer.DeserializeRequestData<FindElementsRequest>(envelope.Value.data);
        roundTripped.Should().NotBeNull();
        roundTripped!.TypeName.Should().Be("Button");
        roundTripped.Text.Should().Be("Save");
        roundTripped.VisibleOnly.Should().BeTrue();
        roundTripped.MaxResults.Should().Be(10);
        roundTripped.PropertyFilter.Should().ContainKey("IsEnabled").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void SerializeRequest_RoundTripsSelectItemRequest()
    {
        var request = new SelectItemRequest
        {
            ElementHandle = "elem_00000007",
            ItemText = "Italia",
            Index = null
        };

        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);

        envelope!.Value.type.Should().Be(IpcRequestType.SelectItem);
        var roundTripped = IpcSerializer.DeserializeRequestData<SelectItemRequest>(envelope.Value.data);
        roundTripped!.ElementHandle.Should().Be("elem_00000007");
        roundTripped.ItemText.Should().Be("Italia");
        roundTripped.Index.Should().BeNull();
    }

    [Fact]
    public void SerializeRequest_RoundTripsClickTypeAndScreenshotMode()
    {
        var click = new ClickElementRequest { ElementHandle = "elem_1", ClickType = ClickType.Right };
        var clickJson = IpcSerializer.SerializeRequest(click);
        var clickBack = IpcSerializer.DeserializeRequestData<ClickElementRequest>(
            IpcSerializer.DeserializeRequest(clickJson)!.Value.data);
        clickBack!.ClickType.Should().Be(ClickType.Right);

        var shot = new CaptureScreenshotRequest { Mode = ScreenshotMode.Screen };
        var shotJson = IpcSerializer.SerializeRequest(shot);
        var shotBack = IpcSerializer.DeserializeRequestData<CaptureScreenshotRequest>(
            IpcSerializer.DeserializeRequest(shotJson)!.Value.data);
        shotBack!.Mode.Should().Be(ScreenshotMode.Screen);

        var fullShot = new CaptureScreenshotRequest { FullContent = true };
        var fullShotJson = IpcSerializer.SerializeRequest(fullShot);
        var fullShotBack = IpcSerializer.DeserializeRequestData<CaptureScreenshotRequest>(
            IpcSerializer.DeserializeRequest(fullShotJson)!.Value.data);
        fullShotBack!.FullContent.Should().BeTrue();
    }

    [Fact]
    public void SerializeRequest_RoundTripsExplainTriggersRequest()
    {
        var req = new ExplainTriggersRequest { ElementHandle = "elem_7", PropertyName = "Background" };
        var envelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(req));
        envelope!.Value.type.Should().Be(IpcRequestType.ExplainTriggers);
        var back = IpcSerializer.DeserializeRequestData<ExplainTriggersRequest>(envelope.Value.data);
        back!.ElementHandle.Should().Be("elem_7");
        back.PropertyName.Should().Be("Background");
    }

    [Fact]
    public void SerializeRequest_RoundTripsEvaluateBindingRequest()
    {
        var req = new EvaluateBindingRequest { ElementHandle = "elem_5", PropertyName = "Text" };
        var envelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(req));
        envelope!.Value.type.Should().Be(IpcRequestType.EvaluateBinding);
        var back = IpcSerializer.DeserializeRequestData<EvaluateBindingRequest>(envelope.Value.data);
        back!.ElementHandle.Should().Be("elem_5");
        back.PropertyName.Should().Be("Text");
    }

    [Fact]
    public void SerializeRequest_RoundTripsSnapshotAndDiffRequests()
    {
        var snap = new SnapshotRequest { ElementHandle = "elem_3", Label = "before", MaxDepth = 10 };
        var snapBack = IpcSerializer.DeserializeRequestData<SnapshotRequest>(
            IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(snap))!.Value.data);
        snapBack!.ElementHandle.Should().Be("elem_3");
        snapBack.Label.Should().Be("before");
        snapBack.MaxDepth.Should().Be(10);

        var diff = new DiffRequest { Before = "before", After = "after" };
        var diffEnvelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(diff));
        diffEnvelope!.Value.type.Should().Be(IpcRequestType.Diff);
        var diffBack = IpcSerializer.DeserializeRequestData<DiffRequest>(diffEnvelope.Value.data);
        diffBack!.Before.Should().Be("before");
        diffBack.After.Should().Be("after");
    }

    [Fact]
    public void SerializeRequest_RoundTripsSetAndRevertPropertyRequests()
    {
        var set = new SetPropertyRequest { ElementHandle = "elem_9", PropertyName = "Visibility", Value = "Collapsed" };
        var setBack = IpcSerializer.DeserializeRequestData<SetPropertyRequest>(
            IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(set))!.Value.data);
        setBack!.ElementHandle.Should().Be("elem_9");
        setBack.PropertyName.Should().Be("Visibility");
        setBack.Value.Should().Be("Collapsed");

        var revert = new RevertPropertyRequest { All = true };
        var revertEnvelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(revert));
        revertEnvelope!.Value.type.Should().Be(IpcRequestType.RevertProperty);
        var revertBack = IpcSerializer.DeserializeRequestData<RevertPropertyRequest>(revertEnvelope.Value.data);
        revertBack!.All.Should().BeTrue();
    }

    [Fact]
    public void SerializeRequest_RoundTripsWaitForElementRequest()
    {
        var request = new WaitForElementRequest
        {
            TypeName = "ProgressBar",
            Text = "Loading",
            Condition = WaitCondition.Hidden,
            TimeoutMs = 8000,
            PollIntervalMs = 200
        };

        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);

        envelope!.Value.type.Should().Be(IpcRequestType.WaitForElement);
        var back = IpcSerializer.DeserializeRequestData<WaitForElementRequest>(envelope.Value.data);
        back!.TypeName.Should().Be("ProgressBar");
        back.Text.Should().Be("Loading");
        back.Condition.Should().Be(WaitCondition.Hidden);
        back.TimeoutMs.Should().Be(8000);
        back.PollIntervalMs.Should().Be(200);
    }

    [Fact]
    public void SerializeRequest_RoundTripsSetTextRequest()
    {
        var request = new SetTextRequest
        {
            ElementHandle = "elem_00000010",
            Text = "hello@example.com"
        };

        var json = IpcSerializer.SerializeRequest(request);
        var envelope = IpcSerializer.DeserializeRequest(json);
        var roundTripped = IpcSerializer.DeserializeRequestData<SetTextRequest>(envelope!.Value.data);

        roundTripped!.ElementHandle.Should().Be("elem_00000010");
        roundTripped.Text.Should().Be("hello@example.com");
        roundTripped.Physical.Should().BeFalse();
    }
}
