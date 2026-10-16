namespace Etp.Reporting.Reporting;

public static class RetailReportingPolicy
{
    public const string Version = "DATA_TRUTH_GST_INCLUSIVE_2026_09";

    public static ApprovedReportingMapping Mapping { get; } = new(
        Version, ApprovedSalesAmountSource.Gross,
        new Dictionary<string, ReportingTransactionType>(StringComparer.OrdinalIgnoreCase)
        {
            ["INV"] = ReportingTransactionType.Sale,
            ["SR"] = ReportingTransactionType.Return,
            ["BC"] = ReportingTransactionType.Return
        },
        new HashSet<string>(
        ["CASH", "CARD", "UPI", "CN", "TC", "Gift Card", "Bank", "Service Cash", "Service Card", "Service UPI", "PAYMENTTYPE25", "CHEQUE", "LOYALTY_POINTS", "GV", "CREDITNOTE_REDEEM", "EXCESS_GV",
         "ROUND_OFF", "NO_REFUND", "OTHERS", "TATA_GV", "GIFTCARD", "TATACLIQ", "GYFTR", "PAYTM",
         "HELIOSOMNI", "ADVANCERDEEM", "BHIMUPI", "PHONEPE", "BHARATPE", "BAJAJFIN", "RAZORPAY",
         "PAYMENTTYPE24", "ISSUED_CREDITNOTE", "CASH_REFUND", "CHEQUE_RTGS_REFUND"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["INV", "SR", "BC", "Purchase Return", "Purchase Receipt", "STM Issue", "STM Receipt", "STM Dispatch", "Stock Issue", "Stock Receipt"], StringComparer.OrdinalIgnoreCase));

    public static ApprovedSalesReportingPolicy Sales { get; } = new(Version,
        new HashSet<ReportingTransactionType>([ReportingTransactionType.Sale, ReportingTransactionType.Return]));
    /// <summary>Decision 68 #7 (1.12.1): a document's revenue and tenders may differ by up to Rs 1 (rounding) and still pass; the variance is kept.</summary>
    /// <summary>1.12.1 review m-5: the Rs 1 tender rule has its own version, so an export or saved result says which pass rule judged it.</summary>
    public const string TenderVersion = "DATA_TRUTH_GST_INCLUSIVE_2026_10_TENDER1";
    public static ApprovedControlRule Tender { get; } = new(TenderVersion, 1m);
    public static ApprovedStockControlRule Stock { get; } = new(Version, 0m);
}
