using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.11.2, lane L5 (report logic and SQL speed). Text and SQL-free checks; the SQL behaviour, including the cross-layer path
/// from C# into the 0060 views by role, is ReportLogicAndSpeedSqlTests in the integration suite.
/// AUDIT-2 S-07 (customer name by role, Owner-only pack setting), AUDIT-1 D6 (staff from the paired R025 line), D7 /
/// decision 41 (a bill cancelled the same day is not a DSR invoice), fix 4 (source tender shortfall), AUDIT-4 spots 2-4.
/// </summary>
public sealed class ReportLogicAndSpeedTests
{
    private const string MigrationName = "0060_report_logic_and_speed.sql";

    // 0056's rule, as written inside EXEC(N'...') (quotes doubled).
    private const string Rule = "IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1";

    private static string Migrations()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "database", "migrations");
    }

    private static string Migration() => File.ReadAllText(Path.Combine(Migrations(), MigrationName)).Replace("\r\n", "\n");

    private static string View(string name)
    {
        var match = Regex.Match(Migration(), $@"EXEC\(N'CREATE OR ALTER VIEW dbo\.{name} AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline);
        Assert.True(match.Success, $"{name} is not defined in {MigrationName}.");
        return match.Groups["body"].Value;
    }

    private static string Squash(string sql) => Regex.Replace(sql, @"\s+", " ");

    [Fact]
    public void The_migration_is_the_only_0060_and_has_no_transaction_or_GO()
    {
        Assert.Equal([MigrationName], Directory.GetFiles(Migrations(), "0060_*.sql").Select(Path.GetFileName));
        var code = string.Join("\n", Migration().Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        Assert.DoesNotMatch(new Regex(@"\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b", RegexOptions.IgnoreCase), code);
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), code);
        Assert.DoesNotMatch(new Regex(@"\b(DROP|TRUNCATE|DELETE\s+FROM|REVOKE|ALTER\s+ROLE)\b", RegexOptions.IgnoreCase), code);
    }

    [Fact]
    public void The_invoice_customer_name_view_gives_the_name_by_0056s_role_rule_and_no_contact_column()
    {
        var body = View("v_sales_invoice_customer_name");
        Assert.Contains($"CASE WHEN {Rule}\n       THEN x.customer_name END customer_name", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, Regex.Escape(Rule)));
        Assert.Equal(3, Regex.Matches(body, @"IS_MEMBER\(").Count);
        Assert.DoesNotContain("etp_viewer", body, StringComparison.Ordinal);
        foreach (var word in new[] { "phone", "mobile", "mail", "address", "gstin", "customer_no" })
            Assert.DoesNotContain(word, body, StringComparison.OrdinalIgnoreCase);
        // Same choice as the old per-invoice lookup: current R024 files only, the newest row of the store, document and date.
        Assert.Contains("WHERE f.is_superseded=0", body, StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY k.store_code,k.document_number,k.transaction_date ORDER BY k.etp_row_id DESC) newest", body, StringComparison.Ordinal);
        Assert.Contains("WHERE x.newest=1", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cancelled_bill_view_carries_no_name_and_matches_a_BC_to_its_invoice_on_the_same_day()
    {
        var body = View("v_sales_cancelled_same_day");
        Assert.StartsWith("SELECT DISTINCT i.store_code,i.document_number,i.transaction_date\n", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("customer|name|phone", RegexOptions.IgnoreCase), Regex.Replace(body, "import_file|original_file_name", ""));
        Assert.Contains("f.report_code=''R025'' AND f.is_superseded=0 AND f.data_truth_version=1", body, StringComparison.Ordinal);
        Assert.Contains("AND i.transaction_date=r.transaction_date", body, StringComparison.Ordinal);
        Assert.Contains("UPPER(LTRIM(RTRIM(r.source_transaction_type)))=N''BC''", body, StringComparison.Ordinal);
        Assert.Contains("(r.reference_invoice_date IS NULL OR r.reference_invoice_date=r.transaction_date)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_views_carry_the_service_views_grants_the_setting_defaults_off_and_the_indexes_exist()
    {
        var script = Migration();
        foreach (var view in new[] { "v_sales_invoice_customer_name", "v_sales_cancelled_same_day" })
        {
            Assert.Contains($"\nGRANT SELECT ON dbo.{view} TO etp_viewer,etp_store_manager,etp_owner;\n", script, StringComparison.Ordinal);
            Assert.Contains($"\nDENY INSERT,UPDATE,DELETE ON dbo.{view} TO etp_store_manager,etp_viewer;\n", script, StringComparison.Ordinal);
        }
        Assert.Contains("ALTER TABLE dbo.product_settings ADD share_pack_customer_names bit NOT NULL\n    CONSTRAINT DF_product_settings_share_pack_customer_names DEFAULT(0);", script, StringComparison.Ordinal);
        // The only grants in the file are the four view lines: product_settings stays Owner-only for writes.
        Assert.Equal(4, Regex.Matches(script, @"^(GRANT|DENY) ", RegexOptions.Multiline).Count);
        Assert.Contains("CREATE INDEX IX_sales_line_enrichments_occurrence ON dbo.sales_line_enrichments\n    (enrichment_type,store_code,transaction_date,document_number,product_code,source_lineage_id)", script, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IX_etp_r024_inv_date ON dbo.etp_r024(inv_date)", script, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IX_etp_r025_transaction_date ON dbo.etp_r025(transaction_date)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_occurrence_index_follows_the_pairing_window_so_the_window_needs_no_sort()
    {
        // The index key is the window's PARTITION BY then its first ORDER BY column (the clustered key follows).
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY x.enrichment_type,x.store_code,x.transaction_date,x.document_number,x.product_code ORDER BY x.source_lineage_id,x.sales_line_enrichment_id) occurrence",
            Squash(EnrichmentOccurrencePairing.EffectiveEnrichments), StringComparison.Ordinal);
    }

    [Fact]
    public void The_invoice_summary_reads_the_customer_name_only_through_the_role_view()
    {
        var sql = Squash(OperationalReportRepository.InvoiceSummarySql);
        Assert.Contains("LEFT JOIN dbo.v_sales_invoice_customer_name customer ON customer.store_code=i.store_code AND customer.document_number=i.document_number AND customer.transaction_date=i.transaction_date", sql, StringComparison.Ordinal);
        Assert.Contains("MAX(customer.customer_name)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("etp_r024", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OUTER APPLY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Staff_sales_take_value_quantity_and_type_from_the_paired_R025_line()
    {
        foreach (var raw in new[] { OperationalReportRepository.StaffSql, OperationalReportRepository.StaffLastYearSql })
        {
            var sql = Squash(raw);
            Assert.Contains("JOIN dbo.sales_lines l ON l.sales_line_id=e.effective_sales_line_id", sql, StringComparison.Ordinal);
            Assert.Contains("COALESCE(SUM(l.source_gross_amount),0)", sql, StringComparison.Ordinal);
            Assert.Contains("e.effective_match_status='Matched' AND UPPER(l.source_transaction_type) IN('INV','SR','BC')", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("e.source_gross_value", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("UPPER(e.source_transaction_type)", sql, StringComparison.Ordinal);
        }
        var current = Squash(OperationalReportRepository.StaffSql);
        Assert.Contains("COALESCE(SUM(l.source_quantity),0)", current, StringComparison.Ordinal);
        Assert.DoesNotContain("e.source_quantity", current, StringComparison.Ordinal);
        Assert.Contains("ORDER BY e.store_code,SUM(l.source_gross_amount) DESC", current, StringComparison.Ordinal);
        Assert.StartsWith("R025_PAIRED_", OperationalReportRepository.StaffMetricPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void Dsr_invoice_count_leaves_out_bills_cancelled_the_same_day_and_value_and_units_are_unchanged()
    {
        var sql = Squash(OperationalReportRepository.DsrFactsSql);
        Assert.Contains("LEFT JOIN dbo.v_sales_cancelled_same_day cancelled ON cancelled.store_code=i.store_code AND cancelled.document_number=i.document_number AND cancelled.transaction_date=i.transaction_date", sql, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(sql, Regex.Escape("AND cancelled.document_number IS NULL THEN i.sales_invoice_id END")).Count);
        // Only the two INVOICE columns read the view: VALUE, VOL and the gift-card line do not.
        Assert.Equal(3, Regex.Matches(sql, "cancelled\\.document_number").Count);
    }

    [Fact]
    public void Sales_query_resolves_the_snapshot_brand_once_per_item_not_per_line()
    {
        var sql = Squash(SqlReportingQueries.Sales);
        Assert.StartsWith("WITH sold AS", sql.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY s.store_code,s.product_code ORDER BY s.snapshot_date DESC,s.stock_snapshot_id DESC) newest", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN latest_product p ON p.store_code=i.store_code AND p.product_code=l.product_code", sql, StringComparison.Ordinal);
        // No per-line probe into the snapshot view any more.
        Assert.DoesNotContain("s.product_code=l.product_code", sql, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(sql, "v_stock_snapshots_effective"));
    }

    [Fact]
    public void The_shared_pack_blanks_the_customer_column_unless_the_owner_setting_is_on()
    {
        var section = new DailyReportPackSection("Customer-wise Invoice Sales", ReconciliationStatus.Passed, 354m, null, "ok");
        InvoiceSalesSummaryRow[] rows = [new(new(2031, 3, 4), "NAMEPK", "N1", "INV", 1m, 354m, 1, "Sample Customer 07")];

        var off = DailyReportingPackService.InvoiceSummaryTable(section, rows, shareCustomerNames: false);
        var on = DailyReportingPackService.InvoiceSummaryTable(section, rows, shareCustomerNames: true);
        var byDefault = DailyReportingPackService.InvoiceSummaryTable(section, rows);
        var customer = off.Data.Columns.Select(x => x.Header).ToList().IndexOf("Customer");

        Assert.True(customer >= 0, "The Customer column stays, so the sheet keeps its layout.");
        Assert.Null(off.Data.Rows.Single()[customer]);
        Assert.Null(byDefault.Data.Rows.Single()[customer]);
        Assert.Equal("Sample Customer 07", on.Data.Rows.Single()[customer]);
        Assert.Equal(354m, off.Data.Rows.Single()[6]);
        Assert.Contains("left out", DailyReportingPackService.InvoiceCustomerNameNote(false), StringComparison.Ordinal);
        Assert.Contains("Owner and Store Manager only", DailyReportingPackService.InvoiceCustomerNameNote(true), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pack_setting_setter_checks_the_owner_in_SQL_and_audits_the_change()
    {
        var sql = Squash(DailyReportingPackService.SetSharePackCustomerNamesSql);
        Assert.Contains("IF ISNULL(IS_MEMBER(N'etp_owner'),0)<>1 AND ISNULL(IS_MEMBER(N'db_owner'),0)<>1 THROW 51060", sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE dbo.product_settings SET share_pack_customer_names=@include", sql, StringComparison.Ordinal);
        Assert.Contains("EXEC dbo.record_operational_audit 'ConfigurationChange','Succeeded'", sql, StringComparison.Ordinal);
        Assert.True(sql.IndexOf("THROW 51060", StringComparison.Ordinal) < sql.IndexOf("UPDATE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1000, 900, 100, 100, true)]    // export pays 100 short, nothing excluded: source shortfall
    [InlineData(1000, 900, 100, null, false)]  // not a source shortfall (an excluded row, or no control)
    [InlineData(1000, 900, 100, 60, false)]    // the variance is more than the source explains
    [InlineData(1000, 1100, -100, null, false)] // excess tender is never a shortfall
    public void A_failed_document_is_a_source_shortfall_only_when_the_export_explains_the_whole_variance(
        int invoice, int tender, int variance, int? shortfall, bool expected)
    {
        var document = new DocumentControlResult("WLMHW", "100001211", invoice, tender, variance, ReconciliationStatus.Failed);
        Assert.Equal(expected, OperationalReportRepository.IsSourceTenderShortfall(document, shortfall));
    }

    [Fact]
    public void A_blank_tender_document_is_never_called_a_source_shortfall()
    {
        var blocked = new DocumentControlResult("WLMHW", "D1", 500m, null, null, ReconciliationStatus.Blocked);
        Assert.False(OperationalReportRepository.IsSourceTenderShortfall(blocked, 500m));
    }

    [Fact]
    public void The_pack_names_only_failed_documents_whose_shortfall_is_in_the_source()
    {
        var tender = new InvoiceTenderReconciliation(ReconciliationStatus.Failed,
        [
            new("WLMHW", "SRC", 22795m, 22666m, 129m, ReconciliationStatus.Failed),
            new("WLMHW", "ROUND", 500m, 499m, 1m, ReconciliationStatus.Passed),
            new("WLMHW", "IMPORT", 700m, 600m, 100m, ReconciliationStatus.Failed),
        ], 23995m, 23765m, 230m, "rule", "Compared.");
        TenderSourceShortfallRow[] source = [new("WLMHW", "SRC", 129m), new("wlmhw", "round", 1m)];

        var failed = OperationalReportRepository.FailedSourceTenderShortfalls(tender, source);

        Assert.Equal("SRC", Assert.Single(failed).DocumentNumber);
        var line = OperationalReportRepository.SourceTenderShortfallSummary(failed);
        Assert.Contains("Source tender shortfall", line, StringComparison.Ordinal);
        Assert.Contains("1 document(s), 129.00", line, StringComparison.Ordinal);
        Assert.Equal("", OperationalReportRepository.SourceTenderShortfallSummary([]));
        Assert.Contains("not an import fault", OperationalReportRepository.SourceTenderShortfallMessage(129m), StringComparison.Ordinal);
    }

    [Fact]
    public void The_source_shortfall_query_skips_documents_with_an_excluded_tender_row_and_counts_the_R020_fill()
    {
        var sql = Squash(SqlReportingQueries.TenderSourceShortfalls);
        Assert.Contains("NOT EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.sales_invoice_id=i.sales_invoice_id AND x.is_reporting_eligible=0)", sql, StringComparison.Ordinal);
        Assert.Contains("HAVING c.control-COALESCE(SUM(t.source_amount),0)>0", sql, StringComparison.Ordinal);
        Assert.Contains("N'TC_R020'", sql, StringComparison.Ordinal);
    }
}
