using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Shared.Ipc;

public class ScrollViewerRequest : IpcRequest
{
    public override string RequestType => "ScrollViewer";
    public string ElementHandle { get; set; } = string.Empty;
    public ScrollViewerAction Action { get; set; }
    public string? Generation { get; set; }
    public long DeadlineUtcTicks { get; set; }
}

public class ScrollViewerResponse : IpcResponse
{
    public string? Method { get; set; }
    public string? ElementType { get; set; }
    public string? Detail { get; set; }
}

public class ScrollItemIntoViewRequest : IpcRequest
{
    public override string RequestType => "ScrollItemIntoView";
    public string ListHandle { get; set; } = string.Empty;
    public int Index { get; set; } = -1;
    public string? Generation { get; set; }
    public long DeadlineUtcTicks { get; set; }
}
