using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using ModelContextProtocol.Server;

namespace WpfVisualTreeMcp.Server;

public partial class WpfTools
{
    [McpServerTool, Description("Capture a bounded subtree, properties, binding state and requested DataContext fields. Independent parts report errors without losing successful observations.")]
    public async Task<DiagnosticCaptureResult> WpfDiagnosticCapture(string element_handle, string[]? data_context_fields = null)
    {
        var identity = await _ipcBridge.GetInspectionIdentityAsync();
        var deadline = DateTime.UtcNow.AddSeconds(25);
        async Task<CaptureSection<T>> Collect<T>(Func<Task<T>> action)
        {
            if (DateTime.UtcNow >= deadline) return CaptureSection<T>.Failed("Capture budget exhausted.");
            try { return CaptureSection<T>.Succeeded(await action().WaitAsync(deadline - DateTime.UtcNow)); }
            catch (Exception error) { return CaptureSection<T>.Failed(error.GetBaseException().Message); }
        }
        return new DiagnosticCaptureResult
        {
            Generation = identity.Generation,
            ProcessId = identity.ProcessId,
            Tree = await Collect(() => _ipcBridge.GetVisualTreeAsync(element_handle, 3)),
            Properties = await Collect(() => _ipcBridge.GetElementPropertiesAsync(element_handle)),
            Bindings = await Collect(() => _ipcBridge.GetBindingsAsync(element_handle)),
            DataContext = await Collect(() => _ipcBridge.ReadClrAsync(element_handle, "DataContext", data_context_fields ?? [], 0, 20, identity.Generation)),
            Screenshot = await Collect(() => _ipcBridge.CaptureScreenshotAsync(element_handle, 1024, 768))
        };
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

public sealed class DiagnosticCaptureResult
{
    [JsonPropertyName("generation")] public string Generation { get; set; } = string.Empty;
    [JsonPropertyName("processId")] public int ProcessId { get; set; }
    [JsonPropertyName("tree")] public CaptureSection<VisualTreeResult> Tree { get; set; } = null!;
    [JsonPropertyName("properties")] public CaptureSection<ElementPropertiesResult> Properties { get; set; } = null!;
    [JsonPropertyName("bindings")] public CaptureSection<BindingsResult> Bindings { get; set; } = null!;
    [JsonPropertyName("dataContext")] public CaptureSection<ClrReadResponse> DataContext { get; set; } = null!;
    [JsonPropertyName("screenshot")] public CaptureSection<ScreenshotResult> Screenshot { get; set; } = null!;
}

[JsonConverter(typeof(CaptureSectionConverterFactory))]
public sealed class CaptureSection<T>
{
    public T? Value { get; private init; }
    public string? Error { get; private init; }
    public static CaptureSection<T> Succeeded(T? value) => new() { Value = value };
    public static CaptureSection<T> Failed(string error) => new() { Error = error };
}

public sealed class CaptureSectionConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(CaptureSection<>);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(CaptureSectionConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

public sealed class CaptureSectionConverter<T> : JsonConverter<CaptureSection<T>>
{
    public override CaptureSection<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject().Count() == 1
            && document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            return CaptureSection<T>.Failed(error.GetString()!);
        return CaptureSection<T>.Succeeded(document.RootElement.Deserialize<T>(options));
    }

    public override void Write(Utf8JsonWriter writer, CaptureSection<T> value, JsonSerializerOptions options)
    {
        if (value.Error == null) JsonSerializer.Serialize(writer, value.Value, options);
        else
        {
            writer.WriteStartObject();
            writer.WriteString("error", value.Error);
            writer.WriteEndObject();
        }
    }
}
