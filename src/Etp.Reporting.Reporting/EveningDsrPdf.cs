using System.Globalization;
using PdfSharp.Drawing;

namespace Etp.Reporting.Reporting;

internal static class EveningDsrPdf
{
    private static readonly CultureInfo India=CultureInfo.GetCultureInfo("en-IN");
    public static void Draw(XGraphics g,DailySalesReportDocument report)
    {
        Write(g,$"Daily Sales Report · {report.BusinessDate:dd MMM yyyy}",18,16,806,23,16,true);
        var stores=report.EveningSheets.Where(x=>x.StoreCode!="COMBINED").ToArray();
        for(var i=0;i<stores.Length;i++)
        {
            var s=stores[i];var width=(806d-9*Math.Max(0,stores.Length-1))/Math.Max(1,stores.Length);var x=18+i*(width+9);
            Write(g,s.StoreName,x,46,width,20,12,true);
            Write(g,$"STORE TGT {Money(s.StoreTarget)}    DAY TGT {Money(s.DayTarget)}",x,69,width,16,8);
            Write(g,$"MTD BLA {Money(s.Balance)}    REQ ADS {Money(s.RequiredAds)}",x,87,width,16,8);
            Table(g,x,109,width,330,s.Rows);
        }
        var combined=report.EveningSheets.FirstOrDefault(x=>x.StoreCode=="COMBINED");
        if(combined is not null)
        {
            Write(g,combined.StoreName,18,448,806,15,10,true);
            Table(g,18,466,806,79,combined.Rows.Where(x=>x.Metric is "VOL" or "VALUE" or "INVOICE" or "CONVERSION %").ToArray());
        }
        Write(g,$"Service · WDC {Money(report.Service.Wdc)}  Cash {Money(report.Service.Cash)}  Card {Money(report.Service.Card)}  UPI {Money(report.Service.Upi)}  Total {Money(report.Service.Total)}",18,550,806,14,8);
        Write(g,"Values include GST. INV-only invoice counts. — = unavailable. Manual totals use available entries. Required daily sales includes today.",18,571,806,12,6.7);
    }
    private static void Table(XGraphics g,double x,double y,double width,double height,IReadOnlyList<EveningMetricRow> rows)
    {
        var rowHeight=height/(rows.Count+1);var labelWidth=width*.23;var numberWidth=(width-labelWidth)/6;
        var headers=new[]{"Metric","FTD","LY","Growth %","MTD","YTD","LY YTD"};
        for(var r=0;r<=rows.Count;r++)
        {
            if(r%2==0)g.DrawRectangle(XBrushes.AliceBlue,x,y+r*rowHeight,width,rowHeight);
            var a=r==0?headers:new[]{rows[r-1].Metric,Format(rows[r-1].Ftd,rows[r-1].Format),Format(rows[r-1].Ly,rows[r-1].Format),Format(rows[r-1].Growth,"percent"),Format(rows[r-1].Mtd,rows[r-1].Format),Format(rows[r-1].Ytd,rows[r-1].Format),Format(rows[r-1].LyYtd,rows[r-1].Format)};
            for(var c=0;c<7;c++)Write(g,a[c],x+(c==0?0:labelWidth+(c-1)*numberWidth),y+r*rowHeight,c==0?labelWidth:numberWidth,rowHeight,Math.Min(8,rowHeight-3),r==0||c==0,r>0&&c==3&&rows[r-1].Growth<0?XBrushes.Firebrick:XBrushes.Black);
        }
    }
    private static string Money(decimal? n)=>n is null?"—":n.Value.ToString("N2",India);
    private static string Format(decimal? n,string format)=>n is null?"—":n.Value.ToString(format=="currency"?"N2":format=="ratio"?"0.00":"0.##",India)+(format=="percent"?"%":"");
    private static void Write(XGraphics g,string text,double x,double y,double width,double height,double size,bool bold=false,XBrush? brush=null)
    {
        var font=new XFont("Segoe UI",size,bold?XFontStyleEx.Bold:XFontStyleEx.Regular);
        while(g.MeasureString(text,font).Width>width-6&&size>3){size-=.2;font=new XFont("Segoe UI",size,bold?XFontStyleEx.Bold:XFontStyleEx.Regular);}
        g.DrawString(text,font,brush??XBrushes.Black,new XRect(x+3,y,width-6,height),XStringFormats.CenterLeft);
    }
}
