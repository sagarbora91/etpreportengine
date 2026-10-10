using Etp.Reporting.Reporting;
using PdfSharp.Pdf.IO;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>RA-EXPORT-05 (1.9.8): the Summary tab and PDF summary page per report family, from the rows already loaded.</summary>
public sealed class ReportSummaryTests
{
    private static ExcelReportMetadata Meta(string name) => new(name, new(2026, 9, 1), new(2026, 9, 30), "Passed", "test", "Synthetic only.", DateTimeOffset.UtcNow);

    private static decimal? Kpi(VisualReportModel model, string label) => Assert.Single(model.Kpis, k => k.Label == label).Value;

    [Fact]
    public void Sales_summary_reads_totals_units_invoices_average_and_top_five_groups()
    {
        var rows = Enumerable.Range(1, 7).Select(i => (IReadOnlyList<object?>)[$"Group {i}", i * 2m, i * 1000m, i, 0]).ToArray();
        var data = new ExcelReportData([new("Group"), new("Units", "#,##0.00"), new("Net Sales", "#,##0.00"), new("Invoices", "#,##0"), new("Returns", "#,##0")], rows,
            ["Total", 56m, 28000m, 28, 0]);
        var model = VisualReportComposer.Compose(Meta("Brand Sales"), data, "sales-brand");

        Assert.Equal(28000m, Kpi(model, "Sales incl. GST"));
        Assert.Equal(56m, Kpi(model, "Units"));
        Assert.Equal(28m, Kpi(model, "Invoices"));
        Assert.Equal(1000m, Kpi(model, "Average invoice"));
        var visual = Assert.Single(model.Visuals);
        Assert.Equal(ReportVisualType.Bar, visual.Type);
        var points = visual.Series[0].Points;
        Assert.Equal(6, points.Count);
        Assert.Equal("Group 7", points[0].Category);
        Assert.Equal("Other", points[^1].Category);
        Assert.Equal(3000m, points[^1].Value); // groups 1 and 2
        Assert.Equal(28000m, points.Sum(p => p.Value));
        Assert.True(model.HasSummary);
        Assert.Same(data, model.Detail);
    }

    [Fact]
    public void Sales_family_resolves_by_export_name_when_no_code_is_given()
    {
        var data = new ExcelReportData([new("Group"), new("Units", "#,##0.00"), new("Net Sales", "#,##0.00")], [["WLMHW", 3m, 450m]]);
        var model = VisualReportComposer.Compose(Meta("Daily Sales"), data);
        Assert.Equal(450m, Kpi(model, "Sales incl. GST"));
        Assert.Equal(VisualValueState.Available, Assert.Single(model.Kpis, k => k.Label == "Invoices").State);
        Assert.Null(Kpi(model, "Invoices"));
        Assert.Equal(VisualValueState.NotApplicable, Assert.Single(model.Kpis, k => k.Label == "Average invoice").State);
    }

    [Fact]
    public void Unknown_reports_and_tables_without_the_family_columns_keep_the_rows_card()
    {
        var data = new ExcelReportData([new("Measure"), new("Quantity", "#,##0")], [["A", 1m], ["B", 2m]]);
        var unknown = VisualReportComposer.Compose(Meta("Wide report"), data);
        Assert.Equal("Rows", Assert.Single(unknown.Kpis).Label);
        Assert.Equal(2m, unknown.Kpis[0].Value);
        Assert.Empty(unknown.Visuals);
        Assert.False(unknown.HasSummary);

        var mismatched = VisualReportComposer.Compose(Meta("Staff CRO Performance"), data, "staff");
        Assert.Equal("Rows", Assert.Single(mismatched.Kpis).Label);

        var lineage = VisualReportComposer.Compose(Meta("Invoice Sales source history"), data, "invoice-lineage");
        Assert.Equal("Rows", Assert.Single(lineage.Kpis).Label);
    }

    [Fact]
    public void Every_catalogue_report_has_a_registry_entry_with_an_export_name()
    {
        foreach (var entry in ProductReportVisualClassificationRegistry.All)
        {
            Assert.NotEmpty(entry.ReportNames);
            foreach (var name in entry.ReportNames) Assert.Same(entry, ProductReportVisualClassificationRegistry.FindByReportName(name));
        }
        Assert.Equal(ReportSummaryFamily.Sales, ProductReportVisualClassificationRegistry.FamilyFor("sales-item", null));
        Assert.Equal(ReportSummaryFamily.Cash, ProductReportVisualClassificationRegistry.FamilyFor(null, "Cash Book"));
        Assert.Equal(ReportSummaryFamily.None, ProductReportVisualClassificationRegistry.FamilyFor(null, "Not a report"));
        Assert.Equal(ReportSummaryFamily.None, ProductReportVisualClassificationRegistry.FamilyFor("dsr", "Daily Sales Report"));
    }

