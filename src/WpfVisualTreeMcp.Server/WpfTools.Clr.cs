using System.ComponentModel;
using ModelContextProtocol.Server;

namespace WpfVisualTreeMcp.Server;

public partial class WpfTools
{
    [McpServerTool, Description("Capture a bounded subtree, properties, binding state and requested DataContext fields. Independent parts report errors without losing successful observations.")]
    public async Task<object> WpfDiagnosticCapture(string element_handle, string[]? data_context_fields = null)
    {
        var identity = await _ipcBridge.GetInspectionIdentityAsync();
        var result = new Dictionary<string, object?> { ["generation"] = identity.Generation, ["processId"] = identity.ProcessId };
        var deadline = DateTime.UtcNow.AddSeconds(25);
        async Task Collect(string name, Func<Task<object?>> action)
        {
            if (DateTime.UtcNow >= deadline) { result[name] = new { error = "Capture budget exhausted." }; return; }
            try { result[name] = await action().WaitAsync(deadline - DateTime.UtcNow); }
            catch (Exception error) { result[name] = new { error = error.GetBaseException().Message }; }
        }
        await Collect("tree", async () => await _ipcBridge.GetVisualTreeAsync(element_handle, 3));
        await Collect("properties", async () => await _ipcBridge.GetElementPropertiesAsync(element_handle));
        await Collect("bindings", async () => await _ipcBridge.GetBindingsAsync(element_handle));
        await Collect("dataContext", async () => await _ipcBridge.ReadClrAsync(element_handle, "DataContext", data_context_fields ?? [], 0, 20, identity.Generation));
        await Collect("screenshot", async () => await _ipcBridge.CaptureScreenshotAsync(element_handle, 1024, 768));
        return result;
    }

    [McpServerTool, Description("Read Inspector generation and process identity before reusing temporary handles.")]
    public Task<WpfVisualTreeMcp.Shared.Ipc.InspectionIdentityResponse> WpfInspectionIdentity() => _ipcBridge.GetInspectionIdentityAsync();

    [McpServerTool, Description("Read a selected CLR property/field path. Project collections with requested fields and bounded paging; no method execution.")]
    public Task<WpfVisualTreeMcp.Shared.Ipc.ClrReadResponse> WpfReadClr(string element_handle, string path = "", string[]? fields = null,
        int skip = 0, int take = 50, string? generation = null) =>
        _ipcBridge.ReadClrAsync(element_handle, path, fields ?? [], skip, take, generation);

    [McpServerTool, Description("List public CLR properties, fields and method signatures without invoking members.")]
    public Task<WpfVisualTreeMcp.Shared.Ipc.ClrReadResponse> WpfClrMembers(string element_handle, string path = "",
        int skip = 0, int take = 100, string? generation = null) =>
        _ipcBridge.ReadClrAsync(element_handle, path, [], skip, take, generation, true);

    [McpServerTool, Description("Reacquire an element by selector. Returns all candidates when the selector is ambiguous; never chooses the first silently.")]
    public async Task<object> WpfReacquire(string? type_name = null, string? element_name = null, string? text = null)
    {
        var identity = await _ipcBridge.GetInspectionIdentityAsync();
        var matches = await _ipcBridge.FindElementsAsync(null, type_name, element_name, text, null, false, 200);
        return new { identity.Generation, identity.ProcessId, unique = matches.Count == 1, ambiguous = matches.Count > 1, matches };
    }
}
