using System.Windows.Controls;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.9 polish, lane ui. RA-EXPORT-04 / RA-UI-20: export titles and file names are the catalogue names. RA-SALES-07:
/// the sales column is "Sales incl. GST". RA-EXPORT-10, RA-SALES-11, RA-TENDER-06, RA-OPS-11, RA-UI-11: every status
/// line starts with its status word and says what is missing. RA-STOCK-11: Stock Movement is not Blocked only for a
/// missing snapshot. RA-OPS-10: the "Unmapped" chip matches unmapped codes only. RA-SALES-09: Store Sales Summary on
/// All stores runs for the first active store and says so.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class PolishUiStatusTests
{
    [Theory]
    [InlineData("sales-store", "Store Sales Summary")]
    [InlineData("sales-combined", "Combined Sales Summary")]
    [InlineData("sales-returns", "Returns")]
    [InlineData("sales-segment", "Brand-Segment Sales")]
    [InlineData("staff", "Staff/CRO Performance")]
    [InlineData("tender", "Tender Reconciliation")]
    [InlineData("stock-variance", "Stock Variance")]
    [InlineData("stock-physical", "Physical Stock")]
    [InlineData("invoice-lineage", "Invoice Source Drill-down")]
    [InlineData("exceptions", "Daily Exception Report")]
    [InlineData("tender-diagnostic", "Tender Diagnostics")]
    public void Export_title_is_the_catalogue_name(string code, string expected)
    {
        Assert.Equal(expected, ReportsWorkspaceView.ExportTitle(code, "internal name"));
        Assert.Equal(ProductReportVisualClassificationRegistry.ForReport(code).Family, ProductReportVisualClassificationRegistry.FamilyFor(null, expected));
    }

    [Fact]
    public void Dsr_and_unknown_codes_keep_the_given_title()
    {
        Assert.Equal("Daily Sales Report", ReportsWorkspaceView.ExportTitle("dsr", "Daily Sales Report"));
        Assert.Equal("Something", ReportsWorkspaceView.ExportTitle("no-such-report", "Something"));
        Assert.Equal("Something", ReportsWorkspaceView.ExportTitle(null, "Something"));
    }

    [Fact]
    public void Running_a_report_titles_the_export_and_the_file_with_the_catalogue_name()
    {
        RunSta(async () =>
        {
            var view = ReportGridPresentationTests.CreateView(out var latest);
            await view.RunReportAsync("sales-combined");
            var metadata = latest().ExportMetadata!;
            Assert.Equal("Combined Sales Summary", metadata.ReportName);
            Assert.Equal("Combined_Sales_Summary_20260825_20260825.xlsx", ReportsWorkspaceView.ProposedFileName(metadata, pdf: false));
            Assert.Contains(ReportSummaryBuilder.SalesHeader, latest().ExportData!.Columns.Select(x => x.Header));
            Assert.DoesNotContain("Net Sales", latest().ExportData!.Columns.Select(x => x.Header));
        });
    }

    [Fact]
    public void Store_sales_on_all_stores_runs_for_the_first_active_store_and_says_so()
    {
        RunSta(async () =>
        {
            var view = ReportGridPresentationTests.CreateView(out var latest);
            view.ApplyScope(new DateTime(2026, 8, 25), new DateTime(2026, 8, 25), StoreScopeCatalog.AllStores);
            await view.RunReportAsync("sales-store");
            var first = TestStoreCatalog.Create().Stores[0];
            var text = ((TextBlock)view.FindName("ReportResult")).Text;
            Assert.StartsWith("Passed", text, StringComparison.Ordinal);
            Assert.EndsWith(ReportsWorkspaceView.AutoStoreNote(StoreScopeCatalog.Label(first)), text, StringComparison.Ordinal);
            Assert.Equal(first.Code, ((TextBox)view.FindName("StoreFilterInput")).Text);
            Assert.Equal("Store Sales Summary", latest().ExportMetadata!.ReportName);
            Assert.True(latest().CanExportReport);
        });
    }

    [Fact]
    public void Customer_wise_status_has_a_status_word_and_the_dates()
    {
        var rows = new Etp.Reporting.Application.Reports.InvoiceSummaryRecord[]
        {
            new(new(2026, 9, 1), "WLMHW", "A", "INV", 1m, 118m, 1, "Name"),
            new(new(2026, 9, 2), "WLMHW", "B", "SR", -1m, -118m, 1, null)
        };
        Assert.Equal("Passed: 01 Sep 2026 – 28 Sep 2026, 1 invoices, 1 returns; 1 missing customer names.",
            ReportsWorkspaceView.CustomerWiseStatus(ReconciliationStatus.Passed, rows, new(2026, 9, 1), new(2026, 9, 28)));
        Assert.Equal("Blocked: 0 invoices.", ReportsWorkspaceView.CustomerWiseStatus(ReconciliationStatus.Blocked, [], new(2026, 9, 1), new(2026, 9, 28)));
        // An empty window still reads as the shared "No data for ..." line.
        Assert.Equal("No data for 01 Sep 2026 – 28 Sep 2026, All stores.",
            ReportsWorkspaceView.NoDataStatus("invoice", 0, new(2026, 9, 1), new(2026, 9, 28), "All stores", "Blocked: 0 invoices."));
    }

    [Fact]
    public void Cash_book_status_says_blocked_and_names_the_missing_inputs()
    {
        static CashBookDay Day(int day, decimal? opening, decimal? serviceCash, decimal? expenses, decimal? deposit, string status) =>
            new(new(2026, 9, day), "WLMHW", opening, "", new Dictionary<string, decimal> { ["Cash"] = 100m }, serviceCash, 1m, 1m,
                expenses, deposit, 0m, null, null, 0m, 0m, status);
        var days = new[]
        {
            Day(1, null, null, null, null, "Enter opening, service cash, expenses and deposit"),
            Day(2, 100m, 5m, null, 10m, "Enter opening, service cash, expenses and deposit"),
            Day(3, 100m, 5m, 5m, 10m, "Complete")
        };
        var text = ReportsWorkspaceView.CashBookStatus(ReconciliationStatus.Blocked, days);
        Assert.StartsWith("Blocked: 2 of 3 day(s) incomplete - missing opening cash, service cash, expenses, cash deposit.", text, StringComparison.Ordinal);
        Assert.EndsWith(ReportsWorkspaceView.CashBookEntryHint, text, StringComparison.Ordinal);

        var noTender = new CashBookDay(new(2026, 9, 4), "WLMHW", 100m, "", new Dictionary<string, decimal>(), 5m, 1m, 1m, 5m, 10m, 0m, null, null, 0m, 0m,
            CashBookTables.TenderSourceMissing, TenderSourceImported: false);
        Assert.StartsWith("Blocked: 1 of 1 day(s) incomplete - missing R022 tender import.",
            ReportsWorkspaceView.CashBookStatus(ReconciliationStatus.Blocked, [noTender]), StringComparison.Ordinal);

        Assert.StartsWith("Passed: 1 of 1 day(s) complete.", ReportsWorkspaceView.CashBookStatus(ReconciliationStatus.Passed, [days[2]]), StringComparison.Ordinal);
    }

    [Fact]
    public void Stock_movement_is_blocked_only_when_no_movement_is_listed()
    {
        Assert.Equal(ReconciliationStatus.Blocked, ReportsWorkspaceView.StockMovementStatus(0));
        Assert.Equal(ReconciliationStatus.Passed, ReportsWorkspaceView.StockMovementStatus(3));
    }

    [Fact]
    public void Physical_stock_status_says_no_count_was_entered_and_where_to_enter_it()
    {
        var day = new DateOnly(2026, 9, 29);
        Assert.Equal("Blocked: No physical count entered for WLMHW on 29 Sep 2026; enter it at Stock > Physical count.",
            ReportsWorkspaceView.PhysicalStockStatus(ReconciliationStatus.Blocked, 0, 0, "WLMHW", day));
        Assert.Equal("Blocked: 16 brand(s). No physical count entered for WLMHW on 29 Sep 2026; enter it at Stock > Physical count.",
            ReportsWorkspaceView.PhysicalStockStatus(ReconciliationStatus.Blocked, 16, 16, "WLMHW", day));
        Assert.Equal("Blocked: 16 brand(s); 4 not counted on 29 Sep 2026. Enter them at Stock > Physical count.",
            ReportsWorkspaceView.PhysicalStockStatus(ReconciliationStatus.Blocked, 16, 4, "WLMHW", day));
        Assert.Equal("Passed: 16 brand(s).", ReportsWorkspaceView.PhysicalStockStatus(ReconciliationStatus.Passed, 16, 0, "WLMHW", day));
    }

    [Fact]
    public void Exceptions_status_tells_inputs_not_entered_from_real_failures()
    {
        (string, string)[] notEntered =
        [
            ("BLOCKER", "SOURCE_MISSING"), ("BLOCKER", "SOURCE_MISSING"),
            ("BLOCKER", "MANUAL_INPUT_MISSING"), ("BLOCKER", "MANUAL_INPUT_MISSING"), ("BLOCKER", "MANUAL_INPUT_MISSING"),
            ("BLOCKER", "CASH_RECONCILIATION"), ("WARNING", "PHYSICAL_COUNT_MISSING")
        ];
        var (status, text) = ReportsWorkspaceView.DailyExceptionStatus(notEntered);
        Assert.Equal(ReconciliationStatus.Blocked, status);
        Assert.Equal("Blocked: 7 exception(s) - 2 source report(s) not imported, 5 input(s) not entered, 0 variance(s) or failure(s).", text);

        var (failed, failedText) = ReportsWorkspaceView.DailyExceptionStatus([("BLOCKER", "MANUAL_INPUT_MISSING"), ("FAIL", "TENDER_VARIANCE"), ("WARNING", "R013_UNMATCHED")]);
        Assert.Equal(ReconciliationStatus.Failed, failed);
        Assert.Equal("Failed: 3 exception(s) - 1 input(s) not entered, 1 variance(s) or failure(s), 1 warning(s).", failedText);

        Assert.Equal((ReconciliationStatus.Passed, "Passed: 0 exception(s)."), ReportsWorkspaceView.DailyExceptionStatus([]));
        Assert.Equal(ReconciliationStatus.Passed, ReportsWorkspaceView.DailyExceptionStatus([("WARNING", "R013_UNMATCHED")]).Status);
    }

    [Theory]
    [InlineData("R013_UNMATCHED", true)]
    [InlineData("R013_AMBIGUOUS", true)]
    [InlineData("BRAND_UNMAPPED", true)]
    [InlineData("SOURCE_MISSING", false)]
    [InlineData("MANUAL_INPUT_MISSING", false)]
    [InlineData("PHYSICAL_COUNT_MISSING", false)]
    [InlineData("TENDER_VARIANCE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Unmapped_chip_matches_only_unmapped_codes(string? code, bool expected) =>
        Assert.Equal(expected, ReportDetailFilter.IsUnmappedCode(code));

    [Fact]
    public void Dsr_and_service_status_lines_start_with_the_status_word()
    {
        var day = new DateOnly(2026, 9, 29);
        Assert.Equal($"Passed: {ReportsWorkspaceView.DsrNote}", ReportsWorkspaceView.DsrStatus(ReconciliationStatus.Passed, null, day));
        Assert.StartsWith("Blocked: R025 not imported", ReportsWorkspaceView.DsrStatus(ReconciliationStatus.Blocked, "R025 not imported for 29 Sep 2026.", day), StringComparison.Ordinal);
        Assert.StartsWith("Blocked: No sales found for 29 Sep 2026;", ReportsWorkspaceView.DsrStatus(ReconciliationStatus.Blocked, null, day), StringComparison.Ordinal);
        Assert.Equal("Passed: m", ReportsWorkspaceView.ServiceStatus(ReconciliationStatus.Passed, "m", "All stores", day));
        Assert.StartsWith("Blocked: Service entries missing for All stores up to 29 Sep 2026;", ReportsWorkspaceView.ServiceStatus(ReconciliationStatus.Blocked, "m", "All stores", day), StringComparison.Ordinal);
        Assert.Contains("Today > Cash > Cash and service entries", ReportsWorkspaceView.ServiceStatus(ReconciliationStatus.Blocked, "m", "All stores", day), StringComparison.Ordinal);
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Polish UI status test failed", failure);
    }
}