    [Fact]
    public void Closing_stock_summary_counts_items_units_value_and_top_brands()
    {
        var data = new ExcelReportData(
            [new("Date"), new("Store"), new("Item"), new("Brand"), new("Inventory Group"), new("Quantity", "#,##0.00"), new("Unit MRP", "#,##0.00"), new("MRP value (GST incl.)", "#,##0.00"), new("Movement Status")],
            [[new DateOnly(2026, 9, 30), "S1", "I1", "Titan", "Watch", 2m, 100m, 200m, StockAgeing.Active],
             [new DateOnly(2026, 9, 30), "S1", "I2", "Titan", "Watch", 1m, 300m, 300m, StockAgeing.Slow],
             [new DateOnly(2026, 9, 30), "S1", "I3", "Fastrack", "Watch", 4m, 50m, 200m, StockAgeing.Watch]],
            ["Total", "", "", "", "", 7m, "", 700m, ""]);
        var model = VisualReportComposer.Compose(Meta("Closing Stock"), data, "stock-closing");
        Assert.Equal(3m, Kpi(model, "Items"));
        Assert.Equal(7m, Kpi(model, "Units"));
        Assert.Equal(700m, Kpi(model, "MRP value (GST incl.)"));
        Assert.Equal(1m, Kpi(model, "Active items"));
        var points = Assert.Single(model.Visuals).Series[0].Points;
        Assert.Equal(["Titan", "Fastrack"], points.Select(p => p.Category));
        Assert.Equal(500m, points[0].Value);

        var slow = VisualReportComposer.Compose(Meta("Slow / Exception Stock"), data, "stock-slow");
        Assert.Equal(1m, Kpi(slow, "Exception items (90+ days)"));
        Assert.Equal("Units by ageing band", Assert.Single(slow.Visuals).Title);
    }

    [Fact]
    public void Brand_stock_movement_variance_and_physical_summaries_read_their_own_columns()
    {
        var brand = VisualReportComposer.Compose(Meta("Brand Stock"), new(
            [new("Store"), new("Brand row"), new("Brand"), new("Inventory Group"), new("Quantity", "#,##0.00"), new("MRP value (GST incl.)", "#,##0.00"), new("Items", "#,##0"), new("Slow Items", "#,##0")],
            [["S1", "TITAN", "Titan", "Watch", 10m, 5000m, 4, 1], ["S1", "SONATA", "Sonata", "Watch", 5m, 1000m, 2, 0]], ["Total", "", "", "", 15m, 6000m, 6, 1]), "stock-brand");
        Assert.Equal(6m, Kpi(brand, "Items"));
        Assert.Equal(1m, Kpi(brand, "Slow items"));
        Assert.Equal("TITAN", Assert.Single(brand.Visuals).Series[0].Points[0].Category);

        var movement = VisualReportComposer.Compose(Meta("Stock Movement"), new(
            [new("Store"), new("Item"), new("Location"), new("Movement Type"), new("Signed Quantity", "#,##0.00"), new("Snapshot")],
            [["S1", "I1", "L", "Purchase Receipt", 10m, ""], ["S1", "I2", "L", "Sale", -4m, ""], ["S1", "I3", "L", "Sale", -1m, ""]], ["Total", "", "", "", 5m, ""]), "stock-movement");
        Assert.Equal(5m, Kpi(movement, "Net movement"));
        Assert.Equal(10m, Kpi(movement, "Inbound units"));
        Assert.Equal(5m, Kpi(movement, "Outbound units"));
        Assert.Equal(2, Assert.Single(movement.Visuals).Series[0].Points.Count);

        var variance = VisualReportComposer.Compose(Meta("Stock Reconciliation"), new(
            [new("Store"), new("Item"), new("Opening", "#,##0.00"), new("Movements", "#,##0.00"), new("Expected Closing", "#,##0.00"), new("Reported Closing", "#,##0.00"), new("Variance", "#,##0.00"), new("Status"), new("Snapshot")],
            [["S1", "I1", 5m, 1m, 6m, 6m, 0m, "Passed", ""], ["S1", "I2", 5m, 0m, 5m, 3m, -2m, "Failed", ""]], ["Total", "", 10m, 1m, 11m, 9m, -2m, "Failed", ""]), "stock-variance");
        Assert.Equal(11m, Kpi(variance, "Expected closing"));
        Assert.Equal(9m, Kpi(variance, "Reported closing"));
        Assert.Equal(-2m, Kpi(variance, "Variance"));
        Assert.Equal(1m, Kpi(variance, "Items with variance"));
        Assert.Equal("I2", Assert.Single(variance.Visuals).Series[0].Points[0].Category);

        var physical = VisualReportComposer.Compose(Meta("Physical Closing Stock"), new(
            [new("Store"), new("Date"), new("Brand"), new("Display", "#,##0.00"), new("Backstock", "#,##0.00"), new("Defective", "#,##0.00"), new("Y Location", "#,##0.00"), new("Physical", "#,##0.00"), new("System", "#,##0.00"), new("System Variance", "#,##0.00"), new("Remarks"), new("Status")],
            [["S1", new DateOnly(2026, 9, 30), "Titan", 3m, 1m, 0m, 0m, 4m, 5m, -1m, "", "FAIL"]]), "stock-physical");
        Assert.Equal(1m, Kpi(physical, "Brands"));
        Assert.Equal(4m, Kpi(physical, "Physical units"));
        Assert.Equal(-1m, Kpi(physical, "Variance"));
    }

