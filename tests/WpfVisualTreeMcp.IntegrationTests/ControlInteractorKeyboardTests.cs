using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class ControlInteractorKeyboardTests
{
    [Fact]
    public void SendKeysNonFocusableContentRoutesArrowToNearestItemInsteadOfPreviouslyFocusedTab()
    {
        OnSta(() =>
        {
            var target = new UserControl { Focusable = false, Content = new TextBlock { Text = "First" } };
            var list = new ListBox { SelectedIndex = 0, ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel))) };
            list.ItemsPanel.VisualTree.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            list.Items.Add(new ListBoxItem { Content = target });
            list.Items.Add(new ListBoxItem { Content = "Second" });
            var first = new TabItem { Header = "First tab", Content = list };
            var tabs = new TabControl { Items = { first, new TabItem { Header = "Unrelated tab" } } };
            WithWindow(tabs, window =>
            {
                Keyboard.Focus(first);
                Assert.True(first.IsKeyboardFocused);
                new ControlInteractor().SendKeys(target, "Right");
                Pump();
                Assert.Equal(0, tabs.SelectedIndex);
                Assert.Equal(1, list.SelectedIndex);
            });
        });
    }

    [Fact]
    public void SendKeysRefusedFocusThrowsWithoutTypingIntoPreviouslyFocusedControl()
    {
        OnSta(() =>
        {
            var previous = new TextBox { Text = "unchanged" };
            var target = new TextBox();
            target.PreviewGotKeyboardFocus += (_, e) => e.Handled = true;
            var panel = new StackPanel { Children = { previous, target } };
            WithWindow(panel, window =>
            {
                Keyboard.Focus(previous);
                var error = Assert.Throws<InvalidOperationException>(() => new ControlInteractor().SendKeys(target, "X"));
                Assert.Contains("keyboard focus", error.Message);
                Pump();
                Assert.Equal("unchanged", previous.Text);
                Assert.Equal("", target.Text);
            });
        });
    }

    [Fact]
    public void SendKeysWithoutFocusableContainerRejectsUnrelatedWindowFocus()
    {
        OnSta(() =>
        {
            var previous = new TextBox { Text = "unchanged" };
            var target = new TextBlock { Text = "Target" };
            var panel = new StackPanel { Children = { previous, new Border { Child = target } } };
            WithWindow(panel, window =>
            {
                Keyboard.Focus(previous);
                var error = Assert.Throws<InvalidOperationException>(() => new ControlInteractor().SendKeys(target, "X"));
                Assert.Contains("focusable", error.Message);
                Pump();
                Assert.Equal("unchanged", previous.Text);
            });
        });
    }

    [Fact]
    public void SendKeysFocusableTargetReceivesInputAfterFocusMovesFromAnotherControl()
    {
        OnSta(() =>
        {
            var previous = new TextBox { Text = "previous" };
            var target = new TextBox { Text = "target", CaretIndex = 0 };
            WithWindow(new StackPanel { Children = { previous, target } }, window =>
            {
                Keyboard.Focus(previous);
                new ControlInteractor().SendKeys(target, "Right");
                Pump();
                Assert.Equal(1, target.CaretIndex);
                Assert.Equal("target", target.Text);
                Assert.Equal("previous", previous.Text);
            });
        });
    }

    [Fact]
    public void SendKeysExplicitWindowPreservesItsRequestedKeyboardScope()
    {
        OnSta(() =>
        {
            var target = new TextBox { Text = "target" };
            WithWindow(target, window =>
            {
                var received = 0;
                window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) received++; };
                Keyboard.Focus(target);
                new ControlInteractor().SendKeys(window, "Escape");
                Pump();
                Assert.Equal(1, received);
                Assert.Equal("target", target.Text);
            });
        });
    }

    [Fact]
    public void SendKeysFocusableListContainerReceivesArrowWithinItsScope()
    {
        OnSta(() =>
        {
            var list = new ListBox { SelectedIndex = 0, Items = { "First", "Second" } };
            var previous = new TextBox { Text = "previous" };
            var received = 0;
            list.PreviewKeyDown += (_, e) => { if (e.Key == Key.Down) received++; };
            WithWindow(new StackPanel { Children = { previous, list } }, window =>
            {
                Keyboard.Focus(previous);
                new ControlInteractor().SendKeys(list, "Down");
                Pump();
                Assert.Equal(1, received);
                Assert.True(list.IsKeyboardFocusWithin);
                Assert.Equal("previous", previous.Text);
            });
        });
    }

    [Fact]
    public void SendKeysEditableComboBoxAcceptsRedirectedTextBoxFocusAndRaisesEnter()
    {
        OnSta(() =>
        {
            var previous = new TextBox { Text = "previous" };
            var combo = new ComboBox { IsEditable = true, Text = "RT0303", Items = { "RT0303", "RT0304" } };
            var entered = 0;
            combo.KeyDown += (_, e) => { if (e.Key == Key.Enter) entered++; };
            WithWindow(new StackPanel { Children = { previous, combo } }, window =>
            {
                Keyboard.Focus(previous);
                new ControlInteractor().SendKeys(combo, "Enter");
                Pump();
                Assert.True(combo.IsKeyboardFocusWithin);
                Assert.IsType<TextBox>(Keyboard.FocusedElement);
                Assert.Equal(1, entered);
                Assert.Equal("RT0303", combo.Text);
                Assert.Equal("previous", previous.Text);
            });
        });
    }

    private static void WithWindow(UIElement content, Action<Window> action)
    {
        var window = new Window { Content = content, Width = 420, Height = 180, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.Activate();
            window.UpdateLayout();
            Pump();
            action(window);
        }
        finally { window.Close(); }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The native keyboard observation completed within its bound.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
