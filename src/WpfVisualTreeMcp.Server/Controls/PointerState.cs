using System.Text.Json;
using System.Text.Json.Serialization;
using WpfVisualTreeMcp.Shared.Ipc;

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
    internal static PointerState FromProjection(JsonElement value) => FromPayload(value.Deserialize<PointerProjection>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Pointer target is null."));
    private static PointerState FromPayload(PointerProjection value) => new(
        PointerElement.FromPayload(value.Target) ?? throw new InvalidOperationException("Pointer target is null."),
        PointerElement.FromPayload(value.Root), value.WindowHandle, PointerPoint.FromPayload(value.Position), PointerPoint.FromPayload(value.Center),
        value.CenterInRoot == null ? null : PointerPoint.FromPayload(value.CenterInRoot), value.IsMouseOver, value.IsMeasureValid,
        value.IsArrangeValid, PointerHit.FromPayload(value.DirectlyOver), PointerHit.FromPayload(value.Captured), PointerHit.FromPayload(value.CenterHit));

}

public sealed record PointerPoint([property: JsonPropertyName("x")] double X, [property: JsonPropertyName("y")] double Y)
{
    internal static PointerPoint FromPayload(PointerPointProjection value) => new(value.X, value.Y);
}

public sealed record PointerElement(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("automationId")] string? AutomationId)
{
    internal static PointerElement? FromPayload(PointerElementProjection? value) => value == null ? null :
        new(value.Type, value.Name, value.AutomationId);
}

public sealed record PointerHit(
    [property: JsonPropertyName("element")] PointerElement? Element,
    [property: JsonPropertyName("targetOrDescendant")] bool TargetOrDescendant,
    [property: JsonPropertyName("ancestors")] IReadOnlyList<PointerElement> Ancestors,
    [property: JsonPropertyName("ancestorsTruncated")] bool AncestorsTruncated)
{
    internal static PointerHit FromPayload(PointerHitProjection value) => new(
        PointerElement.FromPayload(value.Element), value.TargetOrDescendant,
        value.Ancestors.Select(item => PointerElement.FromPayload(item) ?? throw new InvalidOperationException("Pointer ancestor is null.")).ToArray(),
        value.AncestorsTruncated);
}
