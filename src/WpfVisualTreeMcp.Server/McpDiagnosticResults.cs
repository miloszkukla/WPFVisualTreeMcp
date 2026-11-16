using System.Text.Json;
using System.Text.Json.Serialization;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Server;

/// <summary>A typed diagnostic payload or the existing unsuccessful response.</summary>
[JsonConverter(typeof(McpDiagnosticResultConverterFactory))]
public sealed record McpDiagnosticResult<T>(T? Payload) where T : class;

public sealed class McpDiagnosticResultConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(McpDiagnosticResult<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    public sealed class Converter<T> : JsonConverter<McpDiagnosticResult<T>> where T : class
    {
        public override McpDiagnosticResult<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 1
                && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                return new(null);
            return new(IpcSerializer.Deserialize<T>(root));
        }

        public override void Write(Utf8JsonWriter writer, McpDiagnosticResult<T> value, JsonSerializerOptions options)
        {
            if (value.Payload != null) IpcSerializer.Write(writer, value.Payload);
            else
            {
                writer.WriteStartObject();
                writer.WriteBoolean("success", false);
                writer.WriteEndObject();
            }
        }
    }
}

public sealed record McpDiffResult(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("changed_count")] int ChangedCount,
    [property: JsonPropertyName("added_count")] int AddedCount,
    [property: JsonPropertyName("removed_count")] int RemovedCount,
    [property: JsonPropertyName("diff"), JsonConverter(typeof(McpDiffPayloadConverter)), JsonIgnore(Condition = JsonIgnoreCondition.Never)] DiffPayload? Diff);

public sealed class McpDiffPayloadConverter : JsonConverter<DiffPayload>
{
    public override bool HandleNull => true;

    public override DiffPayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return IpcSerializer.Deserialize<DiffPayload>(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, DiffPayload value, JsonSerializerOptions options)
    {
        if (value != null) IpcSerializer.Write(writer, value);
        else
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }
}
