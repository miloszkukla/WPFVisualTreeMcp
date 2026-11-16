using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ClrReadKind = WpfVisualTreeMcp.Shared.Models.ClrReadKind;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Explicit CLR reads with shallow values and bounded collection traversal.</summary>
public sealed class ClrReader
{
    private static readonly Regex Segment = new(@"^([A-Za-z_]\w*)(?:\[(\d+)\])?$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(null, false) } };

    public string Read(object root, string path, string[] fields, int skip, int take, bool membersOnly = false)
    {
        if (skip < 0 || skip > 10000 || take < 1 || take > 200 || fields.Length > 50)
            throw new ArgumentOutOfRangeException(nameof(take), "skip 0-10000, take 1-200 and at most 50 fields are supported.");
        var value = Resolve(root, path);
        if (membersOnly)
        {
            if (value == null) return JsonSerializer.Serialize(new { kind = ClrReadKind.Null, members = new object[0] }, Options);
            var members = value.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m is PropertyInfo || m is FieldInfo || m is MethodInfo method && !method.IsSpecialName)
                .Select(m => new { name = m.Name, kind = m.MemberType, signature = m.ToString() })
                .OrderBy(m => m.name, StringComparer.Ordinal).ThenBy(m => m.signature, StringComparer.Ordinal).ToArray();
            return JsonSerializer.Serialize(new { kind = ClrReadKind.Members, type = value.GetType().FullName,
                totalCount = members.Length, skip, truncated = members.Length > skip + take, members = members.Skip(skip).Take(take).ToArray() }, Options);
        }
        if (value is IEnumerable enumerable && !(value is string))
        {
            var clock = Stopwatch.StartNew();
            var rows = new List<object?>();
            var iterator = enumerable.GetEnumerator();
            var seen = 0;
            var hasMore = false;
            var deadlineReached = false;
            try
            {
                while (seen < skip && iterator.MoveNext())
                {
                    seen++;
                    if (clock.ElapsedMilliseconds > 500) { deadlineReached = true; break; }
                }
                while (!deadlineReached && rows.Count < take && iterator.MoveNext())
                {
                    rows.Add(Project(iterator.Current, fields));
                    seen++;
                    if (clock.ElapsedMilliseconds > 500) deadlineReached = true;
                }
                if (!deadlineReached) hasMore = iterator.MoveNext();
            }
            finally { (iterator as IDisposable)?.Dispose(); }
            return JsonSerializer.Serialize(new { kind = ClrReadKind.Collection, type = value.GetType().FullName,
                totalCount = value is ICollection collection ? (int?)collection.Count : null,
                skip, returned = rows.Count, truncated = hasMore || deadlineReached, deadlineReached, items = rows }, Options);
        }
        return JsonSerializer.Serialize(new { kind = fields.Length == 0 ? ClrReadKind.Value : ClrReadKind.Object, value = Project(value, fields) }, Options);
    }

