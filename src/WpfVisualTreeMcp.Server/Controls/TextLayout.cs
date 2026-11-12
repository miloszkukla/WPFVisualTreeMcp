using System.Text.Json;

namespace WpfVisualTreeMcp.Server.Controls;

/// <summary>A rendered text layout and the physical bounds of its characters.</summary>
public sealed class TextLayout(JsonElement projection)
{
    public string? Text => projection.GetProperty("text").GetString();
    public bool IsVisible => projection.GetProperty("visible").GetBoolean();
    public bool IsTruncated => projection.GetProperty("truncated").GetBoolean();
    public bool DeadlineReached => projection.GetProperty("deadlineReached").GetBoolean();
    public long WindowHandle => projection.GetProperty("windowHandle").GetInt64();
    public IEnumerable<TextCharacter> Characters => projection.GetProperty("runs").EnumerateArray()
        .SelectMany(run => run.GetProperty("characters").EnumerateArray().Select(character => new TextCharacter(character)));
}

public sealed class TextCharacter(JsonElement projection)
{
    public int Offset => projection.GetProperty("offset").GetInt32();
    public int Length => projection.GetProperty("length").GetInt32();
    public bool IsVisible => projection.GetProperty("visible").GetBoolean();
    public double X => projection.GetProperty("x").GetDouble();
    public double Y => projection.GetProperty("y").GetDouble();
    public double Width => projection.GetProperty("width").GetDouble();
    public double Height => projection.GetProperty("height").GetDouble();
}
