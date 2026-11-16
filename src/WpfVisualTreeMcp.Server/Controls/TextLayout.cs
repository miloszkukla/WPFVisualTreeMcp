using System.Text.Json;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Server.Controls;

/// <summary>A rendered text layout and the physical bounds of its characters.</summary>
public sealed class TextLayout
{
    private readonly TextLayoutProjection projection;
    public TextLayout(JsonElement value) => projection = value.Deserialize<TextLayoutProjection>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Missing text layout.");
    public string? Text => projection.Text;
    public bool IsVisible => projection.Visible;
    public bool IsTruncated => projection.Truncated;
    public bool DeadlineReached => projection.DeadlineReached;
    public long WindowHandle => projection.WindowHandle;
    public IEnumerable<TextCharacter> Characters => projection.Runs.SelectMany(run => run.Characters.Select(character => new TextCharacter(character)));
}

public sealed class TextCharacter(TextCharacterProjection projection)
{
    public int Offset => projection.Offset;
    public int Length => projection.Length;
    public bool IsVisible => projection.Visible;
    public double X => projection.X;
    public double Y => projection.Y;
    public double Width => projection.Width;
    public double Height => projection.Height;
}
