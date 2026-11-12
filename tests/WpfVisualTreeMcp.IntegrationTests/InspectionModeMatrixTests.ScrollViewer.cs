using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;
using Xunit.Sdk;

namespace WpfVisualTreeMcp.IntegrationTests;

public partial class InspectionModeMatrixTests
{
    [IntegrationTheory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "Integration")]
    public async Task Bridge_scrolls_exact_viewer_for_target_architecture_and_mode(
        string targetFramework,
        string architecture,
        string mode)
    {
        var samplePath = Path.Combine(
            GetRequiredEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES"),
            targetFramework, architecture, mode, "SampleWpfApp.exe");
        File.Exists(samplePath).Should().BeTrue("the integration runner should publish every sample matrix variant");
        using var sample = StartSample(samplePath, mode, architecture);
        try
        {
            await WaitForMainWindowAsync(sample, TimeSpan.FromSeconds(30));
            GetProcessArchitecture(sample).Should().Be(architecture);
            var arguments = new List<string> { "attach", "--pid", sample.Id.ToString(), "--compact" };
            if (mode == "AutoInjection") arguments.Add("--auto-inject");
            var attach = await RunCliAsync(arguments, TimeSpan.FromSeconds(20));
            attach.ExitCode.Should().Be(0, FormatCommandFailure("attach", attach));

            var manager = new ProcessManager(NullLogger<ProcessManager>.Instance);
            await manager.AttachToProcessAsync(sample.Id, null);
            IIpcBridge bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, manager);
            var identity = await bridge.GetInspectionIdentityAsync();
            identity.Success.Should().BeTrue(identity.Error);
            identity.ProcessId.Should().Be(sample.Id);
            var viewer = await FindElementHandleAsync(sample.Id, "ScreenshotScrollViewer", TimeSpan.FromSeconds(10));
            var button = await FindElementHandleAsync(sample.Id, "SubmitButton", TimeSpan.FromSeconds(10));
            var input = await FindElementHandleAsync(sample.Id, "UserNameTextBox", TimeSpan.FromSeconds(10));
            await bridge.SendKeysAsync(input, "Home");
            var initial = await ReadScrollStateAsync(bridge, viewer, identity.Generation);
            initial.maximum.Should().BeGreaterThan(0);
            initial.viewport.Should().BeGreaterThan(0);

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var home = await bridge.ScrollViewerAsync(viewer, ScrollViewerAction.Home, identity.Generation);
                home.Method.Should().Be("ScrollViewer.Home");
                home.ElementType.Should().Be("System.Windows.Controls.ScrollViewer");
                await WaitForScrollOffsetAsync(bridge, viewer, identity.Generation, 0);
                var page = await bridge.ScrollViewerAsync(viewer, ScrollViewerAction.PageDown, identity.Generation);
                page.Method.Should().Be("ScrollViewer.PageDown");
                await WaitForScrollOffsetAsync(bridge, viewer, identity.Generation, Math.Min(initial.viewport, initial.maximum));
            }

            await bridge.ScrollViewerAsync(viewer, ScrollViewerAction.End, identity.Generation);
            await WaitForScrollOffsetAsync(bridge, viewer, identity.Generation, initial.maximum);
            await bridge.ScrollViewerAsync(viewer, ScrollViewerAction.PageDown, identity.Generation);
            await WaitForScrollOffsetAsync(bridge, viewer, identity.Generation, initial.maximum);
            await bridge.ScrollViewerAsync(viewer, ScrollViewerAction.PageUp, identity.Generation);
            await WaitForScrollOffsetAsync(bridge, viewer, identity.Generation, Math.Max(0, initial.maximum - initial.viewport));
            await bridge.ScrollViewerAsync(viewer, ScrollViewerAction.Home, identity.Generation);
            await WaitForScrollOffsetAsync(bridge, viewer, identity.Generation, 0);
            await bridge.ScrollViewerAsync(viewer, ScrollViewerAction.PageUp, identity.Generation);
            await WaitForScrollOffsetAsync(bridge, viewer, identity.Generation, 0);

            var wrongType = () => bridge.ScrollViewerAsync(button, ScrollViewerAction.PageDown, identity.Generation);
            await wrongType.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Button*not a ScrollViewer*");
            var staleHandle = () => bridge.ScrollViewerAsync("elem_missing_scroll_target", ScrollViewerAction.PageDown, identity.Generation);
            await staleHandle.Should().ThrowAsync<InvalidOperationException>().WithMessage("*handle*not found*");
            foreach (var action in new[] { (ScrollViewerAction)0, (ScrollViewerAction)999 })
            {
                var invalid = () => bridge.ScrollViewerAsync(viewer, action, identity.Generation);
                await invalid.Should().ThrowAsync<InvalidOperationException>().WithMessage("Invalid ScrollViewer action:*");
            }
            var staleGeneration = () => bridge.ScrollViewerAsync(viewer, ScrollViewerAction.PageDown, "previous-inspector");
            await staleGeneration.Should().ThrowAsync<InvalidOperationException>().WithMessage("Stale inspection generation.*");
            (await ReadScrollStateAsync(bridge, viewer, identity.Generation)).offset.Should().Be(0,
                "rejected requests must not issue a scroll action");
            await VerifyScrollItemIntoViewAsync(bridge, sample.Id, identity.Generation, button);
        }
        finally
        {
            await StopProcessAsync(sample);
        }
    }

    private static async Task VerifyScrollItemIntoViewAsync(IIpcBridge bridge, int processId, string generation, string button)
    {
        var list = await FindElementHandleAsync(processId, "ItemsListBox", TimeSpan.FromSeconds(10));
        var viewers = await bridge.FindElementsAsync(list, null, null, null,
            new Dictionary<string, string> { ["$type"] = "ScrollViewer" });
        viewers.Count.Should().Be(1);
        var viewer = viewers.Elements.Single().Handle;
        var baseline = await bridge.ReadClrAsync(list, "", new[] { "Items.Count", "SelectedIndex" }, 0, 1, generation);
        using var baselineDocument = JsonDocument.Parse(baseline.Json!);
        var baselineValue = baselineDocument.RootElement.GetProperty("value");
        var count = baselineValue.GetProperty("Items.Count").GetProperty("value").GetInt32();
        var selection = baselineValue.GetProperty("SelectedIndex").GetProperty("value").GetInt32();
        count.Should().Be(40);
        var itemsBefore = await bridge.ReadClrAsync(list, "Items", new[] { "Name", "Description" }, 0, 100, generation);
        var realizedBefore = await bridge.FindElementsAsync(list, null, null, null,
            new Dictionary<string, string> { ["$type"] = "ListBoxItem" }, maxResults: 100);
        realizedBefore.Count.Should().BeLessThan(count, "the target starts outside the realized virtualized range");

        var outcome = await bridge.ScrollItemIntoViewAsync(list, count - 1, generation);
        outcome.Method.Should().Be("ListBox.ScrollIntoView");
        outcome.ElementType.Should().Be("System.Windows.Controls.ListBox");
        await WaitForScrollBoundaryAsync(bridge, viewer, generation, atEnd: true);
        var lastText = await bridge.FindElementsAsync(list, null, null, null,
            new Dictionary<string, string> { ["$type"] = "TextBlock", ["Text"] = "Item 40" }, visibleOnly: true);
        lastText.Elements.Count(element => element.Text == "Item 40").Should().Be(1, "ScrollIntoView should realize the requested item");
        await bridge.ScrollItemIntoViewAsync(list, 0, generation);
        await WaitForScrollBoundaryAsync(bridge, viewer, generation, atEnd: false);

        var wrongType = () => bridge.ScrollItemIntoViewAsync(button, 0, generation);
        await wrongType.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Button*not a ListBox*");
        var staleHandle = () => bridge.ScrollItemIntoViewAsync("elem_missing_list_target", 0, generation);
        await staleHandle.Should().ThrowAsync<InvalidOperationException>().WithMessage("*handle*not found*");
        foreach (var index in new[] { -1, count })
        {
            var invalid = () => bridge.ScrollItemIntoViewAsync(list, index, generation);
            await invalid.Should().ThrowAsync<InvalidOperationException>().WithMessage("ListBox item index*out of range*");
        }
        var staleGeneration = () => bridge.ScrollItemIntoViewAsync(list, count - 1, "previous-inspector");
        await staleGeneration.Should().ThrowAsync<InvalidOperationException>().WithMessage("Stale inspection generation.*");
        (await ReadScrollStateAsync(bridge, viewer, generation)).offset.Should().Be(0);
        var final = await bridge.ReadClrAsync(list, "", new[] { "Items.Count", "SelectedIndex" }, 0, 1, generation);
        using var finalDocument = JsonDocument.Parse(final.Json!);
        var finalValue = finalDocument.RootElement.GetProperty("value");
        finalValue.GetProperty("Items.Count").GetProperty("value").GetInt32().Should().Be(count);
        finalValue.GetProperty("SelectedIndex").GetProperty("value").GetInt32().Should().Be(selection);
        var itemsAfter = await bridge.ReadClrAsync(list, "Items", new[] { "Name", "Description" }, 0, 100, generation);
        itemsAfter.Json.Should().Be(itemsBefore.Json, "scrolling must preserve every item in order");
    }

    private static async Task WaitForScrollBoundaryAsync(IIpcBridge bridge, string handle, string generation, bool atEnd)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            var state = await ReadScrollStateAsync(bridge, handle, generation);
            state.maximum.Should().BeGreaterThan(0);
            var expected = atEnd ? state.maximum : 0;
            stable = Math.Abs(state.offset - expected) < 0.5 ? stable + 1 : 0;
            if (stable == 2) return;
            await Task.Delay(50);
        }

        throw new XunitException($"ListBox did not scroll to {(atEnd ? "last" : "first")} item within 5 seconds.");
    }

    private static async Task<(double offset, double maximum, double viewport)> ReadScrollStateAsync(
        IIpcBridge bridge, string handle, string generation)
    {
        var response = await bridge.ReadClrAsync(handle, "",
            new[] { "VerticalOffset", "ScrollableHeight", "ViewportHeight" }, 0, 1, generation);
        using var document = JsonDocument.Parse(response.Json!);
        var value = document.RootElement.GetProperty("value");
        return (value.GetProperty("VerticalOffset").GetProperty("value").GetDouble(),
            value.GetProperty("ScrollableHeight").GetProperty("value").GetDouble(),
            value.GetProperty("ViewportHeight").GetProperty("value").GetDouble());
    }

    private static async Task WaitForScrollOffsetAsync(IIpcBridge bridge, string handle, string generation, double expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        double? last = null;
        var stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            var state = await ReadScrollStateAsync(bridge, handle, generation);
            stable = Math.Abs(state.offset - expected) < 0.5 ? stable + 1 : 0;
            if (stable == 2) return;
            last = state.offset;
            await Task.Delay(50);
        }

        throw new XunitException($"Scroll offset was {last}; expected {expected} within 5 seconds.");
    }
}
