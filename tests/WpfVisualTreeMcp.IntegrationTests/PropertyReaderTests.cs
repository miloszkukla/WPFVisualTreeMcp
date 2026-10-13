using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class PropertyReaderTests
{
    [Fact]
    public void GetProperties_and_layout_serialize_double_with_invariant_culture()
    {
        string? json = null;
        string? layoutJson = null;
        Exception? exception = null;
        Thread thread = new(() =>
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Border border = new() { Height = 812.8, Margin = new(1.1, 2.2, 3.3, 4.4) };
                PropertyReader propertyReader = new();
                json = propertyReader.GetProperties(border);
                layoutJson = propertyReader.GetLayoutInfo(border);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        exception.Should().BeNull();
        using JsonDocument document = JsonDocument.Parse(json!);
        JsonElement height = document.RootElement.GetProperty("properties")
            .EnumerateArray().Single(property => property.GetProperty("name").GetString() == "Height");
        height.GetProperty("value").GetDouble().Should().Be(812.8);
        using JsonDocument layout = JsonDocument.Parse(layoutJson!);
        layout.RootElement.GetProperty("layout").GetProperty("margin").GetProperty("left").GetDouble().Should().Be(1.1);
    }
}
