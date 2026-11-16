using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using WpfVisualTreeMcp.Server.Controls;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Models;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Server.Controls;

// Call the user's inspector library directly; one process manager/bridge per test.
// The library handles injection and bounded IPC to the application's WPF dispatcher.
public class WpfSession(int processId, string output) : IWpfControlTransport
{
    public int ProcessId { get; } = processId;
    public static string LibraryPath => typeof(NamedPipeBridge).Assembly.Location;
    private readonly ProcessManager processManager = new(NullLogger<ProcessManager>.Instance);
    private readonly long processStartedUtcTicks = Process.GetProcessById(processId).StartTime.ToUniversalTime().Ticks;
    public CancellationToken CancellationToken { get; set; }
    public Action<object?>? Waiting { get; set; }
    private NamedPipeBridge? bridge;
    private string? generation;
    private WpfControls? controls;
    public WpfControls Controls => controls ??= new WpfControls(this);
    public Task<T> FindAsync<T>(string? automationId = null, string? name = null, string? text = null,
        WpfElement? root = null, bool visibleOnly = true, int timeoutMs = 10000, bool matchType = true) where T : WpfElement =>
        Controls.FindAsync<T>(automationId, name, text, root, visibleOnly, timeoutMs, matchType);
    public long ProcessStartedUtcTicks => processStartedUtcTicks;
    public bool IsAttached => bridge != null && generation != null;
    private NamedPipeBridge Bridge => bridge ?? throw new InvalidOperationException("Attach to the application first.");

    private static Dictionary<string, string>? ExactCriteria(string? automationId, string? type)
    {
        if (automationId == null && type == null) return null;
        var criteria = new Dictionary<string, string>();
        if (automationId != null) criteria["$automationId"] = automationId;
        if (type != null) criteria["$type"] = type;
        return criteria;
    }

