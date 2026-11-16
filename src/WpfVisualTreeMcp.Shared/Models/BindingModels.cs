using System.Text.Json.Serialization;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>
/// Information about a data binding.
/// </summary>
public class BindingInfo
{
    public string Property { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public BindingKind? Kind { get; set; }
    public BindingSourceKind? SourceKind { get; set; }
    public BindingRelativeSourceMode? RelativeSourceMode { get; set; }
    public string? SourceType { get; set; }
    public string? ElementName { get; set; }
    public string? AncestorType { get; set; }
    public int? AncestorLevel { get; set; }
    public BindingMode? Mode { get; set; }
    public UpdateSourceTrigger? UpdateTrigger { get; set; }
    public BindingStatus? Status { get; set; }
    public string? CurrentValue { get; set; }
}

/// <summary>
/// Result of a bindings query.
/// </summary>
public class BindingsResult
{
    public ElementInfo Element { get; set; } = new();
    public List<BindingInfo> Bindings { get; set; } = new();
}

/// <summary>
/// Information about a binding error.
/// </summary>
public class BindingError
{
    public string ElementHandle { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty;
    public string? ElementName { get; set; }
    public string Property { get; set; } = string.Empty;
    public string BindingPath { get; set; } = string.Empty;
    public BindingErrorType ErrorType { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of a binding errors query.
/// </summary>
public class BindingErrorsResult
{
    public List<BindingError> Errors { get; set; } = new();
    public int Count { get; set; }
}

/// <summary>
/// Result of a DataContext query.
/// </summary>
public class DataContextResult
{
    public ElementInfo Element { get; set; } = new();
    [JsonIgnore] public DataContextPayload? Payload { get; set; }
    public string? DataContextJson
    {
        get => Payload == null ? null : IpcSerializer.Serialize(Payload);
        set => Payload = string.IsNullOrEmpty(value) ? null : IpcSerializer.Deserialize<DataContextPayload>(value);
    }
}
