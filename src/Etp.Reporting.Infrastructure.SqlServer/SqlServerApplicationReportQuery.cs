using Etp.Reporting.Application.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerApplicationReportQuery :
    IControlledReportQuery,
    IOperationalReportQuery<DailySalesReportDocument>,
    IManagementTrendQuery
{
    private readonly SqlBackedReportingExecutor executor;
    private readonly SqlServerReportingQueryRepository raw;
    private readonly OperationalReportRepository operational;
    private readonly Phase2OperationsRepository management;

    public SqlServerApplicationReportQuery(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        raw = new(connectionString);
        operational = new(connectionString);
        management = new(connectionString);
        executor = new(raw, RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
    }

    public async Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default) =>
        Map(await executor.ExecuteSalesSummaryAsync(ToScope(scope), ToDimension(dimension), cancellationToken).ConfigureAwait(false));

    public async Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
        Map(await executor.ExecuteTenderReconciliationAsync(ToScope(scope), cancellationToken).ConfigureAwait(false));

    public async Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
        Map(await executor.ExecuteStockReconciliationAsync(ToScope(scope), cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default)
    {
        var data = await raw.LoadStockAsync(ToScope(scope), cancellationToken).ConfigureAwait(false);
        return data.Movements.Select(row => new StockMovementRecord(row.StoreCode, row.ItemCode, row.SourceMovementType, row.SourceSignedQuantity)).ToArray();
    }

    public async Task<IReadOnlyList<InvoiceSummaryRecord>> LoadInvoiceSummaryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
        (await operational.LoadInvoiceSummaryAsync(ToScope(scope), cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public async Task<IReadOnlyList<InvoiceLineageRecord>> LoadInvoiceLineageAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
        (await operational.LoadInvoiceLineageAsync(ToScope(scope), cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public async Task<IReadOnlyList<DsrManagementRecord>> LoadDsrAsync(DateOnly businessDate, IReadOnlyList<string> storeCodes, CancellationToken cancellationToken = default) =>
        (await operational.LoadDsrAsync(businessDate, storeCodes, cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public Task<DailySalesReportDocument> ComposeDsrDocumentAsync(DateOnly businessDate, IReadOnlyList<DsrManagementRecord> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return operational.ComposeDailySalesReportDocumentAsync(businessDate, rows.Select(ToInfrastructure).ToArray(), cancellationToken);
    }

    public async Task<StaffPerformanceReport> LoadStaffPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
        Map(await operational.LoadStaffPerformanceAsync(ToScope(scope), cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<ServiceSalesRecord>> LoadServiceSalesAsync(DateOnly businessDate, IReadOnlyList<string>? storeCodes = null, CancellationToken cancellationToken = default) =>
        (await operational.LoadServiceSalesAsync(businessDate, storeCodes, cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public async Task<CashReconciliationReport> LoadCashReconciliationAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        Map(await operational.LoadCashReconciliationAsync(storeCode, businessDate, cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<PhysicalStockRecord>> LoadPhysicalStockAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        (await operational.LoadPhysicalStockAsync(storeCode, businessDate, cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public async Task<IReadOnlyList<StockInventoryRecord>> LoadStockInventoryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
        (await operational.LoadStockInventoryAsync(ToScope(scope), cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public async Task<IReadOnlyList<DailyExceptionRecord>> LoadDailyExceptionsAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) =>
        (await operational.LoadDailyExceptionsAsync(storeCode, businessDate, cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public async Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var rows = await management.LoadManagementTrendAsync(scope.DateFrom, scope.DateTo, cancellationToken).ConfigureAwait(false);
        if (scope.StoreCodes is { Count: > 0 })
            rows = rows.Where(row => scope.StoreCodes.Contains(row.StoreCode, StringComparer.OrdinalIgnoreCase)).ToArray();
        return rows.Select(Map).ToArray();
    }

    public static ReportingQueryScope ToScope(ReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new(scope.DateFrom, scope.DateTo, scope.StoreCodes, scope.BrandSegments, scope.TransactionTypes, scope.ItemCodes);
    }

    public static ReportStatus Map(ReconciliationStatus status) => status switch
    {
        ReconciliationStatus.Passed => ReportStatus.Passed,
        ReconciliationStatus.Failed => ReportStatus.Failed,
        ReconciliationStatus.Blocked => ReportStatus.Blocked,
        _ => ReportStatus.NotRun
    };

    public static SalesSummaryReport Map(SalesSummaryResult source) => new(ToDimension(source.Dimension), Map(source.Status), source.Rows.Select(row => new SalesSummaryRecord(row.Key, row.SourceSignedQuantity, row.SourceSignedNetAmount, row.DistinctInvoices)).ToArray(), source.PolicyVersion, source.Message);
    public static TenderReconciliationReport Map(InvoiceTenderReconciliation source) => new(Map(source.Status), source.Documents.Select(row => new TenderDocumentRecord(row.StoreCode, row.DocumentNumber, row.InvoiceAmount, row.TenderAmount, row.Variance, Map(row.Status))).ToArray(), source.InvoiceTotal, source.TenderTotal, source.Variance, source.RuleVersion, source.Message);
    public static StockReconciliationReport Map(StockReconciliationResult source) => new(Map(source.Status), source.Items.Select(row => new StockControlRecord(row.StoreCode, row.ItemCode, row.Opening, row.SourceSignedMovements, row.ExpectedClosing, row.ReportedClosing, row.Variance, Map(row.Status))).ToArray(), source.RuleVersion, source.Message);
    public static InvoiceSummaryRecord Map(InvoiceSalesSummaryRow row) => new(row.BusinessDate, row.StoreCode, row.DocumentNumber, row.TransactionTypes, row.Quantity, row.NetValue, row.SourceRows);
    public static InvoiceLineageRecord Map(InvoiceSalesLineageRow row) => new(row.BusinessDate, row.StoreCode, row.DocumentNumber, row.LineIdentifier, row.ProductCode, row.Brand, row.BrandSegment, row.TransactionType, row.Quantity, row.NetValue, row.CroNumber, row.SourceWorkbook, row.SourceSheet, row.SourceRow);
    public static DsrManagementRecord Map(DsrManagementRow row) => new(row.Period, row.Store, row.PeriodStart, row.PeriodEnd, row.TySales, row.LySales, row.GrowthPercent, row.GrowthStatus, row.TyUnits, row.LyUnits, row.TyInvoices, row.LyInvoices, row.Upt, row.Atv, row.WalkIns, row.ConversionPercent, row.MetricPolicy);
    public static StaffPerformanceReport Map(StaffPerformanceResult source) => new(source.Rows.Select(row => new StaffPerformanceRecord(row.StoreCode, row.CroNumber, row.NetSales, row.LastYearSales, row.GrowthPercent, row.GrowthStatus, row.NetQuantity, row.Discount, row.Transactions, row.Upt, row.Atv, row.ContributionPercent, row.TargetSales, row.TargetAchievementPercent, row.Rank)).ToArray(), source.CanonicalSales, source.AttributedSales, source.Variance, Map(source.Status), source.Message, source.MetricPolicy);
    public static ServiceSalesRecord Map(ServiceSalesRow row) => new(row.Period, row.StoreCode, row.PeriodStart, row.PeriodEnd, row.Cash, row.Card, row.Upi, row.Total, row.LastYearTotal, row.GrowthPercent, row.Availability);
    public static CashReconciliationReport Map(CashReconciliationResult row) => new(row.StoreCode, row.BusinessDate, row.OpeningCash, row.RetailCash, row.ServiceCash, row.Expenses, row.CashDeposit, row.Adjustment, row.CalculatedClosing, row.CountedClosing, row.Variance, Map(row.Status), row.Message);
    public static PhysicalStockRecord Map(PhysicalStockReportRow row) => new(row.StoreCode, row.BusinessDate, row.InventoryGroupCode, row.DisplayQuantity, row.BackstockQuantity, row.DefectiveQuantity, row.YLocationQuantity, row.ComponentTotal, row.CountedPhysicalQuantity, row.CompositionVariance, row.SystemQuantity, row.SystemVariance, row.Remarks, row.Status);
    public static StockInventoryRecord Map(StockInventoryReportRow row) => new(row.SnapshotDate, row.StoreCode, row.ProductCode, row.Brand, row.InventoryGroup, row.Quantity, row.UnitCost, row.TotalCost, row.LastSaleDate, row.DaysSinceLastSale, row.MovementStatus);
    public static DailyExceptionRecord Map(DailyExceptionRow row) => new(row.Severity, row.Area, row.Code, row.StoreCode, row.BusinessDate, row.DocumentNumber, row.ItemCode, row.Variance, row.SourceWorkbook, row.SourceSheet, row.SourceRow, row.Message, row.RecommendedAction);
    public static ManagementTrendRecord Map(ManagementTrendRow row) => new(row.BusinessDate, row.StoreCode, row.NetSales, row.Units, row.Invoices, row.TenderVariance, row.UnmatchedEnrichmentRows);

    private static DsrManagementRow ToInfrastructure(DsrManagementRecord row) => new(row.Period, row.Store, row.PeriodStart, row.PeriodEnd, row.TySales, row.LySales, row.GrowthPercent, row.GrowthStatus, row.TyUnits, row.LyUnits, row.TyInvoices, row.LyInvoices, row.Upt, row.Atv, row.WalkIns, row.ConversionPercent, row.MetricPolicy);
    private static SalesSummaryDimension ToDimension(ReportSalesDimension dimension) => dimension switch
    {
        ReportSalesDimension.Daily => SalesSummaryDimension.Daily,
        ReportSalesDimension.Store => SalesSummaryDimension.Store,
        ReportSalesDimension.Brand => SalesSummaryDimension.Brand,
        ReportSalesDimension.BrandSegment => SalesSummaryDimension.BrandSegment,
        ReportSalesDimension.Item => SalesSummaryDimension.Item,
        ReportSalesDimension.Returns => SalesSummaryDimension.Returns,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unsupported sales-summary dimension.")
    };

    private static ReportSalesDimension ToDimension(SalesSummaryDimension dimension) => dimension switch
    {
        SalesSummaryDimension.Daily => ReportSalesDimension.Daily,
        SalesSummaryDimension.Store => ReportSalesDimension.Store,
        SalesSummaryDimension.Brand => ReportSalesDimension.Brand,
        SalesSummaryDimension.BrandSegment => ReportSalesDimension.BrandSegment,
        SalesSummaryDimension.Item => ReportSalesDimension.Item,
        SalesSummaryDimension.Returns => ReportSalesDimension.Returns,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unsupported sales-summary dimension.")
    };
}
