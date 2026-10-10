using System.Globalization;
using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ReportsRequestOrderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Older_report_completion_cannot_replace_new_route_preview_or_export(bool failOlderRequest)
    {
        RunSta(async () =>
        {
            var query = new DeferredTrendQuery();
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(query, previews);
            var older = view.RunReportAsync("management-trend");
            await view.RunReportAsync("sales-store");
            Assert.Single(previews);
            Assert.Equal("sales-store", previews[0].ReportCode);
            Assert.Equal(42m, previews[0].ExportData!.Rows[0][2]);
            var currentStatus = ((TextBlock)view.FindName("ReportResult")).Text;
            if (failOlderRequest) query.Completion.SetException(new InvalidOperationException("Old query failed"));
            else query.Completion.SetResult([new(new(2026, 9, 10), "WLMHW", 999m, 1m, 1, 0, 0m, 0)]);
            await older;
            Assert.Single(previews);
            Assert.Equal(currentStatus, ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.Equal("sales-store", view.CurrentReportCode);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
        });
    }

    [Fact]
    public void Management_trend_export_shows_r022_missing_with_a_blank_variance_and_a_blank_total()
    {
        // WLMHW FIX-07: a store-day without R022 exports a blank variance and a Tender Source note, and the total is blank, not 0.00.
        RunSta(async () =>
        {
            var query = new DeferredTrendQuery();
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(query, previews);
            query.Completion.SetResult([new(new(2026, 9, 9), "WLMHW", 500m, 2m, 1, 1, null, 0), new(new(2026, 9, 10), "WLMHW", 999m, 1m, 1, 0, 3m, 0)]);
            await view.RunReportAsync("management-trend");

            var data = Assert.Single(previews).ExportData!;
            // Owner decision 13 Q6 (L9): INV-only Invoices plus a separate Returns count replace L5's interim "Documents (incl. returns)".
            Assert.Equal(["Date", "Store", "Sales incl. GST", "Units", "Invoices", "Returns", "Tender Variance", "Tender Source", "Unmatched Staff Rows"], data.Columns.Select(x => x.Header));
            Assert.Equal(1, data.Rows[0][5]);
            Assert.Null(data.Rows[0][6]);
            Assert.Equal(ManagementTrendTenderSource.Missing, data.Rows[0][7]);
            Assert.Equal(3m, data.Rows[1][6]);
            Assert.Equal(ManagementTrendTenderSource.Imported, data.Rows[1][7]);
            Assert.Equal(2, data.Totals![4]);
            Assert.Equal(1, data.Totals[5]);
            Assert.Null(data.Totals[6]);
            Assert.Equal("1 missing", data.Totals[7]);
            Assert.Equal(1499m, data.Totals[2]);
            Assert.Contains("1 store-day(s) show R022 missing / not imported; their tender variance is blank, not zero.", ((TextBlock)view.FindName("ReportResult")).Text);
        });
    }

    [Fact]
    public void Management_trend_export_totals_the_variance_when_every_day_has_r022()
    {
        RunSta(async () =>
        {
            var query = new DeferredTrendQuery();
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(query, previews);
            query.Completion.SetResult([new(new(2026, 9, 9), "WLMHW", 500m, 2m, 1, 0, 2m, 0), new(new(2026, 9, 10), "WLMHW", 999m, 1m, 1, 0, 3m, 0)]);
            await view.RunReportAsync("management-trend");

            var data = Assert.Single(previews).ExportData!;
            Assert.Equal(5m, data.Totals![6]);
            Assert.Equal("", data.Totals[7]);
            Assert.DoesNotContain("R022 missing", ((TextBlock)view.FindName("ReportResult")).Text);
        });
    }

    [Fact]
    public void Date_change_invalidates_export_and_pending_results_until_report_is_rerun()
    {
        RunSta(async () =>
        {
            var query = new DeferredTrendQuery();
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(query, previews);
            await view.RunReportAsync("sales-store");
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            var older = view.RunReportAsync("management-trend");
            view.SetBusinessDate(new(2026, 9, 9));
            query.Completion.SetResult([new(new(2026, 9, 10), "WLMHW", 999m, 1m, 1, 0, 0m, 0)]);
            await older;
            Assert.Single(previews);
            Assert.False(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            Assert.Contains("Filters changed", ((TextBlock)view.FindName("ReportResult")).Text);
            await view.RunReportAsync("sales-store");
            Assert.Equal(new DateOnly(2026, 9, 9), previews[^1].ExportMetadata!.DateTo);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
        });
    }

    [Fact]
    public void Snapshot_refresh_does_not_discard_the_retained_range_start()
    {
        RunSta(async () =>
        {
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(new DeferredTrendQuery(), previews);
            view.ApplyTaskScope("stock-physical", new(2026, 9, 10), new(2026, 9, 10), "WLMHW");
            await view.RunReportAsync("sales-store");
            Assert.Equal(new DateOnly(2026, 9, 1), previews[^1].ExportMetadata!.DateFrom);
            Assert.Equal(new DateOnly(2026, 9, 10), previews[^1].ExportMetadata!.DateTo);
            view.ApplyTaskScope("stock-variance", new(2026, 9, 5), new(2026, 9, 10), "WLMHW");
            await view.RunReportAsync("sales-store");
            Assert.Equal(new DateOnly(2026, 9, 5), previews[^1].ExportMetadata!.DateFrom);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Export_completion_cannot_overwrite_a_new_report_and_reentry_is_ignored(bool pdf, bool fail)
    {
        RunSta(async () =>
        {
            var exporter = new DeferredExport();
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(new DeferredTrendQuery(), previews, exporter);
            await view.RunReportAsync("sales-store");
            var saving = view.ExportReportToPathAsync(Path.Combine(Path.GetTempPath(), "etp-test-" + Guid.NewGuid().ToString("N") + ".xlsx"), pdf);
            Assert.True(view.IsExportInProgress);
            await view.ExportReportToPathAsync(Path.Combine(Path.GetTempPath(), "etp-test-" + Guid.NewGuid().ToString("N") + ".xlsx"), pdf);
            Assert.Equal(1, exporter.Calls);
            await view.RunReportAsync("sales-combined");
            var status = ((TextBlock)view.FindName("ReportResult")).Text;
            if (fail) exporter.Completion.SetException(new IOException("Synthetic disk failure"));
            else exporter.Completion.SetResult();
            await saving;
            Assert.False(view.IsExportInProgress);
            Assert.Equal(status, ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.Equal("sales-combined", view.CurrentReportCode);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Successful_export_records_only_privacy_safe_detail(bool pdf)
    {
        RunSta(async () =>
        {
            var exporter = new DeferredExport();
            var view = CreateView(new DeferredTrendQuery(), [], exporter);
            var recorded = new List<(string Kind, string Outcome, string Detail)>();
            view.AttachHost(_ => true, (kind, outcome, detail) =>
            {
                if (detail.Length > 200 || detail.IndexOfAny([':', '/', '\\']) >= 0 || detail.Any(char.IsDigit))
                    throw new ArgumentException("Audit details must not contain paths or identifiers.", nameof(detail));
                recorded.Add((kind, outcome, detail));
                return Task.CompletedTask;
            }, (_, _, _) => { }, _ => { }, _ => { });
            var path = Path.Combine(Path.GetTempPath(), "etp-test-" + Guid.NewGuid().ToString("N") + (pdf ? ".pdf" : ".xlsx"));
            try
            {
                await view.RunReportAsync("sales-store");
                exporter.Completion.SetResult();
                await view.ExportReportToPathAsync(path, pdf);

                Assert.True(File.Exists(path));
                Assert.Equal((pdf ? "ExportPdf" : "ExportExcel", "Succeeded", "Report exported"),
                    Assert.Single(recorded, entry => entry.Kind.StartsWith("Export", StringComparison.Ordinal)));
                var status = ((TextBlock)view.FindName("ReportResult")).Text;
                Assert.Contains("report saved to", status);
                Assert.DoesNotContain("Activity history could not be updated", status);
                Assert.DoesNotContain("export failed", status);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
    }

    [Fact]
    public void Saved_file_is_not_reported_as_failed_when_activity_history_fails()
    {
        RunSta(async () =>
        {
            var exporter = new DeferredExport();
            var view = CreateView(new DeferredTrendQuery(), [], exporter);
            view.AttachHost(_ => true, (kind, _, _) => kind.StartsWith("Export") ? Task.FromException(new IOException("Audit unavailable")) : Task.CompletedTask,
                (_, _, _) => { }, _ => { }, _ => { });
            await view.RunReportAsync("sales-store");
            var saving = view.ExportReportToPathAsync(Path.Combine(Path.GetTempPath(), "etp-test-" + Guid.NewGuid().ToString("N") + ".xlsx"), false);
            exporter.Completion.SetResult();
            await saving;
            var status = ((TextBlock)view.FindName("ReportResult")).Text;
            Assert.Contains("report saved to", status);
            Assert.Contains("Activity history could not be updated", status);
            Assert.DoesNotContain("export failed", status);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_or_cancelled_export_keeps_current_result_and_allows_retry(bool cancelled)
    {
        RunSta(async () =>
        {
            var exporter = new DeferredExport();
            var view = CreateView(new DeferredTrendQuery(), [], exporter);
            await view.RunReportAsync("sales-store");
            var saving = view.ExportReportToPathAsync(Path.Combine(Path.GetTempPath(), "etp-test-" + Guid.NewGuid().ToString("N") + ".xlsx"), false);
            if (cancelled) exporter.Completion.SetCanceled();
            else exporter.Completion.SetException(new IOException("Synthetic disk failure"));
            await saving;
            Assert.Contains(cancelled ? "Export cancelled" : "Excel export failed", ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.Equal("sales-store", view.CurrentReportCode);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            await view.ExportReportToPathAsync(Path.Combine(Path.GetTempPath(), "etp-test-" + Guid.NewGuid().ToString("N") + ".xlsx"), false);
            Assert.Equal(2, exporter.Calls);
        });
    }

    private sealed class DeferredExport : IReportExportCoordinator
    {
        public TaskCompletionSource Completion { get; } = new();
        public int Calls { get; private set; }
        private async Task WriteAsync(string path) { await Completion.Task; File.WriteAllText(path,"Synthetic export"); }
        public Task ExportReportExcelAsync(string path, Etp.Reporting.Reporting.ExcelReportMetadata metadata, Etp.Reporting.Reporting.ExcelReportData data, Etp.Reporting.Reporting.VisualReportModel? visual, CancellationToken token = default) { Calls++; return WriteAsync(path); }
        public Task ExportReportPdfAsync(string path, Etp.Reporting.Reporting.ExcelReportMetadata metadata, Etp.Reporting.Reporting.ExcelReportData data, Etp.Reporting.Reporting.VisualReportModel? visual, Etp.Reporting.Reporting.DailySalesReportDocument? dsr, CancellationToken token = default) { Calls++; return WriteAsync(path); }
        public Task ExportPackExcelAsync(string path, Etp.Reporting.Reporting.ReportPackDocument document, CancellationToken token = default) => throw new NotSupportedException();
        public Task ExportPackPdfAsync(string path, Etp.Reporting.Reporting.ReportPackDocument document, CancellationToken token = default) => throw new NotSupportedException();
        public Task ExportManagementSummaryPdfAsync(string path, Etp.Reporting.Reporting.ExcelReportMetadata metadata, Etp.Reporting.Reporting.ExcelReportData data, CancellationToken token = default) => throw new NotSupportedException();
    }

    [Fact]
    public void Sales_summary_status_line_uses_indian_digit_grouping_whatever_the_thread_culture()
    {
        RunSta(async () =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(new DeferredTrendQuery(), previews, salesAmount: 1015259.10m);
            await view.RunReportAsync("sales-store");
            var status = ((TextBlock)view.FindName("ReportResult")).Text;
            Assert.Contains("Sales incl. GST 10,15,259.10", status);
            Assert.DoesNotContain("1,015,259.10", status);
        });
    }

    private static ReportsWorkspaceView CreateView(DeferredTrendQuery query, List<ReportPresentationSnapshot> previews, IReportExportCoordinator? exporter = null, decimal salesAmount = 42m)
    {
        var view = new ReportsWorkspaceView(() => "synthetic", _ => new SalesQuery(salesAmount),
            _ => throw new InvalidOperationException("Unexpected operational query"), _ => query,
            exporter ?? new ReportExportCoordinator(), new UnusedDiagnostic());
        view.SetStores(TestStoreCatalog.Create());
        view.ApplyScope(new(2026, 9, 1), new(2026, 9, 10), "WLMHW");
        view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask,
            (snapshot, _, _) => previews.Add(snapshot), _ => { }, _ => { });
        return view;
    }

    private sealed class DeferredTrendQuery : IManagementTrendQuery
    {
        public TaskCompletionSource<IReadOnlyList<ManagementTrendRecord>> Completion { get; } = new();
        public Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default) => Completion.Task;
    }

    private sealed class SalesQuery(decimal salesAmount = 42m) : IControlledReportQuery
    {
        public Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesSummaryReport(dimension, ReportStatus.Passed, [new("Synthetic", 1m, salesAmount, 1, 0)], "test", "Synthetic sales"));
        public Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedDiagnostic : ITenderVarianceDiagnostic
    {
        public TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance) => throw new NotSupportedException();
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA test failed.", failure);
    }
}
