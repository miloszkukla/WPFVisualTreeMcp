using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace WpfVisualTreeMcp.Server.Controls;

/// <summary>A decoded projection field that preserves errors until the field is accessed.</summary>
public readonly struct ProjectedValue<T>
{
    private readonly T? _value;
    private readonly ExceptionDispatchInfo? _error;
    private readonly string? _missingField;
    private readonly string? _unreadableField;
    private readonly string? _unreadableMember;

    private ProjectedValue(T? value, ExceptionDispatchInfo? error = null, string? missingField = null,
        string? unreadableField = null, string? unreadableMember = null)
    {
        _value = value;
        _error = error;
        _missingField = missingField;
        _unreadableField = unreadableField;
        _unreadableMember = unreadableMember;
    }

    public T Value
    {
        get
        {
            if (_missingField != null) throw new KeyNotFoundException();
            if (_unreadableField != null) throw new InvalidOperationException($"Cannot read {_unreadableField}: {_unreadableMember}");
            _error?.Throw();
            return _value!;
        }
    }

    public static ProjectedValue<T> Capture(JsonElement projection, string field, Func<JsonElement, T> convert)
    {
        try
        {
            if (projection.ValueKind == JsonValueKind.Object && !projection.TryGetProperty(field, out _))
                return new(default, missingField: field);
            var member = projection.GetProperty(field);
            if (!member.TryGetProperty("value", out var value))
                return new(default, unreadableField: field, unreadableMember: member.ToString());
            return new(convert(value));
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
