namespace Etp.Reporting.Reporting;

public sealed record CashBookDay(DateOnly Date, string Store, decimal? Opening, string OpeningSource,
    IReadOnlyDictionary<string,decimal> Modes, decimal? ServiceCash, decimal? ServiceCard, decimal? ServiceUpi,
    decimal? Expenses, decimal? Deposit, decimal Adjustment, decimal? Closing, decimal? Counted,
    decimal CreditNoteIssued, decimal CreditNoteRedeemed, string Status)
{
    public decimal RetailTotal => Modes.Values.Sum();
    public decimal? TotalSale => ServiceCash is null || ServiceCard is null || ServiceUpi is null ? null : RetailTotal+ServiceCash+ServiceCard+ServiceUpi;
}

public static class CashBookTables
{
    public static ExcelReportData Create(IReadOnlyList<CashBookDay> days)
    {
        var rows=new List<IReadOnlyList<object?>>();
        foreach(var d in days)
        {
            void Add(string debit,decimal? dr,string credit,decimal? cr,string note="")=>rows.Add([d.Date,d.Store,debit,dr,credit,cr,note]);
            Add("Expenses",d.Expenses,"Opening balance",d.Opening,d.OpeningSource+"; "+d.Status);
            Add("Bank cash deposit",d.Deposit,"",null);
            foreach(var mode in new[]{"Cash","Card","UPI","CN","TC","Gift Card","Bank"}.Union(d.Modes.Keys).Select(key=>new KeyValuePair<string,decimal>(key,d.Modes.GetValueOrDefault(key))))
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
            var total=d.Opening+d.TotalSale+Math.Max(d.Adjustment,0);
            Add("Total",d.Closing is null?null:total,"Total",total);
            Add("",null,"Total sale (information)",d.TotalSale,"Already included above; do not add again");
        }
        return new([new("Date","date"),new("Store"),new("Dr — Particular"),new("Dr — Amount","#,##0.00"),new("Cr — Particular"),new("Cr — Amount","#,##0.00"),new("Notes")],rows);
    }
}