    public static object? Resolve(object? current, string path)
    {
        if (string.IsNullOrEmpty(path)) return current;
        if (path.Length > 1024 || path.Split('.').Length > 16) throw new ArgumentException("CLR path exceeds its bound.");
        foreach (var segment in path.Split('.'))
        {
            var match = Segment.Match(segment);
            if (!match.Success) throw new ArgumentException("Only property/field paths and integer indexes are supported.");
            if (current == null) return null;
            var type = current.GetType();
            var name = match.Groups[1].Value;
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) throw new InvalidOperationException("Unreadable property: " + name);
                current = property.GetValue(current, null);
            }
            else
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field == null) throw new MissingMemberException(type.FullName, name);
                current = field.GetValue(current);
            }
            if (match.Groups[2].Success)
            {
                if (!(current is IList list)) throw new InvalidOperationException("Indexed CLR paths require IList.");
                var index = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (index >= list.Count) throw new ArgumentOutOfRangeException(nameof(path), "Collection index out of range.");
                current = list[index];
            }
        }
        return current;
    }

    private static object? Project(object? value, string[] fields)
    {
        if (fields.Length == 0) return Shallow(value);
        var result = new Dictionary<string, object?>();
        foreach (var field in fields)
        {
            try
            {
                if (field == "$pointer")
                {
                    if (!(value is UIElement pointerTarget)) throw new MissingMemberException(value?.GetType().FullName, field);
                    result[field] = new { value = Pointer(pointerTarget) };
                    continue;
                }
                if (field == "$textLayout")
                {
                    if (!(value is TextBlock layoutBlock)) throw new MissingMemberException(value?.GetType().FullName, field);
                    result[field] = new { value = TextLayout(layoutBlock) };
                    continue;
                }
                var projected = field == "$renderedText"
                    ? value is TextBlock textBlock ? TreeWalker.GetRenderedText(textBlock) : throw new MissingMemberException(value?.GetType().FullName, field)
                    : Resolve(value, field);
                result[field] = new { value = Shallow(projected) };
            }
            catch (MissingMemberException error) { result[field] = new { missing = true, error = error.Message }; }
            catch (Exception error) { result[field] = new { error = error.GetBaseException().Message }; }
        }
        return result;
    }

    private static object Pointer(UIElement target)
    {
        target.Dispatcher.VerifyAccess();
        var source = PresentationSource.FromVisual(target) as HwndSource;
        var root = source?.RootVisual as UIElement;
        var position = Mouse.GetPosition(target);
        var center = new Point(target.RenderSize.Width / 2, target.RenderSize.Height / 2);
        Point? centerInRoot = root == null ? null : target.TranslatePoint(center, root);
        return new
        {
            target = PointerElement(target),
            root = PointerElement(root),
            windowHandle = source?.Handle.ToInt64() ?? 0,
            position = new { x = position.X, y = position.Y },
            center = new { x = center.X, y = center.Y },
            centerInRoot = centerInRoot.HasValue ? new { x = centerInRoot.Value.X, y = centerInRoot.Value.Y } : null,
            isMouseOver = target.IsMouseOver,
            isMeasureValid = target.IsMeasureValid,
            isArrangeValid = target.IsArrangeValid,
            directlyOver = PointerHit(target, Mouse.DirectlyOver),
            captured = PointerHit(target, Mouse.Captured),
            centerHit = PointerHit(target, centerInRoot.HasValue ? root!.InputHitTest(centerInRoot.Value) : null)
        };
    }

    private static object PointerHit(UIElement target, IInputElement? hit)
    {
        var current = hit as DependencyObject;
        if (hit != null && current == null)
            throw new InvalidOperationException("The WPF input hit is not a DependencyObject.");
        var ancestors = new List<object>();
        var targetOrDescendant = false;
        var ancestorsTruncated = false;
        for (var depth = 0; current != null; depth++, current = PointerParent(current))
        {
            if (depth >= 128) throw new InvalidOperationException("The WPF pointer ancestry exceeds its 128-node read bound.");
            targetOrDescendant |= ReferenceEquals(current, target);
            if (depth == 0) continue;
            if (ancestors.Count < 10) ancestors.Add(PointerElement(current)!);
            else ancestorsTruncated = true;
        }
        return new { element = PointerElement(hit as DependencyObject), targetOrDescendant, ancestors, ancestorsTruncated };
    }

    private static DependencyObject? PointerParent(DependencyObject element)
    {
        if (element is Visual || element is Visual3D) return VisualTreeHelper.GetParent(element);
        if (element is ContentElement content)
            return ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent;
        return LogicalTreeHelper.GetParent(element);
    }

    private static object? PointerElement(DependencyObject? element)
    {
        if (element == null) return null;
        var name = (element as FrameworkElement)?.Name ?? (element as FrameworkContentElement)?.Name;
        return new { type = PointerString(element.GetType().FullName), name = PointerString(name),
            automationId = PointerString(AutomationProperties.GetAutomationId(element)) };
    }

    private static string? PointerString(string? text) => text != null && text.Length > 128 ? text.Substring(0, 128) : text;

    private static object TextLayout(TextBlock block)
    {
        var clock = Stopwatch.StartNew();
        var text = TreeWalker.GetRenderedText(block);
        var source = PresentationSource.FromVisual(block) as HwndSource;
        var windowHandle = source?.Handle.ToInt64() ?? 0;
        var visible = block.IsVisible && block.Opacity > 0 && source != null && block.ContentStart.HasValidLayout;
        var truncated = text.Length > 4096;
        var deadlineReached = false;
        var runs = new List<object>();
        var offset = 0;
        if (visible && !truncated)
        {
            if (!GetClientRect(source!.Handle, out var client) || !ClientToScreen(source.Handle, out var origin))
                throw new InvalidOperationException("The rendered text window has no readable client viewport.");
            var viewport = new Rect(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
            for (var position = block.ContentStart; position != null && position.CompareTo(block.ContentEnd) < 0;
                position = position.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (clock.ElapsedMilliseconds > 500) { deadlineReached = true; break; }
                if (position.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
                if (runs.Count >= 200) { truncated = true; break; }
                var runText = position.GetTextInRun(LogicalDirection.Forward);
                var start = text.IndexOf(runText, offset, StringComparison.Ordinal);
                if (start < 0) throw new InvalidOperationException("The rendered inline text could not be mapped to its layout.");
                var characters = new List<object>();
                for (var index = 0; index < runText.Length;)
                {
                    if (clock.ElapsedMilliseconds > 500) { deadlineReached = true; break; }
                    var length = char.IsHighSurrogate(runText[index]) && index + 1 < runText.Length && char.IsLowSurrogate(runText[index + 1]) ? 2 : 1;
                    var first = position.GetPositionAtOffset(index, LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Forward);
                    var last = position.GetPositionAtOffset(index + length, LogicalDirection.Backward).GetCharacterRect(LogicalDirection.Backward);
                    var local = Rect.Union(first, last);
                    if (!local.IsEmpty && local.Width > 0 && local.Height > 0)
                    {
                        var corners = new[] { block.PointToScreen(local.TopLeft), block.PointToScreen(local.TopRight),
                            block.PointToScreen(local.BottomLeft), block.PointToScreen(local.BottomRight) };
                        var left = corners.Min(point => point.X);
                        var top = corners.Min(point => point.Y);
                        var width = corners.Max(point => point.X) - left;
                        var height = corners.Max(point => point.Y) - top;
                        characters.Add(new { offset = start + index, length, x = left, y = top, width, height,
                            visible = IsTextPointVisible(block, new Point(left + width / 2, top + height / 2), viewport) });
                    }
                    index += length;
                }
                runs.Add(new { text = runText, offset = start, characters });
                offset = start + runText.Length;
                if (deadlineReached) break;
            }
        }
        return new { text = text.Length > 4096 ? text.Substring(0, 4096) : text, windowHandle, visible, truncated = truncated || deadlineReached, deadlineReached, runs };
    }

    private static bool IsTextPointVisible(TextBlock block, Point screen, Rect viewport)
    {
        if (!viewport.Contains(screen)) return false;
        for (DependencyObject? current = block; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (!(current is UIElement element)) continue;
            if (!element.IsVisible || element.Opacity <= 0) return false;
            if (element is Window window && window.WindowState == WindowState.Minimized) return false;
            var point = element.PointFromScreen(screen);
            if (element.ClipToBounds && !new Rect(element.RenderSize).Contains(point)) return false;
            if (element.Clip != null && !element.Clip.FillContains(point)) return false;
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct ScreenRect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out ScreenRect rectangle);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, out ScreenPoint point);

    private static object? Shallow(object? value)
    {
        if (value == null) return null;
        if (value is string text) return text.Length <= 4096 ? (object)text : new { text = text.Substring(0, 4096), truncated = true };
        if (value is Enum) return value.ToString();
        if (value is double d && (double.IsNaN(d) || double.IsInfinity(d))) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value is float f && (float.IsNaN(f) || float.IsInfinity(f))) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value.GetType().IsPrimitive || value is decimal || value is DateTime || value is DateTimeOffset || value is Guid) return value;
        return new { type = value.GetType().FullName, count = value is ICollection collection ? (int?)collection.Count : null };
    }
}
