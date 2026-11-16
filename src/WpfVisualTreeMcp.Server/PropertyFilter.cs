using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfVisualTreeMcp.Server;

/// <summary>Property names and their text-matched expected values.</summary>
[JsonConverter(typeof(PropertyFilterConverter))]
public sealed class PropertyFilter : Dictionary<string, string>;

public sealed class PropertyFilterConverter : JsonConverter<PropertyFilter>
{
    public override PropertyFilter? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var filter = new PropertyFilter();
        foreach (var property in document.RootElement.EnumerateObject()) filter[property.Name] = property.Value.ToString();
        return filter;
    }

    public override void Write(Utf8JsonWriter writer, PropertyFilter value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var property in value)
        {
            writer.WritePropertyName(property.Key);
            writer.WriteStringValue(property.Value);
        }
        writer.WriteEndObject();
    }
}
