using System.Text.Json;
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
    DateTimeOffset GeneratedAtUtc,
    ReportPackDocument Document,
    int GenerationNumber,
    string ContentSha256);

public sealed class DailyReportingPackService(string connectionString)
{
    public async Task<ReportPackDocument> GenerateCombinedAsync(
        DateOnly businessDate,
        string? generatedBy = null,
        CancellationToken cancellationToken = default)
    {
        generatedBy = string.IsNullOrWhiteSpace(generatedBy) ? Environment.UserName : generatedBy.Trim();
        var packs = await Task.WhenAll(
            GenerateAsync("WLMHW", businessDate, generatedBy, cancellationToken),
            GenerateAsync("HEMW", businessDate, generatedBy, cancellationToken));
        var dsrDocument = await new OperationalReportRepository(connectionString).LoadDailySalesReportDocumentAsync(businessDate,cancellationToken);
        var overall = packs.Any(x => x.Status == ReconciliationStatus.Failed) ? ReconciliationStatus.Failed
            : packs.All(x => x.Status == ReconciliationStatus.Passed) ? ReconciliationStatus.Passed : ReconciliationStatus.NotRun;
        var message = overall == ReconciliationStatus.Passed
            ? "Titan World, Helios and combined controls are reconciled."
            : "The combined management pack retains every store-level warning, blocker and exact variance.";
        var controlRows = packs.SelectMany(pack => pack.Sections.Select(section =>
            (IReadOnlyList<object?>)[pack.StoreCode,section.Report,section.Status.ToString(),section.ControlTotal,section.Variance,section.Message])).ToArray();
        var tables = new List<ReportPackTable>
        {
            new("Combined Control Summary", overall.ToString(), message,
                new([new("Store"),new("Report"),new("Status"),new("Control Total","#,##0.00"),new("Variance","#,##0.00"),new("Message")], controlRows)),
            new("Titan Helios Combined DSR", overall.ToString(), "The same store matrices and combined values as the DSR screen.",EveningReportTables.Dsr(dsrDocument.EveningSheets))
        };
        foreach (var pack in packs)
            tables.AddRange(pack.Document.Tables.Skip(1).Select(table => table with { Name = $"{pack.StoreCode} {table.Name}" }));
        var document = new ReportPackDocument("ETP Complete Daily Management Pack — Titan World + Helios", businessDate, businessDate, overall.ToString(),
            RetailReportingPolicy.Version, message, DateTimeOffset.UtcNow, tables);
        var controlJson = JsonSerializer.Serialize(new
        {
            storeCode = "COMBINED",
            businessDate,
            status = overall,
            stores = packs.Select(x => new { x.StoreCode, x.GenerationNumber, x.ContentSha256 }),
            tableControls = document.Tables.Select(x => new { x.Name, x.Status, rowCount = x.Data.Rows.Count, x.Data.Totals })
        });
        await new OperationalCompletionRepository(connectionString).SaveReportGenerationAsync(
            "COMBINED", businessDate, generatedBy, controlJson, ReportPackArchiveCodec.Serialize(document), cancellationToken);
        return document;
    }

