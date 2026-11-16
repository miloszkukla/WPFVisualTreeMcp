using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;
using Xunit.Sdk;

namespace WpfVisualTreeMcp.IntegrationTests;

public partial class InspectionModeMatrixTests
{
    private const uint Th32csSnapModule = 0x00000008;
    private const uint Th32csSnapModule32 = 0x00000010;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static TheoryData<string, string, string> Cases => new()
    {
        { "net472", "x86", "SelfHosted" },
        { "net472", "x86", "AutoInjection" },
        { "net472", "x64", "SelfHosted" },
        { "net472", "x64", "AutoInjection" },
        { "net48", "x86", "SelfHosted" },
        { "net48", "x86", "AutoInjection" },
        { "net48", "x64", "SelfHosted" },
        { "net48", "x64", "AutoInjection" },
        { "net8.0-windows", "x86", "SelfHosted" },
        { "net8.0-windows", "x86", "AutoInjection" },
        { "net8.0-windows", "x64", "SelfHosted" },
        { "net8.0-windows", "x64", "AutoInjection" },
    };

    [Fact]
    public void CalculateRealizedPixelAdvance_VariableHeightContainers_UsesSharedContainerGeometry()
    {
        var previousPositions = new Dictionary<int, int>
        {
            [2] = 120,
            [3] = 150,
        };
        var currentPositions = new Dictionary<int, int>
        {
            [2] = 0,
            [3] = 30,
        };

        var advance = ScreenshotCapture.CalculateRealizedPixelAdvance(
            previousPositions, currentPositions);

        advance.Should().Be(120);
    }

