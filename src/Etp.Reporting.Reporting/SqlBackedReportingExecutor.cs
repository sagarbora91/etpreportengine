namespace Etp.Reporting.Reporting;

public sealed class SqlBackedReportingExecutor(
    IReportingQueryRepository repository,
    ApprovedReportingMapping mapping,
    ApprovedSalesReportingPolicy salesPolicy,
    ApprovedControlRule tenderRule,
    ApprovedStockControlRule stockRule)
{
    public async Task<SalesSummaryResult> ExecuteSalesSummaryAsync(
        ReportingQueryScope scope, SalesSummaryDimension dimension, CancellationToken cancellationToken = default)
    {
        Validate(scope);
        var rows = await repository.LoadSalesAsync(scope, cancellationToken);
        var projected = new List<SalesReportingLine>(rows.Count);
        var unknownRows = 0;
        foreach (var row in rows)
        {
            if (!TryClassify(row.SourceTransactionType, out var type)) { unknownRows++; continue; }
            if (!TryAmount(row, out var amount))
                return new(dimension, ReconciliationStatus.Blocked, [], salesPolicy.Version,
                    "The GST-inclusive amount is missing. Re-import the source export.");
            projected.Add(new(row.TransactionDate, row.StoreCode, row.DocumentNumber, row.LineIdentifier,
                row.Brand ?? string.Empty, row.BrandSegment ?? string.Empty, row.ProductCode,
                type, row.SourceQuantity, amount, row.InvoiceYear));
        }
        var result = new SalesReportingService().Summarize(projected, dimension, salesPolicy);
        return unknownRows == 0 ? result : result with { Message = $"Warning: skipped {unknownRows} rows with unknown transaction types. {result.Message}" };
    }

    public async Task<InvoiceTenderReconciliation> ExecuteTenderReconciliationAsync(
        ReportingQueryScope scope, CancellationToken cancellationToken = default)
    {
        Validate(scope);
        var controls = await repository.LoadInvoiceControlsAsync(scope, cancellationToken);
        var invoices = controls.Select(x => new InvoiceControlValue(x.StoreCode, x.DocumentNumber, x.SourceNetValue, x.InvoiceYear)).ToArray();
        var tenderRows = await repository.LoadTendersAsync(scope, cancellationToken);
        var tenders = tenderRows.Select(x => new TenderControlValue(x.StoreCode, x.DocumentNumber,
            x.TenderType, x.SourceAmount, Contains(mapping.TenderTypes, x.TenderType), x.InvoiceYear)).ToArray();
        var result = new InvoiceTenderReconciliationService().Reconcile(invoices, tenders, tenderRule);
        var modes = string.Join(" / ", tenderRows.GroupBy(x => x.TenderType, StringComparer.OrdinalIgnoreCase)
            .Select(x => new { Mode = x.Key, Total = x.Sum(t => t.SourceAmount) }).OrderByDescending(x => x.Total)
            .Select(x => $"{x.Mode}: {x.Total:N2}"));
        var gaps = await repository.LoadTenderCoverageGapsAsync(scope, cancellationToken);
        if (gaps.Count > 0)
        {
            // Blocked because the period is incomplete, but what the covered days showed is kept after the gap text:
            // the reconciliation's own message and, when it had failed, the failed-document count and variance.
            var failed = result.Status == ReconciliationStatus.Failed
                ? $" On the days that were reconciled, {result.Documents.Count(x => x.Status == ReconciliationStatus.Failed):N0} document(s) failed; variance {result.Variance:N2}."
                : string.Empty;
            return result with { Status = ReconciliationStatus.Blocked, Message = $"{DescribeTenderGaps(gaps)}{failed} {result.Message} Tender modes: {modes}" };
        }
        return result with { Message = $"{result.Message} Tender modes: {modes}" };
    }

    /// <summary>Names the uncovered dates so a missing R022 never passes as 0 against 0 (Titan audit FIX-07).</summary>
    public static string DescribeTenderGaps(IReadOnlyList<TenderCoverageGapRow> gaps)
    {
        const int shown = 10;
        var stores = gaps.GroupBy(x => x.StoreCode, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(store =>
        {
            var dates = store.Select(x => x.BusinessDate).Distinct().Order().ToArray();
            var listed = string.Join(", ", dates.Take(shown).Select(x => x.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)));
            return dates.Length > shown ? $"{store.Key}: {listed} and {dates.Length - shown:N0} more" : $"{store.Key}: {listed}";
        });
        return $"R022 missing / not imported for {gaps.Count:N0} store-day(s) with sales ({string.Join("; ", stores)}). Import the Revenue Report (R022) for these dates; tenders are not treated as zero.";
    }

    public async Task<StockReconciliationResult> ExecuteStockReconciliationAsync(
        ReportingQueryScope scope, CancellationToken cancellationToken = default)
    {
        Validate(scope);
        var data = await repository.LoadStockAsync(scope, cancellationToken);
        var coverage = LedgerCoverageWarning(data.LedgerCoverage, scope.DateTo);
        var missingClosing = data.Positions.Where(x => x.SourceClosingQuantity is null).Select(x => x.StoreCode)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingNote = missingClosing.Length == 0 ? null
            : $"Closing stock missing for {Day(scope.DateTo)} ({string.Join(", ", missingClosing)}); items are listed without a closing figure (no snapshot). Import the Closing Stock export of that date to check stock variance.";
        if (data.Positions.Any(x => x.SourceOpeningQuantity is null))
            return new(ReconciliationStatus.Blocked, [], stockRule.Version, Join(coverage, Join(missingNote,
                "The ledger opening could not be found for every stock key.")));
        if (missingNote is not null) return WithoutClosing(data, missingClosing, Join(coverage, missingNote));
        var result = Reconcile(data.Positions, data.Movements);
        // Titan report audit R-13: a ledger that stops before the To date misses the last movements, so every variance is suspect.
        // The items stay listed for review; the result is Blocked and says how far the ledger goes.
        if (coverage is not null) return result with { Status = ReconciliationStatus.Blocked, Message = Join(coverage, result.Message) };
        var quiet = QuietDaysNote(data.LedgerCoverage, scope.DateTo);
        return quiet is null ? result : result with { Message = $"{result.Message} {quiet}" };
    }

    private StockReconciliationResult Reconcile(IEnumerable<StockPositionQueryRow> positions, IEnumerable<StockMovementQueryRow> movements) =>
        new StockReconciliationService().Reconcile(
            positions.Select(x => new StockPositionValue(x.StoreCode, x.ItemCode, x.SourceOpeningQuantity!.Value, x.SourceClosingQuantity!.Value)),
            movements.Select(x => new StockMovementValue(x.StoreCode, x.ItemCode, x.SourceMovementType, x.SourceSignedQuantity,
                Contains(mapping.StockMovementTypes, x.SourceMovementType))),
            stockRule);

    // Owner answer Q9 (decision 13): a store with no closing-stock snapshot on the To date is marked, not hidden. Its items
    // are listed with opening, movements and expected closing, a blank ("no snapshot") reported closing and variance, and
    // status Blocked; stores that have a snapshot keep their PASS/FAIL check. The whole result is Blocked.
    private StockReconciliationResult WithoutClosing(StockQueryData data, IReadOnlyCollection<string> missingStores, string message)
    {
        var missing = missingStores.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unmarked = data.Positions.Where(x => !missing.Contains(x.StoreCode)).ToArray();
        var checkedPart = unmarked.Length == 0 ? null
            : Reconcile(unmarked, data.Movements.Where(x => !missing.Contains(x.StoreCode)));
        var movementTotals = data.Movements.Where(x => missing.Contains(x.StoreCode))
            .GroupBy(x => (Store: x.StoreCode.ToUpperInvariant(), Item: x.ItemCode.ToUpperInvariant()))
            .ToDictionary(x => x.Key, x => x.Sum(m => m.SourceSignedQuantity));
        var marked = data.Positions.Where(x => missing.Contains(x.StoreCode)).Select(x =>
        {
            var moved = movementTotals.GetValueOrDefault((x.StoreCode.ToUpperInvariant(), x.ItemCode.ToUpperInvariant()));
            var opening = x.SourceOpeningQuantity!.Value;
            return new StockControlResult(x.StoreCode, x.ItemCode, opening, moved, opening + moved, null, null, ReconciliationStatus.Blocked);
        });
        var items = (checkedPart?.Items ?? []).Concat(marked)
            .OrderBy(x => x.StoreCode, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(ReconciliationStatus.Blocked, items, stockRule.Version, checkedPart is null ? message : $"{message} {checkedPart.Message}");
    }

    // A ledger's stored end is its last movement (the import dates a ledger by its rows), so a gap before the To date
    // blocks only when it is shown to be short: no ledger at all, or a sale of the store after the ledger's last day.
    // A gap with no sale is taken as days without stock movement (a closed or quiet day) and only noted.
    private static bool IsShort(StockLedgerCoverageRow row, DateOnly dateTo) =>
        row.LedgerCoversTo is null || (row.LedgerCoversTo < dateTo && row.FirstSaleAfterLedger is not null);

    private static string? LedgerCoverageWarning(IReadOnlyList<StockLedgerCoverageRow>? coverage, DateOnly dateTo)
    {
        var uncovered = (coverage ?? []).Where(x => IsShort(x, dateTo))
            .OrderBy(x => x.StoreCode, StringComparer.OrdinalIgnoreCase).ToArray();
        if (uncovered.Length == 0) return null;
        var parts = uncovered.Select(x => x.LedgerCoversTo is { } covered
            ? $"Ledger covers to {Day(covered)} for {x.StoreCode}" + (x.FirstSaleAfterLedger is { } sale ? $" (sales on {Day(sale)} are not in it)" : "")
            : $"No stock ledger is imported for {x.StoreCode}");
        return $"{string.Join("; ", parts)}, before the To date {Day(dateTo)}. Movements after that are not in this check, so variances can be false. Import the stock ledger up to {Day(dateTo)}.";
    }

    private static string? QuietDaysNote(IReadOnlyList<StockLedgerCoverageRow>? coverage, DateOnly dateTo)
    {
        var quiet = (coverage ?? []).Where(x => x.LedgerCoversTo < dateTo && !IsShort(x, dateTo))
            .OrderBy(x => x.StoreCode, StringComparer.OrdinalIgnoreCase).ToArray();
        if (quiet.Length == 0) return null;
        return $"Last ledger movement {string.Join("; ", quiet.Select(x => $"{Day(x.LedgerCoversTo!.Value)} for {x.StoreCode}"))}; no sales after it to {Day(dateTo)}, so those days are taken as days without stock movement.";
    }

    private static string Join(string? warning, string message) => warning is null ? message : $"{warning} {message}";
    private static string Day(DateOnly date) => date.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private void Validate(ReportingQueryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Validate();
        mapping.Validate();
        salesPolicy.Validate();
        tenderRule.Validate();
        stockRule.Validate();
        if (!string.Equals(mapping.Version, salesPolicy.Version, StringComparison.Ordinal))
            throw new InvalidOperationException("Reporting mapping and sales policy versions must match.");
    }

    private bool TryClassify(string? sourceType, out ReportingTransactionType type)
    {
        type = ReportingTransactionType.Unknown;
        if (string.IsNullOrWhiteSpace(sourceType)) return false;
        foreach (var pair in mapping.SalesTransactionTypes)
            if (string.Equals(pair.Key, sourceType.Trim(), StringComparison.OrdinalIgnoreCase)) { type = pair.Value; return true; }
        return false;
    }

    private bool TryAmount(SalesQueryRow row, out decimal amount)
    {
        var value = mapping.SalesAmountSource == ApprovedSalesAmountSource.Net
            ? row.SourceNetAmount : row.SourceGrossAmount;
        amount = value.GetValueOrDefault();
        return value.HasValue;
    }

    private static bool Contains(IReadOnlySet<string> values, string value) =>
        values.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
}
