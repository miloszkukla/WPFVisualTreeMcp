using System.Text.Json;

namespace WpfVisualTreeMcp.Server.Controls;

public sealed class DataGridColumnInfo(JsonElement projection)
{
    public int DisplayIndex => WpfSession.Value(projection, "DisplayIndex").GetInt32();
    public string? SortMemberPath => WpfSession.Value(projection, "SortMemberPath").GetString();
    public static DataGridColumnInfo FromProjection(JsonElement projection) => new(projection);
}

public sealed class HeaderValue(JsonElement value)
{
    public bool IsString => value.ValueKind == JsonValueKind.String;
    public bool IsObject => value.ValueKind == JsonValueKind.Object;
    public string? Text => value.GetString();
    public string? TypeName => value.GetProperty("type").GetString();
}

public sealed class BindingInfo(JsonElement projection)
{
    public string? Property => projection.GetProperty("Property").GetString();
    public string? Path => projection.GetProperty("Path").GetString();
}

public readonly record struct InspectedText(bool IsAvailable, string Text);

public enum WpfTextField { Text, Header, Content }

public enum WindowState { Normal, Minimized, Maximized }

public enum ObservedTextKind { Undefined, Object, Array, String, Number, True, False, Null }

public readonly record struct ObservedText(string? Text, ObservedTextKind Kind)
{
    public bool IsString => Kind == ObservedTextKind.String;
    public static ObservedText FromString(string? value) => new(value, value == null ? ObservedTextKind.Null : ObservedTextKind.String);
    internal static ObservedText FromProjection(JsonElement value) => new(value.ValueKind == JsonValueKind.String ? value.GetString() : null, value.ValueKind switch
    {
        JsonValueKind.Undefined => ObservedTextKind.Undefined,
        JsonValueKind.Object => ObservedTextKind.Object,
        JsonValueKind.Array => ObservedTextKind.Array,
        JsonValueKind.String => ObservedTextKind.String,
        JsonValueKind.Number => ObservedTextKind.Number,
        JsonValueKind.True => ObservedTextKind.True,
        JsonValueKind.False => ObservedTextKind.False,
        JsonValueKind.Null => ObservedTextKind.Null,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    });
}