    [IntegrationTheory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "Integration")]
    public async Task Cli_inspects_and_captures_full_content_for_target_architecture_and_mode(
        string targetFramework,
        string architecture,
        string mode)
    {
        var samplePath = Path.Combine(
            GetRequiredEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES"),
            targetFramework,
            architecture,
            mode,
            "SampleWpfApp.exe");
        File.Exists(samplePath).Should().BeTrue("the integration runner should publish every sample matrix variant");

        var screenshotDirectory = Path.Combine(
            Path.GetTempPath(),
            $"WpfVisualTreeMcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(screenshotDirectory);
        using var sample = StartSample(samplePath, mode, architecture);
        try
        {
            await WaitForMainWindowAsync(sample, TimeSpan.FromSeconds(30));

            GetProcessArchitecture(sample).Should().Be(architecture);
            var runtimeModule = targetFramework == "net8.0-windows" ? "coreclr.dll" : "clr.dll";
            await WaitForModuleAsync(sample.Id, runtimeModule, present: true, TimeSpan.FromSeconds(10));

            var autoInject = mode == "AutoInjection";
            if (autoInject)
            {
                File.Exists(Path.Combine(
                        Path.GetDirectoryName(samplePath)!,
                        "WpfVisualTreeMcp.Inspector.dll"))
                    .Should().BeFalse(
                        "the auto-injection target must not contain its own Inspector payload");

                var beforeInjection = await RunCliAsync(
                    new[]
                    {
                        "find",
                        "--pid",
                        sample.Id.ToString(),
                        "--name",
                        "SubmitButton",
                        "--compact",
                    },
                    TimeSpan.FromSeconds(8));
                beforeInjection.ExitCode.Should().NotBe(
                    0,
                    "the auto-injection case must not be inspectable before attach --auto-inject");
            }

            var attachArguments = new List<string>
            {
                "attach",
                "--pid",
                sample.Id.ToString(),
                "--compact",
            };
            if (autoInject)
                attachArguments.Add("--auto-inject");

            var attach = await RunCliAsync(attachArguments, TimeSpan.FromSeconds(20));
            attach.ExitCode.Should().Be(0, FormatCommandFailure("attach", attach));

            using (var attachJson = JsonDocument.Parse(attach.StandardOutput))
            {
                attachJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
                attachJson.RootElement.GetProperty("processId").GetInt32().Should().Be(sample.Id);
                attachJson.RootElement.GetProperty("inspectorStatus").GetString()
                    .Should().Be(autoInject ? "LoadedInjected" : "LoadedExisting");
            }

            if (autoInject)
            {
                var secondAttach = await RunCliAsync(attachArguments, TimeSpan.FromSeconds(20));
                secondAttach.ExitCode.Should().Be(0, FormatCommandFailure("second attach", secondAttach));

                using var secondAttachJson = JsonDocument.Parse(secondAttach.StandardOutput);
                secondAttachJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
                secondAttachJson.RootElement.GetProperty("processId").GetInt32().Should().Be(sample.Id);
                secondAttachJson.RootElement.GetProperty("inspectorStatus").GetString()
                    .Should().Be("LoadedExisting",
                        "the second attach should reuse the loaded Inspector without reinjecting it");
            }

            var find = await FindSubmitButtonAsync(sample.Id, TimeSpan.FromSeconds(15));
            find.ExitCode.Should().Be(0, FormatCommandFailure("find", find));

            using var findJson = JsonDocument.Parse(find.StandardOutput);
            var elements = findJson.RootElement.GetProperty("elements");
            elements.GetArrayLength().Should().Be(1);
            var submitButton = elements[0];
            submitButton.GetProperty("name").GetString().Should().Be("SubmitButton");
            submitButton.GetProperty("typeName").GetString().Should().Be("System.Windows.Controls.Button");
            submitButton.GetProperty("handle").GetString().Should().StartWith("elem_");
            findJson.RootElement.GetProperty("count").GetInt32().Should().Be(1);

            var identity = await RunCliAsync(new[] { "identity", "--pid", sample.Id.ToString(), "--compact" }, TimeSpan.FromSeconds(15));
            identity.ExitCode.Should().Be(0, FormatCommandFailure("identity", identity));
            using var identityJson = JsonDocument.Parse(identity.StandardOutput);
            var generation = identityJson.RootElement.GetProperty("generation").GetString()!;
            identityJson.RootElement.GetProperty("processId").GetInt32().Should().Be(sample.Id);
            var read = await RunCliAsync(new[] { "read", "--pid", sample.Id.ToString(), "--handle", submitButton.GetProperty("handle").GetString()!,
                "--path", "Content", "--generation", generation, "--compact" }, TimeSpan.FromSeconds(15));
            read.ExitCode.Should().Be(0, FormatCommandFailure("read", read));
            using var readJson = JsonDocument.Parse(read.StandardOutput);
            readJson.RootElement.GetProperty("generation").GetString().Should().Be(generation);
            readJson.RootElement.GetProperty("data").GetProperty("value").GetString().Should().NotBeNullOrEmpty();
            var stale = await RunCliAsync(new[] { "read", "--pid", sample.Id.ToString(), "--handle", submitButton.GetProperty("handle").GetString()!,
                "--path", "Content", "--generation", "previous-inspector", "--compact" }, TimeSpan.FromSeconds(15));
            stale.ExitCode.Should().NotBe(0);
            stale.StandardOutput.Should().Contain("Stale inspection generation");

            foreach (var selector in new[] { "SubmitButton", "NoMatchingButton" })
            {
                var reacquire = await RunCliAsync(new[] { "reacquire", "--pid", sample.Id.ToString(), "--name", selector, "--compact" }, TimeSpan.FromSeconds(15));
                reacquire.ExitCode.Should().Be(0, FormatCommandFailure("reacquire", reacquire));
                using var reacquireJson = JsonDocument.Parse(reacquire.StandardOutput);
                reacquireJson.RootElement.GetProperty("generation").GetString().Should().Be(generation);
                reacquireJson.RootElement.GetProperty("unique").GetBoolean().Should().Be(selector == "SubmitButton");
                reacquireJson.RootElement.GetProperty("ambiguous").GetBoolean().Should().BeFalse();
                reacquireJson.RootElement.GetProperty("matches").GetProperty("elements").GetArrayLength().Should().Be(selector == "SubmitButton" ? 1 : 0);
            }
            var ambiguous = await RunCliAsync(new[] { "reacquire", "--pid", sample.Id.ToString(), "--type", "Button", "--compact" }, TimeSpan.FromSeconds(15));
            ambiguous.ExitCode.Should().Be(0, FormatCommandFailure("ambiguous reacquire", ambiguous));
            using var ambiguousJson = JsonDocument.Parse(ambiguous.StandardOutput);
            ambiguousJson.RootElement.GetProperty("unique").GetBoolean().Should().BeFalse();
            ambiguousJson.RootElement.GetProperty("ambiguous").GetBoolean().Should().BeTrue();
            ambiguousJson.RootElement.GetProperty("matches").GetProperty("elements").GetArrayLength().Should().BeGreaterThan(1);
            var diagnostics = await RunCliAsync(new[] { "diagnostics", "--pid", sample.Id.ToString(), "--handle", submitButton.GetProperty("handle").GetString()!,
                "--fields", "NoMatchingClrProperty", "--compact" }, TimeSpan.FromSeconds(30));
            diagnostics.ExitCode.Should().Be(0, FormatCommandFailure("diagnostics", diagnostics));
            using var diagnosticsJson = JsonDocument.Parse(diagnostics.StandardOutput);
            diagnosticsJson.RootElement.GetProperty("generation").GetString().Should().Be(generation);
            diagnosticsJson.RootElement.GetProperty("tree").GetProperty("totalElements").GetInt32().Should().BeGreaterThan(0);
            diagnosticsJson.RootElement.GetProperty("properties").GetProperty("properties").EnumerateArray()
                .Single(property => property.GetProperty("name").GetString() == "Content").GetProperty("value").GetString().Should().Be("Submit");
            diagnosticsJson.RootElement.GetProperty("bindings").GetProperty("bindings").EnumerateArray()
                .Single(binding => binding.GetProperty("path").GetString() == "SubmitCommand").GetProperty("status").GetString().Should().Be("Active");
            diagnosticsJson.RootElement.GetProperty("screenshot").GetProperty("imageBase64").GetString().Should().NotBeNullOrEmpty();
            using var diagnosticModel = JsonDocument.Parse(diagnosticsJson.RootElement.GetProperty("dataContext").GetProperty("json").GetString()!);
            diagnosticModel.RootElement.GetProperty("value").GetProperty("NoMatchingClrProperty").GetProperty("missing").GetBoolean().Should().BeTrue();

            await VerifyExactSelectorsAsync(sample.Id);
            await VerifyRenderedTextAsync(sample.Id);
            await VerifyKeyboardFocusAsync(sample.Id);

            var listBoxHandle = await FindElementHandleAsync(
                sample.Id, "ItemsListBox", TimeSpan.FromSeconds(15));
            var select = await RunCliAsync(
                new[]
                {
                    "select-item",
                    "--pid",
                    sample.Id.ToString(),
                    "--handle",
                    listBoxHandle,
                    "--index",
                    "39",
                    "--compact",
                },
                TimeSpan.FromSeconds(15));
            select.ExitCode.Should().Be(0, FormatCommandFailure("select-item", select));

            var beforePath = Path.Combine(screenshotDirectory, "before.png");
            var fullPath = Path.Combine(screenshotDirectory, "full.png");
            var afterPath = Path.Combine(screenshotDirectory, "after.png");
            var beforeCapture = await CaptureScreenshotAsync(sample.Id, listBoxHandle, beforePath, false);
            var fullCapture = await CaptureScreenshotAsync(sample.Id, listBoxHandle, fullPath, true);
            var afterCapture = await CaptureScreenshotAsync(sample.Id, listBoxHandle, afterPath, false);

            fullCapture.height.Should().BeGreaterThan(
                beforeCapture.height,
                "full-content capture should include virtualized items outside the ListBox viewport");
            afterCapture.Should().Be(beforeCapture);
            File.ReadAllBytes(afterPath).Should().Equal(
                File.ReadAllBytes(beforePath),
                "full-content capture should restore the original scroll position");

            if (targetFramework == "net8.0-windows" && architecture == "x64" && mode == "SelfHosted")
            {
                var scrollViewerHandle = await FindElementHandleAsync(
                    sample.Id, "ScreenshotScrollViewer", TimeSpan.FromSeconds(15));
                var viewportCapture = await CaptureScreenshotAsync(
                    sample.Id,
                    scrollViewerHandle,
                    Path.Combine(screenshotDirectory, "direct-viewport.png"),
                    false);
                var directCapture = await CaptureScreenshotAsync(
                    sample.Id,
                    scrollViewerHandle,
                    Path.Combine(screenshotDirectory, "direct-full.png"),
                    true);
                directCapture.height.Should().BeGreaterThan(
                    viewportCapture.height,
                    "non-virtualized ScrollViewer content should be rendered beyond the viewport");
            }
        }
        finally
        {
            await StopProcessAsync(sample);
            if (Directory.Exists(screenshotDirectory))
                Directory.Delete(screenshotDirectory, true);
        }
    }

    private static Process StartSample(string samplePath, string mode, string architecture)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = samplePath,
            UseShellExecute = false,
        };
        startInfo.Environment["WPF_VISUAL_TREE_MCP_SELF_HOSTED"] =
            mode == "SelfHosted" ? "true" : "false";
        if (architecture == "x86")
        {
            var runtimeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet");
            startInfo.Environment["DOTNET_ROOT"] = runtimeRoot;
            startInfo.Environment["DOTNET_ROOT_X86"] = runtimeRoot;
            startInfo.Environment["DOTNET_ROOT(x86)"] = runtimeRoot;
        }

        return Process.Start(startInfo) ?? throw new XunitException($"Failed to start {samplePath}.");
    }

    private static async Task VerifyExactSelectorsAsync(int processId)
    {
        var exact = await RunCliAsync(new[] { "find", "--pid", processId.ToString(), "--filter", "{\"$type\":\"TabControl\"}", "--compact" }, TimeSpan.FromSeconds(15));
        exact.ExitCode.Should().Be(0, FormatCommandFailure("exact type", exact));
        using var exactJson = JsonDocument.Parse(exact.StandardOutput);
        var tabs = exactJson.RootElement.GetProperty("elements");
        tabs.GetArrayLength().Should().Be(1);
        tabs[0].GetProperty("name").GetString().Should().Be("FocusSafetyTabs");

        var fuzzy = await RunCliAsync(new[] { "find", "--pid", processId.ToString(), "--type", "TabControl", "--compact" }, TimeSpan.FromSeconds(15));
        fuzzy.ExitCode.Should().Be(0, FormatCommandFailure("legacy fuzzy type", fuzzy));
        using var fuzzyJson = JsonDocument.Parse(fuzzy.StandardOutput);
        fuzzyJson.RootElement.GetProperty("elements").EnumerateArray().Should().Contain(item => item.GetProperty("name").GetString() == "NamespaceTypeImpostor");

        var automation = await RunCliAsync(new[] { "find", "--pid", processId.ToString(), "--filter", "{\"$type\":\"System.Windows.Controls.Primitives.ButtonBase\",\"$automationId\":\"SubmitButtonExact\"}", "--compact" }, TimeSpan.FromSeconds(15));
        automation.ExitCode.Should().Be(0, FormatCommandFailure("exact automation identity", automation));
        using var automationJson = JsonDocument.Parse(automation.StandardOutput);
        automationJson.RootElement.GetProperty("count").GetInt32().Should().Be(1);
        automationJson.RootElement.GetProperty("elements")[0].GetProperty("name").GetString().Should().Be("SubmitButton");

        var prefix = await RunCliAsync(new[] { "find", "--pid", processId.ToString(), "--filter", "{\"$automationId\":\"SubmitButton\"}", "--compact" }, TimeSpan.FromSeconds(15));
        prefix.ExitCode.Should().Be(0, FormatCommandFailure("automation prefix rejection", prefix));
        using var prefixJson = JsonDocument.Parse(prefix.StandardOutput);
        prefixJson.RootElement.GetProperty("count").GetInt32().Should().Be(0);
    }

    private static async Task VerifyKeyboardFocusAsync(int processId)
    {
        var tab = await FindElementHandleAsync(processId, "FocusSafetyFirstTab", TimeSpan.FromSeconds(10));
        var target = await FindElementHandleAsync(processId, "NonFocusableKeyboardTarget", TimeSpan.FromSeconds(10));
        var list = await FindElementHandleAsync(processId, "KeyboardFocusList", TimeSpan.FromSeconds(10));
        var tabs = await FindElementHandleAsync(processId, "FocusSafetyTabs", TimeSpan.FromSeconds(10));
        var focusTab = await RunCliAsync(new[] { "send-keys", "--pid", processId.ToString(), "--handle", tab, "--keys", "Home", "--compact" }, TimeSpan.FromSeconds(15));
        focusTab.ExitCode.Should().Be(0, FormatCommandFailure("focus first tab", focusTab));
        var right = await RunCliAsync(new[] { "send-keys", "--pid", processId.ToString(), "--handle", target, "--keys", "Right", "--compact" }, TimeSpan.FromSeconds(15));
        right.ExitCode.Should().Be(0, FormatCommandFailure("nonfocusable content arrow", right));
        (await ReadSelectedIndexAsync(processId, list)).Should().Be(1, "the arrow should reach the nearest ListBoxItem container");
        (await ReadSelectedIndexAsync(processId, tabs)).Should().Be(0, "the arrow must not navigate the previously focused tab bar");
        foreach (var name in new[] { "RefusingKeyboardTarget", "NoKeyboardContainerTarget" })
        {
            var invalidTarget = await FindElementHandleAsync(processId, name, TimeSpan.FromSeconds(10));
            var refused = await RunCliAsync(new[] { "send-keys", "--pid", processId.ToString(), "--handle", invalidTarget, "--keys", "Right", "--compact" }, TimeSpan.FromSeconds(15));
            refused.ExitCode.Should().NotBe(0, "unverified focus must reject OS keyboard input");
            refused.StandardOutput.Should().Contain("no keys were sent");
            (await ReadSelectedIndexAsync(processId, list)).Should().Be(1);
            (await ReadSelectedIndexAsync(processId, tabs)).Should().Be(0);
        }
        var combo = await FindElementHandleAsync(processId, "EditableKeyboardComboBox", TimeSpan.FromSeconds(10));
        var enter = await RunCliAsync(new[] { "send-keys", "--pid", processId.ToString(), "--handle", combo, "--keys", "Enter", "--compact" }, TimeSpan.FromSeconds(15));
        enter.ExitCode.Should().Be(0, FormatCommandFailure("editable ComboBox Enter", enter));
        var readCombo = await RunCliAsync(new[] { "read", "--pid", processId.ToString(), "--handle", combo, "--fields", "Tag,IsKeyboardFocusWithin", "--compact" }, TimeSpan.FromSeconds(15));
        readCombo.ExitCode.Should().Be(0, FormatCommandFailure("editable ComboBox focus", readCombo));
        using var comboJson = JsonDocument.Parse(readCombo.StandardOutput);
        var values = comboJson.RootElement.GetProperty("data").GetProperty("value");
        values.GetProperty("Tag").GetProperty("value").GetInt32().Should().Be(1);
        values.GetProperty("IsKeyboardFocusWithin").GetProperty("value").GetBoolean().Should().BeTrue();
    }

    private static async Task VerifyRenderedTextAsync(int processId)
    {
        var source = await FindElementHandleAsync(processId, "UserNameTextBox", TimeSpan.FromSeconds(10));
        var text = await FindElementHandleAsync(processId, "RenderedInlineTextTarget", TimeSpan.FromSeconds(10));
        var set = await RunCliAsync(new[] { "set-text", "--pid", processId.ToString(), "--handle", source, "--text", "Native inline", "--compact" }, TimeSpan.FromSeconds(15));
        set.ExitCode.Should().Be(0, FormatCommandFailure("set bound inline source", set));
        var read = await RunCliAsync(new[] { "read", "--pid", processId.ToString(), "--handle", text, "--fields", "Text,$renderedText,$textLayout", "--compact" }, TimeSpan.FromSeconds(15));
        read.ExitCode.Should().Be(0, FormatCommandFailure("read rendered inline text", read));
        using var readJson = JsonDocument.Parse(read.StandardOutput);
        readJson.RootElement.GetProperty("data").GetProperty("value").GetProperty("$renderedText").GetProperty("value").GetString().Should().Be("Native inline joined");
        var find = await RunCliAsync(new[] { "find", "--pid", processId.ToString(), "--root", text, "--text", "Native inline joined", "--filter", "{\"$type\":\"TextBlock\"}", "--compact" }, TimeSpan.FromSeconds(15));
        find.ExitCode.Should().Be(0, FormatCommandFailure("find rendered inline text", find));
        using var findJson = JsonDocument.Parse(find.StandardOutput);
        findJson.RootElement.GetProperty("count").GetInt32().Should().Be(1);
        findJson.RootElement.GetProperty("elements")[0].GetProperty("text").GetString().Should().Be("Native inline joined");
        var layout = readJson.RootElement.GetProperty("data").GetProperty("value").GetProperty("$textLayout").GetProperty("value");
        layout.GetProperty("visible").GetBoolean().Should().BeTrue();
        layout.GetProperty("truncated").GetBoolean().Should().BeFalse();
        layout.GetProperty("deadlineReached").GetBoolean().Should().BeFalse();
        var character = layout.GetProperty("runs").EnumerateArray().SelectMany(run => run.GetProperty("characters").EnumerateArray())
            .Single(item => item.GetProperty("offset").GetInt32() == "Native inline ".Length);
        character.GetProperty("visible").GetBoolean().Should().BeTrue();
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        previousDpi.Should().NotBe(IntPtr.Zero);
        try
        {
            SetCursorPos((int)Math.Round(character.GetProperty("x").GetDouble() + character.GetProperty("width").GetDouble() / 2),
                (int)Math.Round(character.GetProperty("y").GetDouble() + character.GetProperty("height").GetDouble() / 2)).Should().BeTrue();
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        }
        finally { SetThreadDpiAwarenessContext(previousDpi); }
        var clicked = await RunCliAsync(new[] { "read", "--pid", processId.ToString(), "--handle", text, "--fields", "Tag", "--compact" }, TimeSpan.FromSeconds(15));
        clicked.ExitCode.Should().Be(0, FormatCommandFailure("read actual inline click", clicked));
        using var clickedJson = JsonDocument.Parse(clicked.StandardOutput);
        clickedJson.RootElement.GetProperty("data").GetProperty("value").GetProperty("Tag").GetProperty("value").ToString().Should().Be("1", clicked.StandardOutput);
    }

    private static async Task<int> ReadSelectedIndexAsync(int processId, string handle)
    {
        var read = await RunCliAsync(new[] { "read", "--pid", processId.ToString(), "--handle", handle, "--fields", "SelectedIndex", "--compact" }, TimeSpan.FromSeconds(15));
        read.ExitCode.Should().Be(0, FormatCommandFailure("read selection", read));
        using var result = JsonDocument.Parse(read.StandardOutput);
        return result.RootElement.GetProperty("data").GetProperty("value").GetProperty("SelectedIndex").GetProperty("value").GetInt32();
    }

    private static async Task WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
                throw new XunitException($"Sample process exited with code {process.ExitCode} before creating a window.");
            if (process.MainWindowHandle != IntPtr.Zero)
                return;
            await Task.Delay(100);
        }

        throw new XunitException($"Sample process {process.Id} did not create a main window within {timeout}.");
    }

    private static async Task WaitForModuleAsync(int processId, string moduleName, bool present, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (GetModuleNames(processId).Contains(moduleName) == present)
                return;
            await Task.Delay(100);
        }

        var expectation = present ? "load" : "unload";
        throw new XunitException($"Process {processId} did not {expectation} {moduleName} within {timeout}.");
    }

    private static string GetProcessArchitecture(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
            return "x86";
        if (!IsWow64Process(process.Handle, out var isWow64))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return isWow64 ? "x86" : "x64";
    }

    private static HashSet<string> GetModuleNames(int processId)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var snapshot = CreateToolhelp32Snapshot(Th32csSnapModule | Th32csSnapModule32, (uint)processId);
            if (snapshot == InvalidHandleValue)
            {
                if (Marshal.GetLastWin32Error() == 24)
                    continue;
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entry = new ModuleEntry32 { Size = (uint)Marshal.SizeOf<ModuleEntry32>() };
                if (!Module32First(snapshot, ref entry))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                do
                {
                    modules.Add(entry.Module);
                }
                while (Module32Next(snapshot, ref entry));

                return modules;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        throw new Win32Exception(24);
    }

    private static async Task<CommandResult> FindSubmitButtonAsync(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        CommandResult? lastResult = null;
        while (DateTime.UtcNow < deadline)
        {
            lastResult = await RunCliAsync(
                new[]
                {
                    "find",
                    "--pid",
                    processId.ToString(),
                    "--name",
                    "SubmitButton",
                    "--compact",
                },
                TimeSpan.FromSeconds(10));

            if (lastResult.ExitCode == 0 && ContainsSubmitButton(lastResult.StandardOutput))
                return lastResult;
            await Task.Delay(250);
        }

        return lastResult ?? throw new XunitException("The CLI find command was not attempted.");
    }

    private static bool ContainsSubmitButton(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("elements").EnumerateArray().Any(element =>
                element.GetProperty("name").GetString() == "SubmitButton");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<string> FindElementHandleAsync(
        int processId, string elementName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        CommandResult? lastResult = null;
        while (DateTime.UtcNow < deadline)
        {
            lastResult = await RunCliAsync(
                new[]
                {
                    "find",
                    "--pid",
                    processId.ToString(),
                    "--name",
                    elementName,
                    "--compact",
                },
                TimeSpan.FromSeconds(10));

            if (lastResult.ExitCode == 0)
            {
                using var document = JsonDocument.Parse(lastResult.StandardOutput);
                var elements = document.RootElement.GetProperty("elements");
                if (elements.GetArrayLength() == 1)
                    return elements[0].GetProperty("handle").GetString()!;
            }
            await Task.Delay(250);
        }

        throw new XunitException(
            $"Element '{elementName}' was not found. " +
            (lastResult == null ? "The CLI find command was not attempted." : FormatCommandFailure("find", lastResult)));
    }

    private static async Task<(int width, int height)> CaptureScreenshotAsync(
        int processId, string elementHandle, string outputPath, bool fullContent)
    {
        var arguments = new List<string>
        {
            "screenshot",
            "--pid",
            processId.ToString(),
            "--handle",
            elementHandle,
            "--out",
            outputPath,
            "--max-width",
            "4000",
            "--max-height",
            "4000",
            "--compact",
        };
        if (fullContent)
            arguments.Add("--full-content");

        var result = await RunCliAsync(arguments, TimeSpan.FromSeconds(20));
        result.ExitCode.Should().Be(0, FormatCommandFailure("screenshot", result));
        File.Exists(outputPath).Should().BeTrue();

        using var document = JsonDocument.Parse(result.StandardOutput);
        return (
            document.RootElement.GetProperty("width").GetInt32(),
            document.RootElement.GetProperty("height").GetInt32());
    }

    private static async Task<CommandResult> RunCliAsync(IEnumerable<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetRequiredEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new XunitException("Failed to start the CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new XunitException($"CLI process timed out after {timeout}.");
        }

        return new CommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
            return;

        process.CloseMainWindow();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) ??
            throw new XunitException($"Environment variable {name} is required.");
    }

    private static string FormatCommandFailure(string command, CommandResult result)
    {
        return $"{command} failed.{Environment.NewLine}" +
               $"stdout: {result.StandardOutput}{Environment.NewLine}" +
               $"stderr: {result.StandardError}";
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry32
    {
        public uint Size;
        public uint ModuleId;
        public uint ProcessId;
        public uint GlobalUsageCount;
        public uint ProcessUsageCount;
        public IntPtr BaseAddress;
        public uint BaseSize;
        public IntPtr ModuleHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Module;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr processHandle, out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Module32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Module32First(IntPtr snapshot, ref ModuleEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Module32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Module32Next(IntPtr snapshot, ref ModuleEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
