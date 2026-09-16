using System.Text.Json;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>Each report has a golden which needs SQL but never the private shop corpus.</summary>
public sealed class SanitisedEveningReportGoldenTests(SanitisedEveningFixture fixture) : IClassFixture<SanitisedEveningFixture>
{
    private static readonly DateOnly Day = new(2026, 8, 25);
    private OperationalReportRepository Reports => new(fixture.Database.ConnectionString);
    private decimal Expected(string name) => fixture.Expected.GetProperty(name).GetDecimal();

    [Fact]
    public async Task Dsr_golden_preserves_signed_sales_invoice_denominator_history_and_combined_totals()
    {
        var document = await Reports.LoadDailySalesReportDocumentAsync(Day);
        var titan = document.EveningSheets.Single(x => x.StoreCode == "WLMHW");
        var combined = document.EveningSheets.Single(x => x.StoreCode == "COMBINED");
        var value = titan.Rows.Single(x => x.Metric == "VALUE");
        Assert.Equal(Expected("titanFtdSales"), value.Ftd);
        Assert.Equal(Expected("titanMtdSales"), value.Mtd);
        Assert.Equal(Expected("titanLySales"), value.Ly);
        Assert.Equal(200m, value.Growth);
        Assert.Equal(Expected("titanFtdUnits"), titan.Rows.Single(x => x.Metric == "VOL").Ftd);
        Assert.Equal(Expected("titanFtdInvoices"), titan.Rows.Single(x => x.Metric == "INVOICE").Ftd);
        Assert.Equal(Expected("titanMtdInvoices"), titan.Rows.Single(x => x.Metric == "INVOICE").Mtd);
        Assert.Equal(1m, titan.Rows.Single(x => x.Metric == "AUPT").Ftd);
        Assert.Equal(177m, titan.Rows.Single(x => x.Metric == "AVPT").Ftd);
        Assert.Equal(3100m, titan.StoreTarget);
        Assert.Equal(100m, titan.DayTarget);
        Assert.Equal(2628m, titan.Balance);
        Assert.Equal(Expected("titanFtdSales"), titan.Rows.Single(x => x.Metric == "Synthetic brand").Ftd);
        Assert.Equal(0m, titan.Rows.Single(x => x.Metric == "Other / unmapped").Ftd);
        Assert.Equal(Expected("combinedFtdSales"), combined.Rows.Single(x => x.Metric == "VALUE").Ftd);
        Assert.Equal(Expected("combinedFtdUnits"), combined.Rows.Single(x => x.Metric == "VOL").Ftd);
        Assert.Equal(Expected("combinedFtdInvoices"), combined.Rows.Single(x => x.Metric == "INVOICE").Ftd);
    }

    [Fact]
    public async Task Closing_stock_golden_uses_source_quantities_and_all_four_count_components()
    {
        foreach (var (store, expected) in new[] { ("WLMHW", "titanStockSystem"), ("HEMW", "heliosStockSystem") })
        {
            var row = Assert.Single(await Reports.LoadBrandPhysicalStockAsync(store, Day));
            Assert.Equal("SAMPLE", row.InventoryGroupCode);
            Assert.Equal(Expected(expected), row.SystemQuantity);
            Assert.Equal(Expected(expected), row.ComponentTotal);
            Assert.Equal(0m, row.SystemVariance);
            Assert.Equal("PASS", row.Status);
        }
    }

    [Fact]
    public async Task Cash_book_golden_carries_previous_closing_and_reconciles_signed_tender_modes()
    {
        var row = Assert.Single(await Reports.LoadCashBookAsync("WLMHW", Day, Day));
        Assert.Equal(Expected("titanCashOpening"), row.Opening);
        Assert.Equal(Expected("titanCashClosing"), row.Closing);
        Assert.Contains("Carried", row.OpeningSource);
        Assert.Equal(Expected("titanFtdSales"), row.Modes["UPI"]);
        Assert.Equal(Expected("titanFtdSales"), row.RetailTotal);
        Assert.Equal(389m, row.TotalSale);
        Assert.Equal("Complete", row.Status);
        var table = CashBookTables.Create([row]);
        var total = table.Rows.Single(x => Equals(x[2], "Total"));
        Assert.Equal(494m, total[3]);
        Assert.Equal(494m, total[5]);
    }

