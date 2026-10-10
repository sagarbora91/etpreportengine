extern alias EtpApplication;

using EtpApplication::Etp.Reporting.Application.Reports;

namespace Etp.Reporting.Desktop;

/// <summary>One on-screen column of a report row type: the property, the header the Excel export uses for the same
/// value, and whether the value is money (₹) rather than a quantity, count or percentage.</summary>
public sealed record ReportGridColumn(string Property, string Header, bool Money = false);

/// <summary>
/// Report audit of 9 Oct 2026 (RA-EXPORT-06/07/08, RA-SALES-06, RA-UI-14/15/16/18, RA-OPS-16): the detail grid and the
/// row-details window show the headers of the Excel export in the export's column order, instead of the C# property
/// names split on capitals ("Source Signed Net Amount", "Cro Number", "Upi", "Wdc"). Each list is the export column
/// list of the matching <c>SetExport</c> call in <see cref="Modules.Reports.ReportsWorkspaceView"/>; a column the
/// export omits but the screen keeps (counted physical stock, transaction types, the stock brand row) is named here
/// too. Properties not listed are internal (ids, InvoiceYear, SourceRows) and stay hidden. Money is explicit: the stock
/// Opening/Closing/Component Total figures are quantities and never carry ₹.
/// </summary>
public static class ReportGridColumns
{
    private static readonly Dictionary<Type, IReadOnlyList<ReportGridColumn>> Maps = new()
    {
        [typeof(SalesSummaryRecord)] =
        [
            new("Key", "Group"), new("SourceSignedQuantity", "Units"), new("SourceSignedNetAmount", "Net Sales", true),
            new("Invoices", Modules.Reports.ReportsWorkspaceView.InvoicesHeader), new("Returns", Modules.Reports.ReportsWorkspaceView.ReturnsHeader)
        ],
        [typeof(InvoiceSummaryRecord)] =
        [
            new("BusinessDate", "Date"), new("StoreCode", "Store"), new("DocumentNumber", "Invoice"), new("TransactionTypes", "Transaction types"),
            new("CustomerName", "Customer name"), new("Quantity", "Invoice quantity"), new("NetValue", "Value incl. GST", true)
        ],
        [typeof(InvoiceLineageRecord)] =
        [
            new("BusinessDate", "Business Date"), new("StoreCode", "Store"), new("DocumentNumber", "Document"), new("LineIdentifier", "Line"),
            new("ProductCode", "Item"), new("Brand", "Brand"), new("BrandSegment", "Segment"), new("TransactionType", "Transaction Type"),
            new("Quantity", "Quantity"), new("NetValue", "Value incl. GST", true), new("CroNumber", "CRO"),
            new("SourceWorkbook", "Workbook"), new("SourceSheet", "Sheet"), new("SourceRow", "Source Row")
        ],
        [typeof(StaffPerformanceRecord)] =
        [
            new("StoreCode", "Store"), new("CroNumber", "CRO"), new("CroName", "CRO name"), new("NetSales", "Value incl. GST", true),
            new("LastYearSales", "LY Sales", true), new("GrowthPercent", "Growth %"), new("GrowthStatus", "Growth Status"),
            new("NetQuantity", "Net Quantity"), new("Discount", "Discount", true), new("Transactions", "Unique invoices"),
            new("Upt", "AUPT"), new("Atv", "ATV", true), new("ContributionPercent", "Contribution %"), new("TargetSales", "Target", true),
            new("TargetAchievementPercent", "Achievement %"), new("Rank", "Rank")
        ],
        [typeof(ServiceSalesRecord)] =
        [
            new("Period", "Period"), new("StoreCode", "Store"), new("PeriodStart", "From"), new("PeriodEnd", "To"), new("Wdc", "WDC", true),
            new("Cash", "Cash", true), new("Card", "Card", true), new("Upi", "UPI", true), new("Total", "Total", true),
            new("LastYearTotal", "LY Total", true), new("GrowthPercent", "Growth %"), new("Availability", "Availability"),
            new("MissingDays", "Missing days"), new("LastYearMissingDays", "LY missing days")
        ],
        [typeof(TenderDocumentRecord)] =
        [
            new("StoreCode", "Store"), new("DocumentNumber", "Document"), new("InvoiceAmount", "Invoice", true),
            new("TenderAmount", "Tender", true), new("Variance", "Variance", true), new("Status", "Status")
        ],
        [typeof(TenderVarianceDiagnosticRecord)] =
        [
            new("StoreCode", "Store"), new("DocumentNumber", "Document"), new("InvoiceAmount", "Invoice", true),
            new("TenderAmount", "Tender", true), new("Variance", "Variance", true), new("LikelyCause", "Likely Cause"),
            new("RecommendedCheck", "Recommended Check")
        ],
        [typeof(StockControlRecord)] =
        [
            new("StoreCode", "Store"), new("ItemCode", "Item"), new("Opening", "Opening"), new("SourceSignedMovements", "Movements"),
            new("ExpectedClosing", "Expected Closing"), new("ReportedClosing", "Reported Closing"), new("Variance", "Variance"),
            new("Status", "Status"), new("Snapshot", "Snapshot")
        ],
        [typeof(StockMovementRecord)] =
        [
            new("StoreCode", "Store"), new("ItemCode", "Item"), new("Location", "Location"), new("SourceMovementType", "Movement Type"),
            new("SourceSignedQuantity", "Signed Quantity"), new("Snapshot", "Snapshot")
        ],
        [typeof(StockInventoryRecord)] =
        [
            new("SnapshotDate", "Date"), new("StoreCode", "Store"), new("ProductCode", "Item"), new("Brand", "Brand"), new("BrandRow", "Brand row"),
            new("InventoryGroup", "Inventory Group"), new("Quantity", "Quantity"), new("UnitCost", Modules.Reports.ReportsWorkspaceView.UnitMrpHeader, true),
            new("TotalCost", Modules.Reports.ReportsWorkspaceView.MrpValueHeader, true), new("LastSaleDate", "Last Sale"),
            new("DaysSinceLastSale", "Days Since Sale"), new("LastReceiptDate", "Last Receipt"), new("DaysSinceReceipt", "Days Since Receipt"),
            new("MovementStatus", "Movement Status"), new("SnapshotSource", "Snapshot Source")
        ],
        [typeof(PhysicalStockRecord)] =
        [
            new("StoreCode", "Store"), new("BusinessDate", "Date"), new("InventoryGroupCode", "Brand"), new("DisplayQuantity", "Display"),
            new("BackstockQuantity", "Backstock"), new("DefectiveQuantity", "Defective"), new("YLocationQuantity", "Y Location"),
            // RA-STOCK-08 (9 Oct 2026): CountedPhysicalQuantity always equals ComponentTotal and CompositionVariance is
            // always blank (EveningReportRepository.LoadBrandPhysicalStockAsync); the export leaves both out, so does the screen.
            new("ComponentTotal", "Physical"), new("SystemQuantity", "System"), new("SystemVariance", "System Variance"), new("Remarks", "Remarks"), new("Status", "Status")
        ],
        [typeof(DailyExceptionRecord)] =
        [
            new("Severity", "Severity"), new("Area", "Area"), new("Code", "Code"), new("StoreCode", "Store"), new("BusinessDate", "Date"),
            new("DocumentNumber", "Document"), new("ItemCode", "Item"), new("Variance", "Variance"), new("SourceWorkbook", "Workbook"),
            new("SourceSheet", "Sheet"), new("SourceRow", "Source Row"), new("Message", "Message"), new("RecommendedAction", "Recommended Action")
        ],
        [typeof(ManagementTrendRecord)] =
        [
            new("BusinessDate", "Date"), new("StoreCode", "Store"), new("NetSales", "Net Sales", true), new("Units", "Units"),
            new("Invoices", Modules.Reports.ReportsWorkspaceView.InvoicesHeader), new("Returns", Modules.Reports.ReportsWorkspaceView.ReturnsHeader),
            new("TenderVariance", "Tender Variance", true), new("TenderSource", "Tender Source"), new("UnmatchedEnrichmentRows", "Unmatched Staff Rows")
        ]
    };

