using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class PhaseFiveAliasTests
{
    [Fact]
    public void Help_topics_render_distinct_headings_in_the_help_destination()
    {
        Sta(() =>
        {
            var view = new HelpCentreView();
            var headings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var topic in HelpCentreRegistry.Topics)
            {
                view.OpenTopic(topic.Id);
                var heading = Descendants(view).OfType<TextBlock>()
                    .FirstOrDefault(block => block.FontSize == 25)?.Text;
                Assert.False(string.IsNullOrWhiteSpace(heading), $"Help topic {topic.Id} has no visible topic heading.");
                Assert.NotNull(heading);
                Assert.True(headings.TryAdd(heading, topic.Id), $"Help topics {headings.GetValueOrDefault(heading)} and {topic.Id} both render '{heading}'.");
            }
        });
    }

    [Fact]
    public void Support_package_action_has_one_audited_destination()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                var navigator = new TaskNavigator(window);
                navigator.DisplayTaskRoute(TaskNavigation.Find("health")!.Route);
                Assert.DoesNotContain(Descendants(window.FocusedWorkspaceHost).OfType<Button>(), button => button.Name == "SupportPackageButton");
                navigator.DisplayTaskRoute(TaskNavigation.Find("support-package")!.Route);
                Assert.Contains(Descendants(window.FocusedWorkspaceHost).OfType<Button>(), button => button.Name == "SupportTaskAction");
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); window.Close(); }
        });
    }

    [Fact]
    public void Management_trend_report_keeps_the_daily_sales_bars_in_its_summary()
    {
        Sta(() =>
        {
            var first = new DateOnly(2026, 8, 25);
            var second = first.AddDays(1);
            var data = new ExcelReportData(
                [new("Date"), new("Store"), new("Net Sales"), new("Units"), new("Invoices"), new("Tender Variance"), new("Unmatched Staff Rows")],
                [[first, "EAST", 120m, 2m, 1, 0m, 0], [first, "WEST", 80m, 1m, 1, 0m, 0], [second, "EAST", -30m, -1m, 1, 0m, 0]]);
            var model = VisualReportComposer.Compose(new("Management Trend", first, second, "Passed", "test", "Synthetic test", DateTimeOffset.UtcNow), data);
            var preview = ReportVisualPresenter.BuildFocusedPreview(model, null);
            var chart = Assert.Single(Descendants(preview).OfType<StackPanel>(), panel => panel.Name == "ManagementTrendChartPanel");
            Assert.Equal(2, chart.Children.Count);
            var labels = Descendants(chart).OfType<TextBlock>().Select(block => block.Text).ToArray();
            Assert.Contains(first.ToString("dd MMM"), labels);
            Assert.Contains(200m.ToString("N2"), labels);
            Assert.Contains((-30m).ToString("N2"), labels);
            Assert.Same(data, model.Detail); // Charting never replaces the report's source rows.
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        if (failure is not null) throw new InvalidOperationException("Phase 5 duplicate-destination regression.", failure);
    }
}
