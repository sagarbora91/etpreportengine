using Etp.Reporting.Import.Stock;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Stamps snapshot rows with their source report and <c>line_seq</c> before they reach
/// <c>dbo.persist_stock_snapshot</c> (migration 0041, section D). Repeated identical rows get distinct line numbers,
/// so each is stored; R010 and R011 readings of one store-day stay separate identities.
/// </summary>
public static class StockSnapshotLines
{
    public static StockSnapshotPersistence[] Assign(IReadOnlyList<StockSnapshotPersistence> snapshots, string source)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var sequence = SnapshotItemSequencer.Assign(snapshots, x => new SnapshotItemRow(x.StoreCode, x.SnapshotDate, source,
            x.ProductCode, x.SourceUid, x.BatchNumber, x.Ean, x.Quantity, x.UnitCost, x.TotalCost, x.Lineage.SheetName,
            x.Lineage.SourceRowNumber));
        return snapshots.Select((x, i) => x with { SourceReportCode = source, LineSeq = sequence.LineSeq[i] }).ToArray();
    }
}
