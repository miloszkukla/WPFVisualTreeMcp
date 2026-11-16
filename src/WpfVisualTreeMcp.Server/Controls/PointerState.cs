using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfVisualTreeMcp.Server.Controls;

/// <summary>A snapshot of WPF mouse input relative to an element.</summary>
public sealed record PointerState(
    [property: JsonPropertyName("target")] PointerElement Target,
    [property: JsonPropertyName("root")] PointerElement? Root,
    [property: JsonPropertyName("windowHandle")] long WindowHandle,
    [property: JsonPropertyName("position")] PointerPoint Position,
    [property: JsonPropertyName("center")] PointerPoint Center,
    [property: JsonPropertyName("centerInRoot")] PointerPoint? CenterInRoot,
    [property: JsonPropertyName("isMouseOver")] bool IsMouseOver,
    [property: JsonPropertyName("isMeasureValid")] bool IsMeasureValid,
    [property: JsonPropertyName("isArrangeValid")] bool IsArrangeValid,
    [property: JsonPropertyName("directlyOver")] PointerHit DirectlyOver,
    [property: JsonPropertyName("captured")] PointerHit Captured,
    [property: JsonPropertyName("centerHit")] PointerHit CenterHit)
{
    internal static PointerState FromProjection(JsonElement value) => new(
        PointerElement.FromProjection(value.GetProperty("target")) ?? throw new InvalidOperationException("Pointer target is null."),
        PointerElement.FromProjection(value.GetProperty("root")),
        value.GetProperty("windowHandle").GetInt64(),
        PointerPoint.FromProjection(value.GetProperty("position")),
        PointerPoint.FromProjection(value.GetProperty("center")),
        value.GetProperty("centerInRoot").ValueKind == JsonValueKind.Null ? null : PointerPoint.FromProjection(value.GetProperty("centerInRoot")),
        value.GetProperty("isMouseOver").GetBoolean(),
        value.GetProperty("isMeasureValid").GetBoolean(),
        value.GetProperty("isArrangeValid").GetBoolean(),
        PointerHit.FromProjection(value.GetProperty("directlyOver")),
        PointerHit.FromProjection(value.GetProperty("captured")),
        PointerHit.FromProjection(value.GetProperty("centerHit")));
}

public sealed record PointerPoint([property: JsonPropertyName("x")] double X, [property: JsonPropertyName("y")] double Y)
{
    internal static PointerPoint FromProjection(JsonElement value) => new(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble());
}

public sealed record PointerElement(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("automationId")] string? AutomationId)
{
    internal static PointerElement? FromProjection(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null :
        new(value.GetProperty("type").GetString(), value.GetProperty("name").GetString(), value.GetProperty("automationId").GetString());
}

public sealed record PointerHit(
    [property: JsonPropertyName("element")] PointerElement? Element,
    [property: JsonPropertyName("targetOrDescendant")] bool TargetOrDescendant,
    [property: JsonPropertyName("ancestors")] IReadOnlyList<PointerElement> Ancestors,
    [property: JsonPropertyName("ancestorsTruncated")] bool AncestorsTruncated)
{
    internal static PointerHit FromProjection(JsonElement value) => new(
        PointerElement.FromProjection(value.GetProperty("element")),
        value.GetProperty("targetOrDescendant").GetBoolean(),
        value.GetProperty("ancestors").EnumerateArray().Select(item => PointerElement.FromProjection(item) ?? throw new InvalidOperationException("Pointer ancestor is null.")).ToArray(),
        value.GetProperty("ancestorsTruncated").GetBoolean());
}
