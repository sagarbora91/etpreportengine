using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record BrandStockEntry(string Brand,decimal System,decimal? Display,decimal? Backstock,decimal? Defective,decimal? YLoc,string? Remark,string Source);

public sealed partial class OperationalReportRepository
{
    public async Task<IReadOnlyList<BrandStockEntry>> LoadBrandStockEntryAsync(string store,DateOnly date,CancellationToken token=default)
    {
        var r=new OperationalCompletionRepository(connectionString);
        var current=await r.LoadManualStockCountsAsync(store,date,token);
        var previous=await r.LoadManualStockCountsAsync(store,date.AddDays(-1),token);
        var system=await LoadStockInventoryAsync(new(date,date,[store]),token);
        var quantities=system.GroupBy(x=>x.Brand??"Unmapped",StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.Sum(v=>v.Quantity),StringComparer.OrdinalIgnoreCase);
        return quantities.Keys.Union(current.Select(x=>x.InventoryGroupCode),StringComparer.OrdinalIgnoreCase).Union(previous.Select(x=>x.InventoryGroupCode),StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).Select(brand=>
        {
            var today=current.FirstOrDefault(x=>x.InventoryGroupCode.Equals(brand,StringComparison.OrdinalIgnoreCase));
            var count=today??previous.FirstOrDefault(x=>x.InventoryGroupCode.Equals(brand,StringComparison.OrdinalIgnoreCase));
            return new BrandStockEntry(brand,quantities.GetValueOrDefault(brand),count?.DisplayQuantity,count?.BackstockQuantity,count?.DefectiveQuantity,count?.YLocationQuantity,count?.Remarks,today is not null?"Saved today":count is not null?"Yesterday's counts — review before saving":"No previous count");
        }).ToArray();
    }

