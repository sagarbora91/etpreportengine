using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>RA-EXPORT-05 (1.9.8): the Summary tab shows the family's KPI cards and its one visual, or the Rows card.</summary>
public sealed class ReportSummaryPresentationTests
{
    private static ExcelReportMetadata Meta(string name) => new(name, new(2026, 9, 1), new(2026, 9, 30), "Passed", "test", "Synthetic only.", DateTimeOffset.UtcNow);

    [Fact]
    public void Sales_summary_tab_shows_kpi_cards_and_the_top_five_bar_list()
    {
        Sta(() =>
        {
            var rows = Enumerable.Range(1, 7).Select(i => (IReadOnlyList<object?>)[$"Group {i}", i * 2m, i * 1000m, i, 0]).ToArray();
            var data = new ExcelReportData([new("Group"), new("Units", "#,##0.00"), new("Net Sales", "#,##0.00"), new("Invoices", "#,##0"), new("Returns", "#,##0")], rows, ["Total", 56m, 28000m, 28, 0]);
            var model = VisualReportComposer.Compose(Meta("Brand Sales"), data, "sales-brand");

            var preview = ReportVisualPresenter.BuildFocusedPreview(model, rows);
            var tabs = Assert.IsType<TabControl>(preview);
            Assert.Equal(["Summary", "Detail rows"], tabs.Items.OfType<TabItem>().Select(tab => tab.Header?.ToString()));
            var cards = Assert.Single(Descendants(preview).OfType<UniformGrid>());
            Assert.Equal(4, cards.Children.Count);
            var texts = Descendants(preview).OfType<TextBlock>().Select(block => block.Text).ToArray();
            Assert.Contains("Sales incl. GST", texts);
            Assert.Contains(IndianNumberFormatter.Format(28000m, "currency"), texts);
            Assert.Contains(IndianNumberFormatter.Format(1000m, "currency"), texts); // average invoice
            Assert.Contains("Top 5 by sales incl. GST", texts);
            Assert.Contains("Other", texts);
            Assert.Equal(6, Descendants(preview).OfType<Grid>().Count(grid => grid.ColumnDefinitions.Count == 3)); // five bars + Other

            var summary = ReportVisualPresenter.BuildSummary(model);
            Assert.Contains("Management summary", Descendants(summary).OfType<TextBlock>().Select(block => block.Text));
        });
    }

    [Fact]
    public void Unknown_report_keeps_the_rows_card_without_visuals()
    {
        Sta(() =>
        {
            var model = VisualReportComposer.Compose(Meta("Wide report"), new([new("A"), new("B")], [["x", 1m], ["y", 2m]]));
            var preview = ReportVisualPresenter.BuildFocusedPreview(model, null);
            var cards = Assert.Single(Descendants(preview).OfType<UniformGrid>());
            Assert.Single(cards.Children);
            var texts = Descendants(preview).OfType<TextBlock>().Select(block => block.Text).ToArray();
            Assert.Contains("Rows", texts);
            Assert.Contains("2", texts);
            Assert.Empty(Descendants(preview).OfType<System.Windows.Shapes.Polyline>());
        });
    }

    [Fact]
    public void Presentation_control_renders_a_cash_book_line_without_failure()
    {
        Sta(() =>
        {
            var day = new CashBookDay(new(2026, 9, 1), "S1", 1000m, "Entered", new Dictionary<string, decimal> { ["Cash"] = 500m }, 0m, 0m, 0m, 100m, 400m, 0m, 1000m, 1000m, 0m, 0m, "Complete");
            var data = CashBookTables.Create([day, day with { Date = new(2026, 9, 2), Closing = 1250m }]);
            var session = new ReportPresentationSession();
            session.BeginReport("cash");
            var snapshot = session.SetReport(Meta("Cash Book"), data);
            Assert.Equal(ReportSummaryFamily.Cash, ProductReportVisualClassificationRegistry.FamilyFor(snapshot.ReportCode, null));
            Assert.Null(new ReportPresentationControl().Show(snapshot));
            Assert.Single(Descendants(ReportVisualPresenter.BuildFocusedPreview(snapshot.VisualReport!, null)).OfType<System.Windows.Shapes.Polyline>());
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
        if (failure is not null) throw new InvalidOperationException("Report summary presentation regression.", failure);
    }
}
