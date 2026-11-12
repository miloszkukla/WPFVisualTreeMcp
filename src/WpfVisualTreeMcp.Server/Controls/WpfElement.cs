using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server.Controls;

public readonly record struct ElementState(bool IsVisible, bool IsEnabled);
public readonly record struct RenderedTextState(string? Text, bool IsVisible);
public readonly record struct ScrollState(double Offset, double Height, double Viewport, bool MeasureValid, bool ArrangeValid);

/// <summary>Identifies the target WPF CLR type represented by a proxy.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WpfTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>The transport boundary for remote control operations.</summary>
public interface IWpfControlTransport
{
    Task<string> FindAsync(string? automationId, string? type, string? name, string? text, string? root, bool visibleOnly, int timeoutMs);
    Task<bool> IsTypeAsync(string handle, string type);
    Task<(string Handle, string? Name, string? Text, string? AutomationId)[]> FindAllAsync(string? type, string? name, string? text, string? root, bool visibleOnly);
    Task<JsonElement> ReadAsync(string handle, string path, string fields);
    Task<JsonElement> TreeAsync(string? handle = null);
    Task<JsonElement> PropertiesAsync(string handle);
    Task<JsonElement> BindingsAsync(string handle);
    Task<string> RenderedTextAsync(string handle);
    Task ClickAsync(string handle, bool physical, string? clickType);
    Task SetTextAsync(string handle, string text);
    Task SelectItemAsync(string handle, string text);
    Task SendKeysAsync(string handle, string keys);
    Task HoverAsync(string handle);
    Task ScrollAsync(string handle, int delta);
    Task WaitForStableBoundsAsync(string handle);
    Task SetPropertyAsync(string handle, string property, string value);
    Task ScrollViewerAsync(string handle, ScrollViewerAction action);
    Task ScrollItemIntoViewAsync(string handle, int index);
}

/// <summary>A client for typed controls in an attached WPF process.</summary>
public sealed class WpfControls(IWpfControlTransport transport)
{
    internal IWpfControlTransport Transport { get; } = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>Finds a typed control. Set <paramref name="matchType"/> to false to select without a type filter, then verify the selected control's type.</summary>
    public async Task<T> FindAsync<T>(string? automationId = null, string? name = null, string? text = null,
        WpfElement? root = null, bool visibleOnly = true, int timeoutMs = 10000, bool matchType = true) where T : WpfElement
    {
        if (root != null && !ReferenceEquals(root.Owner, this))
            throw new ArgumentException("The search root belongs to another inspector session.", nameof(root));
        var type = ProxyType<T>();
        var handle = await Transport.FindAsync(automationId, matchType ? type : null, name, text, root?.Handle, visibleOnly, timeoutMs);
        if (!matchType && type != null && !await Transport.IsTypeAsync(handle, type))
            throw new InvalidOperationException($"The selected control does not match {type}.");
        return Create<T>(handle);
    }

    public async Task<T[]> FindAllAsync<T>(string? name = null, string? text = null, WpfElement? root = null,
        bool visibleOnly = true) where T : WpfElement
    {
        if (root != null && !ReferenceEquals(root.Owner, this))
            throw new ArgumentException("The search root belongs to another inspector session.", nameof(root));
        var found = await Transport.FindAllAsync(ProxyType<T>(), name, text, root?.Handle, visibleOnly);
        return found.Select(item => Create<T>(item.Handle, item.Name, item.Text, item.AutomationId)).ToArray();
    }

    private static string? ProxyType<T>() where T : WpfElement => typeof(T) == typeof(WpfElement) ? null :
        typeof(T).GetCustomAttribute<WpfTypeAttribute>()?.Name
        ?? throw new InvalidOperationException($"{typeof(T).FullName} does not declare a WpfTypeAttribute.");

    private T Create<T>(string handle, string? name = null, string? text = null, string? automationId = null) where T : WpfElement
    {
        var element = new WpfElement(this, handle, ProxyType<T>(), name, text, automationId);
        if (typeof(T) == typeof(WpfElement)) return (T)element;
        return (T)(Activator.CreateInstance(typeof(T), element)
            ?? throw new InvalidOperationException($"Cannot construct control proxy {typeof(T).FullName}."));
    }
}

/// <summary>A session-bound reference to an element in the target visual tree.</summary>
public class WpfElement : IEquatable<WpfElement>
{
    internal WpfControls Owner { get; }
    internal string Handle { get; }
    private readonly string? verifiedType;
    // Search metadata is a snapshot and is populated only by a multi-element search.
    public string? MatchedName { get; }
    public string? MatchedText { get; }
    public string? MatchedAutomationId { get; }

