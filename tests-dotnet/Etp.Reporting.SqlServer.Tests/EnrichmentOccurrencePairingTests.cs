using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.4, R-WLMHW-01 (report audit of 3 Oct 2026). The Staff/CRO report read R013 rows by their import-time
/// match_status: an invoice with the same item on two R025 lines made both R013 rows Ambiguous (dropped), and two
/// identical R013 rows could both be Matched to one line (counted twice). Reports now pair R013 rows with lines by
/// occurrence through <see cref="EnrichmentOccurrencePairing"/>. These tests read the shipped SQL text; the
/// behaviour is covered by StaffOccurrencePairingSqlTests in the SQL integration suite.
/// </summary>
public sealed partial class EnrichmentOccurrencePairingTests
{
    // Every file that reads dbo.sales_line_enrichments for a report, a count or the problems list.
    private static readonly string[] Consumers =
    [
        "OperationalReportRepository.cs",
        "Phase2OperationsRepository.cs",
        "RetailEnrichmentSqlImportOrchestrator.cs",
    ];

    private static string SourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "Etp.Reporting.Infrastructure.SqlServer");
    }

    private static string Squash(string sql) => WhitespaceRun().Replace(sql, " ");

    [Theory]
    [MemberData(nameof(ConsumerFiles))]
    public void No_consumer_reads_the_import_time_match_status(string file)
    {
        var text = File.ReadAllText(Path.Combine(SourceDirectory(), file));
        Assert.DoesNotMatch(StoredStatusRead(), text);
        Assert.Contains("EnrichmentOccurrencePairing.EffectiveEnrichments", text);
    }

    public static TheoryData<string> ConsumerFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Consumers) data.Add(file);
        return data;
    }

    [Fact]
    public void R013_rows_and_sales_lines_are_numbered_on_the_same_key_in_source_order()
    {
        var sql = Squash(EnrichmentOccurrencePairing.EffectiveEnrichments);
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY x.enrichment_type,x.store_code,x.transaction_date,x.document_number,x.product_code ORDER BY x.source_lineage_id,x.sales_line_enrichment_id) occurrence", sql);
        // The sales-line side numbers only the R013 row's own invoice lines for its item (correlated OUTER APPLY),
        // in source order, so a report call never sorts the whole sales_lines table.
        Assert.Contains("OUTER APPLY (SELECT q.sales_line_id,q.line_count FROM (SELECT sl.sales_line_id,ROW_NUMBER() OVER(ORDER BY sl.source_lineage_id,sl.sales_line_id) occurrence,COUNT(*) OVER() line_count", sql);
        Assert.Contains("AND si.store_code=pe.store_code AND si.transaction_date=pe.transaction_date AND si.document_number=pe.document_number AND sl.product_code=pe.product_code) q", sql);
        Assert.DoesNotContain("LEFT JOIN", sql);
        // The nth R013 row takes the nth line, so a line takes at most one R013 row and a surplus row reads Missing.
        Assert.Contains("WHERE q.occurrence=pe.occurrence) pl", sql);
        Assert.Contains("CASE WHEN pe.enrichment_type='R013' THEN CASE WHEN pl.sales_line_id IS NULL THEN 'Missing' ELSE 'Matched' END", sql);
        Assert.Contains("CASE WHEN pe.enrichment_type='R013' OR pl.line_count=pe.key_rows THEN pl.sales_line_id ELSE pe.matched_sales_line_id END effective_sales_line_id", sql);
    }

    [Fact]
    public void Only_an_all_ambiguous_R003_key_is_paired_and_only_when_its_rows_equal_its_lines()
    {
        // P12 (1.12.1, DQ-UNMATCHED-ENRICHMENT): 137 live Titan R003 rows on invoices that repeat an item on 2..18 identical
        // lines read Ambiguous. They are paired with the R013 numbering (same partition, same source order on both sides).
        var sql = Squash(EnrichmentOccurrencePairing.EffectiveEnrichments);
        const string key = "PARTITION BY x.enrichment_type,x.store_code,x.transaction_date,x.document_number,x.product_code";
        Assert.Contains($"COUNT(*) OVER({key}) key_rows", sql);
        Assert.Contains($"COUNT(CASE WHEN x.match_status='Ambiguous' THEN 1 END) OVER({key}) key_ambiguous", sql);
        // Lines are numbered for R013, and for R003 only when every row of the key is stored Ambiguous: a Matched R003 row
        // keeps its stored line (several discounts on one line are correct), so re-pairing its neighbours could double a line.
        Assert.Contains("WHERE (pe.enrichment_type='R013' OR (pe.enrichment_type='R003' AND pe.match_status='Ambiguous' AND pe.key_ambiguous=pe.key_rows)) AND si.store_code=pe.store_code", sql);
        // Unequal counts (two discounts on each of two lines, a missing or a surplus row) keep the stored status: genuinely ambiguous.
        Assert.Contains("WHEN pl.line_count=pe.key_rows THEN 'Matched' ELSE pe.match_status END effective_match_status", sql);
        // Identical R003 rows are real (one per repeated line) and are never dropped as duplicates.
        Assert.Contains("WHERE x.enrichment_type<>'R013' OR x.same_content=1) pe", sql);
    }

    [Theory]
    [InlineData("R003", "Ambiguous", "Discount enrichment", "R003_AMBIGUOUS")]
    [InlineData("R003", "Missing", "Discount enrichment", "R003_MISSING")]
    [InlineData("R013", "Ambiguous", "Staff enrichment", "R013_AMBIGUOUS")]
    [InlineData("R013", "Missing", "Staff enrichment", "R013_MISSING")]
    [InlineData("r003 ", "Ambiguous", "Discount enrichment", "R003_AMBIGUOUS")]
    public void Daily_exceptions_label_an_enrichment_row_by_its_own_family(string type, string status, string area, string code)
    {
        var (actualArea, actualCode, message) = OperationalReportRepository.EnrichmentException(type, status);
        Assert.Equal(area, actualArea);
        Assert.Equal(code, actualCode);
        // Only an R013 row may claim to be a staff/CRO problem.
        Assert.Equal(code.StartsWith("R013_", StringComparison.Ordinal), message.Contains("staff/CRO enrichment", StringComparison.Ordinal));
    }

    [Fact]
    public void The_exceptions_query_reads_the_family_and_no_label_is_hard_coded()
    {
        var text = File.ReadAllText(Path.Combine(SourceDirectory(), "OperationalReportRepository.cs"));
        Assert.DoesNotContain("$\"R013_{", text);
        Assert.Contains("s.source_row_number,\n                   e.enrichment_type\n", text.ReplaceLineEndings("\n"));
        Assert.Contains("EnrichmentException(reader.GetString(8), reader.GetString(4))", text);
    }

    [Fact]
    public void An_R013_row_identical_to_an_earlier_one_is_dropped_but_content_is_never_ignored()
    {
        // 1.11.2 L4 (AUDIT-1 fix 3): the Titan export lists an invoice line twice; the importer keeps one sales line, so
        // the identical extra R013 row is removed instead of reading Missing (R013_MISSING).
        var sql = Squash(EnrichmentOccurrencePairing.EffectiveEnrichments);
        Assert.Contains("WHERE x.enrichment_type<>'R013' OR x.same_content=1) pe", sql);
        var partition = sql[sql.IndexOf("PARTITION BY d.enrichment_type", StringComparison.Ordinal)..];
        partition = partition[..partition.IndexOf("ORDER BY", StringComparison.Ordinal)];
        // Every content column is part of "identical"; any difference keeps both rows.
        foreach (var column in new[] { "store_code", "transaction_date", "document_number", "product_code", "source_transaction_type",
                     "source_quantity", "source_net_value", "source_cro_number", "scheme_discount", "user_discount", "pre_discount",
                     "other_charges", "activation_details", "user_discount_details", "invoice_year", "source_gross_value", "staff_name" })
            Assert.Contains("d." + column, partition);
        // Row identity columns differ per row by design and must not be compared.
        Assert.DoesNotContain("content_key", partition);
        Assert.DoesNotContain("source_lineage_id", partition);
        Assert.DoesNotContain("sales_line_enrichment_id", partition);
    }

    [Fact]
    public void Invoice_lineage_looks_up_the_paired_staff_row_by_the_line_key()
    {
        // The lineage export filters its lines by date and store; the CRO lookup is correlated on that line's key so
        // the pairing is evaluated per invoice item, not over every enrichment row.
        var text = Squash(File.ReadAllText(Path.Combine(SourceDirectory(), "OperationalReportRepository.cs")));
        Assert.Contains("WHERE e.enrichment_type='R013' AND e.store_code=i.store_code AND e.transaction_date=i.transaction_date AND e.document_number=i.document_number AND e.product_code=l.product_code AND e.effective_match_status='Matched' AND e.effective_sales_line_id=l.sales_line_id ) cro", text);
    }

    // e.match_status / match_status compared with 'Matched' (the 1.9.3 reads), but not effective_match_status.
    [GeneratedRegex(@"(?<![A-Za-z_])(?:e\.)?match_status\s*(?:=|<>)\s*'Matched'")]
    private static partial Regex StoredStatusRead();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