    [Fact]
    public void Staff_summary_reads_the_control_row_variance_and_ranks_cros()
    {
        var data = new ExcelReportData(
            [new("Store"), new("CRO"), new("CRO name"), new("Value incl. GST", "#,##0.00"), new("LY Sales", "#,##0.00"), new("Growth %", "0.00%"), new("Growth Status"), new("Net Quantity", "#,##0.00"), new("Discount", "#,##0.00"), new("Unique invoices", "#,##0"), new("AUPT", "#,##0.00"), new("ATV", "#,##0.00"), new("Contribution %", "0.00%"), new("Target", "#,##0.00"), new("Achievement %", "0.00%"), new("Rank", "#,##0")],
            [["S1", "C1", "CRO One", 6000m, 5000m, 20m, "Up", 6m, 0m, 3, 2m, 2000m, 60m, 5000m, 120m, 1],
             ["S1", "C2", "CRO Two", 4000m, 5000m, -20m, "Down", 4m, 0m, 2, 2m, 2000m, 40m, 5000m, 80m, 2]],
            ["Control", "", "", 10000m, "", "", "", "", "", 5, "", "", 250m, "", "", ""]);
        var model = VisualReportComposer.Compose(Meta("Staff CRO Performance"), data, "staff");
        Assert.Equal(10000m, Kpi(model, "Attributed sales"));
        Assert.Equal(250m, Kpi(model, "Variance vs recorded"));
        Assert.Equal(5m, Kpi(model, "Unique invoices"));
        Assert.Equal(100m, Kpi(model, "Target achievement"));
        Assert.Equal("CRO One", Assert.Single(model.Visuals).Series[0].Points[0].Category);
    }

    [Fact]
    public void Tender_summaries_read_invoice_tender_variance_and_group_by_status_or_cause()
    {
        ExcelReportColumn[] columns = [new("Store"), new("Document"), new("Invoice", "#,##0.00"), new("Tender", "#,##0.00"), new("Variance", "#,##0.00"), new("Status")];
        var data = new ExcelReportData(columns, [["S1", "D1", 100m, 100m, 0m, "Passed"], ["S1", "D2", 200m, 150m, 50m, "Failed"], ["S1", "D3", 300m, null, null, "Failed"]], ["Total", "", 600m, 250m, 350m, "Failed"]);
        var tender = VisualReportComposer.Compose(Meta("Invoice Tender Reconciliation"), data, "tender");
        Assert.Equal(600m, Kpi(tender, "Invoice total"));
        Assert.Equal(250m, Kpi(tender, "Tender total"));
        Assert.Equal(350m, Kpi(tender, "Variance"));
        Assert.Equal(2m, Kpi(tender, "Documents with variance"));
        var byStatus = Assert.Single(tender.Visuals).Series[0].Points;
        Assert.Equal(2m, Assert.Single(byStatus, p => p.Category == "Failed").Value);

        var diagnostic = VisualReportComposer.Compose(Meta("Tender Variance Diagnostics"), new(
            [new("Store"), new("Document"), new("Invoice", "#,##0.00"), new("Tender", "#,##0.00"), new("Variance", "#,##0.00"), new("Likely Cause"), new("Recommended Check")],
            [["S1", "D2", 200m, 150m, 50m, "PartialTender", ""], ["S1", "D4", 100m, 130m, -30m, "ExcessTender", ""]]), "tender-diagnostic");
        Assert.Equal(20m, Kpi(diagnostic, "Variance"));
        var byCause = Assert.Single(diagnostic.Visuals).Series[0].Points;
        Assert.Equal(50m, Assert.Single(byCause, p => p.Category == "PartialTender").Value);
        Assert.Equal(30m, Assert.Single(byCause, p => p.Category == "ExcessTender").Value);
    }