    internal WpfElement(WpfControls owner, string handle, string? verifiedType, string? name = null, string? searchText = null, string? automationId = null)
    {
        Owner = owner;
        Handle = string.IsNullOrEmpty(handle) ? throw new ArgumentException("An element handle is required.", nameof(handle)) : handle;
        this.verifiedType = verifiedType;
        MatchedName = name;
        MatchedText = searchText;
        MatchedAutomationId = automationId;
    }

    protected WpfElement(WpfElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var expected = GetType().GetCustomAttribute<WpfTypeAttribute>()?.Name;
        if (expected == null || element.verifiedType != expected)
            throw new ArgumentException($"The control was not located as {expected ?? GetType().Name}.", nameof(element));
        Owner = element.Owner;
        Handle = element.Handle;
        verifiedType = element.verifiedType;
        MatchedName = element.MatchedName;
        MatchedText = element.MatchedText;
        MatchedAutomationId = element.MatchedAutomationId;
    }

    public Task<T> FindAsync<T>(string? automationId = null, string? name = null, string? text = null,
        bool visibleOnly = true, int timeoutMs = 10000, bool matchType = true) where T : WpfElement =>
        Owner.FindAsync<T>(automationId, name, text, this, visibleOnly, timeoutMs, matchType);

    public Task<T[]> FindAllAsync<T>(string? name = null, string? text = null, bool visibleOnly = true) where T : WpfElement =>
        Owner.FindAllAsync<T>(name, text, this, visibleOnly);

    public Task ClickAsync(string? clickType = null) => Owner.Transport.ClickAsync(Handle, false, clickType);
    public Task PhysicalClickAsync(string? clickType = null) => Owner.Transport.ClickAsync(Handle, true, clickType);
    public Task SendKeysAsync(string keys) => Owner.Transport.SendKeysAsync(Handle, keys);
    public Task HoverAsync() => Owner.Transport.HoverAsync(Handle);
    public Task ScrollAsync(int delta) => Owner.Transport.ScrollAsync(Handle, delta);
    public Task WaitForStableBoundsAsync() => Owner.Transport.WaitForStableBoundsAsync(Handle);
    public Task<string> RenderedTextAsync() => Owner.Transport.RenderedTextAsync(Handle);
    public async Task<TextLayout> TextLayoutAsync() => new(WpfSession.Value((await ReadAsync("", "$textLayout")).GetProperty("value"), "$textLayout"));
    public async Task<bool> AncestorIsEnabledAsync(int generations)
    {
        if (generations < 1) throw new ArgumentOutOfRangeException(nameof(generations));
        var path = string.Join(".", Enumerable.Repeat("Parent", generations));
        return WpfSession.Value((await ReadAsync(path, "IsEnabled")).GetProperty("value"), "IsEnabled").GetBoolean();
    }
    public async Task<bool> OwnedToolTipIsOpenAsync() => WpfSession.Value((await ReadAsync("ToolTip", "IsOpen")).GetProperty("value"), "IsOpen").GetBoolean();
    public async Task<bool> HasNoEffectAsync() => WpfSession.Value((await ReadAsync("", "Effect")).GetProperty("value"), "Effect").ValueKind == JsonValueKind.Null;
    public async Task<string?> EffectTypeNameAsync() => WpfSession.Value((await ReadAsync("", "Effect")).GetProperty("value"), "Effect").GetProperty("type").GetString();
    public Task<JsonElement> ReadAsync(string path, string fields) => Owner.Transport.ReadAsync(Handle, path, fields);
    public Task<JsonElement> BindingsAsync() => Owner.Transport.BindingsAsync(Handle);
    public async Task<bool> BindingContainsAsync(string expected) => ContainsString(await BindingsAsync(), expected);

