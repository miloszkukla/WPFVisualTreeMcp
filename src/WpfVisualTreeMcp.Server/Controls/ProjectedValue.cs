using System.Runtime.ExceptionServices;
using System.Text.Json;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Server.Controls;

/// <summary>A decoded projection field that preserves errors until the field is accessed.</summary>
public readonly struct ProjectedValue<T>
{
    private readonly T? _value;
    private readonly ExceptionDispatchInfo? _error;
    private readonly string? _unreadableField;
    private readonly string? _unreadableMember;
    private readonly bool _hasStringValue;

    private ProjectedValue(T? value, ExceptionDispatchInfo? error = null,
        string? unreadableField = null, string? unreadableMember = null, bool hasStringValue = false)
    {
        _value = value;
        _error = error;
        _unreadableField = unreadableField;
        _unreadableMember = unreadableMember;
        _hasStringValue = hasStringValue;
    }

    /// <summary>Whether the projection contains a string, including when decoding it fails.</summary>
    public bool HasStringValue => _hasStringValue;

    public T Value
    {
        get
        {
            if (_unreadableField != null) throw new InvalidOperationException($"Cannot read {_unreadableField}: {_unreadableMember}");
            _error?.Throw();
            return _value!;
        }
    }

    public static ProjectedValue<T> Capture(JsonElement projection, string field, Func<JsonElement, T> convert)
    {
        try
        {
            var member = projection.GetProperty(field);
            if (member.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Projected field is not an object.");
            var projected = IpcSerializer.Deserialize<ClrProjectedField>(member) ?? throw new InvalidOperationException("Missing projected field.");
            if (!projected.HasValue)
                return new(default, unreadableField: field, unreadableMember: member.ToString());
            var value = projected.Value;
            var hasString = value.ValueKind == JsonValueKind.String;
            try { return new(convert(value), hasStringValue: hasString); }
            catch (Exception error) { return new(default, ExceptionDispatchInfo.Capture(error), hasStringValue: hasString); }
        }
        catch (Exception error)
        {
            return new(default, ExceptionDispatchInfo.Capture(error));
        }
    }

    public static ProjectedValue<T> Capture(Func<T> read)
    {
        try { return new(read()); }
        catch (Exception error) { return new(default, ExceptionDispatchInfo.Capture(error)); }
    }
}
