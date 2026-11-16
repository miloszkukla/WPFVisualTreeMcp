using System.Text.Json.Serialization;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Shared.Ipc;

[JsonConverter(typeof(InspectionEnumConverter))]
public enum IpcRequestType
{
    GetVisualTree, GetElementProperties, FindElements, FindElementsDeep, ExplainTriggers, EvaluateBinding,
    Snapshot, Diff, SetProperty, RevertProperty, WaitForElement, GetLayoutInfo, GetBindings, GetBindingErrors,
    GetDataContext, ClearBindingErrors, GetResources, GetStyles, HighlightElement, WatchProperty, ExportTree,
    CaptureScreenshot, ClickElement, SetText, SelectItem, SendKeys, ReadClr, InspectionIdentity, ScrollViewer, ScrollItemIntoView
}
