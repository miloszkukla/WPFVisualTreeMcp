using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Shared.Ipc;

/// <summary>
/// Base class for all IPC requests.
/// </summary>
public abstract class IpcRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public abstract IpcRequestType RequestType { get; }
}

/// <summary>
/// Base class for all IPC responses.
/// </summary>
public abstract class IpcResponse
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
}

// Visual Tree
public class GetVisualTreeRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.GetVisualTree;
    public string? RootHandle { get; set; }
    public int MaxDepth { get; set; } = 25;
}

public class GetVisualTreeResponse : IpcResponse
{
    public string? TreeJson { get; set; }
    public int TotalElements { get; set; }
    public bool MaxDepthReached { get; set; }
}

// Element Properties
public class GetElementPropertiesRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.GetElementProperties;
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetElementPropertiesResponse : IpcResponse
{
    public string? PropertiesJson { get; set; }
}

// Find Elements
public class FindElementsRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.FindElements;
    public string? RootHandle { get; set; }
    public string? TypeName { get; set; }
    public string? ElementName { get; set; }

    /// <summary>Case-insensitive substring match against the element's visible text content.</summary>
    public string? Text { get; set; }

    public Dictionary<string, string>? PropertyFilter { get; set; }

    /// <summary>When true, only elements currently visible on screen are returned.</summary>
    public bool VisibleOnly { get; set; }

    public int MaxResults { get; set; } = 50;
}

public class FindElementsResponse : IpcResponse
{
    public string? ElementsJson { get; set; }
    public int Count { get; set; }
}

// Find Elements Deep (unlimited search)
public class FindElementsDeepRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.FindElementsDeep;
    public string? RootHandle { get; set; }
    public string? TypeName { get; set; }
    public string? ElementName { get; set; }

    /// <summary>Case-insensitive substring match against the element's visible text content.</summary>
    public string? Text { get; set; }

    public Dictionary<string, string>? PropertyFilter { get; set; }

    /// <summary>When true, only elements currently visible on screen are returned.</summary>
    public bool VisibleOnly { get; set; }
}

public class FindElementsDeepResponse : IpcResponse
{
    public string? ElementsJson { get; set; }
    public int Count { get; set; }
}

// Explain Triggers (evaluate Style/Template triggers + attribute a property's value)
public class ExplainTriggersRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.ExplainTriggers;
    public string ElementHandle { get; set; } = string.Empty;

    /// <summary>Optional: attribute this property's current value to its source (style setter / active trigger / local / default).</summary>
    public string? PropertyName { get; set; }
}

public class ExplainTriggersResponse : IpcResponse
{
    public string? Json { get; set; }
}

// Evaluate Binding (explain why a property has its value)
public class EvaluateBindingRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.EvaluateBinding;
    public string ElementHandle { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
}

public class EvaluateBindingResponse : IpcResponse
{
    public string? EvaluationJson { get; set; }
}

// Snapshot (capture element subtree state for later diff)
public class SnapshotRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.Snapshot;

    /// <summary>Root of the subtree to snapshot. Omit for the whole main window.</summary>
    public string? ElementHandle { get; set; }

    /// <summary>Name to store the snapshot under (e.g. "before"). Auto-generated if omitted.</summary>
    public string? Label { get; set; }

    public int MaxDepth { get; set; } = 25;
}

public class SnapshotResponse : IpcResponse
{
    public string? Label { get; set; }
    public int ElementCount { get; set; }
}

// Diff (compare two snapshots)
public class DiffRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.Diff;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}

public class DiffResponse : IpcResponse
{
    public string? DiffJson { get; set; }
    public int ChangedCount { get; set; }
    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }
}

// Set Property (live-edit a dependency property)
public class SetPropertyRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.SetProperty;
    public string ElementHandle { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>Value as a string, converted to the property's type (e.g. "10,0,10,0", "Red", "Collapsed", "{null}").</summary>
    public string Value { get; set; } = string.Empty;
}

public class SetPropertyResponse : IpcResponse
{
    public string? ElementType { get; set; }

