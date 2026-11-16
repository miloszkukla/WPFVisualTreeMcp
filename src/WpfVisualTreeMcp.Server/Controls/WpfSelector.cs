using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace WpfVisualTreeMcp.Server.Controls;

public readonly record struct SelectorValue<T>(bool IsAvailable, T Value);

/// <summary>Finds controls by typed inspected values while handling stale elements.</summary>
public static class WpfSelector
{
    public static async Task<SelectorValue<string>> TextAsync(WpfElement element, string path, string field)
    {
        var value = await element.ReadTextAsync(path, field);
        return new(value.IsAvailable, value.Text);
    }

    public static Task<SelectorValue<string>> ContentAsync(ContentControl element) => TextAsync(element, "", "Content");
    public static Task<SelectorValue<string>> ToolTipAsync(FrameworkElement element) => TextAsync(element, "", "ToolTip");

    public static async Task<SelectorValue<int>> IndexAsync(WpfElement element, string path, string field)
    {
        var projection = await element.ReadAsync(path, field);
        if (!projection.TryGetProperty("value", out var values) || !values.TryGetProperty(field, out var member)
            || !member.TryGetProperty("value", out var value)) return new(false, default);
        var index = 0;
        var text = value.ToString();
        var available = value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
            && index.ToString(CultureInfo.InvariantCulture) == text;
        return new(available, available ? index : default);
    }

    public static async Task<T> FindAsync<T, TValue>(Func<Task<T[]>> candidates, Func<T, Task<SelectorValue<TValue>>> read,
        TValue expected, CancellationToken cancellationToken, string label, TimeSpan? timeout = null)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            var found = new List<T>();
            var replaced = false;
            foreach (var candidate in await candidates())
            {
                SelectorValue<TValue> value;
                try { value = await read(candidate); }
                catch (InvalidOperationException error) when (error.Message.StartsWith("Element handle '", StringComparison.Ordinal)
                    && error.Message.Contains("not found", StringComparison.Ordinal))
                {
                    replaced = true;
                    break;
                }
                if (!value.IsAvailable) continue;
                if (EqualityComparer<TValue>.Default.Equals(value.Value, expected)) found.Add(candidate);
            }
            if (!replaced && found.Count > 1) throw new InvalidOperationException($"Ambiguous {typeof(T).Name} {label}={expected}: {found.Count}.");
            if (!replaced && found.Count == 1) return found[0];
            await Task.Delay(150, cancellationToken);
        } while (clock.Elapsed < (timeout ?? TimeSpan.FromSeconds(10)));
        throw new InvalidOperationException($"Missing {typeof(T).Name} {label}={expected}.");
    }
}
