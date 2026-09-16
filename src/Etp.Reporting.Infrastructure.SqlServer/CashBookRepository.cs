using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class OperationalReportRepository
{
    public async Task<IReadOnlyList<CashBookDay>> LoadCashBookAsync(string store,DateOnly from,DateOnly to,CancellationToken token=default)
    {
        new ReportingQueryScope(from,to,[store]).Validate();
        await using var c=await OpenAsync(token);
        var entries=new Dictionary<(DateOnly Date,string Field),decimal>();
        var reasons=new Dictionary<DateOnly,string>();
        await using(var q=new SqlCommand("SELECT business_date,field_code,numeric_value,change_reason FROM dbo.manual_operational_inputs WHERE store_code=@store AND business_date<=@to AND field_code IN('OPENING_CASH','SERVICE_CASH','SERVICE_CARD','SERVICE_UPI','EXPENSES','CASH_DEPOSIT','CASH_ADJUSTMENT','CLOSING_CASH_COUNTED') AND numeric_value IS NOT NULL",c))
        {
            q.Parameters.AddWithValue("@store",store);q.Parameters.AddWithValue("@to",to);
            await using var r=await q.ExecuteReaderAsync(token);
            while(await r.ReadAsync(token)){var date=r.GetFieldValue<DateOnly>(0);var field=r.GetString(1);entries[(date,field)]=r.GetDecimal(2);if(field=="OPENING_CASH")reasons[date]=r.IsDBNull(3)?"":r.GetString(3);}
        }
        var anchors=entries.Keys.Where(x=>x.Field=="OPENING_CASH"&&x.Date<=from).Select(x=>x.Date).ToArray();
        var start=anchors.Length==0?from:anchors.Max();
        var tenders=new List<(DateOnly Date,string Mode,string Source,decimal Amount)>();
        await using(var q=new SqlCommand("""
            SELECT i.transaction_date,COALESCE(m.mode,CONCAT('Unmapped: ',t.tender_type)),t.tender_type,SUM(t.source_amount)
            FROM dbo.reporting_sales_tenders t JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id
            LEFT JOIN dbo.tender_modes m ON m.source_tender_code=t.tender_type AND m.active=1
            WHERE i.store_code=@store AND i.transaction_date BETWEEN @from AND @to GROUP BY i.transaction_date,m.mode,t.tender_type;
            """,c))
        {
            q.Parameters.AddWithValue("@store",store);q.Parameters.AddWithValue("@from",start);q.Parameters.AddWithValue("@to",to);
            await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))tenders.Add((r.GetFieldValue<DateOnly>(0),r.GetString(1),r.GetString(2),r.GetDecimal(3)));
        }
        var ranges=new List<(DateOnly From,DateOnly To)>();
        await using(var q=new SqlCommand("SELECT COALESCE(period_start,business_date),COALESCE(period_end,business_date) FROM dbo.import_files WHERE store_code=@store AND report_code IN('R020','R022') AND is_superseded=0 AND data_truth_version=1",c))
        {
            q.Parameters.AddWithValue("@store",store);await using var r=await q.ExecuteReaderAsync(token);
            while(await r.ReadAsync(token))if(!r.IsDBNull(0)&&!r.IsDBNull(1))ranges.Add((r.GetFieldValue<DateOnly>(0),r.GetFieldValue<DateOnly>(1)));
        }
        decimal? carried=null;var result=new List<CashBookDay>();
        for(var date=start;date<=to;date=date.AddDays(1))
        {
            decimal? Value(string field)=>entries.TryGetValue((date,field),out var v)?v:null;
            var explicitOpening=Value("OPENING_CASH");var opening=explicitOpening??carried;
            var source=explicitOpening is not null?"Opening entered: "+reasons.GetValueOrDefault(date):carried is not null?$"Carried from {date.AddDays(-1):dd MMM yyyy}":"Opening or previous closing required";
            var td=tenders.Where(x=>x.Date==date).ToArray();var modes=td.GroupBy(x=>x.Mode).ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Amount));
            var completeSource=ranges.Any(x=>date>=x.From&&date<=x.To)&&!modes.Keys.Any(x=>x.StartsWith("Unmapped:"));
            var cash=Value("SERVICE_CASH");var card=Value("SERVICE_CARD");var upi=Value("SERVICE_UPI");var expense=Value("EXPENSES");var deposit=Value("CASH_DEPOSIT");var adjustment=Value("CASH_ADJUSTMENT")??0;
            var closing=completeSource?opening+modes.GetValueOrDefault("Cash")+cash-expense-deposit+adjustment:null;
            var status=!completeSource?"Tender source missing or unmapped":closing is null?"Enter opening, service cash, expenses and deposit":card is null||upi is null?"Cash balance ready; service Card/UPI missing":"Complete";
            if(closing is not null && Value("CLOSING_CASH_COUNTED") is decimal counted && counted!=closing)status="Counted cash variance";
            var row=new CashBookDay(date,store,opening,source,modes,cash,card,upi,expense,deposit,adjustment,closing,Value("CLOSING_CASH_COUNTED"),td.Where(x=>x.Source=="ISSUED_CREDITNOTE").Sum(x=>x.Amount),td.Where(x=>x.Source=="CREDITNOTE_REDEEM").Sum(x=>x.Amount),status);
            if(date>=from)result.Add(row);carried=closing;
        }
        return result;
    }
}
