namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Gift-card sales lines (owner decision 13 / A2): product code GIFT CARD or R025 BRAND GC. They are not
/// merchandise, so the DSR keeps them out of VALUE, VOL and INVOICE and shows them on their own GIFT CARD line.
/// Hard-coded by decision; an Owner-editable list would need a table (1.10.0).
/// </summary>
internal static class GiftCardSql
{
    public const string RowLabel = "GIFT CARD";

    /// <summary>The availability note on the DSR GIFT CARD line.</summary>
    public const string DsrNote = "Gift-card sales (GIFT CARD / BRAND GC); not in VALUE, VOL or INVOICE";

    /// <summary>A SQL predicate that is true for a gift-card line of the sales_lines alias given.</summary>
    public static string SalesLine(string alias) =>
        $"(UPPER(LTRIM(RTRIM(COALESCE({alias}.product_code,N''))))=N'GIFT CARD' OR UPPER(LTRIM(RTRIM(COALESCE({alias}.source_brand_code,N''))))=N'GC')";
}
