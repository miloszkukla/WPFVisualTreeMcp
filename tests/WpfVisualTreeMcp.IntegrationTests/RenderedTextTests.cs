using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using WpfVisualTreeMcp.Inspector;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class RenderedTextTests
{
    [Fact]
    public void RenderedTextProjectsBoundInlineContentWithoutShadowingClrText()
    {
        OnSta(() =>
        {
            var model = new InlineModel();
            var run = new Run();
            run.SetBinding(Run.TextProperty, new Binding(nameof(InlineModel.Value)) { Source = model });
            var text = new TextBlock();
            text.Inlines.Add(run);
            text.Inlines.Add(new Run(" joined"));
            model.Value = "Actual inline";
            Assert.Equal("Actual inline", run.Text);
            var clrText = text.Text;
            using var result = JsonDocument.Parse(new ClrReader().Read(text, "", new[] { "Text", "$renderedText" }, 0, 1));
            var fields = result.RootElement.GetProperty("value");
            Assert.Equal(clrText, fields.GetProperty("Text").GetProperty("value").GetString());
            Assert.Equal("Actual inline joined", fields.GetProperty("$renderedText").GetProperty("value").GetString());
            using var found = JsonDocument.Parse(new TreeWalker().FindElements(text, new FindCriteria { Text = "Actual inline joined" }));
            Assert.Equal(1, found.RootElement.GetProperty("count").GetInt32());
        });
    }

    [Fact]
    public void RenderedTextPreservesNestedInlineOrderAndLineBreaks()
    {
        OnSta(() =>
        {
            var text = new TextBlock();
            text.Inlines.Add(new Bold(new Run("First")));
            text.Inlines.Add(new LineBreak());
            text.Inlines.Add(new Italic(new Run("Second ¶")));
            using var result = JsonDocument.Parse(new ClrReader().Read(text, "", new[] { "$renderedText" }, 0, 1));
            Assert.Equal("First\r\nSecond ¶", result.RootElement.GetProperty("value").GetProperty("$renderedText").GetProperty("value").GetString());
        });
    }

    [Fact]
    public void RenderedTextRetainsExistingStringBoundsAndRejectsNonTextBlocks()
    {
        OnSta(() =>
        {
            var text = new TextBlock(new Run(new string('A', 4097)));
            using var longResult = JsonDocument.Parse(new ClrReader().Read(text, "", new[] { "$renderedText" }, 0, 1));
            var value = longResult.RootElement.GetProperty("value").GetProperty("$renderedText").GetProperty("value");
            Assert.True(value.GetProperty("truncated").GetBoolean());
            Assert.Equal(4096, value.GetProperty("text").GetString()!.Length);
            using var unsupported = JsonDocument.Parse(new ClrReader().Read(new TextBox { Text = "native" }, "", new[] { "Text", "$renderedText" }, 0, 1));
            var fields = unsupported.RootElement.GetProperty("value");
            Assert.Equal("native", fields.GetProperty("Text").GetProperty("value").GetString());
            Assert.True(fields.GetProperty("$renderedText").GetProperty("missing").GetBoolean());
        });
    }

    private sealed class InlineModel : INotifyPropertyChanged
    {
        private string value = "";
        public string Value { get => value; set { this.value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The inline text observation completed within its bound.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
