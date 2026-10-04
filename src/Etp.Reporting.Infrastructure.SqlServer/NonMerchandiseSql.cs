namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Non-merchandise lines (owner answer Q2, decision 13; Helios report audit R-10): a gift card is sold through the till but
/// is not stock. It is identified the way the Helios exports carry it: product code <c>GIFT CARD</c> (sales lines, closing
/// stock and the stock ledger) or brand code <c>GC</c> (R025 BRAND on sales lines, the snapshot's brand code). The list is
/// hard-coded; an Owner-editable list needs a table, which needs a migration (1.10.0 if wanted).
/// <para>
/// Each fragment is a parenthesised SQL predicate that is true for a gift-card row and false (never NULL) otherwise, so
/// <c>AND NOT (...)</c> never drops a row with a blank brand. The constants use the alias named in their summary; the
/// methods take any alias.
/// </para>
/// </summary>
internal static class NonMerchandiseSql
{
    public const string GiftCardProductCode = "GIFT CARD";
    public const string GiftCardBrandCode = "GC";

    /// <summary>A sales line (alias <c>l</c>, dbo.sales_lines) that is a gift card.</summary>
    public const string SalesLineOfL = "(UPPER(LTRIM(RTRIM(COALESCE(l.product_code,N''))))=N'GIFT CARD' OR UPPER(LTRIM(RTRIM(COALESCE(l.source_brand_code,N''))))=N'GC')";

    /// <summary>A stock snapshot row (alias <c>s</c>, dbo.v_stock_snapshots_effective) that is a gift card.</summary>
    public const string StockItemOfS = "(UPPER(LTRIM(RTRIM(COALESCE(s.product_code,N''))))=N'GIFT CARD' OR UPPER(LTRIM(RTRIM(COALESCE(s.brand_code,N''))))=N'GC')";

    /// <summary>A stock ledger row (alias <c>m</c>, dbo.stock_movements, which has no brand) that is a gift card.</summary>
    public const string LedgerItemOfM = "(UPPER(LTRIM(RTRIM(COALESCE(m.product_code,N''))))=N'GIFT CARD')";

    public static string SalesLine(string alias) => SalesLineOfL.Replace("l.", Alias(alias), StringComparison.Ordinal);
    public static string StockItem(string alias) => StockItemOfS.Replace("s.", Alias(alias), StringComparison.Ordinal);
    public static string LedgerItem(string alias) => LedgerItemOfM.Replace("m.", Alias(alias), StringComparison.Ordinal);

    private static string Alias(string alias) =>
        string.IsNullOrWhiteSpace(alias) ? "" : alias.All(c => char.IsLetterOrDigit(c) || c == '_') ? alias + "." : throw new ArgumentException("Invalid SQL alias.", nameof(alias));
}
