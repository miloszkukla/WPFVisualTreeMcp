using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Models;
using WpfVisualTreeMcp.Shared.Ipc;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class TypedDiagnosticTests
{
    [Fact]
    public void SharedEnumsMatchTheActualWpfDiagnosticEnums()
    {
        var valueSources = Enum.GetNames(typeof(System.Windows.BaseValueSource)).OrderBy(value => value).ToArray();
        Assert.Equal(valueSources, Enum.GetNames(typeof(WpfBaseValueSource)).OrderBy(value => value).ToArray());
        Assert.Equal(valueSources, Enum.GetNames(typeof(DataContextSource)).Where(value => value != nameof(DataContextSource.NotApplicable)).OrderBy(value => value).ToArray());
        Assert.Equal(Enum.GetNames(typeof(System.Windows.Data.BindingStatus)).OrderBy(value => value), Enum.GetNames(typeof(WpfBindingStatus)).OrderBy(value => value));
    }

    [Fact]
    public void RealBindingAndLayoutCrossTypedIpcResponse()
    {
        RunSta(() =>
        {
            var element = new TextBlock { Margin = new System.Windows.Thickness(1, 2, 3, 4) };
            BindingOperations.SetBinding(element, TextBlock.TextProperty, new Binding("Name") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) });
            var analyzer = new BindingAnalyzer();
            var bindings = analyzer.GetTypedBindings(element);
            var wire = IpcSerializer.SerializeResponse(new GetBindingsResponse { Bindings = bindings });
            var roundTrip = IpcSerializer.DeserializeResponse<GetBindingsResponse>(wire)!;
            var binding = Assert.Single(roundTrip.Bindings!.Bindings, item => item.Property == "Text");
            Assert.Equal("Name", binding.Path);
            Assert.Equal(BindingSourceKind.RelativeSource, binding.SourceKind);
            Assert.Equal(BindingRelativeSourceMode.Self, binding.RelativeSourceMode);
            var layout = new PropertyReader().GetTypedLayoutInfo(element);
            var layoutWire = IpcSerializer.SerializeResponse(new GetLayoutInfoResponse { Layout = layout });
            Assert.Equal(3, IpcSerializer.DeserializeResponse<GetLayoutInfoResponse>(layoutWire)!.Layout!.Layout!.Margin!.Right);
        });
    }

    [Fact]
    public void DirectBindingAndLayoutPayloadsMatchTheExistingStructuredContract()
    {
        RunSta(() =>
        {
            var element = new TextBlock { Margin = new System.Windows.Thickness(1, 2, 3, 4) };
            var multi = new MultiBinding { StringFormat = "{0} {1}" };
            multi.Bindings.Add(new Binding("Name") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) });
            multi.Bindings.Add(new Binding("Tag") { ElementName = "source" });
            BindingOperations.SetBinding(element, TextBlock.TextProperty, multi);
            var analyzer = new BindingAnalyzer();
            var oldBindings = IpcSerializer.Serialize(IpcSerializer.Deserialize<BindingsPayload>(analyzer.GetBindings(element))!);
            var newBindings = IpcSerializer.Serialize(analyzer.GetTypedBindings(element));
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(oldBindings), System.Text.Json.Nodes.JsonNode.Parse(newBindings)), $"{oldBindings} != {newBindings}");
            var reader = new PropertyReader();
            var oldLayout = IpcSerializer.Serialize(IpcSerializer.Deserialize<LayoutPayload>(reader.GetLayoutInfo(element))!);
            var newLayout = IpcSerializer.Serialize(reader.GetTypedLayoutInfo(element));
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(oldLayout), System.Text.Json.Nodes.JsonNode.Parse(newLayout)), $"{oldLayout} != {newLayout}");
        });
    }

    [Fact]
    public void RealDiagnosticPayloadsSupportContextAndMissingDependencyProperty()
    {
        RunSta(() =>
        {
            var element = new TextBlock { DataContext = new { Label = "example" } };
            var analyzer = new BindingAnalyzer();
            var context = IpcSerializer.Deserialize<DataContextPayload>(analyzer.GetDataContext(element))!;
            Assert.Equal(DataContextSource.Local, context.DataContextSource);
            Assert.Contains(context.DataContext!.Properties!, property => property.Name == "Label");
            var evaluation = IpcSerializer.Deserialize<BindingEvaluationPayload>(analyzer.EvaluateBinding(element, "NoSuchProperty"))!;
            Assert.Contains("not a dependency property", evaluation.Error);
            var plain = IpcSerializer.Deserialize<DataContextPayload>(analyzer.GetDataContext(new System.Windows.DependencyObject()))!;
            Assert.Equal(DataContextSource.NotApplicable, plain.DataContextSource);
        });
    }

    [Fact]
    public void Relative_binding_source_and_mode_are_separate()
    {
        RunSta(() =>
        {
            var element = new TextBlock();
            BindingOperations.SetBinding(element, TextBlock.TextProperty, new Binding("Name") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) });
            using var result = JsonDocument.Parse(new BindingAnalyzer().EvaluateBinding(element, "Text"));
            var binding = result.RootElement.GetProperty("binding");
            Assert.Equal(BindingSourceKind.RelativeSource, binding.GetProperty("sourceKind").Deserialize<BindingSourceKind>());
            Assert.Equal(BindingRelativeSourceMode.Self, binding.GetProperty("relativeSourceMode").Deserialize<BindingRelativeSourceMode>());
        });
    }

    [Fact]
    public void Binding_listing_separates_source_kind_mode_and_element_name()
    {
        RunSta(() =>
        {
            var element = new TextBlock();
            BindingOperations.SetBinding(element, TextBlock.TextProperty, new Binding("Name") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) });
            using var result = JsonDocument.Parse(new BindingAnalyzer().GetBindings(element));
            var binding = result.RootElement.GetProperty("bindings").EnumerateArray().Single();
            Assert.Equal(BindingKind.Binding, binding.GetProperty("type").Deserialize<BindingKind>());
            Assert.Equal(BindingSourceKind.RelativeSource, binding.GetProperty("sourceKind").Deserialize<BindingSourceKind>());
            Assert.Equal(BindingRelativeSourceMode.Self, binding.GetProperty("relativeSourceMode").Deserialize<BindingRelativeSourceMode>());
        });
    }

    [Fact]
    public void Invisible_click_has_a_typed_error()
    {
        RunSta(() =>
        {
            var element = new Button { Visibility = System.Windows.Visibility.Collapsed };
            var error = Assert.Throws<InspectionException>(() => new ControlInteractor().Click(element, false));
            Assert.Equal(InspectionErrorCode.ElementNotVisible, error.Code);
        });
    }

    [Fact]
    public void Property_attribution_separates_kind_from_wpf_source()
    {
        RunSta(() =>
        {
            var element = new TextBlock { Text = "local" };
            using var result = JsonDocument.Parse(new ResourceInspector().ExplainTriggers(element, "Text"));
            var attribution = result.RootElement.GetProperty("attribution");
            var setBy = attribution.GetProperty("setBy");
            Assert.Equal(PropertyAttributionKind.ValueSource, setBy.GetProperty("kind").Deserialize<PropertyAttributionKind>());
            Assert.Equal(PropertyValueSource.Local, setBy.GetProperty("valueSource").Deserialize<PropertyValueSource>());
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }
}
