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
