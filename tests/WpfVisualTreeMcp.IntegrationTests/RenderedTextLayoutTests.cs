using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class RenderedTextLayoutTests
{
    [Fact]
    public void TextLayoutMapsStyledInlineCharactersToActualScreenPositions()
    {
        OnSta(() =>
        {
            var block = new TextBlock { FontSize = 16, LayoutTransform = new ScaleTransform(1.25, 1.25) };
            block.Inlines.Add(new Run("Prefix "));
            block.Inlines.Add(new Bold(new Run("target") { FontSize = 24 }));
            var original = block.Text;
            WithWindow(block, window =>
            {
                var layout = Read(block);
                Assert.Equal("Prefix target", layout.GetProperty("text").GetString());
                Assert.False(layout.GetProperty("truncated").GetBoolean());
                Assert.False(layout.GetProperty("deadlineReached").GetBoolean());
                Assert.True(layout.GetProperty("visible").GetBoolean());
                Assert.Equal(new WindowInteropHelper(window).Handle.ToInt64(), layout.GetProperty("windowHandle").GetInt64());
                var character = Characters(layout).Single(item => item.GetProperty("offset").GetInt32() == 7);
                var point = Centre(character);
                var bounds = new Rect(block.PointToScreen(new Point()), block.PointToScreen(new Point(block.ActualWidth, block.ActualHeight)));
                Assert.True(bounds.Contains(point));
                Assert.True(character.GetProperty("visible").GetBoolean());
                var hit = window.InputHitTest(window.PointFromScreen(point));
                Assert.True(ReferenceEquals(hit, block) || hit is Run run && run.Parent is Bold);
                Assert.Equal(original, block.Text);
                Assert.Equal(2, layout.GetProperty("runs").GetArrayLength());
            });
        });
    }

    [Fact]
    public void TextLayoutKeepsOffsetsAcrossLineBreaksAndSurrogatePairs()
    {
        OnSta(() =>
        {
            var block = new TextBlock { FontSize = 18 };
            block.Inlines.Add(new Run("A😀B"));
            block.Inlines.Add(new LineBreak());
            block.Inlines.Add(new Run("Second"));
            WithWindow(block, _ =>
            {
                var layout = Read(block);
                Assert.Equal("A😀B\r\nSecond", layout.GetProperty("text").GetString());
                var characters = Characters(layout);
                var surrogate = characters.Single(item => item.GetProperty("offset").GetInt32() == 1);
                Assert.Equal(2, surrogate.GetProperty("length").GetInt32());
                var secondLine = characters.Single(item => item.GetProperty("offset").GetInt32() == 6);
                Assert.True(Centre(secondLine).Y > Centre(surrogate).Y);
                Assert.All(characters, item => Assert.True(item.GetProperty("visible").GetBoolean()));
            });
        });
    }

    [Fact]
    public void TextLayoutMarksGlyphsOutsideAncestorClipAsNotVisible()
    {
        OnSta(() =>
        {
            var block = new TextBlock { Text = "Visible and clipped text", FontSize = 18, Width = 300 };
            var clip = new Border { Width = 48, Height = 35, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = block };
            WithWindow(clip, _ =>
            {
                var characters = Characters(Read(block));
                Assert.True(characters[0].GetProperty("visible").GetBoolean());
                Assert.False(characters[^1].GetProperty("visible").GetBoolean());
            });
        });
    }

    [Fact]
    public void TextLayoutMarksGlyphsBeyondTheHwndClientViewportAsNotVisible()
    {
        OnSta(() =>
        {
            var block = new TextBlock { Text = new string('W', 80), FontSize = 18, Width = 1400 };
            WithWindow(new StackPanel { Orientation = Orientation.Horizontal, Children = { block } }, _ =>
            {
                var characters = Characters(Read(block));
                Assert.True(characters[0].GetProperty("visible").GetBoolean());
                Assert.False(characters[^1].GetProperty("visible").GetBoolean());
            });
        });
    }

    [Fact]
    public void TextLayoutDoesNotManufactureGeometryForHiddenOrUnrealizedText()
    {
        OnSta(() =>
        {
            var block = new TextBlock { Text = "Unrealized" };
            Assert.False(Read(block).GetProperty("visible").GetBoolean());
            Assert.Empty(Characters(Read(block)));
            WithWindow(block, _ =>
            {
                block.Visibility = Visibility.Hidden;
                var hidden = Read(block);
                Assert.False(hidden.GetProperty("visible").GetBoolean());
                Assert.Empty(Characters(hidden));
            });
        });
    }

    [Fact]
    public void TextLayoutBoundsTextAndRunCountsAndRejectsOtherControls()
    {
        OnSta(() =>
        {
            var oversized = Read(new TextBlock { Text = new string('x', 4097) });
            Assert.True(oversized.GetProperty("truncated").GetBoolean());
            Assert.Equal(4096, oversized.GetProperty("text").GetString()!.Length);
            var block = new TextBlock();
            for (var index = 0; index < 201; index++) block.Inlines.Add(new Run("x"));
            WithWindow(block, _ =>
            {
                var manyRuns = Read(block);
                Assert.True(manyRuns.GetProperty("truncated").GetBoolean());
                Assert.InRange(manyRuns.GetProperty("runs").GetArrayLength(), 0, 200);
            });
            using var unsupported = JsonDocument.Parse(new ClrReader().Read(new TextBox(), "", new[] { "$textLayout" }, 0, 1));
            Assert.True(unsupported.RootElement.GetProperty("value").GetProperty("$textLayout").GetProperty("missing").GetBoolean());
        });
    }

    private static JsonElement Read(TextBlock block)
    {
        using var document = JsonDocument.Parse(new ClrReader().Read(block, "", new[] { "$textLayout" }, 0, 1));
        return document.RootElement.GetProperty("value").GetProperty("$textLayout").GetProperty("value").Clone();
    }

    private static JsonElement[] Characters(JsonElement layout) => layout.GetProperty("runs").EnumerateArray()
        .SelectMany(run => run.GetProperty("characters").EnumerateArray()).ToArray();

    private static Point Centre(JsonElement character) => new(character.GetProperty("x").GetDouble() + character.GetProperty("width").GetDouble() / 2,
        character.GetProperty("y").GetDouble() + character.GetProperty("height").GetDouble() / 2);

    private static void WithWindow(UIElement content, Action<Window> action)
    {
        var window = new Window { Content = content, Width = 440, Height = 180, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.UpdateLayout();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            action(window);
        }
        finally { window.Close(); }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The native text layout observation completed within its bound.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