    private static bool ContainsString(JsonElement value, string expected) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() == expected,
        JsonValueKind.Object => value.EnumerateObject().Any(member => ContainsString(member.Value, expected)),
        JsonValueKind.Array => value.EnumerateArray().Any(item => ContainsString(item, expected)),
        _ => false
    };

    public async Task<InspectedText> ReadTextAsync(string path, string field)
    {
        var projection = await ReadAsync(path, field);
        if (!projection.TryGetProperty("value", out var values) || !values.TryGetProperty(field, out var member)
            || !member.TryGetProperty("value", out var value)) return new InspectedText(false, "");
        return new InspectedText(true, value.ToString());
    }

    public async Task<ObservedText> ObservedTextAsync(WpfTextField field)
    {
        if (field == WpfTextField.Text) return ObservedText.FromString(await RenderedTextAsync());
        var name = field switch
        {
            WpfTextField.Header => "Header",
            WpfTextField.Content => "Content",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var value = WpfSession.Value((await ReadAsync("", name)).GetProperty("value"), name);
        return ObservedText.FromProjection(value);
    }
    public async Task<BindingInfo[]> BindingInfoAsync() => (await BindingsAsync()).GetProperty("Bindings").EnumerateArray().Select(binding => new BindingInfo(binding)).ToArray();
    public Task<JsonElement> PropertiesAsync() => Owner.Transport.PropertiesAsync(Handle);
    public async Task<string?> RuntimeTypeNameAsync() => (await Owner.Transport.TreeAsync(Handle)).GetProperty("Root").GetProperty("TypeName").GetString();
    public Task SetPropertyAsync(string property, string value) => Owner.Transport.SetPropertyAsync(Handle, property, value);

    public async Task<T?> PropertyAsync<T>(string property)
    {
        var projection = await ReadAsync("", property);
        var member = projection.GetProperty("value").GetProperty(property);
        if (!member.TryGetProperty("value", out var value))
            throw new InvalidOperationException($"Cannot read {property}: {member}");
        return value.Deserialize<T>();
    }

    public bool Equals(WpfElement? other) => other != null && ReferenceEquals(Owner, other.Owner) && Handle == other.Handle;
    public override bool Equals(object? other) => other is WpfElement element && Equals(element);
    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Owner), Handle);
}

[WpfType("DependencyObject")]
public class DependencyObject(WpfElement element) : WpfElement(element);

[WpfType("Visual")]
public class Visual(WpfElement element) : DependencyObject(element);

[WpfType("UIElement")]
public class UIElement(WpfElement element) : Visual(element);

[WpfType("FrameworkElement")]
public class FrameworkElement(WpfElement element) : UIElement(element)
{
    public Task<bool> IsVisibleAsync() => PropertyAsync<bool>("IsVisible");
    public Task<bool> IsEnabledAsync() => PropertyAsync<bool>("IsEnabled");
    public Task<string?> ToolTipTextAsync() => PropertyAsync<string>("ToolTip");
    public async Task<string?> ToolTipTextIfStringAsync()
    {
        var value = WpfSession.Value((await ReadAsync("", "ToolTip")).GetProperty("value"), "ToolTip");
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
    public async Task<bool> HasEmptyToolTipAsync()
    {
        var member = (await ReadAsync("", "ToolTip")).GetProperty("value").GetProperty("ToolTip");
        var value = member.TryGetProperty("value", out var scalar) ? scalar : member;
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString());
    }
    public async Task<ElementState> StateAsync()
    {
        var projection = (await ReadAsync("", "IsVisible,IsEnabled")).GetProperty("value");
        return new ElementState(projection.GetProperty("IsVisible").GetProperty("value").GetBoolean(),
            projection.GetProperty("IsEnabled").GetProperty("value").GetBoolean());
    }
}

[WpfType("Control")]
public class Control(WpfElement element) : FrameworkElement(element);

