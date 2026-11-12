using FluentAssertions;
using WpfVisualTreeMcp.Server.Controls;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public partial class InspectionModeMatrixTests
{
    [IntegrationTheory]
    [InlineData("net472")]
    [InlineData("net8.0-windows")]
    [Trait("Category", "Integration")]
    public async Task TypedControlsReadAndModifyTheActualTarget(string targetFramework)
    {
        var samplePath = Path.Combine(GetRequiredEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES"),
            targetFramework, "x64", "SelfHosted", "SampleWpfApp.exe");
        var directory = Path.Combine(Path.GetTempPath(), $"WpfTypedControls-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var sample = StartSample(samplePath, "SelfHosted", "x64");
        try
        {
            await WaitForMainWindowAsync(sample, TimeSpan.FromSeconds(30));
            var session = new WpfSession(sample.Id, directory);
            await session.AttachAsync();
            var textBox = await session.FindAsync<TextBox>(name: "UserNameTextBox");
            (await session.FindAsync<Control>(name: "UserNameTextBox")).Should().NotBeNull();
            (await session.FindAsync<Control>(name: "UserNameTextBox", matchType: false)).Should().Be(textBox);
            var wrongSelectedType = () => session.FindAsync<Button>(name: "UserNameTextBox", matchType: false);
            await wrongSelectedType.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not match Button*");
            var wrongType = () => session.FindAsync<Button>(name: "UserNameTextBox", timeoutMs: 100);
            await wrongType.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Control not found*");
            var ambiguous = () => session.FindAsync<TextBox>();
            await ambiguous.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Ambiguous selector*");
            await textBox.SetTextAsync("Typed WPF control");
            (await textBox.TextAsync()).Should().Be("Typed WPF control");
            var button = await session.FindAsync<Button>(automationId: "SubmitButtonExact");
            (await button.IsEnabledAsync()).Should().BeTrue();

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            session.CancellationToken = cancelled.Token;
            var cancelledRead = () => textBox.TextAsync();
            await cancelledRead.Should().ThrowAsync<OperationCanceledException>();
            session.CancellationToken = CancellationToken.None;

            await StopProcessAsync(sample);
            var readAfterExit = () => textBox.TextAsync();
            await readAfterExit.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            await StopProcessAsync(sample);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