    /// <summary>The export-aligned columns of a report row type, in export order; null for any other type.</summary>
    public static IReadOnlyList<ReportGridColumn>? For(Type? type) => type is not null && Maps.TryGetValue(type, out var columns) ? columns : null;

    /// <summary>
    /// RA-UI-16 / RA-EXPORT-09 (9 Oct 2026): the column "Variance only" filters on, by row type. A mapped type's variance
    /// column is the one named Variance, else the one ending in Variance (Management Trend: TenderVariance, Physical Stock:
    /// SystemVariance); an unmapped type qualifies by a decimal Variance property. Null when the type has no variance
    /// column, in which case the checkbox is disabled instead of silently emptying the grid.
    /// </summary>
    public static string? VarianceProperty(Type? type)
    {
        if (type is null) return null;
        if (For(type) is { } mapped)
            return (mapped.FirstOrDefault(column => column.Property == "Variance")
                ?? mapped.FirstOrDefault(column => column.Property.EndsWith("Variance", StringComparison.Ordinal)))?.Property;
        return type.GetProperty("Variance") is { } property
            && (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) == typeof(decimal) ? "Variance" : null;
    }

    /// <summary>Property names that no screen shows for any row type (navigation ids and the financial-year key).</summary>
    public static bool IsHidden(string property) => property is "TargetTaskId" or "TargetId" or "NavigationHint" or "InvoiceYear";

    /// <summary>Header for a property of a type without a map, when the split property name would mislead.</summary>
    public static string? GenericHeader(string property) => property switch
    {
        "StoreCode" => "Store",
        "BrandRow" => "Brand row",
        "MrpValue" => Modules.Reports.ReportsWorkspaceView.MrpValueHeader,
        _ => null
    };
}
