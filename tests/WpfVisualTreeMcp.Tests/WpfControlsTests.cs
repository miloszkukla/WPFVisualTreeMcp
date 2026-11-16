using System.Text.Json;
using FluentAssertions;
using Moq;
using WpfVisualTreeMcp.Server.Controls;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class WpfControlsTests
{
    [Fact]
    public async Task ReadValueUsesTheRequestedPathAndReportsUnreadableFields()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.SetupSequence(x => x.ReadAsync("button", "Column", "DisplayIndex"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { DisplayIndex = new { value = 3 } } }))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { DisplayIndex = new { error = "getter failed" } } }));
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        (await button.ReadValueAsync("Column", "DisplayIndex", value => value.GetInt32())).Should().Be(3);
        await FluentActions.Invoking(() => button.ReadValueAsync("Column", "DisplayIndex", value => value.GetInt32()))
            .Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("*DisplayIndex*getter failed*");
        transport.VerifyAll();
    }

    [Fact]
    public async Task FindUsesTheProxyTypeAndPreservesTheScopeAndSelector()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "FrameworkElement", "panel", null, null, true, 10000)).ReturnsAsync("panel-handle");
        transport.Setup(x => x.FindAsync("Save", "Button", null, null, "panel-handle", true, 2500)).ReturnsAsync("button-handle");
        transport.Setup(x => x.ClickAsync("button-handle", true, "double")).Returns(Task.CompletedTask);
        var controls = new WpfControls(transport.Object);

        var panel = await controls.FindAsync<FrameworkElement>(name: "panel");
        var button = await panel.FindAsync<Button>(automationId: "Save", timeoutMs: 2500);
        await button.PhysicalClickAsync("double");

        transport.VerifyAll();
    }

    [Fact]
    public async Task SearchRejectsRootFromAnotherSession()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "FrameworkElement", null, null, null, true, 10000)).ReturnsAsync("root");
        var root = await new WpfControls(transport.Object).FindAsync<FrameworkElement>();
        var other = new WpfControls(transport.Object);

        var action = () => other.FindAsync<Button>(root: root);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*another inspector session*");
        transport.VerifyAll();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UntypedSelectorVerifiesTheChosenControlAfterTheSearch(bool correctType)
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync("id", null, null, null, null, true, 10000)).ReturnsAsync("selected");
        transport.Setup(x => x.IsTypeAsync("selected", "Button")).ReturnsAsync(correctType);
        var controls = new WpfControls(transport.Object);

        if (correctType) (await controls.FindAsync<Button>(automationId: "id", matchType: false)).Should().NotBeNull();
        else await controls.Invoking(x => x.FindAsync<Button>(automationId: "id", matchType: false))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not match Button*");
        transport.VerifyAll();
    }

    [Fact]
    public async Task UntypedSelectorPreservesAmbiguityBeforeCheckingType()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync("id", null, null, null, null, true, 10000))
            .ThrowsAsync(new InvalidOperationException("Ambiguous selector id: 2 matches."));
        var controls = new WpfControls(transport.Object);

        await controls.Invoking(x => x.FindAsync<Button>(automationId: "id", matchType: false))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Ambiguous selector*");
        transport.VerifyAll();
    }

    [Fact]
    public async Task ProxyCannotWrapAnElementLocatedAsAnotherType()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "CheckBox", null, null, null, true, 10000)).ReturnsAsync("check");
        var checkBox = await new WpfControls(transport.Object).FindAsync<CheckBox>();

        var action = () => new Button(checkBox);

        action.Should().Throw<ArgumentException>().WithMessage("*not located as Button*");
    }

    [Fact]
    public async Task TypedPropertyReadsPreserveNullAndRejectMissingMembers()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "CheckBox", null, null, null, true, 10000)).ReturnsAsync("checkbox");
        transport.Setup(x => x.ReadAsync("checkbox", "", "IsChecked"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { IsChecked = new { value = (bool?)null } } }));
        transport.Setup(x => x.ReadAsync("checkbox", "", "IsEnabled"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { IsEnabled = new { missing = true } } }));
        var checkBox = await new WpfControls(transport.Object).FindAsync<CheckBox>();

        (await checkBox.IsCheckedAsync()).Should().BeNull();
        var action = () => checkBox.IsEnabledAsync();
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IsEnabled*");
    }

    [Fact]
    public async Task ElementStateReadsVisibilityAndEnabledTogether()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.Setup(x => x.ReadAsync("button", "", "IsVisible,IsEnabled"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { IsVisible = new { value = true }, IsEnabled = new { value = false } } }));
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        var state = await button.StateAsync();

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeFalse();
        transport.VerifyAll();
    }

    [Fact]
    public async Task HeaderTextReturnsOnlyStringHeaders()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "TabItem", null, null, null, true, 10000)).ReturnsAsync("tab");
        transport.SetupSequence(x => x.ReadAsync("tab", "", "Header"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Header = new { value = "Details" } } }))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Header = new { type = "SomeHeader" } } }));
        var tab = await new WpfControls(transport.Object).FindAsync<TabItem>();

        (await tab.HeaderTextAsync()).Should().Be("Details");
        (await tab.HeaderTextAsync()).Should().BeNull();
    }

    [Fact]
    public async Task BindingsAreInspectedOnTheSelectedElement()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Grid", null, null, null, true, 10000)).ReturnsAsync("grid");
        transport.Setup(x => x.GetBindingsAsync("grid"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { path = "QsFilterModulesFound" }));
        var grid = await new WpfControls(transport.Object).FindAsync<Grid>();

        (await grid.BindingsAsync()).GetProperty("path").GetString().Should().Be("QsFilterModulesFound");
        transport.VerifyAll();
    }

    [Fact]
    public async Task PropertiesAndRuntimeTypeUseDedicatedInspectionMethods()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Grid", null, null, null, true, 10000)).ReturnsAsync("grid");
        transport.Setup(x => x.GetPropertiesAsync("grid"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { Name = "Example" }));
        transport.Setup(x => x.GetTreeAsync("grid"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { Root = new { TypeName = "Example.Grid" } }));
        var grid = await new WpfControls(transport.Object).FindAsync<Grid>();

        (await grid.PropertiesAsync()).GetProperty("Name").GetString().Should().Be("Example");
        (await grid.RuntimeTypeNameAsync()).Should().Be("Example.Grid");
        transport.VerifyAll();
    }

    [Fact]
    public async Task FailedObservationsRecordTheCallingMethodAndDistinctSearchStep()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WpfSessionTrace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var session = new WpfSession(Environment.ProcessId, directory);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetTreeAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.SearchAsync("handle", "text"));

            var operations = File.ReadAllLines(Path.Combine(directory, "commands.jsonl"))
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line).GetProperty("operation").GetString()).ToArray();
            operations.Should().Equal("GetTreeAsync", "SetTextAsync");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ContentTextReturnsOnlyStringContent()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "CheckBox", null, null, null, true, 10000)).ReturnsAsync("check");
        transport.SetupSequence(x => x.ReadAsync("check", "", "Content"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Content = new { value = "Show codes" } } }))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Content = new { type = "SomeContent" } } }));
        var check = await new WpfControls(transport.Object).FindAsync<CheckBox>();

        (await check.ContentTextAsync()).Should().Be("Show codes");
        (await check.ContentTextAsync()).Should().BeNull();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("text", false)]
    public async Task EmptyTooltipDistinguishesNullAndEmptyFromText(string? text, bool expected)
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.Setup(x => x.ReadAsync("button", "", "ToolTip"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { ToolTip = new { value = text } } }));
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        (await button.HasEmptyToolTipAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task TooltipOpenStateUsesTheBooleanValue()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "ToolTip", null, null, null, true, 10000)).ReturnsAsync("tip");
        transport.Setup(x => x.SetPropertyAsync("tip", "IsOpen", "true")).Returns(Task.CompletedTask);
        transport.Setup(x => x.SetPropertyAsync("tip", "IsOpen", "false")).Returns(Task.CompletedTask);
        var tip = await new WpfControls(transport.Object).FindAsync<ToolTip>();

        await tip.SetOpenAsync(true);
        await tip.SetOpenAsync(false);

        transport.VerifyAll();
    }

    [Fact]
    public async Task TreeItemExpansionUsesTheSelectedItem()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "TreeViewItem", null, null, null, true, 10000)).ReturnsAsync("item");
        transport.Setup(x => x.SetPropertyAsync("item", "IsExpanded", "True")).Returns(Task.CompletedTask);
        var item = await new WpfControls(transport.Object).FindAsync<TreeViewItem>();

        await item.SetExpandedAsync(true);

        transport.VerifyAll();
    }

    [Fact]
    public async Task RenderedTextStateIncludesVisibilityAndRejectsTruncation()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "TextBlock", null, null, null, true, 10000)).ReturnsAsync("text");
        transport.SetupSequence(x => x.ReadAsync("text", "", "$renderedText,IsVisible"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["value"] = new Dictionary<string, object> { ["$renderedText"] = new { value = "shown" }, ["IsVisible"] = new { value = true } } }))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["value"] = new Dictionary<string, object> { ["$renderedText"] = new { value = new { truncated = true } } } }));
        var block = await new WpfControls(transport.Object).FindAsync<TextBlock>();

        var state = await block.RenderedStateAsync();
        state.Text.Should().Be("shown");
        state.IsVisible.Should().BeTrue();
        await block.Invoking(x => x.RenderedStateAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*truncated*");
    }

    [Fact]
    public async Task ConsumerDefinedControlUsesItsDeclaredTargetType()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "C1DataGrid", null, null, null, false, 10000)).ReturnsAsync("grid");
        var grid = await new WpfControls(transport.Object).FindAsync<CustomGrid>(visibleOnly: false);

        grid.Should().NotBeNull();
        transport.VerifyAll();
    }

    [Fact]
    public async Task TextBoxAndRichTextBoxShareTheWpfTextBoxBase()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "TextBox", null, null, null, true, 10000)).ReturnsAsync("text");
        transport.Setup(x => x.FindAsync(null, "RichTextBox", null, null, null, true, 10000)).ReturnsAsync("rich");
        var controls = new WpfControls(transport.Object);

        (await controls.FindAsync<TextBox>()).Should().BeAssignableTo<TextBoxBase>();
        (await controls.FindAsync<RichTextBox>()).Should().BeAssignableTo<TextBoxBase>();
    }

    [Fact]
    public async Task ScopedFindAllReturnsTypedControlsWithSearchMetadata()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "FrameworkElement", null, null, null, true, 10000)).ReturnsAsync("root");
        transport.Setup(x => x.FindAllAsync("Button", null, "Save", "root", false))
            .ReturnsAsync(new (string, string?, string?, string?)[] { ("button", "save", "Save tooltip", "save-id") });
        transport.Setup(x => x.RenderedTextAsync("button")).ReturnsAsync("Save");
        var root = await new WpfControls(transport.Object).FindAsync<FrameworkElement>();

        var buttons = await root.FindAllAsync<Button>(text: "Save", visibleOnly: false);

        buttons.Should().ContainSingle();
        buttons[0].MatchedAutomationId.Should().Be("save-id");
        buttons[0].MatchedText.Should().Be("Save tooltip");
        (await buttons[0].RenderedTextAsync()).Should().Be("Save");
        transport.VerifyAll();
    }

    [Fact]
    public async Task ElementIdentityIncludesItsOwningSession()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("same-handle");
        var session = new WpfControls(transport.Object);
        var first = await session.FindAsync<Button>();
        var same = await session.FindAsync<Button>();
        var otherSession = await new WpfControls(transport.Object).FindAsync<Button>();

        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Should().NotBe(otherSession);
        new[] { first, same, otherSession }.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task TypedScrollingPreservesTheOriginalControlAndAction()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "ScrollViewer", null, null, null, true, 10000)).ReturnsAsync("scroll");
        transport.Setup(x => x.FindAsync(null, "ListBox", null, null, null, true, 10000)).ReturnsAsync("list");
        transport.Setup(x => x.ScrollViewerAsync("scroll", ScrollViewerAction.PageDown)).Returns(Task.CompletedTask);
        transport.Setup(x => x.ScrollItemIntoViewAsync("list", 3)).Returns(Task.CompletedTask);
        var controls = new WpfControls(transport.Object);

        await (await controls.FindAsync<ScrollViewer>()).ScrollAsync(ScrollViewerAction.PageDown);
        await (await controls.FindAsync<ListBox>()).ScrollItemIntoViewAsync(3);

        transport.VerifyAll();
    }

    [Fact]
    public async Task ComboBoxOperationsAndLayoutWaitUseTheSelectedControls()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "ComboBox", null, null, null, true, 10000)).ReturnsAsync("combo");
        transport.Setup(x => x.FindAsync(null, "ScrollViewer", "ItemsList", null, "combo", true, 10000)).ReturnsAsync("popup");
        transport.Setup(x => x.ReadAsync("combo", "", "SelectedIndex")).ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { SelectedIndex = new { value = 2 } } }));
        transport.Setup(x => x.ReadAsync("combo", "", "IsDropDownOpen")).ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { IsDropDownOpen = new { value = true } } }));
        transport.Setup(x => x.SetPropertyAsync("combo", "SelectedIndex", "2")).Returns(Task.CompletedTask);
        transport.Setup(x => x.SetPropertyAsync("combo", "IsDropDownOpen", "False")).Returns(Task.CompletedTask);
        transport.Setup(x => x.WaitForStableBoundsAsync("popup")).Returns(Task.CompletedTask);
        var combo = await new WpfControls(transport.Object).FindAsync<ComboBox>();

        await combo.SetSelectedIndexAsync(2);
        (await combo.SelectedIndexAsync()).Should().Be(2);
        (await combo.IsDropDownOpenAsync()).Should().BeTrue();
        await combo.SetDropDownOpenAsync(false);
        await (await combo.FindAsync<ScrollViewer>(name: "ItemsList")).WaitForStableBoundsAsync();

        transport.VerifyAll();
    }

    [Fact]
    public void WpfControlProxiesFollowTheRelevantInheritanceHierarchy()
    {
        typeof(DataGrid).IsAssignableTo(typeof(MultiSelector)).Should().BeTrue();
        typeof(MultiSelector).IsAssignableTo(typeof(Selector)).Should().BeTrue();
        typeof(FrameworkElement).IsAssignableTo(typeof(UIElement)).Should().BeTrue();
        typeof(UIElement).IsAssignableTo(typeof(Visual)).Should().BeTrue();
        typeof(Visual).IsAssignableTo(typeof(DependencyObject)).Should().BeTrue();
    }

    [Fact]
    public async Task ScrollViewportStateIsReadAsOneObservation()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "ScrollViewer", null, null, null, true, 10000)).ReturnsAsync("scroll");
        transport.Setup(x => x.ReadAsync("scroll", "", "VerticalOffset,ScrollableHeight,ViewportHeight,IsMeasureValid,IsArrangeValid"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { VerticalOffset = new { value = 4.0 }, ScrollableHeight = new { value = 10.0 }, ViewportHeight = new { value = 6.0 }, IsMeasureValid = new { value = true }, IsArrangeValid = new { value = false } } }));
        var scroll = await new WpfControls(transport.Object).FindAsync<ScrollViewer>();

        var state = await scroll.ViewportStateAsync();

        state.Should().Be(new ScrollState(4, 10, 6, true, false));
        transport.VerifyAll();
    }

    [Fact]
    public async Task LookupPropagatesTransportFailuresAndCancellation()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000))
            .ThrowsAsync(new OperationCanceledException());
        var action = () => new WpfControls(transport.Object).FindAsync<Button>();

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StaleElementErrorsPropagateWithoutReacquiringAnotherControl()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "TextBox", "input", null, null, true, 10000)).ReturnsAsync("stale");
        transport.Setup(x => x.ReadAsync("stale", "", "Text"))
            .ThrowsAsync(new InvalidOperationException("Element handle 'stale' not found"));
        var input = await new WpfControls(transport.Object).FindAsync<TextBox>(name: "input");

        var read = () => input.TextAsync();

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("Element handle 'stale' not found");
        transport.Verify(x => x.FindAsync(null, "TextBox", "input", null, null, true, 10000), Times.Once);
    }

    [Fact]
    public async Task LookupPropagatesAnAmbiguousMatch()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000))
            .ThrowsAsync(new InvalidOperationException("Ambiguous selector Button: 2 matches."));
        var action = () => new WpfControls(transport.Object).FindAsync<Button>();

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Ambiguous selector*2 matches*");
    }

    [WpfType("C1DataGrid")]
    public sealed class CustomGrid(WpfElement element) : Control(element);

    [Fact]
    public async Task PointerStatePreservesMouseHitsGeometryAndTraceShape()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        using var json = JsonDocument.Parse("""
            {"value":{"$pointer":{"value":{
                "target":{"type":"Button","name":"save","automationId":"save-id"},
                "root":null,"windowHandle":123,"position":{"x":-2.5,"y":4},"center":{"x":10,"y":12},"centerInRoot":null,
                "isMouseOver":false,"isMeasureValid":true,"isArrangeValid":false,
                "directlyOver":{"element":{"type":"Popup","name":null,"automationId":null},"targetOrDescendant":false,
                    "ancestors":[{"type":"Panel","name":"container","automationId":null}],"ancestorsTruncated":true},
                "captured":{"element":null,"targetOrDescendant":false,"ancestors":[],"ancestorsTruncated":false},
                "centerHit":{"element":{"type":"Button","name":"save","automationId":"save-id"},"targetOrDescendant":true,
                    "ancestors":[],"ancestorsTruncated":false}
            }}}}
            """);
        transport.Setup(x => x.ReadAsync("button", "", "$pointer")).ReturnsAsync(json.RootElement.Clone());
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        var pointer = await button.PointerAsync();
        pointer.Target.Name.Should().Be("save");
        pointer.Root.Should().BeNull();
        pointer.WindowHandle.Should().Be(123);
        pointer.Position.Should().Be(new PointerPoint(-2.5, 4));
        pointer.Center.Should().Be(new PointerPoint(10, 12));
        pointer.CenterInRoot.Should().BeNull();
        pointer.IsMouseOver.Should().BeFalse();
        pointer.IsMeasureValid.Should().BeTrue();
        pointer.IsArrangeValid.Should().BeFalse();
        pointer.DirectlyOver.Element!.Type.Should().Be("Popup");
        pointer.DirectlyOver.Ancestors.Should().ContainSingle().Which.Name.Should().Be("container");
        pointer.DirectlyOver.AncestorsTruncated.Should().BeTrue();
        pointer.Captured.Element.Should().BeNull();
        pointer.CenterHit.TargetOrDescendant.Should().BeTrue();
        System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(pointer)),
            System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetProperty("value").GetProperty("$pointer").GetProperty("value").GetRawText())).Should().BeTrue();
        transport.VerifyAll();
    }

    [Fact]
    public void PointerStatePreservesRootCoordinatesAndCapture()
    {
        using var json = JsonDocument.Parse("""
            {"target":{"type":"Button","name":null,"automationId":null},
             "root":{"type":"Window","name":"main","automationId":null},"windowHandle":12,
             "position":{"x":1,"y":2},"center":{"x":3,"y":4},"centerInRoot":{"x":5,"y":6},
             "isMouseOver":true,"isMeasureValid":true,"isArrangeValid":true,
             "directlyOver":{"element":null,"targetOrDescendant":false,"ancestors":[],"ancestorsTruncated":false},
             "captured":{"element":{"type":"PopupRoot","name":null,"automationId":null},"targetOrDescendant":false,"ancestors":[],"ancestorsTruncated":false},
             "centerHit":{"element":null,"targetOrDescendant":false,"ancestors":[],"ancestorsTruncated":false}}
            """);

        var pointer = PointerState.FromProjection(json.RootElement);
        pointer.Root!.Name.Should().Be("main");
        pointer.CenterInRoot.Should().Be(new PointerPoint(5, 6));
        pointer.Captured.Element!.Type.Should().Be("PopupRoot");
    }

    [Fact]
    public async Task PointerStatePropagatesUnreadableProjection()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.Setup(x => x.ReadAsync("button", "", "$pointer"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new Dictionary<string, object> { ["$pointer"] = new { error = "unreadable" } } }));
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        await button.Invoking(x => x.PointerAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*unreadable*");
    }

    [Fact]
    public async Task TextLayoutPreservesCharacterGeometryAndIncompleteState()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "TextBlock", null, null, null, true, 10000)).ReturnsAsync("text");
        transport.Setup(x => x.ReadAsync("text", "", "$textLayout"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new Dictionary<string, object> { ["$textLayout"] = new { value = new {
                text = "ab", visible = true, truncated = true, deadlineReached = false, windowHandle = 123,
                runs = new[] { new { characters = new[] { new { offset = 0, length = 1, visible = true, x = 1.0, y = 2.0, width = 3.0, height = 4.0 } } } }
            } } } }));
        var block = await new WpfControls(transport.Object).FindAsync<TextBlock>();

        var layout = await block.TextLayoutAsync();
        layout.Text.Should().Be("ab");
        layout.IsVisible.Should().BeTrue();
        layout.IsTruncated.Should().BeTrue();
        layout.DeadlineReached.Should().BeFalse();
        layout.WindowHandle.Should().Be(123);
        var character = layout.Characters.Single();
        character.Offset.Should().Be(0);
        character.Length.Should().Be(1);
        character.IsVisible.Should().BeTrue();
        character.Width.Should().Be(3.0);
        character.Height.Should().Be(4.0);
        transport.VerifyAll();
    }


    [Fact]
    public async Task TooltipSelectorIgnoresObjectValuesButRejectsUnreadableProperties()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.SetupSequence(x => x.ReadAsync("button", "", "ToolTip"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { ToolTip = new { value = new { type = "ToolTip" } } } }))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { ToolTip = new { error = "unreadable" } } }));
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        (await button.ToolTipTextIfStringAsync()).Should().BeNull();
        await button.Invoking(x => x.ToolTipTextIfStringAsync()).Should().ThrowAsync<InvalidOperationException>();
        transport.VerifyAll();
    }


    [Fact]
    public async Task BindingSearchFindsExactNestedValuesWithoutSubstringMatches()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.Setup(x => x.GetBindingsAsync("button")).ReturnsAsync(JsonSerializer.SerializeToElement(new { Bindings = new[] { new { Details = new[] { "Parent", "Exact.Path" } } } }));
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        (await button.BindingContainsAsync("Exact.Path")).Should().BeTrue();
        (await button.BindingContainsAsync("Exact")).Should().BeFalse();
        transport.VerifyAll();
    }

    [Fact]
    public async Task InspectedTextDistinguishesUnreadableFromPresentNullAndNumber()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.SetupSequence(x => x.ReadAsync("button", "DataContext", "Name"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Name = new { missing = true } } }))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Name = new { error = "unreadable" } } }))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Name = new { value = (string?)null } } }))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Name = new { value = 12 } } }));
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        (await button.ReadTextAsync("DataContext", "Name")).Should().Be(new InspectedText(false, ""));
        (await button.ReadTextAsync("DataContext", "Name")).Should().Be(new InspectedText(false, ""));
        (await button.ReadTextAsync("DataContext", "Name")).Should().Be(new InspectedText(true, ""));
        (await button.ReadTextAsync("DataContext", "Name")).Should().Be(new InspectedText(true, "12"));
        transport.VerifyAll();
    }

    [Fact]
    public async Task ObservedTextKeepsNonStringKindForRenderedMismatchDiagnostics()
    {
        var transport = new Mock<IWpfControlTransport>(MockBehavior.Strict);
        transport.Setup(x => x.FindAsync(null, "Button", null, null, null, true, 10000)).ReturnsAsync("button");
        transport.Setup(x => x.ReadAsync("button", "", "Content"))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { value = new { Content = new { value = 12 } } }));
        var button = await new WpfControls(transport.Object).FindAsync<Button>();

        var value = await button.ObservedTextAsync(WpfTextField.Content);
        value.IsString.Should().BeFalse();
        value.Kind.Should().Be(ObservedTextKind.Number);
        value.Text.Should().BeNull();
        await button.Invoking(x => x.ObservedTextAsync((WpfTextField)42)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        transport.VerifyAll();
    }

}