    private async Task<T> ObserveAsync<T>(object? arguments, Func<Task<T>> action, [CallerMemberName] string operation = "")
    {
        var timer = Stopwatch.StartNew();
        try
        {
            CancellationToken.ThrowIfCancellationRequested();
            using var process = Process.GetProcessById(ProcessId);
            if (process.StartTime.ToUniversalTime().Ticks != processStartedUtcTicks)
                throw new InvalidOperationException("The application process identity changed; reacquire the target.");
            if (bridge != null && generation != null)
            {
                var identity = await Bridge.GetInspectionIdentityAsync().WaitAsync(CancellationToken);
                if (!identity.Success || identity.Generation != generation)
                    throw new InvalidOperationException("The inspector generation changed; reacquire the target.");
            }
            var result = await action().WaitAsync(CancellationToken);
            if (result is IpcResponse { Success: false } response)
                throw new InvalidOperationException(response.Error ?? operation + " failed.");
            await File.AppendAllTextAsync(Path.Combine(output, "commands.jsonl"), JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow, operation, arguments, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds, result
            }) + Environment.NewLine);
            return result;
        }
        catch (Exception error)
        {
            await File.AppendAllTextAsync(Path.Combine(output, "commands.jsonl"), JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow, operation, arguments, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds, error = error.ToString()
            }) + Environment.NewLine);
            throw;
        }
    }

    public async Task AttachAsync()
    {
        using var target = Process.GetProcessById(ProcessId);
        if (target.SessionId != Process.GetCurrentProcess().SessionId)
            throw new InvalidOperationException("Run this test in the same interactive Windows session as the application.");
        var session = await ObserveAsync(new { ProcessId }, () => processManager.AttachToProcessAsync(ProcessId, null, autoInject: true));
        EnsureInspectorReady(session, ProcessId);
        bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, processManager);
        var identity = await ObserveAsync(null, Bridge.GetInspectionIdentityAsync, nameof(GetIdentityAsync));
        if (!identity.Success || string.IsNullOrEmpty(identity.Generation))
            throw new InvalidOperationException(identity.Error ?? "Inspector did not return its identity.");
        generation = identity.Generation;
    }

    internal static void EnsureInspectorReady(InspectionSession session, int processId)
    {
        if (session.InspectorStatus is not (InspectorAttachmentStatus.LoadedExisting or InspectorAttachmentStatus.LoadedInjected))
            throw new InvalidOperationException("Inspector attach failed for PID " + processId + ": " + session.InspectorStatus +
                (session.InspectorStatusDetail == null ? "" : ": " + session.InspectorStatusDetail));
    }

    public async Task<string> FindAsync(string automationId, string type, string? root = null)
    {
        // Narrow by type/root, then match the returned AutomationId exactly.
        var result = await ObserveAsync(new { automationId, type, root }, () =>
            Bridge.FindElementsAsync(root, null, null, null, ExactCriteria(automationId, type), visibleOnly: true, maxResults: 200));
        if (result.Count >= 200)
            throw new InvalidOperationException($"Narrow the search for {automationId}; candidate limit reached.");
        var elements = result.Elements.Where(element => element.AutomationId == automationId).ToArray();
        if (elements.Length != 1)
            throw new InvalidOperationException($"Expected one visible {automationId}, found {elements.Length}.");
        return elements[0].Handle;
    }

    public async Task<JsonElement> ReadAsync(string handle, string path, string fields)
    {
        var response = await ObserveAsync(new { handle, path, fields }, () =>
            Bridge.ReadClrAsync(handle, path, fields.Split(','), 0, 200, generation));
        if (response.Data == null) throw new InvalidOperationException("Inspector returned no CLR data.");
        using var projection = JsonDocument.Parse(IpcSerializer.Serialize(response.Data));
        return projection.RootElement.Clone();
    }

    public static JsonElement Value(JsonElement projection, string field)
    {
        var member = projection.GetProperty(field);
        if (!member.TryGetProperty("value", out var value))
            throw new InvalidOperationException($"Cannot read {field}: {member}");
        return value;
    }

    public async Task<string> RenderedTextAsync(string handle)
    {
        var projection = (await ReadAsync(handle, "", "$renderedText")).GetProperty("value");
        var member = projection.GetProperty("$renderedText");
        if (member.TryGetProperty("missing", out var missing) && missing.GetBoolean())
        {
            var textBlocks = await ObserveAsync(new { handle }, () =>
                Bridge.FindElementsAsync(handle, null, null, null, ExactCriteria(null, "TextBlock"), false, 1));
            if (textBlocks.Elements.Any(element => element.Handle == handle))
                throw new InvalidOperationException("This TextBlock inspector payload lacks rendered text support; restart the target after deployment.");
            return Value((await ReadAsync(handle, "", "Text")).GetProperty("value"), "Text").GetString()!;
        }
        var value = Value(projection, "$renderedText");
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean())
            throw new InvalidOperationException("Rendered text observation was truncated: " + handle);
        return value.GetString()!;
    }

    public async Task SelectItemAsync(string handle, string text) =>
        await ObserveAsync(new { handle, text }, () => Bridge.SelectItemAsync(handle, text, null));

    public async Task SearchAsync(string handle, string text)
    {
        await ObserveAsync(new { handle, text }, () => Bridge.SetTextAsync(handle, text, physical: false), nameof(SetTextAsync));
        await ObserveAsync(new { handle, keys = "Enter" }, () => Bridge.SendKeysAsync(handle, "Enter"), nameof(SendKeysAsync));
    }

    public async Task ScreenshotAsync(string path)
    {
        await ObserveAsync(new { path }, async () =>
        {
            var shot = await Bridge.CaptureScreenshotAsync(null, 1920, 1080, mode: ScreenshotMode.Screen);
            if (string.IsNullOrEmpty(shot.ImageBase64))
                throw new InvalidOperationException("Inspector returned no screenshot.");
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(shot.ImageBase64));
            return new { path, shot.Width, shot.Height };
        });
    }

    public async Task<string> FindControlAsync(string? automationId = null, string? type = null, string? name = null,
        string? text = null, string? root = null, bool visibleOnly = true, int timeoutMs = 10000)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            var result = await ObserveAsync(new { automationId, type, name, text, root }, () =>
                Bridge.FindElementsAsync(root, null, name, text, ExactCriteria(automationId, type), visibleOnly, 200));
            if (result.Count >= 200) throw new InvalidOperationException("Selector reached the candidate limit; narrow its scope.");
            var matches = result.Elements.Where(element => (automationId == null || element.AutomationId == automationId)
                && (name == null || element.Name == name)).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException($"Ambiguous selector {automationId ?? name ?? text ?? type}: {matches.Length} matches.");
            if (matches.Length == 1) { Waiting?.Invoke(null); return matches[0].Handle; }
            Waiting?.Invoke(new { operation = "find", automationId, type, name, text, root, expectedMatches = 1, actualMatches = 0 });
            if (timer.ElapsedMilliseconds >= timeoutMs) break;
            await Task.Delay(150, CancellationToken);
        } while (true);
        throw new InvalidOperationException($"Control not found: {automationId ?? name ?? text ?? type}");
    }

    public async Task ClickAsync(string handle, ClickType? clickType = null) =>
        await ObserveAsync(new { handle, clickType }, () => Bridge.ClickElementAsync(handle, false, clickType));

    public async Task PhysicalClickAsync(string handle, ClickType? clickType = null) =>
        await ObserveAsync(new { handle, clickType }, () => Bridge.ClickElementAsync(handle, true, clickType));

    public async Task<(string Handle, string? Name, string? Text, string? AutomationId)[]> FindControlsAsync(string? type = null,
        string? name = null, string? text = null, string? root = null, bool visibleOnly = true)
    {
        var result = await ObserveAsync(new { type, name, text, root }, () => Bridge.FindElementsAsync(root, null, name, text, ExactCriteria(null, type), visibleOnly, 200));
        if (result.Count >= 200) throw new InvalidOperationException("The candidate limit was reached; narrow the search.");
        return result.Elements.Where(element => name == null || element.Name == name).Select(element => (element.Handle, element.Name, element.Text, element.AutomationId)).ToArray();
    }

    public async Task SetTextAsync(string handle, string text) =>
        await ObserveAsync(new { handle, text }, () => Bridge.SetTextAsync(handle, text, false));

    public async Task HoverAsync(string handle)
    {
        var found = await ObserveAsync(new { handle }, () => Bridge.FindElementsAsync(handle, null, null, null, null, true, 1));
        var element = found.Elements.SingleOrDefault(item => item.Handle == handle);
        var bounds = element?.ScreenBounds ?? throw new InvalidOperationException("Target has no visible screen bounds: " + handle);
        if (!SetCursorPos((int)(bounds.X + bounds.Width / 2), (int)(bounds.Y + bounds.Height / 2))) throw new InvalidOperationException("Could not move the pointer to the target.");
        await Task.Delay(100, CancellationToken);
    }

    public async Task<ScreenBounds?> GetScreenBoundsAsync(string handle)
    {
        var found = await ObserveAsync(new { handle }, () => Bridge.FindElementsAsync(handle, null, null, null, null, true, 1));
        return found.Elements.SingleOrDefault(item => item.Handle == handle)?.ScreenBounds;
    }

    public async Task ScrollAsync(string handle, int delta)
    {
        await HoverAsync(handle);
        mouse_event(0x0800, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
        await Task.Delay(100, CancellationToken);
    }

    public async Task WaitForStableBoundsAsync(string handle)
    {
        var timer = Stopwatch.StartNew();
        string? previous = null;
        do
        {
            var result = await ObserveAsync(new { handle }, () => Bridge.FindElementsAsync(handle, null, null, null, null, true, 1));
            var bounds = result.Elements.SingleOrDefault(element => element.Handle == handle)?.ScreenBounds;
            var current = bounds != null && bounds.Width > 0 && bounds.Height > 0 ? JsonSerializer.Serialize(bounds) : null;
            if (current != null && current == previous) { Waiting?.Invoke(null); return; }
            previous = current;
            Waiting?.Invoke(new { condition = "Visible popup layout has stopped moving", handle, bounds });
            await Task.Delay(75, CancellationToken);
        } while (timer.Elapsed < TimeSpan.FromSeconds(3));
        throw new TimeoutException("Visible screen bounds did not stabilize for " + handle);
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    public async Task SendKeysAsync(string handle, string keys) =>
        await ObserveAsync(new { handle, keys }, () => Bridge.SendKeysAsync(handle, keys));

    public async Task ScrollViewerAsync(string handle, ScrollViewerAction action) =>
        await ObserveAsync(new { handle, action }, () => Bridge.ScrollViewerAsync(handle, action, generation, CancellationToken));

    public async Task ScrollItemIntoViewAsync(string handle, int index) =>
        await ObserveAsync(new { handle, index }, () => Bridge.ScrollItemIntoViewAsync(handle, index, generation, CancellationToken));

    public async Task SetPropertyAsync(string handle, string property, string value) =>
        await ObserveAsync(new { handle, property, value }, () => Bridge.SetPropertyAsync(handle, property, value));

    public Task<JsonElement> GetIdentityAsync() =>
        ObserveInspectionAsync(null, "", async () => await Bridge.GetInspectionIdentityAsync());

    public Task<InspectionIdentityResponse> GetTypedIdentityAsync() =>
        ObserveAsync(new { handle = (string?)null, path = "", fields = "" }, () => Bridge.GetInspectionIdentityAsync(), nameof(GetIdentityAsync));

    public Task<VisualTreeResult> GetTypedTreeAsync(string? handle = null) =>
        ObserveAsync(new { handle, path = "", fields = "" }, () => Bridge.GetVisualTreeAsync(handle, 3), nameof(GetTreeAsync));

    public Task<BindingsResult> GetTypedBindingsAsync(string handle) =>
        ObserveAsync(new { handle, path = "", fields = "" }, () => Bridge.GetBindingsAsync(handle ?? throw new ArgumentException("A handle is required.")), nameof(GetBindingsAsync));

    public Task<JsonElement> GetTreeAsync(string? handle = null) =>
        ObserveInspectionAsync(handle, "", async () => await Bridge.GetVisualTreeAsync(handle, 3));

    public Task<JsonElement> GetPropertiesAsync(string handle) =>
        ObserveInspectionAsync(handle, "", async () => await Bridge.GetElementPropertiesAsync(handle ?? throw new ArgumentException("A handle is required.")));

    public Task<ElementPropertiesResult> GetTypedPropertiesAsync(string handle) =>
        ObserveAsync(new { handle, path = "", fields = "" }, () => Bridge.GetElementPropertiesAsync(handle ?? throw new ArgumentException("A handle is required.")), nameof(GetPropertiesAsync));

    public Task<JsonElement> GetBindingsAsync(string handle) =>
        ObserveInspectionAsync(handle, "", async () => await Bridge.GetBindingsAsync(handle ?? throw new ArgumentException("A handle is required.")));

    public Task<JsonElement> GetBindingErrorsAsync() =>
        ObserveInspectionAsync(null, "", async () => await Bridge.GetBindingErrorsAsync());

    public Task<BindingErrorsResult> GetTypedBindingErrorsAsync() =>
        ObserveAsync(new { handle = (string?)null, path = "", fields = "" }, () => Bridge.GetBindingErrorsAsync(), nameof(GetBindingErrorsAsync));

    public Task<JsonElement> GetLayoutAsync(string handle) =>
        ObserveInspectionAsync(handle, "", async () => await Bridge.GetLayoutInfoAsync(handle ?? throw new ArgumentException("A handle is required.")));

    public Task<LayoutInfoResult> GetTypedLayoutAsync(string handle) =>
        ObserveAsync(new { handle, path = "", fields = "" }, () => Bridge.GetLayoutInfoAsync(handle ?? throw new ArgumentException("A handle is required.")), nameof(GetLayoutAsync));

    public Task<JsonElement> GetMembersAsync(string? handle, string path = "") =>
        ObserveInspectionAsync(handle, path, async () => await Bridge.ReadClrAsync(handle!, path, [], 0, 50, generation, true));

    private async Task<JsonElement> ObserveInspectionAsync(string? handle, string path, Func<Task<object>> action, [CallerMemberName] string operation = "")
    {
        var result = await ObserveAsync(new { handle, path, fields = "" }, action, operation);
        return JsonSerializer.SerializeToElement(result);
    }

    Task<string> IWpfControlTransport.FindAsync(string? automationId, string? type, string? name, string? text, string? root, bool visibleOnly, int timeoutMs) =>
        FindControlAsync(automationId, type, name, text, root, visibleOnly, timeoutMs);

    async Task<bool> IWpfControlTransport.IsTypeAsync(string handle, string type)
    {
        var result = await ObserveAsync(new { handle, type }, () =>
            Bridge.FindElementsAsync(handle, null, null, null, ExactCriteria(null, type), false, 1));
        return result.Elements.Any(element => element.Handle == handle);
    }

    Task<(string Handle, string? Name, string? Text, string? AutomationId)[]> IWpfControlTransport.FindAllAsync(string? type, string? name, string? text, string? root, bool visibleOnly) =>
        FindControlsAsync(type, name, text, root, visibleOnly);

    Task IWpfControlTransport.ClickAsync(string handle, bool physical, ClickType? clickType) =>
        physical ? PhysicalClickAsync(handle, clickType) : ClickAsync(handle, clickType);
}
