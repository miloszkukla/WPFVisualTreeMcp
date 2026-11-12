using System.Text.Json;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfVisualTreeMcp.Inspector;
using TreeWalker = WpfVisualTreeMcp.Inspector.TreeWalker;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests
{
    public class TreeWalkerExactFilterTests
    {
        [Theory]
        [InlineData("TabControl")]
        [InlineData("System.Windows.Controls.TabControl")]
        public void ExactTypeMatchesBaseHierarchyAndExcludesNamespaceSubstring(string exactType)
        {
            OnSta(() =>
            {
                var root = new StackPanel();
                root.Children.Add(new DerivedTabs { Name = "ActualTabs" });
                root.Children.Add(new TabControls.Parts.SimpleTabHeader { Name = "NamespaceImpostor" });
                using var result = JsonDocument.Parse(new TreeWalker().FindElements(root, new FindCriteria
                {
                    PropertyFilter = new() { ["$type"] = exactType }
                }));
                var elements = result.RootElement.GetProperty("elements");
                Assert.Equal(1, elements.GetArrayLength());
                Assert.Equal("ActualTabs", elements[0].GetProperty("name").GetString());
            });
        }

        [Fact]
        public void ExactAutomationIdRejectsPrefixSuffixAndDifferentCase()
        {
            OnSta(() =>
            {
                var root = new StackPanel();
                foreach (var id in new[] { "Target", "TargetSuffix", "PrefixTarget", "target" })
                {
                    var button = new Button { Name = id };
                    AutomationProperties.SetAutomationId(button, id);
                    root.Children.Add(button);
                }
                using var result = JsonDocument.Parse(new TreeWalker().FindElements(root, new FindCriteria
                {
                    PropertyFilter = new() { ["$automationId"] = "Target" }
                }));
                var elements = result.RootElement.GetProperty("elements");
                Assert.Equal(1, elements.GetArrayLength());
                Assert.Equal("Target", elements[0].GetProperty("name").GetString());
            });
        }

        [Theory]
        [InlineData("tabcontrol")]
        [InlineData("Tab")]
        [InlineData("Controls.TabControl")]
        public void ExactTypeRejectsDifferentCaseAndPartialNames(string exactType)
        {
            OnSta(() =>
            {
                using var result = JsonDocument.Parse(new TreeWalker().FindElements(new TabControl(), new FindCriteria
                {
                    PropertyFilter = new() { ["$type"] = exactType }
                }));
                Assert.Equal(0, result.RootElement.GetProperty("count").GetInt32());
            });
        }

        [Fact]
        public void ExactFiltersCombineWithOrdinaryPropertiesUsingAndSemantics()
        {
            OnSta(() =>
            {
                var root = new StackPanel();
                foreach (var enabled in new[] { true, false })
                {
                    var button = new Button { Name = enabled ? "EnabledTarget" : "DisabledTarget", IsEnabled = enabled };
                    AutomationProperties.SetAutomationId(button, "Target");
                    root.Children.Add(button);
                }
                using var result = JsonDocument.Parse(new TreeWalker().FindElements(root, new FindCriteria
                {
                    PropertyFilter = new() { ["$type"] = "Button", ["$automationId"] = "Target", ["IsEnabled"] = "True" }
                }));
                var elements = result.RootElement.GetProperty("elements");
                Assert.Equal(1, elements.GetArrayLength());
                Assert.Equal("EnabledTarget", elements[0].GetProperty("name").GetString());
            });
        }

        [Fact]
        public void LegacyTypeAndPropertySubstringFiltersRemainFuzzy()
        {
            OnSta(() =>
            {
                var root = new TabControls.Parts.SimpleTabHeader { Name = "NamespaceImpostor", Content = "Submit" };
                using var result = JsonDocument.Parse(new TreeWalker().FindElements(root, new FindCriteria
                {
                    TypeName = "tabcontrol", PropertyFilter = new() { ["Content"] = "UBM" }
                }));
                Assert.Equal(1, result.RootElement.GetProperty("count").GetInt32());
            });
        }

        private sealed class DerivedTabs : TabControl { }

        private static void OnSta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The bounded STA observation completed.");
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

namespace WpfVisualTreeMcp.IntegrationTests.TabControls.Parts
{
    public sealed class SimpleTabHeader : ContentControl { }
}