    [Fact]
    public async Task Service_report_golden_keeps_available_mode_values_and_missing_day_counts()
    {
        var rows = await Reports.LoadServiceSalesAsync(Day, ["WLMHW"]);
        var day = rows.Single(x => x.Period == "FTD");
        var month = rows.Single(x => x.Period == "MTD");
        Assert.Equal(Expected("titanServiceFtd"), day.Total);
        Assert.Equal(20m, day.Cash);
        Assert.Equal(10m, day.Card);
        Assert.Equal(5m, day.Upi);
        Assert.Equal(0, day.MissingDays);
        Assert.Equal(Expected("titanServiceMtd"), month.Total);
        Assert.Equal(23, month.MissingDays);
        Assert.Null(month.LastYearTotal);
    }

    [Fact]
    public async Task Customer_invoice_golden_groups_product_lines_and_preserves_return_values()
    {
        var rows = await Reports.LoadInvoiceSummaryAsync(new(Day, Day, ["WLMHW"]));
        Assert.Equal((int)Expected("titanCustomerRows"), rows.Count);
        Assert.Equal(Expected("titanFtdSales"), rows.Sum(x => x.NetValue));
        Assert.Equal(Expected("titanFtdUnits"), rows.Sum(x => x.Quantity));
        var invoice = rows.Single(x => x.DocumentNumber == "TODAY1");
        Assert.Equal(3m, invoice.Quantity);
        Assert.Equal(354m, invoice.NetValue);
        Assert.Equal(2, invoice.SourceRows);
        Assert.Equal("Sample Customer", invoice.CustomerName);
        Assert.Equal(-118m, rows.Single(x => x.DocumentNumber == "RETURN").NetValue);
        Assert.Equal(-118m, rows.Single(x => x.DocumentNumber == "CANCEL").NetValue);
    }

    [Fact]
    public async Task Cro_report_golden_uses_owner_confirmed_unique_invoice_atv_and_aupt()
    {
        var report = await Reports.LoadStaffPerformanceAsync(new(Day, Day, ["WLMHW"]));
        Assert.Equal(ReconciliationStatus.Passed, report.Status);
        Assert.Equal(Expected("titanFtdSales"), report.AttributedSales);
        Assert.Equal(0m, report.Variance);
        var cro = report.Rows.Single(x => x.CroNumber == "A");
        Assert.Equal(Expected("croANetSales"), cro.NetSales);
        Assert.Equal(Expected("croANetQuantity"), cro.NetQuantity);
        Assert.Equal((int)Expected("croAUniqueInvoices"), cro.Transactions);
        Assert.Equal(Expected("croAAtv"), cro.Atv);
        Assert.Equal(Expected("croAAupt"), cro.Upt);
        Assert.Equal(1000m, cro.TargetSales);
        Assert.Equal(11.8m, cro.TargetAchievementPercent);
    }

    [Fact]
    public async Task Cro_unique_invoice_identity_includes_financial_year()
    {
        var report = await Reports.LoadStaffPerformanceAsync(new(new(2025, 3, 31), new(2025, 4, 1), ["WLMHW"]));
        var cro = Assert.Single(report.Rows);
        Assert.Equal(2, cro.Transactions);
        Assert.Equal(236m, cro.NetSales);
        Assert.Equal(2m, cro.NetQuantity);
        Assert.Equal(118m, cro.Atv);
        Assert.Equal(1m, cro.Upt);
    }
}