[WpfType("ContentControl")]
public class ContentControl(WpfElement element) : Control(element)
{
    public async Task<string> ContentValueTextAsync() => WpfSession.Value((await ReadAsync("", "Content")).GetProperty("value"), "Content").ToString();
    public async Task<bool> IsContentNullAsync() => WpfSession.Value((await ReadAsync("", "Content")).GetProperty("value"), "Content").ValueKind == JsonValueKind.Null;
    public async Task<string?> ContentTextAsync()
    {
        var member = (await ReadAsync("", "Content")).GetProperty("value").GetProperty("Content");
        var value = member.TryGetProperty("value", out var scalar) ? scalar : member;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

[WpfType("UserControl")]
public class UserControl(WpfElement element) : ContentControl(element);

[WpfType("HeaderedContentControl")]
public class HeaderedContentControl(WpfElement element) : ContentControl(element)
{
    public async Task<HeaderValue> HeaderAsync() => new(WpfSession.Value((await ReadAsync("", "Header")).GetProperty("value"), "Header"));
    public async Task<string> HeaderObjectTextAsync() => WpfSession.Value((await ReadAsync("Header", "Text")).GetProperty("value"), "Text").ToString();
    public async Task<string?> HeaderTextAsync()
    {
        var member = (await ReadAsync("", "Header")).GetProperty("value").GetProperty("Header");
        var value = member.TryGetProperty("value", out var scalar) ? scalar : member;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

[WpfType("ButtonBase")]
public class ButtonBase(WpfElement element) : ContentControl(element)
{
    public async Task<bool> IsCommandNullAsync() => WpfSession.Value((await ReadAsync("", "Command")).GetProperty("value"), "Command").ValueKind == JsonValueKind.Null;
}

[WpfType("Button")]
public class Button(WpfElement element) : ButtonBase(element);

[WpfType("RangeBase")]
public class RangeBase(WpfElement element) : Control(element);

[WpfType("Slider")]
public class Slider(WpfElement element) : RangeBase(element);

[WpfType("TextBlock")]
public class TextBlock(WpfElement element) : FrameworkElement(element)
{
    public Task<string?> TextAsync() => PropertyAsync<string>("Text");
    public async Task<RenderedTextState> RenderedStateAsync()
    {
        var projection = (await ReadAsync("", "$renderedText,IsVisible")).GetProperty("value");
        var text = WpfSession.Value(projection, "$renderedText");
        if (text.ValueKind == JsonValueKind.Object && text.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean())
            throw new InvalidOperationException("Rendered text observation was truncated.");
        if (text.ValueKind != JsonValueKind.String) return new RenderedTextState(null, false);
        return new RenderedTextState(text.GetString(), WpfSession.Value(projection, "IsVisible").GetBoolean());
    }
}

[WpfType("TextBoxBase")]
public class TextBoxBase(WpfElement element) : Control(element);

[WpfType("TextBox")]
public class TextBox(WpfElement element) : TextBoxBase(element)
{
    public Task SetTextAsync(string text) => Owner.Transport.SetTextAsync(Handle, text);
    public Task<string?> TextAsync() => PropertyAsync<string>("Text");
}

[WpfType("RichTextBox")]
public class RichTextBox(WpfElement element) : TextBoxBase(element);

[WpfType("PasswordBox")]
public class PasswordBox(WpfElement element) : Control(element);

[WpfType("CheckBox")]
public class CheckBox(WpfElement element) : ToggleButton(element);

[WpfType("ToggleButton")]
public class ToggleButton(WpfElement element) : ButtonBase(element)
{
    public Task<bool?> IsCheckedAsync() => PropertyAsync<bool?>("IsChecked");
}

[WpfType("RadioButton")]
public class RadioButton(WpfElement element) : ToggleButton(element);

[WpfType("ItemsControl")]
public class ItemsControl(WpfElement element) : Control(element)
{
    public Task<int> ItemCountAsync() => PropertyAsync<int>("Items.Count");
}

[WpfType("HeaderedItemsControl")]
public class HeaderedItemsControl(WpfElement element) : ItemsControl(element)
{
    public async Task<HeaderValue> HeaderAsync() => new(WpfSession.Value((await ReadAsync("", "Header")).GetProperty("value"), "Header"));
}

[WpfType("MenuItem")]
public class MenuItem(WpfElement element) : HeaderedItemsControl(element);

[WpfType("TreeView")]
public class TreeView(WpfElement element) : ItemsControl(element);

[WpfType("TreeViewItem")]
public class TreeViewItem(WpfElement element) : HeaderedItemsControl(element)
{
    public Task SetExpandedAsync(bool value) => SetPropertyAsync("IsExpanded", value ? "True" : "False");
}

[WpfType("Selector")]
public class Selector(WpfElement element) : ItemsControl(element)
{
    public Task<int> SelectedIndexAsync() => PropertyAsync<int>("SelectedIndex");
    public async Task<string?> SelectedItemTypeNameAsync() => WpfSession.Value((await ReadAsync("", "SelectedItem")).GetProperty("value"), "SelectedItem").GetProperty("type").GetString();
    public Task SetSelectedIndexAsync(int index) => SetPropertyAsync("SelectedIndex", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public Task SelectItemAsync(string text) => Owner.Transport.SelectItemAsync(Handle, text);
}

[WpfType("ComboBox")]
public class ComboBox(WpfElement element) : Selector(element)
{
    public Task<string?> TextAsync() => PropertyAsync<string>("Text");
    public Task<bool> IsDropDownOpenAsync() => PropertyAsync<bool>("IsDropDownOpen");
    public Task SetDropDownOpenAsync(bool value) => SetPropertyAsync("IsDropDownOpen", value ? "True" : "False");
    public async Task SearchAsync(string text)
    {
        await Owner.Transport.SetTextAsync(Handle, text);
        await SendKeysAsync("Enter");
    }
}

[WpfType("ListBox")]
public class ListBox(WpfElement element) : Selector(element)
{
    public Task ScrollItemIntoViewAsync(int index) => Owner.Transport.ScrollItemIntoViewAsync(Handle, index);
}

[WpfType("ListView")]
public class ListView(WpfElement element) : ListBox(element);

[WpfType("MultiSelector")]
public class MultiSelector(WpfElement element) : Selector(element);

[WpfType("DataGrid")]
public class DataGrid(WpfElement element) : MultiSelector(element);

[WpfType("ListBoxItem")]
public class ListBoxItem(WpfElement element) : ContentControl(element);

[WpfType("ListViewItem")]
public class ListViewItem(WpfElement element) : ListBoxItem(element);

[WpfType("TabControl")]
public class TabControl(WpfElement element) : Selector(element);

[WpfType("TabItem")]
public class TabItem(WpfElement element) : HeaderedContentControl(element)
{
    public Task<bool> IsSelectedAsync() => PropertyAsync<bool>("IsSelected");
}

[WpfType("DataGridRow")]
public class DataGridRow(WpfElement element) : Control(element);

[WpfType("DataGridCell")]
public class DataGridCell(WpfElement element) : ContentControl(element)
{
    public Task<int> ColumnDisplayIndexAsync() => PropertyAsync<int>("Column.DisplayIndex");
}

[WpfType("ScrollViewer")]
public class ScrollViewer(WpfElement element) : ContentControl(element)
{
    public Task ScrollAsync(ScrollViewerAction action) => Owner.Transport.ScrollViewerAsync(Handle, action);
    public async Task<ScrollState> ViewportStateAsync()
    {
        var values = (await ReadAsync("", "VerticalOffset,ScrollableHeight,ViewportHeight,IsMeasureValid,IsArrangeValid")).GetProperty("value");
        return new ScrollState(WpfSession.Value(values, "VerticalOffset").GetDouble(), WpfSession.Value(values, "ScrollableHeight").GetDouble(),
            WpfSession.Value(values, "ViewportHeight").GetDouble(), WpfSession.Value(values, "IsMeasureValid").GetBoolean(),
            WpfSession.Value(values, "IsArrangeValid").GetBoolean());
    }
}

[WpfType("ToolTip")]
public class ToolTip(WpfElement element) : ContentControl(element)
{
    public Task SetOpenAsync(bool value) => SetPropertyAsync("IsOpen", value ? "true" : "false");
}

[WpfType("Label")]
public class Label(WpfElement element) : ContentControl(element);

[WpfType("Expander")]
public class Expander(WpfElement element) : HeaderedContentControl(element);

[WpfType("GroupItem")]
public class GroupItem(WpfElement element) : ContentControl(element);

[WpfType("Popup")]
public class Popup(WpfElement element) : FrameworkElement(element)
{
    public Task<bool> IsOpenAsync() => PropertyAsync<bool>("IsOpen");
}

[WpfType("PopupRoot")]
public class PopupRoot(WpfElement element) : FrameworkElement(element);

[WpfType("ContentPresenter")]
public class ContentPresenter(WpfElement element) : FrameworkElement(element);

[WpfType("Window")]
public class Window(WpfElement element) : ContentControl(element)
{
    public Task<bool> IsActiveAsync() => PropertyAsync<bool>("IsActive");
    public Task<string?> TitleAsync() => PropertyAsync<string>("Title");
    public Task<string?> WindowStateAsync() => PropertyAsync<string>("WindowState");
}

[WpfType("Panel")]
public class Panel(WpfElement element) : FrameworkElement(element);

[WpfType("Grid")]
public class Grid(WpfElement element) : Panel(element);

[WpfType("StackPanel")]
public class StackPanel(WpfElement element) : Panel(element);

[WpfType("Decorator")]
public class Decorator(WpfElement element) : FrameworkElement(element);

[WpfType("Border")]
public class Border(WpfElement element) : Decorator(element);

[WpfType("Image")]
public class Image(WpfElement element) : FrameworkElement(element)
{
    public Task<string?> SourceUriAsync() => PropertyAsync<string>("Source.UriSource.AbsoluteUri");
}

[WpfType("Shape")]
public class Shape(WpfElement element) : FrameworkElement(element);

[WpfType("Path")]
public class WpfPath(WpfElement element) : Shape(element);
