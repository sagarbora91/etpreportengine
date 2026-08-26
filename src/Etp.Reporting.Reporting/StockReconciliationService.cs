namespace Etp.Reporting.Reporting;

public sealed record StockPositionValue(
    string StoreCode, string ItemCode, decimal SourceOpeningQuantity, decimal SourceReportedClosingQuantity);
public sealed record StockMovementValue(
    string StoreCode, string ItemCode, string MovementType, decimal SourceSignedQuantity, bool IsRecognizedType);
public sealed record ApprovedStockControlRule(string Version, decimal AbsoluteQuantityTolerance)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version)) throw new ArgumentException("An approved rule version is required.");
        if (AbsoluteQuantityTolerance < 0) throw new ArgumentOutOfRangeException(nameof(AbsoluteQuantityTolerance));
    }
}
public sealed record StockControlResult(
    string StoreCode, string ItemCode, decimal Opening, decimal SourceSignedMovements,
    decimal ExpectedClosing, decimal ReportedClosing, decimal Variance, ReconciliationStatus Status);
public sealed record StockReconciliationResult(
    ReconciliationStatus Status, IReadOnlyList<StockControlResult> Items, string RuleVersion, string Message);

public sealed class StockReconciliationService
{
    public StockReconciliationResult Reconcile(
        IEnumerable<StockPositionValue> positions,
        IEnumerable<StockMovementValue> movements,
        ApprovedStockControlRule rule)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(movements);
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();
        var positionRows = positions.ToArray();
        var movementRows = movements.ToArray();
        if (positionRows.Any(x => Missing(x.StoreCode) || Missing(x.ItemCode)) ||
            movementRows.Any(x => Missing(x.StoreCode) || Missing(x.ItemCode) || Missing(x.MovementType) || !x.IsRecognizedType))
            return new(ReconciliationStatus.Blocked, [], rule.Version,
                "Missing identifiers or unknown movement types prevent stock reconciliation.");

        var duplicatePosition = positionRows.GroupBy(Key).FirstOrDefault(x => x.Count() > 1);
        if (duplicatePosition is not null)
            return new(ReconciliationStatus.Blocked, [], rule.Version,
                "More than one opening/closing position exists for a store and item.");

        var positionKeys = positionRows.Select(Key).ToHashSet();
        if (movementRows.Any(x => !positionKeys.Contains(Key(x))))
            return new(ReconciliationStatus.Blocked, [], rule.Version,
                "A stock movement has no matching opening/closing position.");

        var movementMap = movementRows.GroupBy(Key)
            .ToDictionary(x => x.Key, x => x.Sum(v => v.SourceSignedQuantity));
        var items = positionRows.OrderBy(x => x.StoreCode).ThenBy(x => x.ItemCode).Select(position =>
        {
            var movement = movementMap.GetValueOrDefault(Key(position));
            var expected = position.SourceOpeningQuantity + movement;
            var variance = expected - position.SourceReportedClosingQuantity;
            return new StockControlResult(position.StoreCode, position.ItemCode, position.SourceOpeningQuantity,
                movement, expected, position.SourceReportedClosingQuantity, variance,
                Math.Abs(variance) <= rule.AbsoluteQuantityTolerance ? ReconciliationStatus.Passed : ReconciliationStatus.Failed);
        }).ToArray();
        var status = items.All(x => x.Status == ReconciliationStatus.Passed)
            ? ReconciliationStatus.Passed : ReconciliationStatus.Failed;
        return new(status, items, rule.Version,
            "For products present in both the ledger period and closing snapshot, expected closing equals the first ledger opening plus source-signed movements.");
    }

    private static (string StoreCode, string ItemCode) Key(StockPositionValue x) => (x.StoreCode, x.ItemCode);
    private static (string StoreCode, string ItemCode) Key(StockMovementValue x) => (x.StoreCode, x.ItemCode);
    private static bool Missing(string value) => string.IsNullOrWhiteSpace(value);
}