    [Fact]
    public void Cash_book_summary_reads_opening_closing_deposits_expenses_and_days()
    {
        var day1 = new CashBookDay(new(2026, 9, 1), "S1", 1000m, "Entered", new Dictionary<string, decimal> { ["Cash"] = 500m }, 0m, 0m, 0m, 100m, 400m, 0m, 1000m, 1000m, 0m, 0m, "Complete");
        var day2 = day1 with { Date = new(2026, 9, 2), Opening = 1000m, Expenses = 50m, Deposit = 200m, Closing = 1250m };
        var model = VisualReportComposer.Compose(Meta("Cash Book"), CashBookTables.Create([day1, day2]), "cash");
        Assert.Equal(1000m, Kpi(model, "Opening balance"));
        Assert.Equal(1250m, Kpi(model, "Closing balance"));
        Assert.Equal(600m, Kpi(model, "Bank deposits"));
        Assert.Equal(150m, Kpi(model, "Expenses"));
        Assert.Equal(2m, Kpi(model, "Days"));
        var line = Assert.Single(model.Visuals);
        Assert.Equal(ReportVisualType.Line, line.Type);
        Assert.Equal([1000m, 1250m], line.Series[0].Points.Select(p => p.Value));
    }

    [Fact]
    public void Service_summary_uses_the_first_period_for_modes_and_charts_totals_by_period()
    {
        var data = new ExcelReportData(
            [new("Period"), new("Store"), new("From"), new("To"), new("WDC", "#,##0.00"), new("Cash", "#,##0.00"), new("Card", "#,##0.00"), new("UPI", "#,##0.00"), new("Total", "#,##0.00"), new("LY Total", "#,##0.00"), new("Growth %", "0.00%"), new("Availability"), new("Missing days", "#,##0"), new("LY missing days", "#,##0")],
            [["FTD", "S1", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 0m, 100m, 200m, 300m, 600m, 500m, 20m, "Complete", 0, 0],
             ["FTD", "S2", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 0m, 10m, 20m, 30m, 60m, 50m, 20m, "Complete", 0, 0],
             ["MTD", "S1", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 0m, 1000m, 2000m, 3000m, 6000m, 5000m, 20m, "Complete", 0, 0],
             ["MTD", "S2", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 0m, null, null, null, null, null, null, "Missing", 3, 0]]);
        var model = VisualReportComposer.Compose(Meta("Service Sales"), data, "service");
        Assert.Equal(110m, Kpi(model, "Cash (FTD)"));
        Assert.Equal(220m, Kpi(model, "Card (FTD)"));
        Assert.Equal(330m, Kpi(model, "UPI (FTD)"));
        Assert.Equal(660m, Kpi(model, "Total (FTD)"));
        var points = Assert.Single(model.Visuals).Series[0].Points;
        Assert.Equal(["FTD", "MTD"], points.Select(p => p.Category));
        Assert.Equal(6000m, points[1].Value);
    }

