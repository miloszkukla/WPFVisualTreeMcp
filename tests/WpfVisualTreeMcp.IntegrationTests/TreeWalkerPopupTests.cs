using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WpfVisualTreeMcp.Inspector;
using TreeWalker = WpfVisualTreeMcp.Inspector.TreeWalker;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class TreeWalkerPopupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedTemplateSearchIncludesDisconnectedPopupAndItsContent(bool visibleOnly)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var panel = new FrameworkElementFactory(typeof(Grid));
                var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
                var child = new FrameworkElementFactory(typeof(Button), "PopupButton");
                popup.SetValue(FrameworkElement.NameProperty, "PART_Popup");
                child.SetValue(FrameworkElement.NameProperty, "PopupButton");
                popup.AppendChild(child);
                panel.AppendChild(popup);
                var combo = new ComboBox { Template = new ControlTemplate(typeof(ComboBox)) { VisualTree = panel } };
                Assert.True(combo.ApplyTemplate());
                var actual = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
                Assert.Equal("PART_Popup", actual.Name);
                if (visibleOnly)
                {
                    window = new Window { Width = 140, Height = 100, Content = combo };
                    window.Show();
                    combo.IsDropDownOpen = true;
                    actual.PlacementTarget = combo;
                    actual.IsOpen = true;
                    window.UpdateLayout();
                }
                foreach (var name in new[] { "PART_Popup", "PopupButton" })
                {
                    using var result = JsonDocument.Parse(new TreeWalker().FindElements(combo, new FindCriteria { ElementName = name, VisibleOnly = visibleOnly }));
                    Assert.True(result.RootElement.GetProperty("count").GetInt32() == 1, name + ": " + result.RootElement);
                    Assert.Equal(name, result.RootElement.GetProperty("elements")[0].GetProperty("name").GetString());
                }
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The scoped popup observation completed.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
