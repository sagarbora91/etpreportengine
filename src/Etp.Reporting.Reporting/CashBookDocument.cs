namespace Etp.Reporting.Reporting;

public sealed record CashBookDay(DateOnly Date, string Store, decimal? Opening, string OpeningSource,
    IReadOnlyDictionary<string,decimal> Modes, decimal? ServiceCash, decimal? ServiceCard, decimal? ServiceUpi,
    decimal? Expenses, decimal? Deposit, decimal Adjustment, decimal? Closing, decimal? Counted,
    decimal CreditNoteIssued, decimal CreditNoteRedeemed, string Status, bool TenderSourceImported = true)
{
    /// <summary>Null when no current R022 covers the day: a missing tender source is not a zero sale.</summary>
    public decimal? RetailTotal => TenderSourceImported ? Modes.Values.Sum() : null;
    /// <summary>Every credit leg of the day (all retail modes and service collections); null while a service figure is missing.</summary>
    public decimal? AllReceipts => ServiceCash is null || ServiceCard is null || ServiceUpi is null ? null : RetailTotal+ServiceCash+ServiceCard+ServiceUpi;
    /// <summary>The owner's "Total sale": all receipts except TC (decision 13 Q3; matches the 25 Aug Titan sheet).</summary>
    public decimal? TotalSale => AllReceipts-Modes.GetValueOrDefault("TC");
}

public static class CashBookTables
{
    public const string TenderSourceMissing = "R022 missing / not imported";

    public static ExcelReportData Create(IReadOnlyList<CashBookDay> days)
    {
        var rows=new List<IReadOnlyList<object?>>();
        foreach(var d in days)
        {
            void Add(string debit,decimal? dr,string credit,decimal? cr,string note="")=>rows.Add([d.Date,d.Store,debit,dr,credit,cr,note]);
            Add("Expenses",d.Expenses,"Opening balance",d.Opening,d.OpeningSource+"; "+d.Status);
            Add("Bank cash deposit",d.Deposit,"",null);
            if(!d.TenderSourceImported)
                foreach(var mode in new[]{"Cash","Card","UPI","CN","TC","Gift Card","Bank"})
                    Add(mode=="Cash"?"":mode+" settlement",null,mode,null,TenderSourceMissing);
            else foreach(var mode in new[]{"Cash","Card","UPI","CN","TC","Gift Card","Bank"}.Union(d.Modes.Keys).Select(key=>new KeyValuePair<string,decimal>(key,d.Modes.GetValueOrDefault(key))))
                if(mode.Key=="CN")
                {
                    Add("Credit note redeemed",d.CreditNoteRedeemed,"Credit note redeemed",d.CreditNoteRedeemed);
                    Add("Credit note issued",d.CreditNoteIssued,"Credit note issued",d.CreditNoteIssued,"Signed issue and redemption legs; net may be zero");
                    if(mode.Value!=d.CreditNoteIssued+d.CreditNoteRedeemed)Add("Other credit note",mode.Value-d.CreditNoteIssued-d.CreditNoteRedeemed,"Other credit note",mode.Value-d.CreditNoteIssued-d.CreditNoteRedeemed);
                }
                else Add(mode.Key=="Cash"?"":mode.Key+" settlement",mode.Key=="Cash"?null:mode.Value,mode.Key,mode.Value);
            Add("",null,"Service Cash",d.ServiceCash);
            Add("Service Card settlement",d.ServiceCard,"Service Card",d.ServiceCard);
            Add("Service UPI settlement",d.ServiceUpi,"Service UPI",d.ServiceUpi);
            Add(d.Adjustment<0?"Cash adjustment":"",d.Adjustment<0?-d.Adjustment:null,d.Adjustment>=0?"Cash adjustment":"",d.Adjustment>=0?d.Adjustment:null);
            Add("Closing balance",d.Closing,"",null,d.Counted is null?"Counted cash not entered":$"Counted cash {d.Counted:0.00}; variance {d.Counted-d.Closing:0.00}");
            var total=d.Opening+d.AllReceipts+Math.Max(d.Adjustment,0);
            Add("Total",d.Closing is null?null:total,"Total",total);
            Add("",null,"Total sale (information)",d.TotalSale,d.Modes.GetValueOrDefault("TC")==0?"Already included above; do not add again":"Already included above; do not add again. TC is left out of total sale");
        }
        return new([new("Date","date"),new("Store"),new("Dr — Particular"),new("Dr — Amount","#,##0.00"),new("Cr — Particular"),new("Cr — Amount","#,##0.00"),new("Notes")],rows);
    }
}