    /// <summary>The value read back after the write (the coerced result).</summary>
    public string? AppliedValue { get; set; }
    public string? ValueType { get; set; }

    /// <summary>What held the property before: "Binding", "Local", or "Unset".</summary>
    public PreviousPropertySource? PreviousSource { get; set; }
}

// Revert Property (undo one or all live edits)
public class RevertPropertyRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.RevertProperty;

    /// <summary>When true, revert every pending live edit. Otherwise revert the most recent match.</summary>
    public bool All { get; set; }

    /// <summary>Optional filter for the single-revert case.</summary>
    public string? ElementHandle { get; set; }
    public string? PropertyName { get; set; }
}

public class RevertPropertyResponse : IpcResponse
{
    /// <summary>How many changes were reverted (0 or 1 for single, N for all).</summary>
    public int RevertedCount { get; set; }

    public string? RevertedHandle { get; set; }
    public string? RevertedProperty { get; set; }

    /// <summary>Live edits still pending after this revert.</summary>
    public int PendingCount { get; set; }
}

// Wait For Element (poll until a condition holds)
public class WaitForElementRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.WaitForElement;
    public string? RootHandle { get; set; }
    public string? TypeName { get; set; }
    public string? ElementName { get; set; }
    public string? Text { get; set; }

    /// <summary>"visible" (default), "exists", "enabled", or "hidden".</summary>
    public WaitCondition Condition { get; set; } = WaitCondition.Visible;

    public int TimeoutMs { get; set; } = 10000;
    public int PollIntervalMs { get; set; } = 250;
}

public class WaitForElementResponse : IpcResponse
{
    /// <summary>True when the condition was met before the timeout.</summary>
    public bool Matched { get; set; }

    /// <summary>Handle of the matched element (for appear/enabled conditions); null otherwise.</summary>
    public string? MatchedHandle { get; set; }

    public string? ElementType { get; set; }
    public int WaitedMs { get; set; }
}

// Layout Info
public class GetLayoutInfoRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.GetLayoutInfo;
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetLayoutInfoResponse : IpcResponse
{
    public string? LayoutJson { get; set; }
}

// Bindings
public class GetBindingsRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.GetBindings;
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetBindingsResponse : IpcResponse
{
    public string? BindingsJson { get; set; }
}

public class GetBindingErrorsRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.GetBindingErrors;
}

public class GetBindingErrorsResponse : IpcResponse
{
    public string? ErrorsJson { get; set; }
    public int Count { get; set; }
}

// DataContext
public class GetDataContextRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.GetDataContext;
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetDataContextResponse : IpcResponse
{
    public string? DataContextJson { get; set; }
}

// Clear Binding Errors
public class ClearBindingErrorsRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.ClearBindingErrors;
}

public class ClearBindingErrorsResponse : IpcResponse
{
    public int Count { get; set; }
}

// Resources & Styles
public class GetResourcesRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.GetResources;
    public ResourceScope Scope { get; set; } = ResourceScope.Application;
    public string? ElementHandle { get; set; }
}

public class GetResourcesResponse : IpcResponse
{
    public string? ResourcesJson { get; set; }
}

public class GetStylesRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.GetStyles;
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetStylesResponse : IpcResponse
{
    public string? StylesJson { get; set; }
}

// Highlight
public class HighlightElementRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.HighlightElement;
    public string ElementHandle { get; set; } = string.Empty;
    public int DurationMs { get; set; } = 2000;
}

public class HighlightElementResponse : IpcResponse { }

// Property Watching
public class WatchPropertyRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.WatchProperty;
    public string ElementHandle { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
}

public class WatchPropertyResponse : IpcResponse
{
    public string WatchId { get; set; } = string.Empty;
    public string? InitialValue { get; set; }
}

// Export
public class ExportTreeRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.ExportTree;
    public string? ElementHandle { get; set; }
    public TreeExportFormat Format { get; set; } = TreeExportFormat.Json;
}

public class ExportTreeResponse : IpcResponse
{
    public string? Content { get; set; }
    public int ElementCount { get; set; }
}

