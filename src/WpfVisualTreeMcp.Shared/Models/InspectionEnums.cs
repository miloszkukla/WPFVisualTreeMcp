using System.Text.Json.Serialization;

namespace WpfVisualTreeMcp.Shared.Models;

public sealed class InspectionEnumConverter : JsonStringEnumConverter
{
    public InspectionEnumConverter() : base(null, false) { }
}

[JsonConverter(typeof(InspectionEnumConverter))]
public enum ClickType { Single, Double, Right }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum ScreenshotMode { Render, Screen }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum WaitCondition { Visible, Exists, Enabled, Hidden }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum HorizontalAlignment { Left, Center, Right, Stretch }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum VerticalAlignment { Top, Center, Bottom, Stretch }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum Visibility { Visible, Hidden, Collapsed }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum BindingMode { Default, OneWay, TwoWay, OneTime, OneWayToSource }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum UpdateSourceTrigger { Default, PropertyChanged, LostFocus, Explicit }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum BindingStatus { Active, Inactive, Detached, PathError, UpdateTargetError, UpdateSourceError, AsyncPending, Unattached, Error, Unknown }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum ResourceScope { Application, Window, Element, All }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum TreeExportFormat { Json, Xaml }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum PropertyValueSource { Default, Inherited, DefaultStyle, DefaultStyleTrigger, Style, TemplateTrigger, StyleTrigger, ImplicitStyle, ParentTemplate, ParentTemplateTrigger, Local, Unknown }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum PreviousPropertySource { Binding, Local, Unset }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum DotNetRuntimeType { Framework, CoreCLR }
[JsonConverter(typeof(InspectionEnumConverter))]
public enum InspectorAttachmentStatus { Unknown, LoadedExisting, LoadedInjected, PipeTimeoutExisting, PipeTimeoutInjected, InjectionFailed, NotLoaded }