public sealed class SanitisedEveningFixture : IAsyncLifetime
{
    public SqlDatabaseFixture Database { get; } = new();
    public JsonElement Expected { get; private set; }

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "evening-reports", "golden.json")));
        Expected = json.RootElement.GetProperty("expected").Clone();
        var sales = json.RootElement.GetProperty("sales").EnumerateArray().Select(x => new Sale(
            x.GetProperty("store").GetString()!, DateOnly.Parse(x.GetProperty("date").GetString()!),
            x.GetProperty("invoice").GetString()!, x.GetProperty("type").GetString()!, x.GetProperty("item").GetString()!,
            x.GetProperty("quantity").GetDecimal(), x.GetProperty("gross").GetDecimal(), x.GetProperty("cro").GetString()!)).ToArray();
        foreach (var group in sales.GroupBy(x => x.Store))
        {
            await Import("R025", group.Select(x => Values(x)).ToArray());
            await Import("R013", group.Select(x => Values(x)).ToArray());
            var invoices = group.GroupBy(x => (x.Date, x.Invoice, x.Type)).Select(x => x.First() with { Quantity = x.Sum(y => y.Quantity), Gross = x.Sum(y => y.Gross) }).ToArray();
            await Import("R024", invoices.Select(x => Values(x)).ToArray());
            await Import("R020", invoices.Select(x => Values(x)).ToArray());
            await Import("R022", invoices.Select(x => Values(x)).ToArray());
            await Import("R011", [new Dictionary<string, object> { ["STORE CODE"] = group.Key, ["Date"] = new DateOnly(2026, 8, 25), ["QTY"] = group.Key == "WLMHW" ? 10m : 5m }]);
        }
        var day = new DateOnly(2026, 8, 25);
        var inputs = new DailyReportingWorkflowRepository(Database.ConnectionString);
        foreach (var store in new[] { "WLMHW", "HEMW" })
        {
            foreach (var date in new[] { day.AddDays(-1), day })
            foreach (var (field, value) in new[] { ("SERVICE_CASH", 20m), ("SERVICE_CARD", 10m), ("SERVICE_UPI", 5m), ("SERVICE_WDC", 0m), ("EXPENSES", 5m), ("CASH_DEPOSIT", 10m), ("WALK_INS", 10m) })
                await inputs.SaveManualInputAsync(store, date, field, value, null, "test", "Synthetic evening golden");
            await inputs.SaveManualInputAsync(store, day.AddDays(-1), "OPENING_CASH", 100m, null, "test", "Synthetic opening anchor");
            await new DataTruthMasterRepository(Database.ConnectionString).SaveMonthlyTargetAsync(new(store, new(2026, 8, 1), 3100m));
            await new EveningMasterRepository(Database.ConnectionString).SaveBrandAsync(new(0, store, "Synthetic brand", 999, "SAMPLE"));
            await new OperationalCompletionRepository(Database.ConnectionString).SaveManualStockCountAsync(store, day, "SAMPLE", store == "WLMHW" ? 7 : 2, 2, 0, 1, store == "WLMHW" ? 10 : 5, "Synthetic count", "test", "Synthetic evening golden");
        }
        await new OperationalCompletionRepository(Database.ConnectionString).SaveStaffTargetAsync("WLMHW", "A", day, day, 1000m, "test", "Synthetic monthly target");
    }

    public Task DisposeAsync() => Database.DisposeAsync();

    private async Task Import(string family, IReadOnlyList<Dictionary<string, object>> values)
    {
        var source = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), family + "_*.xlsx").Single();
        var sample = await new OpenXmlWorkbookReader().ReadAsync(source);
        var sheet = sample.Sheets[0];
        var rows = values.Select((value, index) => new WorkbookRow(index + 2, sheet.Rows[0].Cells.Select((cell, column) =>
            value.TryGetValue(sheet.Headers[column], out var replacement) ? new WorkbookCell(replacement) : cell).ToArray())).ToArray();
        var workbook = sample with { Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), Sheets = [sheet with { Rows = rows }] };
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
        var result = await new SqlServerImportPersistenceUseCase(Database.ConnectionString).PersistAsync(new(accepted, accepted.Scope.PeriodEnd!.Value, accepted.Scope.StoreCode!, "Synthetic evening golden"));
        Assert.Equal(values.Count, result.PersistedRows);
    }

    private static Dictionary<string, object> Values(Sale row) => new()
    {
        ["STORE CODE"] = row.Store, ["TRANS_TYPE"] = row.Type,
        ["INVNUMBER"] = row.Invoice, ["INV NUMBER"] = row.Invoice,
        ["INVDATE"] = row.Date, ["INV DATE"] = row.Date,
        ["INVOICEDATE"] = row.Date, ["INVOICEYEAR"] = row.Date.Month >= 4 ? row.Date.Year + 1 : row.Date.Year,
        ["ITEMNUMBER"] = row.Item, ["QTY"] = row.Quantity,
        ["InvoiceQuantity"] = row.Quantity, ["NetValue"] = row.Gross,
        ["NETAMOUNT"] = row.Gross, ["NETVALUE"] = row.Gross / 1.18m, ["TAX"] = row.Gross - row.Gross / 1.18m,
        ["TAX INC"] = row.Gross - row.Gross / 1.18m,
        ["CRO NUMBER"] = row.Cro, ["CRO NAME"] = "Synthetic staff " + row.Cro,
        ["INVOICE AMOUNT"] = row.Gross, ["PAYMENTTYPE25"] = row.Gross
    };

    private sealed record Sale(string Store, DateOnly Date, string Invoice, string Type, string Item, decimal Quantity, decimal Gross, string Cro);
}
