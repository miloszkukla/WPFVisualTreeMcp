using System.Text.Json;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server.Controls;

/// <summary>A bounded collection read, or the non-collection value returned at that path.</summary>
public sealed class CollectionRead<T>(JsonElement projection, Func<JsonElement, T> convert)
{
    public ClrReadKind Kind => projection.GetProperty("kind").Deserialize<ClrReadKind>();
    public bool IsCollection => Kind == ClrReadKind.Collection;
    public bool IsObject => Kind == ClrReadKind.Object;
    public bool IsTruncated => projection.GetProperty("truncated").GetBoolean();
    public bool DeadlineReached => projection.GetProperty("deadlineReached").GetBoolean();
    public bool IsComplete => IsCollection && !IsTruncated && !DeadlineReached;

    public T[] Items => projection.GetProperty("items").EnumerateArray().Select(item => convert(item.Clone())).ToArray();

    public TValue Field<TValue>(string field, Func<JsonElement, TValue> convertValue) =>
        ProjectedValue<TValue>.Capture(projection.GetProperty("value"), field, convertValue).Value;

    public bool FieldIsNull(string field) => Field(field, static value => value.ValueKind == JsonValueKind.Null);
}