    public async Task<DailyReportPackResult> GenerateAsync(
        string storeCode,
        DateOnly businessDate,
        string? generatedBy = null,
        CancellationToken cancellationToken = default)
    {
        generatedBy = string.IsNullOrWhiteSpace(generatedBy) ? Environment.UserName : generatedBy.Trim();
        var scope = new ReportingQueryScope(businessDate, businessDate, [storeCode]);
        var operational = new OperationalReportRepository(connectionString);
        var workflow = await new DailyReportingWorkflowRepository(connectionString).LoadAsync(storeCode, businessDate, cancellationToken);
        var executor = new SqlBackedReportingExecutor(new SqlServerReportingQueryRepository(connectionString),
            RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);

        var invoice = await operational.LoadInvoiceSummaryAsync(scope, cancellationToken);
        var invoiceLineage = await operational.LoadInvoiceLineageAsync(scope, cancellationToken);
        var dsr = await operational.LoadDsrAsync(businessDate, [storeCode], cancellationToken);
        var tender = await executor.ExecuteTenderReconciliationAsync(scope, cancellationToken);
        var stock = await executor.ExecuteStockReconciliationAsync(scope, cancellationToken);
        var physicalStock = await operational.LoadBrandPhysicalStockAsync(storeCode, businessDate, cancellationToken);
        var staff = await operational.LoadStaffPerformanceAsync(scope, cancellationToken);
        var service = await operational.LoadServiceSalesAsync(businessDate, [storeCode], cancellationToken);
        var cash = await operational.LoadCashReconciliationAsync(storeCode, businessDate, cancellationToken);
        var cashBook=await operational.LoadCashBookAsync(storeCode,businessDate,businessDate,cancellationToken);
        var evening=await operational.LoadEveningSheetsAsync(businessDate,dsr,cancellationToken);
        var exceptions = await operational.LoadDailyExceptionsAsync(storeCode, businessDate, cancellationToken);
        var invoiceTotal = invoice.Sum(x => x.NetValue);
        var dsrFtd = dsr.SingleOrDefault(x => x.Period == "FTD" && string.Equals(x.Store, storeCode, StringComparison.OrdinalIgnoreCase));
        var serviceFtd = service.SingleOrDefault(x => x.Period == "FTD" && string.Equals(x.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase));
        var hasR025 = workflow.ImportedReports.Contains("R025", StringComparer.OrdinalIgnoreCase);
        var hasR013 = workflow.ImportedReports.Contains("R013", StringComparer.OrdinalIgnoreCase);
        var hasRevenue=workflow.ImportedReports.Contains("R022",StringComparer.OrdinalIgnoreCase);
        var dsrVariance = dsrFtd?.TySales is null || !hasRevenue ? null : tender.InvoiceTotal - dsrFtd.TySales;
        var dsrStatus = !hasR025 || !hasRevenue || dsrFtd?.TySales is null ? ReconciliationStatus.Blocked
            : dsrVariance != 0 ? ReconciliationStatus.Failed : ReconciliationStatus.Passed;
        var physicalMissing = physicalStock.Count(x => x.Status is not ("PASS" or "FAIL"));
        var physicalFailures = physicalStock.Count(x => x.Status == "FAIL");

        var sections = new List<DailyReportPackSection>
        {
            new("Customer-wise Invoice Sales", !hasR025 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed,
                hasR025 ? invoiceTotal : null, null,
                !hasR025 ? "R025 source is missing for this business date." : $"{invoice.Count:N0} invoices; zero sales remains distinct from a missing source. Customer names use active R024; unavailable names remain blank."),
            new("Daily Sales Report", dsrStatus, dsrFtd?.TySales, dsrVariance,
                "FTD DSR uses R025 and is compared independently with imported Revenue Report controls (R022). MTD/YTD use the selected business date."),
            new("Service Sale Report", serviceFtd?.Total is null ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed,
                serviceFtd?.Total, null, serviceFtd?.Total is null
                    ? "Enter separate service cash, card and UPI values; missing values are not converted to zero."
                    : "Service tender values are controlled operational facts and remain separate from retail sales."),
            new("Tender Reconciliation", tender.Status, tender.TenderTotal, tender.Variance, tender.Message),
            new("Daily Cash Reconciliation", cash.Status, cash.CalculatedClosing, cash.Variance, cash.Message),
            new("System Closing Stock", stock.Status, stock.Items.Count == 0 ? null : stock.Items.Sum(x => x.ReportedClosing),
                stock.Items.Count == 0 ? null : stock.Items.Sum(x => x.Variance), stock.Message),
            new("Physical Closing Stock", physicalFailures > 0 ? ReconciliationStatus.Failed : physicalMissing > 0 || physicalStock.Count==0 ? ReconciliationStatus.NotRun : ReconciliationStatus.Passed,
                physicalStock.Count==0||physicalMissing>0?null:physicalStock.Sum(x => x.ComponentTotal),
                physicalStock.Where(x => x.SystemVariance is not null).Sum(x => x.SystemVariance),
                physicalMissing == 0 ? "Physical is the sum of all four entered components; difference is Physical minus System."
                    : $"{physicalMissing:N0} brand(s) do not yet have a counted physical quantity; this remains visible without changing system stock."),
            new("Staff / CRO Performance", !hasR013 ? ReconciliationStatus.Blocked : staff.Status,
                hasR013 ? staff.AttributedSales : null, hasR013 ? staff.Variance : null,
                !hasR013 ? "R013 source is missing for this business date." : staff.Message),
            new("Manual Operational Inputs", workflow.MissingRequiredInputs.Count == 0 ? ReconciliationStatus.Passed : ReconciliationStatus.Blocked,
                workflow.ManualInputs.Count(x => x.IsPresent), workflow.MissingRequiredInputs.Count,
                workflow.MissingRequiredInputs.Count == 0 ? "Required manual inputs are complete." : $"Missing: {string.Join(", ", workflow.MissingRequiredInputs)}"),
            new("Exception / Reconciliation Report", exceptions.Any(x => x.Severity is "BLOCKER" or "FAIL") ? ReconciliationStatus.Failed : ReconciliationStatus.Passed,
                exceptions.Count, exceptions.Count(x => x.Variance is not null),
                exceptions.Count == 0 ? "No daily exceptions were found." : $"{exceptions.Count:N0} traceable exception(s) remain visible."),
            new("Business Date Finalisation", workflow.Status == DailyReadinessStatus.Locked ? ReconciliationStatus.Passed : ReconciliationStatus.NotRun,
                null, null, workflow.StatusMessage)
        };
        var status = sections.Any(x => x.Status is ReconciliationStatus.Blocked or ReconciliationStatus.Failed)
            ? ReconciliationStatus.Failed
            : sections.All(x => x.Status == ReconciliationStatus.Passed) ? ReconciliationStatus.Passed : ReconciliationStatus.NotRun;
        var message = status switch
        {
            ReconciliationStatus.Passed when workflow.Status == DailyReadinessStatus.Locked => "The complete daily reporting pack is reconciled and finalised.",
            ReconciliationStatus.Passed => "The complete daily reporting pack is reconciled and awaits finalisation.",
            ReconciliationStatus.Failed => "The pack was generated with visible missing sources or reconciliation exceptions.",
            _ => "The pack was generated and awaits finalisation."
        };
        var generatedAt = DateTimeOffset.UtcNow;
        var document = BuildDocument(storeCode, businessDate, status, message, generatedAt, sections, invoice, invoiceLineage, dsr,
            service, tender, cashBook, evening, stock, physicalStock, staff, exceptions, workflow);
        var controlJson = JsonSerializer.Serialize(new
        {
            storeCode,
            businessDate,
            status,
            sections,
            tableControls = document.Tables.Select(x => new { x.Name, x.Status, rowCount = x.Data.Rows.Count, x.Data.Totals })
        });
        var generation = await new OperationalCompletionRepository(connectionString).SaveReportGenerationAsync(
            storeCode, businessDate, generatedBy, controlJson, ReportPackArchiveCodec.Serialize(document), cancellationToken);
        return new(storeCode, businessDate, status, sections, message, generatedAt, document, generation.GenerationNumber, generation.ContentSha256);
    }

