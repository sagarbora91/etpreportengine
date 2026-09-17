namespace Etp.Reporting.Reporting;

public sealed record EveningMetricRow(string Metric, decimal? Ftd, decimal? Ly, decimal? Growth,
    decimal? Mtd, decimal? Ytd, decimal? LyYtd, string Format = "number", string? Note = null);
public sealed record EveningStoreSheet(string StoreCode, string StoreName, decimal? StoreTarget, decimal? DayTarget,
    decimal? Balance, decimal? RequiredAds, IReadOnlyList<EveningMetricRow> Rows);

public static class EveningReportTables
{
    public static ExcelReportData Dsr(IReadOnlyList<EveningStoreSheet> sheets)
    {
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var sheet in sheets)
        {
            rows.Add([sheet.StoreName,"Store target",sheet.StoreTarget,null,null,null,null,null,""]);
            rows.Add([sheet.StoreName,"Day target",sheet.DayTarget,null,null,null,null,null,""]);
            rows.Add([sheet.StoreName,"MTD balance",sheet.Balance,null,null,null,null,null,""]);
            rows.Add([sheet.StoreName,"Required daily sales",sheet.RequiredAds,null,null,null,null,null,"Remaining days include today"]);
            rows.AddRange(sheet.Rows.Select(x => (IReadOnlyList<object?>)[sheet.StoreName,x.Metric,x.Ftd,x.Ly,x.Growth,x.Mtd,x.Ytd,x.LyYtd,x.Note]));
        }
        return new([new("Store"),new("Metric"),new("FTD","#,##0.00"),new("LY","#,##0.00"),new("Growth %","0.00'%'"),new("MTD","#,##0.00"),new("YTD","#,##0.00"),new("LY YTD","#,##0.00"),new("Availability")],rows);
    }
}
