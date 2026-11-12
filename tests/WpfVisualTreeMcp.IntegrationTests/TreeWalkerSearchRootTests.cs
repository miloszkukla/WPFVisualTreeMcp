using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using WpfVisualTreeMcp.Inspector;
using TreeWalker = WpfVisualTreeMcp.Inspector.TreeWalker;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class TreeWalkerSearchRootTests
{
    [Fact]
    public void GlobalSearchDiscoversTooltipInLiveHwndSourceWithoutApplicationWindow()
    {
        OnSta(() =>
        {
            var tooltip = new ToolTip { Name = "NativeTooltip", Content = "Current tooltip" };
            using var source = HiddenSource(tooltip);
            var roots = TreeWalker.GetAllSearchRoots();
            Assert.Contains(tooltip, roots);
            var walker = new TreeWalker();
            var criteria = new FindCriteria
            {
                ElementName = "NativeTooltip", PropertyFilter = new() { ["$type"] = "ToolTip" }
            };
            using var result = JsonDocument.Parse(walker.FindElementsAcrossRoots(roots, criteria));
            Assert.Equal(1, result.RootElement.GetProperty("count").GetInt32());
            Assert.Equal("NativeTooltip", result.RootElement.GetProperty("elements")[0].GetProperty("name").GetString());
            var first = walker.FindFirstMatch(criteria);
            Assert.True(first.HasValue);
            Assert.Same(tooltip, walker.ResolveHandle(first.Value.Handle));
            source.Dispose();
            Assert.DoesNotContain(tooltip, TreeWalker.GetAllSearchRoots());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingMainPopupAndHwndRootsReturnEachElementOnceWithinSharedLimit(bool deep)
    {
        OnSta(() =>
        {
            var shared = new Button { Name = "SharedPopupButton" };
            var popup = new Popup { Child = shared };
            var main = new Grid();
            main.Children.Add(popup);
            using var popupSource = HiddenSource(shared);
            var distinct = new Button { Name = "DistinctPopupButton" };
            using var otherSource = HiddenSource(distinct);
            var roots = new DependencyObject[] { main, popupSource.RootVisual, otherSource.RootVisual, main };
            var walker = new TreeWalker();
            using var result = JsonDocument.Parse(walker.FindElementsAcrossRoots(roots, new FindCriteria
            {
                PropertyFilter = new() { ["$type"] = "Button" }
            }, 2, deep));
            var elements = result.RootElement.GetProperty("elements").EnumerateArray().ToArray();
            Assert.Equal(2, result.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(new[] { "SharedPopupButton", "DistinctPopupButton" }, elements.Select(element => element.GetProperty("name").GetString()).ToArray());
            Assert.Equal(2, elements.Select(element => element.GetProperty("handle").GetString()).Distinct().Count());
            Assert.Same(shared, walker.ResolveHandle(elements[0].GetProperty("handle").GetString()!));
            if (deep) Assert.True(result.RootElement.GetProperty("truncated").GetBoolean());

            using var limited = JsonDocument.Parse(walker.FindElementsAcrossRoots(roots, new FindCriteria
            {
                PropertyFilter = new() { ["$type"] = "Button" }
            }, 1, deep));
            Assert.Equal(1, limited.RootElement.GetProperty("count").GetInt32());
            Assert.Equal("SharedPopupButton", limited.RootElement.GetProperty("elements")[0].GetProperty("name").GetString());
        });
    }

    private static HwndSource HiddenSource(System.Windows.Media.Visual root)
    {
        var source = new HwndSource(new HwndSourceParameters("Inspector search root test")
        {
            WindowStyle = unchecked((int)0x80000000), Width = 240, Height = 80
        });
        source.RootVisual = root;
        return source;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The hidden HWND observation completed.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
