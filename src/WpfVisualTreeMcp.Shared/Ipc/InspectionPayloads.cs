using System.Text.Json;
using System.Text.Json.Serialization;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Shared.Ipc;

public sealed class InspectedElement
{
    public string? TypeName { get; set; }
    public string? Name { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class BindingsPayload
{
    public InspectedElement? Element { get; set; }
    public List<BindingPayload> Bindings { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class BindingPayload
{
    public string? Property { get; set; }
    public string? Path { get; set; }
    public BindingKind? Type { get; set; }
    public BindingSourceKind? SourceKind { get; set; }
    public BindingRelativeSourceMode? RelativeSourceMode { get; set; }
    public string? SourceType { get; set; }
    public bool? SourceImplementsINPC { get; set; }
    public string? ElementName { get; set; }
    public string? AncestorType { get; set; }
    public int? AncestorLevel { get; set; }
    public BindingMode? Mode { get; set; }
    public UpdateSourceTrigger? UpdateTrigger { get; set; }
    public BindingStatus? Status { get; set; }
    public string? Converter { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement ConverterParameter { get; set; }
    public string? StringFormat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement FallbackValue { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement TargetNullValue { get; set; }
    public bool? IsAsync { get; set; }
    public string? ValidationError { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement CurrentValue { get; set; }
    public List<BindingPayload>? ChildBindings { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class LayoutPayload
{
    public InspectedElement? Element { get; set; }
    public InspectedLayout? Layout { get; set; }
    public string? Error { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class InspectedLayout
{
    public double? ActualWidth { get; set; }
    public double? ActualHeight { get; set; }
    public InspectedSize? DesiredSize { get; set; }
    public InspectedSize? RenderSize { get; set; }
    public InspectedThickness? Margin { get; set; }
    public InspectedThickness? Padding { get; set; }
    public HorizontalAlignment? HorizontalAlignment { get; set; }
    public VerticalAlignment? VerticalAlignment { get; set; }
    public Visibility? Visibility { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class InspectedSize
{
    public double? Width { get; set; }
    public double? Height { get; set; }
}

public sealed class InspectedThickness
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Right { get; set; }
    public double? Bottom { get; set; }
}

public sealed class VisualTreePayload
{
    public VisualTreeNode? Root { get; set; }
    public int TotalElements { get; set; }
    public bool MaxDepthReached { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class FindElementsPayload
{
    public List<FoundElement> Elements { get; set; } = new();
    public int Count { get; set; }
    public bool? Truncated { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class PropertiesPayload
{
    public InspectedElement? Element { get; set; }
    public List<InspectedProperty> Properties { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class InspectedProperty
{
    public string? Name { get; set; }
    public string? TypeName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    public PropertyValueSource? Source { get; set; }
    public bool? IsBinding { get; set; }
    public PropertyBindingDetails? BindingDetails { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class PropertyBindingDetails
{
    public string? Path { get; set; }
    public string? SourceType { get; set; }
    public BindingRelativeSourceMode? RelativeSourceMode { get; set; }
    public string? AncestorType { get; set; }
    public int? AncestorLevel { get; set; }
    public string? ElementName { get; set; }
    public BindingMode? Mode { get; set; }
    public UpdateSourceTrigger? UpdateSourceTrigger { get; set; }
    public string? Converter { get; set; }
    public BindingStatus? Status { get; set; }
    public bool? HasError { get; set; }
    public string? ErrorMessage { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class BindingErrorsPayload
{
    public List<BindingError> Errors { get; set; } = new();
    public int Count { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class ResourcePayload
{
    public string? Key { get; set; }
    public string? TypeName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    public string? Source { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class StylesPayload
{
    public bool? HasStyle { get; set; }
    public InspectedStyle? Style { get; set; }
    public bool? ImplicitStyleAvailable { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class InspectedStyle
{
    public string? Key { get; set; }
    public string? TargetType { get; set; }
    public string? BasedOn { get; set; }
    public List<InspectedSetter> Setters { get; set; } = new();
    public List<InspectedTrigger>? Triggers { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class InspectedSetter
{
    public string? Property { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class InspectedTrigger
{
    public string? Type { get; set; }
    public string? Origin { get; set; }
    public string? Property { get; set; }
    public string? Binding { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    public string? RoutedEvent { get; set; }
    public string? Note { get; set; }
    public bool? Active { get; set; }
    public List<TriggerCondition>? Conditions { get; set; }
    public List<InspectedSetter>? Setters { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class TriggerCondition
{
    public string? Property { get; set; }
    public string? Binding { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Expected { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Current { get; set; }
    public bool? Matches { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class ExplainTriggersPayload
{
    public InspectedElement? Element { get; set; }
    public List<InspectedTrigger>? StyleTriggers { get; set; }
    public List<InspectedTrigger>? TemplateTriggers { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public PropertyAttribution? Attribution { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class PropertyAttribution
{
    public string? Property { get; set; }
    public PropertyValueSource? ValueSource { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement EffectiveValue { get; set; }
    public AttributionSource? SetBy { get; set; }
    public string? Error { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class AttributionSource
{
    public PropertyAttributionKind? Kind { get; set; }
    public PropertyValueSource? ValueSource { get; set; }
    public string? Style { get; set; }
    public string? Setter { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    public string? Detail { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

[JsonConverter(typeof(InspectionEnumConverter))]
public enum WpfBaseValueSource
{
    Unknown, Default, Inherited, DefaultStyle, DefaultStyleTrigger, Style, TemplateTrigger, StyleTrigger,
    ImplicitStyleReference, ParentTemplate, ParentTemplateTrigger, Local
}

public sealed class BindingEvaluationPayload
{
    public InspectedElement? Element { get; set; }
    public string? Property { get; set; }
    public WpfBaseValueSource? ValueSource { get; set; }
    public bool? IsExpression { get; set; }
    public bool? IsAnimated { get; set; }
    public bool? IsCoerced { get; set; }
    public string? EffectiveValue { get; set; }
    public string? EffectiveValueType { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public BindingEvaluation? Binding { get; set; }
    public string? Explanation { get; set; }
    public string? Error { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

[JsonConverter(typeof(InspectionEnumConverter))]
public enum WpfBindingStatus { Unattached, Inactive, Active, Detached, AsyncRequestPending, PathError, UpdateTargetError, UpdateSourceError }

public sealed class BindingEvaluation
{
    public BindingKind? Kind { get; set; }
    public string? Path { get; set; }
    public BindingMode? Mode { get; set; }
    public WpfBindingStatus? Status { get; set; }
    public string? Converter { get; set; }
    public string? FallbackValue { get; set; }
    public string? TargetNullValue { get; set; }
    public BindingSourceKind? SourceKind { get; set; }
    public BindingRelativeSourceMode? RelativeSourceMode { get; set; }
    public string? SourceType { get; set; }
    public bool? HasError { get; set; }
    public string? ErrorMessage { get; set; }
    public BindingResolution? Resolution { get; set; }
    public string? Note { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class BindingResolution
{
    public string? RootType { get; set; }
    public List<BindingResolutionHop>? Hops { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? ResolvedValue { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? BrokenAt { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class BindingResolutionHop
{
    public string? Segment { get; set; }
    public bool? Found { get; set; }
    public string? ValueType { get; set; }
    public string? Value { get; set; }
    public string? Reason { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class DiffPayload
{
    public DiffSummary? Summary { get; set; }
    public List<DiffEntry>? Changed { get; set; }
    public List<DiffEntry>? Added { get; set; }
    public List<DiffEntry>? Removed { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class DiffSummary
{
    public int Changed { get; set; }
    public int Added { get; set; }
    public int Removed { get; set; }
}

public sealed class DiffEntry
{
    public string? Handle { get; set; }
    public string? TypeName { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public Dictionary<string, DiffValue>? Changes { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class DiffValue
{
    public string? From { get; set; }
    public string? To { get; set; }
}

public sealed class DataContextPayload
{
    public InspectedElement? Element { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public InspectedDataContext? DataContext { get; set; }
    public DataContextSource? DataContextSource { get; set; }
    public List<DataContextAncestor>? InheritanceChain { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class InspectedDataContext
{
    public string? Type { get; set; }
    public string? ShortType { get; set; }
    public bool? ImplementsINPC { get; set; }
    public List<DataContextProperty>? Properties { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class DataContextProperty
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool? CanRead { get; set; }
    public bool? CanWrite { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class DataContextAncestor
{
    public string? TypeName { get; set; }
    public string? Name { get; set; }
    public string? DataContextType { get; set; }
    public WpfBaseValueSource? Source { get; set; }
    public bool? SetsDataContext { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

[JsonConverter(typeof(DataContextSourceConverter))]
public enum DataContextSource
{
    NotApplicable, Default, Inherited, DefaultStyle, DefaultStyleTrigger, Style, TemplateTrigger, StyleTrigger,
    ImplicitStyleReference, ParentTemplate, ParentTemplateTrigger, Local, Unknown
}

public sealed class DataContextSourceConverter : JsonConverter<DataContextSource>
{
    public override DataContextSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase)) return DataContextSource.NotApplicable;
        if (value != null && Enum.TryParse(value, true, out DataContextSource source) && Enum.IsDefined(typeof(DataContextSource), source)
            && string.Equals(source.ToString(), value, StringComparison.OrdinalIgnoreCase)) return source;
        throw new JsonException("Unknown DataContext source: " + value);
    }

    public override void Write(Utf8JsonWriter writer, DataContextSource value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(typeof(DataContextSource), value)) throw new JsonException("Unknown DataContext source: " + value);
        writer.WriteStringValue(value == DataContextSource.NotApplicable ? "N/A" : value.ToString());
    }
}
