using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class TypedDiagnosticTests
{
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