    private static ReportPackDocument BuildDocument(
        string storeCode,
        DateOnly businessDate,
        ReconciliationStatus status,
        string message,
        DateTimeOffset generatedAt,
        IReadOnlyList<DailyReportPackSection> sections,
        IReadOnlyList<InvoiceSalesSummaryRow> invoice,
        IReadOnlyList<InvoiceSalesLineageRow> invoiceLineage,
        IReadOnlyList<DsrManagementRow> dsr,
        IReadOnlyList<ServiceSalesRow> service,
        InvoiceTenderReconciliation tender,
        IReadOnlyList<CashBookDay> cashBook,
        IReadOnlyList<EveningStoreSheet> evening,
        StockReconciliationResult stock,
        IReadOnlyList<PhysicalStockReportRow> physicalStock,
        StaffPerformanceResult staff,
        IReadOnlyList<DailyExceptionRow> exceptions,
        DailyWorkflowSnapshot workflow)
    {
        var tables = new List<ReportPackTable>
        {
            new("Control Summary", status.ToString(), message,
                new([new("Report"),new("Status"),new("Control Total","#,##0.00"),new("Variance","#,##0.00"),new("Message")],
                    sections.Select(x => (IReadOnlyList<object?>)[x.Report,x.Status.ToString(),x.ControlTotal,x.Variance,x.Message]).ToArray(),
                    ["Overall",status.ToString(),null,null,message])),
            new("Invoice Summary", sections[0].Status.ToString(), sections[0].Message,
                new([new("Date"),new("Store"),new("Document"),new("Customer"),new("Transaction Type"),new("Quantity","#,##0.00"),new("Net Value","#,##0.00"),new("Source Rows","#,##0")],
                    invoice.Select(x => (IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.DocumentNumber,x.CustomerName,x.TransactionTypes,x.Quantity,x.NetValue,x.SourceRows]).ToArray(),
                    ["Total","","","","",invoice.Sum(x=>x.Quantity),invoice.Sum(x=>x.NetValue),invoice.Sum(x=>x.SourceRows)])),
            new("Invoice Lineage", sections[0].Status.ToString(), "Canonical line detail with source workbook, sheet and row; customer PII remains excluded.",
                new([new("Date"),new("Store"),new("Document"),new("Line"),new("Item"),new("Brand"),new("Segment"),new("Type"),new("Quantity","#,##0.00"),new("Net Value","#,##0.00"),new("CRO"),new("Workbook"),new("Sheet"),new("Source Row","#,##0")],
                    invoiceLineage.Select(x => (IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.DocumentNumber,x.LineIdentifier,x.ProductCode,x.Brand,x.BrandSegment,x.TransactionType,x.Quantity,x.NetValue,x.CroNumber,x.SourceWorkbook,x.SourceSheet,x.SourceRow]).ToArray(),
                    ["Total","","","","","","","",invoiceLineage.Sum(x=>x.Quantity),invoiceLineage.Sum(x=>x.NetValue),"","","",invoiceLineage.Count])),
            new("DSR", sections[1].Status.ToString(), sections[1].Message,EveningReportTables.Dsr(evening.Where(x=>x.StoreCode==storeCode).ToArray())),
            new("Service Sales", sections[2].Status.ToString(), sections[2].Message,
                new([new("Period"),new("Store"),new("From"),new("To"),new("WDC","#,##0.00"),new("Cash","#,##0.00"),new("Card","#,##0.00"),new("UPI","#,##0.00"),new("Total","#,##0.00"),new("LY Total","#,##0.00"),new("Growth %","0.00%"),new("Availability"),new("Missing days","#,##0"),new("LY missing days","#,##0")],
                    service.Select(x => (IReadOnlyList<object?>)[x.Period,x.StoreCode,x.PeriodStart,x.PeriodEnd,x.Wdc,x.Cash,x.Card,x.Upi,x.Total,x.LastYearTotal,x.GrowthPercent,x.Availability,x.MissingDays,x.LastYearMissingDays]).ToArray())),
            new("Tender Reconciliation", tender.Status.ToString(), tender.Message,
                new([new("Store"),new("Document"),new("Revenue","#,##0.00"),new("Tender","#,##0.00"),new("Variance","#,##0.00"),new("Status")],
                    tender.Documents.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.DocumentNumber,x.InvoiceAmount,x.TenderAmount,x.Variance,x.Status.ToString()]).ToArray(),
                    ["Total","",tender.InvoiceTotal,tender.TenderTotal,tender.Variance,tender.Status.ToString()])),
            new("Cash Book", sections[4].Status.ToString(), sections[4].Message,CashBookTables.Create(cashBook)),
            new("System Stock", stock.Status.ToString(), stock.Message,
                new([new("Store"),new("Item"),new("Opening","#,##0.00"),new("Movements","#,##0.00"),new("Expected","#,##0.00"),new("System Closing","#,##0.00"),new("Variance","#,##0.00"),new("Status")],
                    stock.Items.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.ItemCode,x.Opening,x.SourceSignedMovements,x.ExpectedClosing,x.ReportedClosing,x.Variance,x.Status.ToString()]).ToArray(),
                    ["Total","",stock.Items.Sum(x=>x.Opening),stock.Items.Sum(x=>x.SourceSignedMovements),stock.Items.Sum(x=>x.ExpectedClosing),stock.Items.Sum(x=>x.ReportedClosing),stock.Items.Sum(x=>x.Variance),stock.Status.ToString()])),
            new("Physical Stock", sections[6].Status.ToString(), sections[6].Message,
                new([new("Store"),new("Date"),new("Brand"),new("Display","#,##0.00"),new("Backstock","#,##0.00"),new("Defective","#,##0.00"),new("Y Location","#,##0.00"),new("Physical","#,##0.00"),new("System","#,##0.00"),new("System Variance","#,##0.00"),new("Remarks"),new("Status")],
                    physicalStock.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.BusinessDate,x.InventoryGroupCode,x.DisplayQuantity,x.BackstockQuantity,x.DefectiveQuantity,x.YLocationQuantity,x.ComponentTotal,x.SystemQuantity,x.SystemVariance,x.Remarks,x.Status]).ToArray(),
                    ["Total","","",physicalStock.Sum(x=>x.DisplayQuantity),physicalStock.Sum(x=>x.BackstockQuantity),physicalStock.Sum(x=>x.DefectiveQuantity),physicalStock.Sum(x=>x.YLocationQuantity),physicalStock.All(x=>x.ComponentTotal!=null)?physicalStock.Sum(x=>x.ComponentTotal):null,physicalStock.Sum(x=>x.SystemQuantity),physicalStock.All(x=>x.SystemVariance!=null)?physicalStock.Sum(x=>x.SystemVariance):null,"",sections[6].Status.ToString()])),
            new("Staff Performance", sections[7].Status.ToString(), sections[7].Message,
                new([new("Store"),new("CRO"),new("CRO name"),new("Value incl. GST","#,##0.00"),new("LY Sales","#,##0.00"),new("Growth %","0.00%"),new("Growth Status"),new("Net Quantity","#,##0.00"),new("Discount","#,##0.00"),new("Unique invoices","#,##0"),new("AUPT","#,##0.00"),new("ATV","#,##0.00"),new("Contribution %","0.00%"),new("Target","#,##0.00"),new("Achievement %","0.00%"),new("Rank","#,##0")],
                    staff.Rows.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.CroNumber,x.CroName,x.NetSales,x.LastYearSales,x.GrowthPercent,x.GrowthStatus,x.NetQuantity,x.Discount,x.Transactions,x.Upt,x.Atv,x.ContributionPercent,x.TargetSales,x.TargetAchievementPercent,x.Rank]).ToArray(),
                    ["Control","","",staff.AttributedSales,"","","","","",staff.Rows.Sum(x=>x.Transactions),"","",staff.Variance,"","",""])),
            new("Exceptions", sections[9].Status.ToString(), sections[9].Message,
                new([new("Severity"),new("Area"),new("Code"),new("Store"),new("Date"),new("Document"),new("Item"),new("Variance","#,##0.00"),new("Workbook"),new("Sheet"),new("Source Row","#,##0"),new("Message"),new("Recommended Action")],
                    exceptions.Select(x => (IReadOnlyList<object?>)[x.Severity,x.Area,x.Code,x.StoreCode,x.BusinessDate,x.DocumentNumber,x.ItemCode,x.Variance,x.SourceWorkbook,x.SourceSheet,x.SourceRow,x.Message,x.RecommendedAction]).ToArray(),
                    ["Total",exceptions.Count,"","","","","",exceptions.Where(x=>x.Variance is not null).Sum(x=>x.Variance),"","","","",""])),
            new("Manual Inputs", sections[8].Status.ToString(), sections[8].Message,
                new([new("Field"),new("Display Name"),new("Kind"),new("Numeric Value","#,##0.00"),new("Text Value"),new("Required"),new("Present"),new("Modified UTC"),new("Modified By")],
                    workflow.ManualInputs.Select(x => (IReadOnlyList<object?>)[x.FieldCode,x.DisplayName,x.ValueKind,x.NumericValue,x.TextValue,x.IsRequired,x.IsPresent,x.ModifiedUtc,x.ModifiedBy]).ToArray()))
        };
        return new($"ETP Daily Reporting Pack — {storeCode}", businessDate, businessDate, status.ToString(), RetailReportingPolicy.Version,
            message, generatedAt, tables);
    }
}
