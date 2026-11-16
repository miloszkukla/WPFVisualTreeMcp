using System.Text.Json;
using System.Text.Json.Serialization;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Shared.Ipc;

public class ClrReadRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.ReadClr;
    public string ElementHandle { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string[] Fields { get; set; } = new string[0];
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
    public string? Generation { get; set; }
    public bool MembersOnly { get; set; }
}

public class ClrReadResponse : IpcResponse
{
    public ClrReadEnvelope? Data { get; set; }
    public string Generation { get; set; } = string.Empty;
    public int ProcessId { get; set; }
}

public class InspectionIdentityRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.InspectionIdentity;
}

public sealed class ClrReadEnvelope
{
    [JsonRequired] public ClrReadKind Kind { get; set; }
    public string? Type { get; set; }
    public int? TotalCount { get; set; }
    public int? Skip { get; set; }
    public int? Returned { get; set; }
    public bool? Truncated { get; set; }
    public bool? DeadlineReached { get; set; }
    public List<JsonElement>? Items { get; set; }
    public List<ClrMember>? Members { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class ClrMember
{
    public string? Name { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public System.Reflection.MemberTypes Kind { get; set; }
    public string? Signature { get; set; }
}
public sealed class InspectionIdentityResponse : IpcResponse
{
    public string Generation { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public InspectionIdentity? Data { get; set; }
}
public sealed class InspectionIdentity
{
    public string Generation { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTime ProcessStartUtc { get; set; }
}

/// <summary>A projected member and its arbitrary CLR value.</summary>
[JsonConverter(typeof(ClrProjectedFieldConverter))]
public sealed class ClrProjectedField
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    public bool? Missing { get; set; }
    public string? Error { get; set; }
    [JsonIgnore] public bool HasValue => Value.ValueKind != JsonValueKind.Undefined;
}

public sealed class ClrProjectedFieldConverter : JsonConverter<ClrProjectedField>
{
    public override ClrProjectedField Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Projected field must be an object.");
        var result = new ClrProjectedField();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a projected field property.");
            var name = reader.GetString();
            if (!reader.Read()) throw new JsonException("Missing projected field property value.");
            switch (name)
            {
                case "value":
                    using (var value = JsonDocument.ParseValue(ref reader)) result.Value = value.RootElement.Clone();
                    break;
                case "missing" when reader.TokenType is JsonTokenType.True or JsonTokenType.False:
                    result.Missing = reader.GetBoolean();
                    break;
                case "error" when reader.TokenType == JsonTokenType.String:
                    result.Error = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Unterminated projected field.");
        return result;
    }

    public override void Write(Utf8JsonWriter writer, ClrProjectedField value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.HasValue) { writer.WritePropertyName("value"); value.Value.WriteTo(writer); }
        if (value.Missing is bool missing) writer.WriteBoolean("missing", missing);
        if (value.Error != null) writer.WriteString("error", value.Error);
        writer.WriteEndObject();
    }
}

/// <summary>A projected member successfully read by the inspector.</summary>
public sealed class ClrProjectedValue
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public object? Value { get; set; }
}

public sealed class ClrProjectedMissing
{
    public bool Missing { get; set; }
    public string? Error { get; set; }
}

public sealed class ClrProjectedError
{
    public string? Error { get; set; }
}

public sealed class ClrTruncatedText
{
    public string Text { get; set; } = string.Empty;
    public bool Truncated { get; set; }
}

public sealed class ClrObjectSummary
{
    public string? Type { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? Count { get; set; }
}
