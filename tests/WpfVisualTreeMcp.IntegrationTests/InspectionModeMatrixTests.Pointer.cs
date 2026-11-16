using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WpfVisualTreeMcp.Server.Services;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public partial class InspectionModeMatrixTests
{
    [IntegrationTheory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "Integration")]
    public async Task Bridge_reads_pointer_hit_for_target_architecture_and_mode(
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
            var button = await FindElementHandleAsync(sample.Id, "ClearButton", TimeSpan.FromSeconds(10));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            JsonElement pointer;
            do
            {
                var response = await bridge.ReadClrAsync(button, "", new[] { "$pointer" }, 0, 1, identity.Generation);
                pointer = response.Data!.Value.GetProperty("$pointer").GetProperty("value").Clone();
                if (pointer.GetProperty("isMeasureValid").GetBoolean() && pointer.GetProperty("isArrangeValid").GetBoolean()) break;
                await Task.Delay(50);
            }
            while (DateTime.UtcNow < deadline);

            pointer.GetProperty("isMeasureValid").GetBoolean().Should().BeTrue();
            pointer.GetProperty("isArrangeValid").GetBoolean().Should().BeTrue();
            pointer.GetProperty("target").GetProperty("type").GetString().Should().Be("System.Windows.Controls.Button");
            pointer.GetProperty("target").GetProperty("name").GetString().Should().Be("ClearButton");
            pointer.GetProperty("target").GetProperty("automationId").GetString().Should().BeEmpty();
            pointer.GetProperty("root").GetProperty("type").GetString().Should().Be("SampleWpfApp.MainWindow");
            pointer.GetProperty("windowHandle").GetInt64().Should().NotBe(0);
            pointer.GetProperty("center").GetProperty("x").GetDouble().Should().BeGreaterThan(0);
            pointer.GetProperty("center").GetProperty("y").GetDouble().Should().BeGreaterThan(0);
            pointer.GetProperty("centerInRoot").GetProperty("x").GetDouble().Should().BeGreaterThan(0);
            pointer.GetProperty("centerInRoot").GetProperty("y").GetDouble().Should().BeGreaterThan(0);
            pointer.GetProperty("position").GetProperty("x").GetDouble().Should().NotBe(double.NaN);
            pointer.GetProperty("position").GetProperty("y").GetDouble().Should().NotBe(double.NaN);
            pointer.GetProperty("centerHit").GetProperty("targetOrDescendant").GetBoolean().Should().BeTrue(pointer.ToString());
            pointer.GetProperty("centerHit").GetProperty("element").ValueKind.Should().Be(JsonValueKind.Object);
            foreach (var field in new[] { "directlyOver", "captured", "centerHit" })
            {
                var hit = pointer.GetProperty(field);
                hit.GetProperty("ancestors").GetArrayLength().Should().BeLessThanOrEqualTo(10);
                hit.GetProperty("targetOrDescendant").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
                hit.GetProperty("ancestorsTruncated").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
            }
        }
        finally
        {
            await StopProcessAsync(sample);
        }
    }
}
