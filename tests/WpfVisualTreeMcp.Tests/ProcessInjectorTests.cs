using FluentAssertions;
using System.Diagnostics;
using WpfVisualTreeMcp.Injector;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class ProcessInjectorTests
{
    private readonly ProcessInjector _injector;

    public ProcessInjectorTests()
    {
        _injector = new ProcessInjector();
    }

    [Fact]
    public void GetInspectorDllPath_ReturnsValidPath()
    {
        // Act
        var path = _injector.GetInspectorDllPath();

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().EndWith("WpfVisualTreeMcp.Inspector.dll");
    }

    [Fact]
    public void IsManagedProcess_WithCurrentProcess_ReturnsTrue()
    {
        // Arrange - current process is a .NET process
        var process = Process.GetCurrentProcess();

        // Act
        var result = _injector.IsManagedProcess(process);

        // Assert - current test process should be managed
        result.Should().BeTrue();
    }

    [Fact]
    public void IsInspectorLoaded_WithCurrentProcess_ReturnsFalse()
    {
        // Arrange - current process doesn't have Inspector loaded
        var process = Process.GetCurrentProcess();

        // Act
        var result = _injector.IsInspectorLoaded(process);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void InjectIntoProcess_WithNonExistentDll_ThrowsFileNotFoundException()
    {
        // Arrange
        var processId = Process.GetCurrentProcess().Id;
        var fakeDllPath = @"C:\NonExistent\Fake.dll";

        // Act & Assert
        var act = () => _injector.InjectIntoProcess(processId, fakeDllPath);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void InjectIntoProcess_WithInvalidProcessId_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidProcessId = int.MaxValue - 1;
        var dllPath = typeof(ProcessInjector).Assembly.Location;

        // Act & Assert
        var act = () => _injector.InjectIntoProcess(invalidProcessId, dllPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void InjectIntoProcess_WithValidManagedProcess_RequiresBootstrapperDll()
    {
        // This test verifies that injection is implemented and either:
        // - Throws FileNotFoundException if bootstrapper DLL is missing
        // - Proceeds to actual injection if bootstrapper is found (may fail for other reasons)

        // Arrange
        var process = Process.GetCurrentProcess();
        var dllPath = _injector.GetInspectorDllPath();

        // Skip if Inspector DLL doesn't exist (build required)
        if (!System.IO.File.Exists(dllPath))
        {
            return; // Skip test if Inspector DLL not built
        }

        var bootstrapperPath = _injector.GetBootstrapperDllPath();
        var bootstrapperExists = System.IO.File.Exists(bootstrapperPath);

        // Act & Assert
        var act = () => _injector.InjectIntoProcess(process.Id, dllPath);

        if (!bootstrapperExists)
        {
            // Bootstrapper not built: should throw FileNotFoundException
            act.Should().Throw<FileNotFoundException>(
                "Should throw FileNotFoundException for missing bootstrapper DLL")
                .WithMessage("*Bootstrapper*");
        }
        else
        {
            // Bootstrapper exists: injection will proceed but may fail (we're injecting into ourselves)
            // Just verify it doesn't throw NotImplementedException
            act.Should().NotThrow<NotImplementedException>(
                "Injection should be implemented, not stubbed");
        }
    }

    [Fact]
    public void GetBootstrapperDllPath_ReturnsValidPath()
    {
        // Act
        var path = _injector.GetBootstrapperDllPath();

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().EndWith("WpfInspectorBootstrapper.dll");
    }

    [Fact]
    public void HelperEnvironmentPrefersExplicitX86RootWithoutChangingParent()
    {
        var parentRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var parentX86 = Environment.GetEnvironmentVariable("DOTNET_ROOT_X86");
        var parentLegacy = Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)");
        var start = new ProcessStartInfo();
        start.Environment.Clear();
        start.Environment["DOTNET_ROOT"] = @"C:\wrong-x64";
        start.Environment["DOTNET_ROOT_X86"] = @"D:\explicit-x86";
        start.Environment["DOTNET_ROOT(x86)"] = @"E:\legacy-x86";

        ProcessInjector.ConfigureHelperEnvironment(start, false, @"C:\not-used");

        start.Environment["DOTNET_ROOT"].Should().Be(@"D:\explicit-x86");
        start.Environment["DOTNET_ROOT_X86"].Should().Be(@"D:\explicit-x86");
        start.Environment["DOTNET_ROOT(x86)"].Should().Be(@"D:\explicit-x86");
        Environment.GetEnvironmentVariable("DOTNET_ROOT").Should().Be(parentRoot);
        Environment.GetEnvironmentVariable("DOTNET_ROOT_X86").Should().Be(parentX86);
        Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)").Should().Be(parentLegacy);
    }

    [Fact]
    public void HelperEnvironmentUsesLegacyX86RootWhenModernOverrideIsEmpty()
    {
        var start = new ProcessStartInfo();
        start.Environment.Clear();
        start.Environment["DOTNET_ROOT"] = @"C:\wrong-x64";
        start.Environment["DOTNET_ROOT_X86"] = " ";
        start.Environment["DOTNET_ROOT(x86)"] = @"D:\legacy-x86";

        ProcessInjector.ConfigureHelperEnvironment(start, false, @"C:\not-used");

        start.Environment["DOTNET_ROOT"].Should().Be(@"D:\legacy-x86");
        start.Environment["DOTNET_ROOT_X86"].Should().Be(@"D:\legacy-x86");
    }

    [Fact]
    public void HelperEnvironmentUsesExistingProgramFilesX86Runtime()
    {
        var parent = Directory.CreateTempSubdirectory("wpf-helper-runtime-").FullName;
        var runtime = Directory.CreateDirectory(Path.Combine(parent, "dotnet")).FullName;
        try
        {
            var start = new ProcessStartInfo();
            start.Environment.Clear();
            start.Environment["DOTNET_ROOT"] = @"C:\wrong-x64";

            ProcessInjector.ConfigureHelperEnvironment(start, false, parent);

            start.Environment["DOTNET_ROOT"].Should().Be(runtime);
            start.Environment["DOTNET_ROOT_X86"].Should().Be(runtime);
            start.Environment["DOTNET_ROOT(x86)"].Should().Be(runtime);
        }
        finally { Directory.Delete(runtime); Directory.Delete(parent); }
    }

    [Fact]
    public void HelperEnvironmentPreservesInheritedRootWhenX86RuntimeIsUnavailable()
    {
        var start = new ProcessStartInfo();
        start.Environment.Clear();
        start.Environment["DOTNET_ROOT"] = @"D:\custom-runtime";

        ProcessInjector.ConfigureHelperEnvironment(start, false, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        start.Environment.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string?>("DOTNET_ROOT", @"D:\custom-runtime"));
    }

    [Fact]
    public void HelperEnvironmentLeavesX64HelperEnvironmentUnchanged()
    {
        var start = new ProcessStartInfo();
        start.Environment.Clear();
        start.Environment["DOTNET_ROOT"] = @"C:\private-x64";
        start.Environment["DOTNET_ROOT_X86"] = @"D:\explicit-x86";

        ProcessInjector.ConfigureHelperEnvironment(start, true, @"C:\Program Files (x86)");

        start.Environment["DOTNET_ROOT"].Should().Be(@"C:\private-x64");
        start.Environment["DOTNET_ROOT_X86"].Should().Be(@"D:\explicit-x86");
        start.Environment.Count.Should().Be(2);
    }

    [Fact]
    public void HelperFailureReportsExitCodeProcessAndBothOutputStreams()
    {
        var action = () => ProcessInjector.ValidateHelperExit(17, 1234, "helper output", "runtime could not load");

        action.Should().Throw<InvalidOperationException>().WithMessage("*code 17*process 1234*stdout: helper output*stderr: runtime could not load*");
    }

    [Fact]
    public void HelperFailureBoundsEachOutputStream()
    {
        var action = () => ProcessInjector.ValidateHelperExit(1, 1234, new string('x', 9000), new string('y', 9000));

        var error = action.Should().Throw<InvalidOperationException>().Which;
        error.Message.Should().Contain(new string('x', 4096) + " [truncated]");
        error.Message.Should().Contain(new string('y', 4096) + " [truncated]");
        error.Message.Length.Should().BeLessThan(8500);
    }

    [Fact]
    public void HelperSuccessRetainsInformationalWarningsWithoutFailure()
    {
        var action = () => ProcessInjector.ValidateHelperExit(0, 1234, "injected", "informational warning");

        action.Should().NotThrow();
    }

    [Fact]
    public async Task HelperOutputDrainsPastCaptureBound()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('x', 100000)));
        using var reader = new StreamReader(stream);

        var output = await ProcessInjector.ReadHelperOutputAsync(reader);

        output.Should().Be(new string('x', 4096) + " [truncated]");
        stream.Position.Should().Be(stream.Length);
    }
}
