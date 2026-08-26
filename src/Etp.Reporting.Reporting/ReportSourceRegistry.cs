namespace Etp.Reporting.Reporting;

public sealed record ReportSourceDefinition(
    string ReportId,
    string Name,
    IReadOnlyList<string> SourceReports,
    IReadOnlyList<string> CanonicalFields,
    IReadOnlyList<string> ManualInputs,
    IReadOnlyList<string> CalculatedFields,
    string ReconciliationRule,
    IReadOnlyList<string> UnresolvedDefinitions);

public static class ReportSourceRegistry
{
    public static IReadOnlyList<ReportSourceDefinition> All { get; } =
    [
        new("RPT-CUSTOMER-TITAN","Titan World Customer / Invoice Sales Summary",["R025","R022"],
            ["store_code","transaction_date","document_number","product_code","source_quantity","source_net_value","source_transaction_type","staff_code","source_lineage"],[],
            ["invoice_count","net_sales","net_units"],"R025 line NETVALUE equals R022 Revenue Report NETVALUE for the same store/date scope.",
            ["Customer PII is intentionally not persisted; customer display requires a separately approved minimal-PII policy."]),
        new("RPT-CUSTOMER-HELIOS","Helios Customer / Invoice Sales Summary",["R025","R022"],
            ["store_code","transaction_date","document_number","product_code","source_quantity","source_net_value","source_transaction_type","staff_code","source_lineage"],[],
            ["invoice_count","net_sales","net_units"],"R025 line NETVALUE equals R022 Revenue Report NETVALUE for the same store/date scope.",
            ["Customer PII is intentionally not persisted; customer display requires a separately approved minimal-PII policy."]),
        new("RPT-DSR","Daily Sales Report",["R025","R022","R013","R003"],
            ["transaction_date","store_code","brand","brand_segment","source_quantity","source_net_value","document_number","source_transaction_type"],
            ["WALK_INS"],["FTD","MTD","YTD","LY equivalents","growth","conversion","UPT","ATV"],
            "Titan plus Helios equals combined; R025 totals equal R022 revenue controls.",
            ["DSR and staff transaction denominators remain separate until approved."]),
        new("RPT-SERVICE","Service Sale Report",["Controlled manual service tender entry","R022 control"],
            [],["SERVICE_CASH","SERVICE_CARD","SERVICE_UPI"],
            ["service_total","MTD","YTD","growth"],"Service tender components equal service total and daily collection service amount.",
            ["A future populated ETP Service Report profile may replace manual entry only after deterministic approval."]),
        new("RPT-CASH","Daily Cash / Tender Reconciliation",["R022","Payment Type Report","Service Report"],
            ["source_net_value","tender_type","tender_amount","source_lineage"],
            ["OPENING_CASH","SERVICE_CASH","EXPENSES","CASH_DEPOSIT","CASH_ADJUSTMENT","CLOSING_CASH_COUNTED","OPERATIONAL_REMARK"],["closing_cash","cash_variance","tender_variance"],
            "ETP tender total versus R022 NETVALUE versus operational settlement.",
            ["TC/payment classifications that are not in the approved tender dictionary remain quarantined."]),
        new("RPT-STOCK-TITAN","Titan World Closing Stock",["CLOSING_STOCK","STOCK_LEDGER"],
            ["store_code","snapshot_date","product_code","brand","brand_segment","quantity","total_cost","source_lineage"],
            ["manual_stock_counts.display_quantity","manual_stock_counts.backstock_quantity","manual_stock_counts.defective_quantity","manual_stock_counts.y_location_quantity","manual_stock_counts.counted_physical_quantity","manual_stock_counts.remarks"],
            ["component_total","composition_variance","system_quantity","system_variance"],"Independently counted physical stock minus ETP closing snapshot; location-component total is a separate diagnostic.",
            ["Whether counted physical must equal the sum of location components requires business approval and is therefore reported as a variance, not forced."]),
        new("RPT-STOCK-HELIOS","Helios Closing Stock",["CLOSING_STOCK","STOCK_LEDGER"],
            ["store_code","snapshot_date","product_code","brand","brand_segment","quantity","total_cost","source_lineage"],
            ["manual_stock_counts.display_quantity","manual_stock_counts.backstock_quantity","manual_stock_counts.defective_quantity","manual_stock_counts.y_location_quantity","manual_stock_counts.counted_physical_quantity","manual_stock_counts.remarks"],
            ["component_total","composition_variance","system_quantity","system_variance"],"Independently counted physical stock minus ETP closing snapshot; location-component total is a separate diagnostic.",
            ["Whether counted physical must equal the sum of location components requires business approval and is therefore reported as a variance, not forced."]),
        new("RPT-STAFF","Staff / CRO Performance",["R013","R025"],
            ["staff_code","store_code","transaction_date","document_number","source_quantity","source_net_value","discount","source_lineage"],
            ["staff_sales_targets.target_sales"],["LY sales","growth","achievement","ranking","contribution","UPT","ATV"],
            "Attributed plus explicitly unassigned sales equal canonical store sales.",
            ["Staff-attributed transaction denominator differs from DSR invoice count and remains independently controlled."])
    ];

    public static ReportSourceDefinition Get(string reportId) => All.Single(x =>
        string.Equals(x.ReportId, reportId, StringComparison.OrdinalIgnoreCase));
}
