using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.9 polish, ops lane (report audit of 9 Oct 2026): RA-OPS-12 (pack physical section with no snapshot),
/// RA-TENDER-13 (pack prints display names, not field codes), RA-OPS-02 (missing texts name the entry screen) and
/// RA-OPS-17 (Management Trend net sales follow the DSR transaction-type and gift-card rule). SQL-free: the trend SQL
/// itself runs at the elevated gate.
/// </summary>
public sealed class PolishOpsMessageTests
{
    private static ManualInputValue Input(string code, string name, decimal? value = null) =>
        new(code, name, "NUMERIC", value, null, true, null, null);

    private static readonly IReadOnlyList<ManualInputValue> Inputs =
    [
        Input("OPENING_CASH", "Opening cash"),
        Input("CASH_DEPOSIT", "Cash deposit"),
        Input("EXPENSES", "Expenses"),
        Input("WALK_INS", "Walk-ins"),
        Input("SERVICE_CASH", "Service cash", 100m)
    ];

    [Fact]
    public void Pack_physical_section_without_a_snapshot_says_so_with_store_and_date()
    {
        var message = DailyReportingPackService.PhysicalStockMessage("HEMW", new DateOnly(2026, 9, 26), brandRows: 0, uncountedBrands: 0);

        Assert.StartsWith("No closing-stock snapshot for HEMW on 26 Sep 2026", message);
        Assert.Contains("Stock > Physical count", message);
        Assert.DoesNotContain("Physical is the sum", message);
    }

    [Fact]
    public void Pack_physical_section_keeps_the_rule_text_when_every_brand_is_counted()
    {
        var message = DailyReportingPackService.PhysicalStockMessage("HEMW", new DateOnly(2026, 9, 29), brandRows: 11, uncountedBrands: 0);

        Assert.Equal("Physical is the sum of all four entered components; difference is Physical minus System.", message);
    }

    [Fact]
    public void Pack_physical_section_with_uncounted_brands_names_the_entry_screen()
    {
        var message = DailyReportingPackService.PhysicalStockMessage("WLMHW", new DateOnly(2026, 9, 29), brandRows: 16, uncountedBrands: 16);

        Assert.StartsWith("16 brand(s) do not yet have a counted physical quantity", message);
        Assert.Contains("enter it on Stock > Physical count", message);
    }

    [Fact]
    public void Pack_manual_inputs_print_display_names_grouped_by_entry_screen()
    {
        var message = DailyReportingPackService.ManualInputsMessage(["CASH_DEPOSIT", "EXPENSES", "OPENING_CASH", "WALK_INS"], Inputs);

        Assert.Equal(
            "Missing: Cash deposit, Expenses, Opening cash (enter on Today > Cash > Cash and service entries); Walk-ins (enter on Today > Walk-ins).",
            message);
        Assert.DoesNotContain("OPENING_CASH", message);
    }

    [Fact]
    public void Pack_manual_inputs_complete_and_unknown_codes()
    {
        Assert.Equal("Required manual inputs are complete.", DailyReportingPackService.ManualInputsMessage([], Inputs));
        // A code with no definition row falls back to the code rather than inventing a name.
        Assert.Equal("Missing: NEW_FIELD (enter on Today > Cash > Cash and service entries).",
            DailyReportingPackService.ManualInputsMessage(["NEW_FIELD"], Inputs));
    }

    [Theory]
    [InlineData("WALK_INS", "Today > Walk-ins")]
    [InlineData("walk_ins", "Today > Walk-ins")]
    [InlineData("OPENING_CASH", "Today > Cash > Cash and service entries")]
    [InlineData("SERVICE_UPI", "Today > Cash > Cash and service entries")]
    public void Each_input_maps_to_its_entry_screen(string code, string screen) =>
        Assert.Equal(screen, OperationalInputScreens.For(code));

    [Fact]
    public void Exception_rows_name_the_field_and_the_entry_screen()
    {
        Assert.Equal("Required operational input Opening cash (OPENING_CASH) is missing.",
            OperationalReportRepository.ManualInputMissingMessage("OPENING_CASH", Inputs));
        Assert.Equal("Required operational input NEW_FIELD is missing.",
            OperationalReportRepository.ManualInputMissingMessage("NEW_FIELD", Inputs));
        Assert.Equal("Enter it on Today > Walk-ins; enter zero explicitly when zero is the true value.",
            OperationalReportRepository.ManualInputMissingAction("WALK_INS"));
        Assert.Equal("Enter it on Today > Cash > Cash and service entries; enter zero explicitly when zero is the true value.",
            OperationalReportRepository.ManualInputMissingAction("EXPENSES"));
        Assert.Contains("Stock > Physical count", OperationalReportRepository.PhysicalCountMissingAction);
    }

    [Fact]
    public void Management_trend_net_sales_and_units_follow_the_dsr_rule()
    {
        var select = Phase2OperationsRepository.ManagementTrendSalesSelect;
        var line = Phase2OperationsRepository.ManagementTrendSalesLine;

        Assert.Contains("IN('INV','SR','BC')", line);
        Assert.Contains($"AND NOT {NonMerchandiseSql.SalesLine("l")}", line);
        Assert.Contains($"SUM(CASE WHEN {line} THEN l.source_gross_amount ELSE 0 END) net_sales", select);
        Assert.Contains($"SUM(CASE WHEN {line} THEN l.source_quantity ELSE 0 END) units", select);
        Assert.DoesNotContain("SUM(l.source_gross_amount)", select);
        Assert.DoesNotContain("SUM(l.source_quantity)", select);
        // Invoice and return counts keep their existing rule.
        Assert.Contains("UPPER(l.source_transaction_type)='INV' THEN i.sales_invoice_id END) invoices", select);
        Assert.Contains("UPPER(l.source_transaction_type) IN('SR','BC') THEN i.sales_invoice_id END) returns", select);
    }
}