    public async Task<IReadOnlyList<EveningStoreSheet>> LoadEveningSheetsAsync(DateOnly date, IReadOnlyList<DsrManagementRow> facts, CancellationToken token = default)
    {
        var policy = new IndianFinancialYearPeriodPolicy();
        var manual = new List<(string Store, DateOnly Date, string Field, decimal Value)>();
        var brands = new List<(string Store, DateOnly Date, string Label, decimal Value)>();
        var start = IndianFinancialYearPeriodPolicy.FinancialYearStart(date).AddYears(-1);
        await using var c = await OpenAsync(token);
        await using (var q = new SqlCommand("SELECT store_code,business_date,field_code,numeric_value FROM dbo.manual_operational_inputs WHERE business_date BETWEEN @start AND @end AND numeric_value IS NOT NULL",c))
        {
            q.Parameters.AddWithValue("@start",start); q.Parameters.AddWithValue("@end",date);
            await using var r = await q.ExecuteReaderAsync(token);
            while(await r.ReadAsync(token)) manual.Add((r.GetString(0),r.GetFieldValue<DateOnly>(1),r.GetString(2),r.GetDecimal(3)));
        }
        await using (var q = new SqlCommand("""
            -- SUM already ignores NULL amounts inside a mixed group, so a group with
            -- no usable amount at all is the same case and must read as zero rather
            -- than NULL: an unguarded GetDecimal would fail the whole DSR screen.
            SELECT i.store_code,i.transaction_date,COALESCE(mapped.row_label,'Other / unmapped'),COALESCE(SUM(l.source_gross_amount),0)
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
            OUTER APPLY (SELECT TOP(1) r.row_label FROM dbo.brand_row_codes b JOIN dbo.brand_rows r ON r.brand_row_id=b.brand_row_id AND r.store_code=b.store_code
              WHERE b.store_code=i.store_code AND b.source_brand IN(l.source_brand_code,l.source_brand_name,l.brand_segment)
              ORDER BY CASE WHEN b.source_brand=l.source_brand_code THEN 0 WHEN b.source_brand=l.source_brand_name THEN 1 ELSE 2 END,r.sort_order,r.brand_row_id) mapped
            WHERE i.transaction_date BETWEEN @start AND @end AND UPPER(l.source_transaction_type) IN('INV','SR','BC')
            GROUP BY i.store_code,i.transaction_date,mapped.row_label;
            """,c))
        {
            q.Parameters.AddWithValue("@start",start); q.Parameters.AddWithValue("@end",date);
            await using var r = await q.ExecuteReaderAsync(token);
            while(await r.ReadAsync(token)) brands.Add((r.GetString(0),r.GetFieldValue<DateOnly>(1),r.GetString(2),r.GetDecimal(3)));
        }
        var definitions = await new EveningMasterRepository(connectionString).LoadBrandsAsync(token);
        var targets = await new EveningMasterRepository(connectionString).LoadTargetsAsync(token);
        var engine = new ManagementMetricEngine(); var result = new List<EveningStoreSheet>();
        foreach(var store in new[]{"WLMHW","HEMW","COMBINED"})
        {
            bool Includes(string code) => store=="COMBINED" ? code is "WLMHW" or "HEMW" : code==store;
            DsrManagementRow? Fact(string period) => facts.FirstOrDefault(x=>x.Store==store && x.Period==period);
            var f=Fact("FTD"); var m=Fact("MTD"); var y=Fact("YTD");
            decimal? Ratio(decimal? a,int? b)=> a is null || b is null or 0 ? null : a/b;
            EveningMetricRow Row(string name,decimal? day,decimal? ly,decimal? month,decimal? year,decimal? prior,string format="number",string? note=null)=>new(name,day,ly,engine.Growth(day,ly).Value,month,year,prior,format,note);
            decimal? Manual(string field,DateRange range)
            {
                var a=manual.Where(x=>Includes(x.Store)&&x.Field==field&&x.Date>=range.Start&&x.Date<=range.End).ToArray();
                return a.Length==0?null:a.Sum(x=>x.Value);
            }
            var fp=policy.Resolve(date,ReportingPeriodKind.Ftd); var mp=policy.Resolve(date,ReportingPeriodKind.Mtd); var yp=policy.Resolve(date,ReportingPeriodKind.Ytd);
            var rows=new List<EveningMetricRow>{Row("VOL",f?.TyUnits,f?.LyUnits,m?.TyUnits,y?.TyUnits,y?.LyUnits),Row("VALUE",f?.TySales,f?.LySales,m?.TySales,y?.TySales,y?.LySales,"currency"),
                Row("AUPT",f?.Upt,Ratio(f?.LyUnits,f?.LyInvoices),m?.Upt,y?.Upt,Ratio(y?.LyUnits,y?.LyInvoices),"ratio"),Row("AVPT",f?.Atv,Ratio(f?.LySales,f?.LyInvoices),m?.Atv,y?.Atv,Ratio(y?.LySales,y?.LyInvoices),"currency")};
            if(store!="COMBINED")
            {
                foreach(var label in definitions.Where(x=>x.StoreCode==store).OrderBy(x=>x.Order).Select(x=>x.Label).Append("Other / unmapped"))
                {
                    decimal? Brand(DateRange period,bool available)=>available?brands.Where(x=>x.Store==store&&x.Label==label&&x.Date>=period.Start&&x.Date<=period.End).Sum(x=>x.Value):null;
                    rows.Add(Row(label,Brand(fp.Current,f?.TySales!=null),Brand(fp.LastYear,f?.LySales!=null),Brand(mp.Current,m?.TySales!=null),Brand(yp.Current,y?.TySales!=null),Brand(yp.LastYear,y?.LySales!=null),"currency",label=="Other / unmapped"?"Assign source brands in Settings":null));
                }
            }
            foreach(var (label,field) in new[]{("RETAIL WALKIN","WALK_INS"),("WCC WALKIN","WCC_WALKIN"),("WCC SALES","WCC_SALES"),("WDC BILLS","WDC_BILLS")})
                rows.Add(Row(label,Manual(field,fp.Current),Manual(field,fp.LastYear),Manual(field,mp.Current),Manual(field,yp.Current),Manual(field,yp.LastYear),field=="WCC_SALES"?"currency":"number","Available entries; missing days are not zero entries"));
            rows.Insert(rows.FindIndex(x=>x.Metric=="WCC WALKIN"),Row("INVOICE",f?.TyInvoices,f?.LyInvoices,m?.TyInvoices,y?.TyInvoices,y?.LyInvoices));
            decimal? Conversion(int? n,DateRange period)
            {
                var entered=manual.Count(x=>Includes(x.Store)&&x.Field=="WALK_INS"&&x.Date>=period.Start&&x.Date<=period.End);
                return entered==period.InclusiveDayCount*(store=="COMBINED"?2:1)?engine.Conversion(n,Manual("WALK_INS",period)).Value:null;
            }
            rows.Insert(rows.FindIndex(x=>x.Metric=="WCC WALKIN"),Row("CONVERSION %",Conversion(f?.TyInvoices,fp.Current),Conversion(f?.LyInvoices,fp.LastYear),Conversion(m?.TyInvoices,mp.Current),Conversion(y?.TyInvoices,yp.Current),Conversion(y?.LyInvoices,yp.LastYear),"percent"));
            var ts=targets.Where(x=>Includes(x.StoreCode)&&x.Month==new DateOnly(date.Year,date.Month,1)).ToArray();
            decimal? target=ts.Length==(store=="COMBINED"?2:1)?ts.Sum(x=>x.TargetSales):null;
            var balance=target-m?.TySales; var days=DateTime.DaysInMonth(date.Year,date.Month);
            result.Add(new(store,store=="WLMHW"?"Titan World":store=="HEMW"?"Helios":"WOT + Helios",target,target/days,balance,balance/(days-date.Day+1),rows));
        }
        return result;
    }

    public async Task<IReadOnlyList<PhysicalStockReportRow>> LoadBrandPhysicalStockAsync(string store,DateOnly date,CancellationToken token=default)
    {
        var system=await LoadStockInventoryAsync(new(date,date,[store]),token);
        var counts=await new OperationalCompletionRepository(connectionString).LoadManualStockCountsAsync(store,date,token);
        var groups=system.GroupBy(x=>x.Brand??"Unmapped").ToDictionary(x=>x.Key,x=>x.Sum(v=>v.Quantity),StringComparer.OrdinalIgnoreCase);
        var map=counts.ToDictionary(x=>x.InventoryGroupCode,StringComparer.OrdinalIgnoreCase);
        return groups.Keys.Union(map.Keys,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).Select(brand=>
        {
            map.TryGetValue(brand,out var count); var physical=count is not null && new[]{count.DisplayQuantity,count.BackstockQuantity,count.DefectiveQuantity,count.YLocationQuantity}.All(x=>x is not null)?count.ComponentTotal:null; var quantity=groups.GetValueOrDefault(brand);
            var variance=physical-quantity;
            return new PhysicalStockReportRow(store,date,brand,count?.DisplayQuantity,count?.BackstockQuantity,count?.DefectiveQuantity,count?.YLocationQuantity,
                physical,physical,null,quantity,variance,count?.Remarks,system.Count==0?"SYSTEM SOURCE MISSING":physical is null?"MANUAL INPUT MISSING":variance==0?"PASS":"FAIL");
        }).ToArray();
    }
}
