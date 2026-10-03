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
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY si.store_code,si.transaction_date,si.document_number,sl.product_code ORDER BY sl.source_lineage_id,sl.sales_line_id) occurrence", sql);
        // The nth R013 row takes the nth line, so a line takes at most one R013 row and a surplus row reads Missing.
        Assert.Contains("AND pl.occurrence=pe.occurrence", sql);
        Assert.Contains("WHEN pl.sales_line_id IS NULL THEN 'Missing' ELSE 'Matched' END effective_match_status", sql);
        // R003 carries one row per discount on a line, so it is never capped at one row per line.
        Assert.Contains("ON pe.enrichment_type='R013' AND", sql);
        Assert.Contains("CASE WHEN pe.enrichment_type<>'R013' THEN pe.match_status", sql);
        Assert.Contains("CASE WHEN pe.enrichment_type='R013' THEN pl.sales_line_id ELSE pe.matched_sales_line_id END effective_sales_line_id", sql);
    }

    // e.match_status / match_status compared with 'Matched' (the 1.9.3 reads), but not effective_match_status.
    [GeneratedRegex(@"(?<![A-Za-z_])(?:e\.)?match_status\s*(?:=|<>)\s*'Matched'")]
    private static partial Regex StoredStatusRead();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
