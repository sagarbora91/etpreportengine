namespace Etp.Reporting.Reporting;

public static class RetailReportingPolicy
{
    public const string Version = "USER_CONFIRMED_2026_08";

    public static ApprovedReportingMapping Mapping { get; } = new(
        Version, ApprovedSalesAmountSource.Net,
        new Dictionary<string, ReportingTransactionType>(StringComparer.OrdinalIgnoreCase)
        {
            ["INV"] = ReportingTransactionType.Sale,
            ["SR"] = ReportingTransactionType.Return
        },
        new HashSet<string>(
        ["CASH", "CARD", "CHEQUE", "LOYALTY_POINTS", "GV", "CREDITNOTE_REDEEM", "EXCESS_GV",
         "ROUND_OFF", "NO_REFUND", "OTHERS", "TATA_GV", "GIFTCARD", "TATACLIQ", "GYFTR", "PAYTM",
         "HELIOSOMNI", "ADVANCERDEEM", "BHIMUPI", "PHONEPE", "BHARATPE", "BAJAJFIN", "RAZORPAY",
         "PAYMENTTYPE24", "ISSUED_CREDITNOTE", "CASH_REFUND", "CHEQUE_RTGS_REFUND"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["INV", "SR", "Purchase Return", "Purchase Receipt"], StringComparer.OrdinalIgnoreCase));

    public static ApprovedSalesReportingPolicy Sales { get; } = new(Version,
        new HashSet<ReportingTransactionType>([ReportingTransactionType.Sale, ReportingTransactionType.Return]));
    public static ApprovedControlRule Tender { get; } = new(Version, 0m);
    public static ApprovedStockControlRule Stock { get; } = new(Version, 0m);
}
