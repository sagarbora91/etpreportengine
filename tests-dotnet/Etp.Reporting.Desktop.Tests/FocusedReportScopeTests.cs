using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class FocusedReportScopeTests
{
    [Fact]
    public void Focused_detail_action_retains_the_exact_source_row()
    {
        RunSta(() =>
        {
            var row = new { Document = "SYNTHETIC-001", SourceRow = 42 };
            object? observed = null;
            var model = new VisualReportModel(new("test", "Test", new(2026,8,25), new(2026,8,25), "test", DateTimeOffset.UtcNow), [], [], new([], []), [], []);
            var tabs = Assert.IsType<TabControl>(Etp.Reporting.Desktop.Modules.Reports.ReportVisualPresenter.BuildFocusedPreview(model, new[] { row }, value => observed = value));
            var body = Assert.IsType<DockPanel>(Assert.IsType<TabItem>(tabs.Items[1]).Content);
            var grid = body.Children.OfType<DataGrid>().Single(); grid.SelectedItem = row;
            var details = body.Children.OfType<Etp.Reporting.Desktop.Modules.Reports.ReportDetailFilter>().Single().Children.OfType<Button>().Single(); Assert.True(details.IsEnabled);
            details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Same(row, observed);
            Assert.True(PageSearch.Matches(row, "SYNTHETIC-001")); Assert.False(PageSearch.Matches(row, "absent"));
        });
    }
    [Fact]
    public void Detail_filters_preserve_full_source_rows_and_distinguish_zero_from_nonzero_variance()
    {
        RunSta(() =>
        {
            var source = new[] { new { Document = "A", Variance = (decimal?)0 }, new { Document = "B", Variance = (decimal?)-2 }, new { Document = "B", Variance = (decimal?)null } };
            var grid = new DataGrid(); var filter = new Etp.Reporting.Desktop.Modules.Reports.ReportDetailFilter(grid, source);
            filter.VarianceOnly.IsChecked = true; Assert.Single(grid.Items.Cast<object>());
            Assert.Same(source[1], grid.Items[0]); filter.Search.Text = "A"; Assert.Empty(grid.Items.Cast<object>());
            filter.VarianceOnly.IsChecked = false; Assert.Same(source[0], grid.Items[0]);
            filter.Search.Clear(); Assert.Equal(3, grid.Items.Count); Assert.Equal(3, source.Length);
        });
    }
    [Fact]
    public void A_late_preview_cannot_enable_export_after_the_scope_changes()
    {
        RunSta(() =>
        {
            var view = new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("stock-closing"));
            view.SetStores(TestStoreCatalog.Create(),StoreScopeCatalog.AllStores);
            view.SelectReport("stock-closing"); view.ShowLoading("Loading original scope");
            view.DateToPicker.SelectedDate = DateTime.Today.AddDays(-1);
            view.SetPreview(new TextBlock { Text = "Late original result" }, "Done");
            Assert.False(view.HasCurrentPreview);
            view.ShowLoading("Loading new scope"); view.SetPreview(new TextBlock { Text = "Current result" }, "Done");
            Assert.True(view.HasCurrentPreview);
            view.ScopeSelector.SelectedIndex = 1;
            Assert.False(view.HasCurrentPreview);
            var exports = Children(view).OfType<Button>().Where(x => x.Content?.ToString()?.StartsWith("Export ") == true).ToArray();
            Assert.Equal(2, exports.Length); Assert.All(exports, button => Assert.False(button.IsEnabled));
        });
    }

    [Fact]
    public void Dsr_keeps_the_new_date_and_rejects_the_old_completion()
    {
        RunSta(() =>
        {
            var date = new DateTime(2026,8,25);
            var view = new DailySalesReportWorkspace(); view.BusinessDatePicker.SelectedDate = date; view.ShowLoading();
            view.BusinessDatePicker.SelectedDate = date.AddDays(1);
            view.SetReport(DailySalesReportBuilder.Build(DateOnly.FromDateTime(date), [], [], new Dictionary<string, decimal?>()));
            Assert.Equal(date.AddDays(1), view.BusinessDatePicker.SelectedDate);
            Assert.False(view.HasCurrentPreview);
            view.ShowLoading(); view.SetReport(DailySalesReportBuilder.Build(DateOnly.FromDateTime(date.AddDays(1)), [], [], new Dictionary<string, decimal?>()));
            Assert.True(view.HasCurrentPreview);
            view.BusinessDatePicker.SelectedDate = date.AddDays(2); Assert.False(view.HasCurrentPreview);
        });
    }

    [Fact]
    public void Single_store_snapshot_reports_require_explicit_scope_and_keep_one_date()
    {
        RunSta(() =>
        {
            var view = new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("stock-physical"));
            view.SetStores(TestStoreCatalog.Create(),StoreScopeCatalog.AllStores);
            view.SelectReport("stock-physical"); view.ConfigureTaskScope("Combined (Titan + Helios)");
            Assert.Equal("Select one store",view.ScopeSelector.SelectedItem); Assert.False(view.DateFromPicker.IsEnabled);
            Assert.DoesNotContain("Combined (Titan + Helios)",view.ScopeSelector.Items.Cast<string>());
            view.DateToPicker.SelectedDate=new DateTime(2026,8,25); Assert.Equal(view.DateToPicker.SelectedDate,view.DateFromPicker.SelectedDate);
            view.SetStoreScope("Helios"); Assert.Equal("Helios (HEMW)",view.ScopeSelector.SelectedItem);
            view = new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("stock-movement"));
            view.SelectReport("stock-movement"); view.ConfigureTaskScope("Combined (Titan + Helios)"); Assert.True(view.DateFromPicker.IsEnabled);
            Assert.Equal(StoreScopeCatalog.AllStores,view.ScopeSelector.SelectedItem);
        });
    }

    [Fact]
    public void Failed_generic_query_replaces_loading_with_the_actionable_message()
    {
        RunSta(() =>
        {
            var session = new Etp.Reporting.Desktop.Modules.Reports.ReportWorkspaceSession();
            var view = Assert.IsType<ReportWorkspaceControl>(session.Activate("cash",DateTime.Today,DateTime.Today,DateTime.Today,(_,_)=>{},(_,_)=>{},"Titan"));
            session.UpdatePreview(new("cash",null,null,null,null,null),null,"Synthetic SQL unavailable. Check the connection.");
            Assert.False(view.HasCurrentPreview); Assert.Empty(Children(view).OfType<LoadingState>());
            Assert.Contains(Children(view).OfType<TextBlock>(),text=>text.Text.Contains("Synthetic SQL unavailable"));
        });
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Focused report scope test failed", failure);
    }
}
