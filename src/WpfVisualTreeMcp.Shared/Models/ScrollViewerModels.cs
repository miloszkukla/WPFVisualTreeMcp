namespace WpfVisualTreeMcp.Shared.Models;

public enum ScrollViewerAction
{
    Home = 1,
    PageDown = 2,
    PageUp = 3,
    End = 4
}

public class ScrollViewerResult
{
    public string Method { get; set; } = string.Empty;
    public string? ElementType { get; set; }
    public string? Detail { get; set; }
}
