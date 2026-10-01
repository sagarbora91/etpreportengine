using System.Globalization;

namespace Etp.Reporting.Import.Stock;

/// <summary>The report codes stored in <c>stock_snapshots.source_report_code</c> (migration 0038, section D).</summary>
public static class StockSnapshotSources
{
    /// <summary>R011 Closing Stock, the preferred source of a store-day.</summary>
    public const string ClosingStock = "CLOSING_STOCK";
    /// <summary>R010 BinWise Stock, used only when no Closing Stock snapshot exists for the store-day.</summary>
    public const string BinWise = "R010";
    /// <summary>The lineage record type of R010 snapshot facts.</summary>
    public const string BinWiseLineageRecordType = "R010_SNAPSHOT";

    /// <summary>The source of a snapshot fact from its lineage record type, as the 0038 backfill derives it
    /// (compared as the database's case-insensitive collation compares it).</summary>
    public static string FromLineageRecordType(string? recordType) =>
        string.Equals(recordType, BinWiseLineageRecordType, StringComparison.OrdinalIgnoreCase) ? BinWise : ClosingStock;

    /// <summary>The name the stock report shows for the snapshot source of a store-day.</summary>
    public static string DisplayName(string sourceReportCode) => sourceReportCode.Trim().ToUpperInvariant() switch
    {
        ClosingStock => "Closing Stock",
        BinWise => "BinWise",
        var other => other
    };
}

/// <summary>The identity and fact fields of one stock-snapshot row (R011 or R010), as the sequencer reads them.</summary>
public readonly record struct SnapshotItemRow(
    string StoreCode,
    DateOnly SnapshotDate,
    string SourceReportCode,
    string ProductCode,
    string? SourceUid,
    string? BatchNumber,
    string? Ean,
    decimal Quantity,
    decimal? UnitCost,
    decimal? TotalCost,
    string SheetName,
    int SourceRowNumber)
{
    /// <summary><c>item_discriminator</c>: COALESCE(source_uid, batch_number, ean, N'').</summary>
    public string ItemDiscriminator => SourceUid ?? BatchNumber ?? Ean ?? "";
}

/// <summary><see cref="LineSeq"/>[i] is the <c>line_seq</c> of input row i.</summary>
public sealed record SnapshotItemSequence(IReadOnlyList<int> LineSeq);

/// <summary>
/// Gives each snapshot row its <c>line_seq</c> inside (store, date, source, product, item discriminator), ordered by
/// quantity, unit cost, total cost (a missing value first, as SQL Server orders NULL), then sheet and sheet row
/// (spec 5.1 D, 7.2 SnapshotItems). Repeated identical rows get distinct numbers and are all stored; the stored
/// multiset does not depend on the input order. Identity text is compared as the database's case-insensitive
/// collation compares it. Shared by both planners.
/// </summary>
public static class SnapshotItemSequencer
{
    public static SnapshotItemSequence Assign<T>(IReadOnlyList<T> rows, Func<T, SnapshotItemRow> describe)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(describe);
        return Assign(rows.Select(describe).ToArray());
    }

    public static SnapshotItemSequence Assign(IReadOnlyList<SnapshotItemRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var lineSeq = new int[rows.Count];
        foreach (var group in Enumerable.Range(0, rows.Count).GroupBy(index => IdentityKey(rows[index]), StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(index => rows[index].Quantity)
                .ThenBy(index => rows[index].UnitCost)
                .ThenBy(index => rows[index].TotalCost)
                .ThenBy(index => rows[index].SheetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(index => rows[index].SourceRowNumber)
                .ToArray();
            for (var position = 0; position < ordered.Length; position++) lineSeq[ordered[position]] = position + 1;
        }
        return new(lineSeq);
    }

    private static string IdentityKey(SnapshotItemRow row) => string.Join('\u001f',
        StockUnitSequencer.SqlText(row.StoreCode), row.SnapshotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        StockUnitSequencer.SqlText(row.SourceReportCode), StockUnitSequencer.SqlText(row.ProductCode),
        StockUnitSequencer.SqlText(row.ItemDiscriminator));
}
