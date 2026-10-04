using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.4, WLMHW report audit FIX-14 (R-WLMHW-14): the stock ledger's LOCATION (bin) is stored on dbo.stock_movements by
/// migration 0047, kept out of the movement identity, and the Stock Variance opening is chained per bin. These tests read
/// the shipped migration and queries; StockLedgerLocationSqlTests in the SQL integration suite runs them.
/// </summary>
public sealed class StockLedgerLocationTests
{
    private const string MigrationName = "0047_stock_ledger_location.sql";

    [Fact]
    public void Migration_0047_adds_a_nullable_bin_column_once_and_backfills_only_missing_bins()
    {
        var sql = Migration(MigrationName);
        Assert.Contains("IF COL_LENGTH(N'dbo.stock_movements',N'location') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE dbo.stock_movements ADD location nvarchar(80) NULL;", sql, StringComparison.Ordinal);
        // Both backfill statements write only rows that still have no bin, so a second run changes nothing.
        Assert.Equal(2, Regex.Matches(sql, @"UPDATE m SET location=").Count);
        Assert.Equal(2, Regex.Matches(sql, @"WHERE m\.location IS NULL").Count);
        // The backfill reads the typed R030 rows, and an ambiguous bin stays NULL.
        Assert.Contains("JOIN dbo.etp_r030 r ON r.source_lineage_id=rl.source_lineage_id", sql, StringComparison.Ordinal);
        Assert.Contains("b.low_location=b.high_location", sql, StringComparison.Ordinal);
        // The locked-day guard is off for the backfill only.
        Assert.Single(Regex.Matches(sql, @"DISABLE TRIGGER dbo\.trg_stock_movements_protect_locked"));
        Assert.Single(Regex.Matches(sql, @"ENABLE TRIGGER dbo\.trg_stock_movements_protect_locked"));
        Assert.True(sql.IndexOf("DISABLE TRIGGER", StringComparison.Ordinal) < sql.IndexOf("ENABLE TRIGGER", StringComparison.Ordinal));
    }

    [Fact]
    public void Migration_0047_keeps_the_bin_out_of_the_movement_identity()
    {
        var sql = Migration(MigrationName);
        var section = Migration("0041_import_engine_fixes.sql");
        // No identity index, line_seq or key column is touched.
        Assert.DoesNotContain("UX_stock_movements_identity", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SET line_seq", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("from_key AS", sql, StringComparison.Ordinal);
        // The procedure's identity text, content hash and identity lookup are 0041's, character for character.
        foreach (var unchanged in new[]
        {
            "SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/'',@date,N''/'',@product,N''/'',UPPER(@type),N''/'',ISNULL(@from,N''''),N''/'',ISNULL(@to,N''''),N''/#'',@line_seq);",
            "SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@opening,N''|'',@transaction,N''|'',@closing)),2));",
            "WHERE store_code=@store AND invoice_year=@year AND document_number=@doc AND document_date=@date AND product_code=@product AND source_transaction_type=@type AND from_key=ISNULL(@from,N'''') AND to_key=ISNULL(@to,N'''') AND line_seq=@line_seq ORDER BY stock_movement_id;",
        })
        {
            Assert.Contains(unchanged, section, StringComparison.Ordinal);
            Assert.Contains(unchanged, sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Persist_stock_movement_takes_the_bin_last_and_fills_a_missing_one_only_on_an_open_day()
    {
        var sql = Migration(MigrationName);
        Assert.Contains("@line_seq int=1,@location nvarchar(80)=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("line_seq,location) VALUES(", sql, StringComparison.Ordinal);
        Assert.Contains("@lineage,@line_seq,@location);", sql, StringComparison.Ordinal);
        Assert.Contains("SET @location=NULLIF(LTRIM(RTRIM(@location)),N'''');", sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE dbo.stock_movements SET location=@location WHERE stock_movement_id=@existing AND location IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("d.status=''LOCKED''", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stock_variance_opening_is_the_sum_of_each_bins_chain_start()
    {
        // 1.9.4 merge: the opening is resolved in C# (StockLedgerOpening, report audit R-HEMW-02 / R-WLMHW-05) from every
        // ledger row of the key, so the query carries each row's bin and ResolveAcrossBins chains each bin on its own.
        Assert.Contains("m.opening_quantity,m.closing_quantity,m.location", SqlReportingQueries.StockLedgerRows, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT TOP(1) m.opening_quantity", SqlReportingQueries.StockPositions, StringComparison.Ordinal);
        // 1 Sep: DEFECTIVEBIN 0 -> 1. 2 Sep: RETAILBIN 5 -> 4. 1.9.3 opened at 0 (the DEFECTIVEBIN chain); the item opened with 5.
        Assert.Equal(5m, Etp.Reporting.Reporting.StockLedgerOpening.ResolveAcrossBins([
            new(Day(1), 1, 1, 0m, 1m, "DEFECTIVEBIN"),
            new(Day(2), 1, 2, 5m, 4m, "RETAILBIN")], Day(1), Day(2), 5m));
    }

    [Fact]
    public void Stock_variance_opening_falls_back_to_the_first_movement_when_any_bin_is_unknown()
    {
        // A movement with no stored bin is a RETAILBIN or DEFECTIVEBIN movement whose bin is unknown, not a bin of its own,
        // so its chain start must not be added to the real bins' (review of FIX-14): 5 + 4 would open at 9.
        Assert.Equal(5m, Etp.Reporting.Reporting.StockLedgerOpening.ResolveAcrossBins([
            new(Day(8), 1, 1, 5m, 4m, null),
            new(Day(9), 1, 2, 4m, 3m, "RETAILBIN")], Day(8), Day(9), 3m));
    }

    private static DateOnly Day(int day) => new(2026, 9, day);

    [Fact]
    public void Stock_movement_report_shows_the_bin()
    {
        var sql = SqlReportingQueries.StockMovements;
        Assert.Contains("SUM(m.transaction_quantity) source_signed_quantity,m.location", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY m.store_code,m.product_code,m.location,m.source_transaction_type", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stock_movement_report_marks_a_store_without_a_snapshot_instead_of_hiding_it()
    {
        // Owner answer Q9: the store-level snapshot test is a column now, not a filter, and never per product.
        var sql = SqlReportingQueries.StockMovements;
        Assert.Contains("s.store_code=m.store_code AND s.snapshot_date=@dateTo)", sql, StringComparison.Ordinal);
        Assert.Contains("END has_snapshot", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AND EXISTS(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("s.product_code=m.product_code", sql, StringComparison.Ordinal);
    }

    private static string Migration(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "database", "migrations", name));
    }
}