// Screenshot Capture
public class CaptureScreenshotRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.CaptureScreenshot;
    public string? ElementHandle { get; set; }
    public int MaxWidth { get; set; } = 1920;
    public int MaxHeight { get; set; } = 1080;

    /// <summary>
    /// "render" (default): off-screen RenderTargetBitmap of the visual — works even when
    /// covered, but cannot see Popups/menus. "screen": GDI capture of the on-screen pixels —
    /// includes open Popups, ComboBox dropdowns, context menus and tooltips.
    /// </summary>
    public ScreenshotMode Mode { get; set; } = ScreenshotMode.Render;

    /// <summary>
    /// Capture the complete content of a ScrollViewer, including content outside
    /// the current viewport. Supported by render mode only.
    /// </summary>
    public bool FullContent { get; set; }
}

public class CaptureScreenshotResponse : IpcResponse
{
    public string? ImageBase64 { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? ElementType { get; set; }
}

// Click / Interaction
public class ClickElementRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.ClickElement;
    public string ElementHandle { get; set; } = string.Empty;

    /// <summary>When true, perform a real OS mouse click instead of a UI Automation invoke.</summary>
    public bool Physical { get; set; }

    /// <summary>"single" (default), "double" or "right". Double and right clicks always use the physical path.</summary>
    public ClickType? ClickType { get; set; }
}

public class ClickElementResponse : IpcResponse
{
    /// <summary>How the click was carried out: Invoke, Toggle, SelectionItem.Select, ExpandCollapse.*, SyntheticMouse, Physical.</summary>
    public InteractionMethod? Method { get; set; }
    public string? ElementType { get; set; }

    /// <summary>Optional extra detail, e.g. the resulting toggle state or click coordinates.</summary>
    public string? Detail { get; set; }
}

// Set Text / Type into a value-bearing element
public class SetTextRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.SetText;
    public string ElementHandle { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>When true, focus the element and type via OS keyboard input instead of UI Automation.</summary>
    public bool Physical { get; set; }
}

public class SetTextResponse : IpcResponse
{
    /// <summary>How the value was applied: ValueProvider.SetValue, DirectProperty.Text, DirectProperty.Password, Reflected.Text, Physical.</summary>
    public InteractionMethod? Method { get; set; }
    public string? ElementType { get; set; }
    public string? Detail { get; set; }
}

// Select an item in a Selector control (ComboBox, ListBox, TabControl, ...)
public class SelectItemRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.SelectItem;
    public string ElementHandle { get; set; } = string.Empty;

    /// <summary>Visible text of the item to select (case-insensitive substring). Alternative to Index.</summary>
    public string? ItemText { get; set; }

    /// <summary>Zero-based index of the item to select. Alternative to ItemText.</summary>
    public int? Index { get; set; }
}

public class SelectItemResponse : IpcResponse
{
    public InteractionMethod? Method { get; set; }
    public string? ElementType { get; set; }

    /// <summary>Which item ended up selected (index and display text).</summary>
    public string? Detail { get; set; }
}

// Send keyboard shortcut / key combination
public class SendKeysRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.SendKeys;

    /// <summary>Optional. When omitted, keys go to whatever is currently focused.</summary>
    public string? ElementHandle { get; set; }

    /// <summary>Key spec like "Ctrl+S", "Ctrl+Shift+F5", "Enter", "Alt+F4", or just "F1".</summary>
    public string Keys { get; set; } = string.Empty;
}

public class SendKeysResponse : IpcResponse
{
    public InteractionMethod? Method { get; set; }
    public string? ElementType { get; set; }
    public string? Detail { get; set; }
}

// Notifications (Inspector -> Server)
public class PropertyChangedNotification
{
    public NotificationType NotificationType => NotificationType.PropertyChanged;
    public string WatchId { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class BindingErrorNotification
{
    public NotificationType NotificationType => NotificationType.BindingError;
    public string ElementType { get; set; } = string.Empty;
    public string? ElementName { get; set; }
    public string Property { get; set; } = string.Empty;
    public string BindingPath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
