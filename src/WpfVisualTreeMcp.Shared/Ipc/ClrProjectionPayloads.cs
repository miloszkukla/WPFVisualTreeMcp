using System.Text.Json.Serialization;

namespace WpfVisualTreeMcp.Shared.Ipc;

public sealed class PointerProjection
{
    [JsonRequired] public PointerElementProjection Target { get; set; } = new();
    [JsonRequired] public PointerElementProjection? Root { get; set; }
    [JsonRequired] public long WindowHandle { get; set; }
    [JsonRequired] public PointerPointProjection Position { get; set; } = new();
    [JsonRequired] public PointerPointProjection Center { get; set; } = new();
    [JsonRequired] public PointerPointProjection? CenterInRoot { get; set; }
    [JsonRequired] public bool IsMouseOver { get; set; }
    [JsonRequired] public bool IsMeasureValid { get; set; }
    [JsonRequired] public bool IsArrangeValid { get; set; }
    [JsonRequired] public PointerHitProjection DirectlyOver { get; set; } = new();
    [JsonRequired] public PointerHitProjection Captured { get; set; } = new();
    [JsonRequired] public PointerHitProjection CenterHit { get; set; } = new();
}
public sealed class PointerPointProjection { [JsonRequired] public double X { get; set; } [JsonRequired] public double Y { get; set; } }
public sealed class PointerElementProjection { [JsonRequired] public string? Type { get; set; } [JsonRequired] public string? Name { get; set; } [JsonRequired] public string? AutomationId { get; set; } }
public sealed class PointerHitProjection
{
    [JsonRequired] public PointerElementProjection? Element { get; set; }
    [JsonRequired] public bool TargetOrDescendant { get; set; }
    [JsonRequired] public List<PointerElementProjection> Ancestors { get; set; } = new();
    [JsonRequired] public bool AncestorsTruncated { get; set; }
}
public sealed class TextLayoutProjection
{
    [JsonRequired] public string Text { get; set; } = "";
    [JsonRequired] public long WindowHandle { get; set; }
    [JsonRequired] public bool Visible { get; set; }
    [JsonRequired] public bool Truncated { get; set; }
    [JsonRequired] public bool DeadlineReached { get; set; }
    [JsonRequired] public List<TextRunProjection> Runs { get; set; } = new();
}
public sealed class TextRunProjection { public string Text { get; set; } = ""; public int Offset { get; set; } [JsonRequired] public List<TextCharacterProjection> Characters { get; set; } = new(); }
public sealed class TextCharacterProjection
{
    [JsonRequired] public int Offset { get; set; } [JsonRequired] public int Length { get; set; }
    [JsonRequired] public double X { get; set; } [JsonRequired] public double Y { get; set; } [JsonRequired] public double Width { get; set; } [JsonRequired] public double Height { get; set; }
    [JsonRequired] public bool Visible { get; set; }
}
