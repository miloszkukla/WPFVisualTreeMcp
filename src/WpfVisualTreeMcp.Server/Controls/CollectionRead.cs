using System.Text.Json;
using WpfVisualTreeMcp.Shared.Models;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Server.Controls;

/// <summary>A bounded collection read, or the non-collection value returned at that path.</summary>
public sealed class CollectionRead<T>(ClrReadEnvelope envelope, Func<JsonElement, T> convert)
{
    public CollectionRead(JsonElement projection, Func<JsonElement, T> convert)
        : this(IpcSerializer.Deserialize<ClrReadEnvelope>(projection.GetRawText()) ?? throw new InvalidOperationException("Missing CLR read envelope."), convert) { }
    public ClrReadKind Kind => envelope.Kind;
    public bool IsCollection => Kind == ClrReadKind.Collection;
    public bool IsObject => Kind == ClrReadKind.Object;
    public bool IsTruncated => envelope.Truncated ?? throw new InvalidOperationException("Missing collection truncation state.");
    public bool DeadlineReached => envelope.DeadlineReached ?? throw new InvalidOperationException("Missing collection deadline state.");
    public bool IsComplete => IsCollection && !IsTruncated && !DeadlineReached;

    public T[] Items => (envelope.Items ?? throw new InvalidOperationException("Missing collection items.")).Select(item => convert(item.Clone())).ToArray();

    public TValue Field<TValue>(string field, Func<JsonElement, TValue> convertValue) =>
        ProjectedValue<TValue>.Capture(envelope.Value, field, convertValue).Value;

    public bool FieldIsNull(string field) => Field(field, static value => value.ValueKind == JsonValueKind.Null);
}
