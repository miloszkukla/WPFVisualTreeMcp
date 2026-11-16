using System.Text.Json;

namespace WpfVisualTreeMcp.Server.Controls;

/// <summary>Inspects the availability of a field in a CLR projection without exposing its JSON envelope.</summary>
public readonly struct ProjectionField(JsonElement projection, string field)
{
    public bool IsMarkedMissing => projection.GetProperty(field).TryGetProperty("missing", out var missing) && missing.GetBoolean();

    public bool HasBooleanValue => projection.GetProperty(field).TryGetProperty("value", out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    public string? StringValueOrNull => projection.TryGetProperty(field, out var member)
        && member.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
