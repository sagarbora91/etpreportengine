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
        return result with { Message = $"{result.Message} Tender modes: {modes}" };
    }

    public async Task<StockReconciliationResult> ExecuteStockReconciliationAsync(
        ReportingQueryScope scope, CancellationToken cancellationToken = default)
    {
        Validate(scope);
        var data = await repository.LoadStockAsync(scope, cancellationToken);
        if (data.Positions.Any(x => x.SourceOpeningQuantity is null || x.SourceClosingQuantity is null))
            return new(ReconciliationStatus.Blocked, [], stockRule.Version,
                "Both opening and closing snapshots are required for each stock key.");
        var positions = data.Positions.Select(x => new StockPositionValue(x.StoreCode, x.ItemCode,
            x.SourceOpeningQuantity!.Value, x.SourceClosingQuantity!.Value)).ToArray();
        var movements = data.Movements.Select(x => new StockMovementValue(x.StoreCode, x.ItemCode,
            x.SourceMovementType, x.SourceSignedQuantity, Contains(mapping.StockMovementTypes, x.SourceMovementType))).ToArray();
        return new StockReconciliationService().Reconcile(positions, movements, stockRule);
    }

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
