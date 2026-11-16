using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public partial class InspectionModeMatrixTests
{
    [IntegrationTheory]
    [InlineData("net472")]
    [InlineData("net48")]
    [InlineData("net8.0-windows")]
    [Trait("Category", "Integration")]
    public async Task TypedPayloadsTravelBetweenRealInspectorAndBridge(string targetFramework)
    {
        var samplePath = Path.Combine(GetRequiredEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES"),
            targetFramework, "x64", "SelfHosted", "SampleWpfApp.exe");
        using var sample = StartSample(samplePath, "SelfHosted", "x64");
        try
        {
            await WaitForMainWindowAsync(sample, TimeSpan.FromSeconds(30));
            var attach = await RunCliAsync(new[] { "attach", "--pid", sample.Id.ToString(), "--compact" }, TimeSpan.FromSeconds(20));
            attach.ExitCode.Should().Be(0, FormatCommandFailure("attach", attach));
            var manager = new ProcessManager(NullLogger<ProcessManager>.Instance);
            await manager.AttachToProcessAsync(sample.Id, null);
            IIpcBridge bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, manager);
            var tree = await bridge.GetVisualTreeAsync(null, 5);
            tree.TotalElements.Should().BeGreaterThan(0);
            var found = await bridge.FindElementsAsync(null, null, "UserNameTextBox", null, null, true);
            var handle = found.Elements.Should().ContainSingle().Which.Handle;
            (await bridge.FindElementsDeepAsync(null, null, "UserNameTextBox")).Elements.Should().ContainSingle();
            (await bridge.GetElementPropertiesAsync(handle)).Properties.Should().Contain(item => item.Name == "Text");
            var bindings = await bridge.GetBindingsAsync(handle);
            bindings.Bindings.Should().Contain(item => item.Property == "Text" && item.Path == "UserName");
            var identity = await bridge.GetInspectionIdentityAsync();
            var read = await bridge.ReadClrAsync(handle, "", new[] { "Text" }, 0, 1, identity.Generation);
            read.Data!.Value.GetProperty("Text").GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
            var members = await bridge.ReadClrAsync(handle, "", [], 0, 20, identity.Generation, true);
            members.Data!.Kind.Should().Be(ClrReadKind.Members);
            members.Data.Members.Should().NotBeEmpty();
            var pointerRead = await bridge.ReadClrAsync(handle, "", ["$pointer"], 0, 1, identity.Generation);
            var pointer = pointerRead.Data!.Value.GetProperty("$pointer").GetProperty("value")
                .Deserialize<WpfVisualTreeMcp.Shared.Ipc.PointerProjection>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            pointer!.Target.Type.Should().Be("System.Windows.Controls.TextBox");
            (await bridge.GetLayoutInfoAsync(handle)).Layout.ActualWidth.Should().BeGreaterThan(0);
            (await bridge.GetBindingErrorsAsync()).Errors.Should().NotBeNull();
            (await bridge.GetResourcesAsync(ResourceScope.Application, null)).Resources.Should().NotBeNull();
            (await bridge.GetStylesAsync(handle)).Style.Should().NotBeNull();
            using var context = JsonDocument.Parse((await bridge.GetDataContextAsync(handle)).DataContextJson!);
            context.RootElement.GetProperty("dataContext").ValueKind.Should().Be(JsonValueKind.Object);
            using var evaluation = JsonDocument.Parse((await bridge.EvaluateBindingAsync(handle, "Text")).Json!);
            evaluation.RootElement.GetProperty("binding").GetProperty("path").GetString().Should().Be("UserName");
            using var triggers = JsonDocument.Parse((await bridge.ExplainTriggersAsync(handle, "Text")).Json!);
            triggers.RootElement.GetProperty("styleTriggers").ValueKind.Should().Be(JsonValueKind.Array);
            await bridge.SnapshotAsync(handle, "payload-before", 2);
            await bridge.SnapshotAsync(handle, "payload-after", 2);
            using var diff = JsonDocument.Parse((await bridge.DiffAsync("payload-before", "payload-after")).Json!);
            diff.RootElement.GetProperty("summary").ValueKind.Should().Be(JsonValueKind.Object);
        }
        finally
        {
            await StopProcessAsync(sample);
        }
    }
}
