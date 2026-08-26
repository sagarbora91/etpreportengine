using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record DailyReportPackSection(
    string Report,
    ReconciliationStatus Status,
    decimal? ControlTotal,
    decimal? Variance,
    string Message);

public sealed record DailyReportPackResult(
    string StoreCode,
    DateOnly BusinessDate,
    ReconciliationStatus Status,
    IReadOnlyList<DailyReportPackSection> Sections,
    string Message,
    DateTimeOffset GeneratedAtUtc);

public sealed class DailyReportingPackService(string connectionString)
{
    public async Task<DailyReportPackResult> GenerateAsync(
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        var scope = new ReportingQueryScope(businessDate, businessDate, [storeCode]);
        var operational = new OperationalReportRepository(connectionString);
        var workflow = await new DailyReportingWorkflowRepository(connectionString).LoadAsync(storeCode, businessDate, cancellationToken);
        var executor = new SqlBackedReportingExecutor(new SqlServerReportingQueryRepository(connectionString),
            RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);

        var invoice = await operational.LoadInvoiceSummaryAsync(scope, cancellationToken);
        var dsr = await operational.LoadDsrAsync(businessDate, [storeCode], cancellationToken);
        var tender = await executor.ExecuteTenderReconciliationAsync(scope, cancellationToken);
        var stock = await executor.ExecuteStockReconciliationAsync(scope, cancellationToken);
        var staff = await operational.LoadStaffPerformanceAsync(scope, cancellationToken);
        var service = await operational.LoadServiceSalesAsync(businessDate, [storeCode], cancellationToken);
        var cash = await operational.LoadCashReconciliationAsync(storeCode, businessDate, cancellationToken);
        var invoiceTotal = invoice.Sum(x => x.NetValue);
        var dsrFtd = dsr.SingleOrDefault(x => x.Period == "FTD" && x.Store == storeCode);
        var serviceFtd = service.SingleOrDefault(x => x.Period == "FTD" && x.StoreCode == storeCode);

        var sections = new List<DailyReportPackSection>
        {
            new("Customer-safe Invoice Sales Summary", invoice.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed,
                invoice.Count == 0 ? null : invoiceTotal, null,
                invoice.Count == 0 ? "R025 source is missing for this business date." : $"{invoice.Count:N0} invoices; customer PII is excluded."),
            new("Daily Sales Report", dsrFtd?.TySales is null ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed,
                dsrFtd?.TySales, dsrFtd?.TySales is null ? null : invoiceTotal - dsrFtd.TySales,
                "FTD/MTD/YTD and LY use the selected ETP business date."),
            new("Service Sale Report", serviceFtd?.Total is null ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed,
                serviceFtd?.Total, null, serviceFtd?.Total is null
                    ? "Enter separate service cash, card and UPI values; missing values are not converted to zero."
                    : "Service tender values are stored as manual operational facts and remain separate from retail sales."),
            new("Tender / Cash Reconciliation", tender.Status, tender.TenderTotal, tender.Variance,
                tender.Message),
            new("Daily Cash Reconciliation", cash.Status, cash.CalculatedClosing, cash.Variance, cash.Message),
            new("Closing Stock", stock.Status, stock.Items.Count == 0 ? null : stock.Items.Sum(x => x.ReportedClosing),
                stock.Items.Count == 0 ? null : stock.Items.Sum(x => x.Variance), stock.Message),
            new("Staff / CRO Performance", staff.Rows.Count == 0 ? ReconciliationStatus.Blocked : staff.Status,
                staff.AttributedSales, staff.Variance, staff.Message),
            new("Manual Operational Inputs", workflow.MissingRequiredInputs.Count == 0 ? ReconciliationStatus.Passed : ReconciliationStatus.Blocked,
                workflow.ManualInputs.Count(x => x.IsPresent), workflow.MissingRequiredInputs.Count,
                workflow.MissingRequiredInputs.Count == 0 ? "Required manual inputs are complete." : $"Missing: {string.Join(", ", workflow.MissingRequiredInputs)}"),
            new("Business Date Finalisation", workflow.Status == DailyReadinessStatus.Locked ? ReconciliationStatus.Passed : ReconciliationStatus.NotRun,
                null, null, workflow.StatusMessage)
        };
        var status = sections.Any(x => x.Status is ReconciliationStatus.Blocked or ReconciliationStatus.Failed)
            ? ReconciliationStatus.Failed
            : sections.All(x => x.Status == ReconciliationStatus.Passed) ? ReconciliationStatus.Passed : ReconciliationStatus.NotRun;
        var message = status switch
        {
            ReconciliationStatus.Passed => "The complete daily reporting pack is reconciled and finalised.",
            ReconciliationStatus.Failed => "The pack was generated with visible missing sources or reconciliation exceptions.",
            _ => "The pack was generated and awaits finalisation."
        };
        return new(storeCode, businessDate, status, sections, message, DateTimeOffset.UtcNow);
    }
}
