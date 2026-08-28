namespace Etp.Reporting.Application.Reports;

public enum ReportStatus { NotRun, Passed, Failed, Blocked }
public enum ReportSalesDimension { Daily, Store, Brand, BrandSegment, Item, Returns }

public sealed record ReportScope(
    DateOnly DateFrom,
    DateOnly DateTo,
    IReadOnlyList<string>? StoreCodes = null,
    IReadOnlyList<string>? BrandSegments = null,
    IReadOnlyList<string>? TransactionTypes = null,
    IReadOnlyList<string>? ItemCodes = null);

public sealed record SalesSummaryRecord(string Key, decimal SourceSignedQuantity, decimal SourceSignedNetAmount, int DistinctInvoices);
public sealed record SalesSummaryReport(ReportSalesDimension Dimension, ReportStatus Status, IReadOnlyList<SalesSummaryRecord> Rows, string PolicyVersion, string Message);
public sealed record TenderDocumentRecord(string StoreCode, string DocumentNumber, decimal InvoiceAmount, decimal TenderAmount, decimal Variance, ReportStatus Status);
public sealed record TenderReconciliationReport(ReportStatus Status, IReadOnlyList<TenderDocumentRecord> Documents, decimal InvoiceTotal, decimal TenderTotal, decimal Variance, string RuleVersion, string Message);
public sealed record StockControlRecord(string StoreCode, string ItemCode, decimal Opening, decimal SourceSignedMovements, decimal ExpectedClosing, decimal ReportedClosing, decimal Variance, ReportStatus Status);
public sealed record StockReconciliationReport(ReportStatus Status, IReadOnlyList<StockControlRecord> Items, string RuleVersion, string Message);
public sealed record StockMovementRecord(string StoreCode, string ItemCode, string SourceMovementType, decimal SourceSignedQuantity);

public sealed record InvoiceSummaryRecord(DateOnly BusinessDate, string StoreCode, string DocumentNumber, string TransactionTypes, decimal Quantity, decimal NetValue, int SourceRows);
public sealed record InvoiceLineageRecord(DateOnly BusinessDate, string StoreCode, string DocumentNumber, string LineIdentifier, string ProductCode, string? Brand, string? BrandSegment, string? TransactionType, decimal Quantity, decimal? NetValue, string? CroNumber, string SourceWorkbook, string SourceSheet, int SourceRow);
public sealed record DsrManagementRecord(string Period, string Store, DateOnly PeriodStart, DateOnly PeriodEnd, decimal? TySales, decimal? LySales, decimal? GrowthPercent, string GrowthStatus, decimal? TyUnits, decimal? LyUnits, int? TyInvoices, int? LyInvoices, decimal? Upt, decimal? Atv, decimal? WalkIns, decimal? ConversionPercent, string MetricPolicy);
public sealed record StaffPerformanceRecord(string StoreCode, string CroNumber, decimal NetSales, decimal? LastYearSales, decimal? GrowthPercent, string GrowthStatus, decimal NetQuantity, decimal Discount, int Transactions, decimal? Upt, decimal? Atv, decimal ContributionPercent, decimal? TargetSales, decimal? TargetAchievementPercent, int Rank);
public sealed record StaffPerformanceReport(IReadOnlyList<StaffPerformanceRecord> Rows, decimal CanonicalSales, decimal AttributedSales, decimal Variance, ReportStatus Status, string Message, string MetricPolicy);
public sealed record PhysicalStockRecord(string StoreCode, DateOnly BusinessDate, string InventoryGroupCode, decimal? DisplayQuantity, decimal? BackstockQuantity, decimal? DefectiveQuantity, decimal? YLocationQuantity, decimal? ComponentTotal, decimal? CountedPhysicalQuantity, decimal? CompositionVariance, decimal SystemQuantity, decimal? SystemVariance, string? Remarks, string Status);
public sealed record StockInventoryRecord(DateOnly SnapshotDate, string StoreCode, string ProductCode, string? Brand, string? InventoryGroup, decimal Quantity, decimal? UnitCost, decimal? TotalCost, DateOnly? LastSaleDate, int? DaysSinceLastSale, string MovementStatus);
public sealed record DailyExceptionRecord(string Severity, string Area, string Code, string StoreCode, DateOnly BusinessDate, string? DocumentNumber, string? ItemCode, decimal? Variance, string? SourceWorkbook, string? SourceSheet, int? SourceRow, string Message, string RecommendedAction);
public sealed record ServiceSalesRecord(string Period, string StoreCode, DateOnly PeriodStart, DateOnly PeriodEnd, decimal? Cash, decimal? Card, decimal? Upi, decimal? Total, decimal? LastYearTotal, decimal? GrowthPercent, string Availability);
public sealed record CashReconciliationReport(string StoreCode, DateOnly BusinessDate, decimal? OpeningCash, decimal RetailCash, decimal? ServiceCash, decimal? Expenses, decimal? CashDeposit, decimal? Adjustment, decimal? CalculatedClosing, decimal? CountedClosing, decimal? Variance, ReportStatus Status, string Message);
public sealed record ManagementTrendRecord(DateOnly BusinessDate, string StoreCode, decimal NetSales, decimal Units, int Invoices, decimal TenderVariance, int UnmatchedEnrichmentRows);
public enum TenderVarianceCause { Matched, MissingTender, PartialTender, ExcessTender, TenderWithoutInvoice }
public sealed record TenderVarianceDiagnosticRecord(string StoreCode, string DocumentNumber, decimal InvoiceAmount, decimal TenderAmount, decimal Variance, TenderVarianceCause LikelyCause, string RecommendedCheck);
public sealed record TenderVarianceDiagnosticReport(ReportStatus Status, IReadOnlyList<TenderVarianceDiagnosticRecord> Rows, int FailedDocuments, decimal AbsoluteVariance, string RuleVersion, string Message);

public interface IControlledReportQuery
{
    Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default);
    Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default);
    Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default);
}

public interface IOperationalReportQuery<TDsrDocument> where TDsrDocument : notnull
{
    Task<IReadOnlyList<InvoiceSummaryRecord>> LoadInvoiceSummaryAsync(ReportScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceLineageRecord>> LoadInvoiceLineageAsync(ReportScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DsrManagementRecord>> LoadDsrAsync(DateOnly businessDate, IReadOnlyList<string> storeCodes, CancellationToken cancellationToken = default);
    Task<TDsrDocument> ComposeDsrDocumentAsync(DateOnly businessDate, IReadOnlyList<DsrManagementRecord> rows, CancellationToken cancellationToken = default);
    Task<StaffPerformanceReport> LoadStaffPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceSalesRecord>> LoadServiceSalesAsync(DateOnly businessDate, IReadOnlyList<string>? storeCodes = null, CancellationToken cancellationToken = default);
    Task<CashReconciliationReport> LoadCashReconciliationAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhysicalStockRecord>> LoadPhysicalStockAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockInventoryRecord>> LoadStockInventoryAsync(ReportScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyExceptionRecord>> LoadDailyExceptionsAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default);
}

public interface IManagementTrendQuery
{
    Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default);
}

public interface ITenderVarianceDiagnostic
{
    TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance);
}