    [Fact]
    public void Exceptions_summary_counts_by_severity_and_charts_by_area()
    {
        var data = new ExcelReportData(
            [new("Severity"), new("Area"), new("Code"), new("Store"), new("Date"), new("Document"), new("Item"), new("Variance", "#,##0.00"), new("Workbook"), new("Sheet"), new("Source Row", "#,##0"), new("Message"), new("Recommended Action")],
            [["BLOCKER", "Tender", "T1", "S1", new DateOnly(2026, 9, 30), "", "", 50m, "", "", null, "", ""],
             ["WARN", "Stock", "S1", "S1", new DateOnly(2026, 9, 30), "", "", null, "", "", null, "", ""],
             ["FAIL", "Tender", "T2", "S1", new DateOnly(2026, 9, 30), "", "", -20m, "", "", null, "", ""]],
            ["Total", 3, "", "", "", "", "", 30m, "", "", "", "", ""]);
        var model = VisualReportComposer.Compose(Meta("Daily Exceptions"), data, "exceptions");
        Assert.Equal(3m, Kpi(model, "Exceptions"));
        Assert.Equal(2m, Kpi(model, "Blockers and failures"));
        Assert.Equal(1m, Kpi(model, "Warnings and others"));
        Assert.Equal(30m, Kpi(model, "Variance total"));
        var byArea = Assert.Single(model.Visuals).Series[0].Points;
        Assert.Equal(2m, Assert.Single(byArea, p => p.Category == "Tender").Value);
    }

    [Fact]
    public void Management_trend_summary_gives_latest_day_average_and_a_daily_line()
    {
        var first = new DateOnly(2026, 9, 1);
        var data = new ExcelReportData(
            [new("Date"), new("Store"), new("Net Sales", "#,##0.00"), new("Units", "#,##0.00"), new("Invoices", "#,##0"), new("Returns", "#,##0"), new("Tender Variance", "#,##0.00"), new("Tender Source"), new("Unmatched Staff Rows", "#,##0")],
            [[first, "EAST", 120m, 2m, 1, 0, 0m, "", 0], [first, "WEST", 80m, 1m, 1, 0, 0m, "", 0], [first.AddDays(1), "EAST", 300m, 3m, 2, 0, null, "missing", 0]]);
        var model = VisualReportComposer.Compose(Meta("Management Trend"), data, "management-trend");
        Assert.Equal(300m, Kpi(model, "Latest day sales"));
        Assert.Equal(250m, Kpi(model, "Daily average"));
        Assert.Equal(500m, Kpi(model, "Total sales"));
        Assert.Equal(2m, Kpi(model, "Days"));
        var line = Assert.Single(model.Visuals);
        Assert.Equal(ReportVisualType.Line, line.Type);
        Assert.Equal([200m, 300m], line.Series[0].Points.Select(p => p.Value));
        Assert.Equal("01 Sep", line.Series[0].Points[0].Category);
    }

    [Fact]
    public void Pdf_gets_a_summary_page_before_the_rows_only_when_the_summary_is_informative()
    {
        var informative = Path.Combine(Path.GetTempPath(), $"etp-summary-{Guid.NewGuid():N}.pdf");
        var fallback = Path.ChangeExtension(informative, ".rows.pdf");
        try
        {
            var rows = Enumerable.Range(1, 7).Select(i => (IReadOnlyList<object?>)[$"Group {i}", i * 2m, i * 1000m, i, 0]).ToArray();
            var sales = VisualReportComposer.Compose(Meta("Brand Sales"), new([new("Group"), new("Units", "#,##0.00"), new("Net Sales", "#,##0.00"), new("Invoices", "#,##0"), new("Returns", "#,##0")], rows), "sales-brand");
            new SimplePdfVisualReportExporter().Export(informative, sales);
            using (var pdf = PdfReader.Open(informative, PdfDocumentOpenMode.Import)) Assert.Equal(2, pdf.PageCount);

            var trend = VisualReportComposer.Compose(Meta("Management Trend"), new([new("Date"), new("Store"), new("Net Sales", "#,##0.00")],
                Enumerable.Range(0, 40).Select(i => (IReadOnlyList<object?>)[new DateOnly(2026, 8, 1).AddDays(i), "S1", 100m + i]).ToArray()), "management-trend");
            new SimplePdfVisualReportExporter().Export(informative, trend); // a line chart over 40 days renders without exception

            var plain = VisualReportComposer.Compose(Meta("Wide report"), new([new("Column 1"), new("Column 2")], [["a", 1m]]));
            new SimplePdfVisualReportExporter().Export(fallback, plain);
            using (var pdf = PdfReader.Open(fallback, PdfDocumentOpenMode.Import)) Assert.Equal(1, pdf.PageCount);
        }
        finally { if (File.Exists(informative)) File.Delete(informative); if (File.Exists(fallback)) File.Delete(fallback); }
    }
}
