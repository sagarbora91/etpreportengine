using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class EveningReportExportTests
{
    [Fact]
    public void Cash_book_actual_lines_balance_and_credit_note_legs_remain_visible()
    {
        var day=new CashBookDay(new(2026,8,25),"WLMHW",1000,"Prior closing",new Dictionary<string,decimal>{{"Cash",500},{"Card",100},{"UPI",200},{"CN",0},{"Gift Card",75},{"Bank",25}},20,30,40,10,200,-5,1305,null,-13870,13870,"Complete");
        var data=CashBookTables.Create([day]);
        var lines=data.Rows.Where(x=>!Equals(x[2],"Total")&&!Equals(x[4],"Total sale (information)")).ToArray();
        decimal Sum(int index)=>lines.Sum(x=>x[index] is decimal n?n:0);
        Assert.Equal(1990m,Sum(3));Assert.Equal(Sum(3),Sum(5));
        Assert.Equal(Sum(3),data.Rows.Single(x=>Equals(x[2],"Total"))[3]);
        Assert.Contains(lines,x=>Equals(x[4],"Credit note redeemed")&&Equals(x[5],13870m));
        Assert.Contains(lines,x=>Equals(x[4],"Credit note issued")&&Equals(x[5],-13870m));
        Assert.Equal(990m,day.TotalSale);
    }

    [Fact]
    public void Cash_book_total_sale_leaves_tc_out_but_the_book_still_balances()
    {
        // Decision 13 Q3: the owner's "Total sale" is Cash + Card + UPI + CN + service, without TC.
        var day=new CashBookDay(new(2026,8,25),"WLMHW",1000,"Prior closing",new Dictionary<string,decimal>{{"Cash",500},{"Card",100},{"UPI",200},{"CN",0},{"TC",129}},20,30,40,10,200,0,1310,null,0,0,"Complete");
        Assert.Equal(890m,day.TotalSale);
        Assert.Equal(1019m,day.AllReceipts);
        var data=CashBookTables.Create([day]);
        var lines=data.Rows.Where(x=>!Equals(x[2],"Total")&&!Equals(x[4],"Total sale (information)")).ToArray();
        decimal Sum(int index)=>lines.Sum(x=>x[index] is decimal n?n:0);
        Assert.Equal(Sum(3),Sum(5));
        Assert.Equal(2019m,data.Rows.Single(x=>Equals(x[2],"Total"))[5]);
        Assert.Contains(lines,x=>Equals(x[4],"TC")&&Equals(x[5],129m));
        var info=data.Rows.Single(x=>Equals(x[4],"Total sale (information)"));
        Assert.Equal(890m,info[5]);
        Assert.Contains("TC is left out",(string)info[6]!);
    }

    [Fact]
    public void Cash_book_day_without_r022_shows_missing_source_instead_of_zero_tender_modes()
    {
        // WLMHW FIX-02: an R020-only day has no tender source; it must not read as 0.00 in every mode.
        var day=new CashBookDay(new(2024,9,16),"WLMHW",1000,"Opening entered: test",new Dictionary<string,decimal>(),20,30,40,10,200,0,null,null,0,0,CashBookTables.TenderSourceMissing,false);
        Assert.Null(day.RetailTotal);
        Assert.Null(day.TotalSale);
        var data=CashBookTables.Create([day]);
        foreach(var mode in new[]{"Cash","Card","UPI","CN","TC","Gift Card","Bank"})
        {
            var row=data.Rows.Single(x=>Equals(x[4],mode));
            Assert.Null(row[3]);Assert.Null(row[5]);
            Assert.Equal("R022 missing / not imported",row[6]);
        }
        Assert.DoesNotContain(data.Rows,x=>Equals(x[4],"Credit note redeemed")||Equals(x[4],"Credit note issued"));
        var total=data.Rows.Single(x=>Equals(x[2],"Total"));
        Assert.Null(total[3]);Assert.Null(total[5]);
        Assert.Null(data.Rows.Single(x=>Equals(x[2],"Closing balance"))[3]);
        Assert.Contains("R022 missing / not imported",(string)data.Rows.Single(x=>Equals(x[4],"Opening balance"))[6]!);
    }

    [Fact]
    public void Excel_preserves_dates_numbers_negative_values_percent_points_and_missing_values()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        try
        {
            new OpenXmlReportExporter().Export(path,new("Typed values",new(2026,8,25),new(2026,8,25),"Passed","v2","₹ —",DateTimeOffset.UtcNow),
                new([new("Date"),new("Amount","#,##0.00"),new("Growth","0.00%"),new("Missing")],[[new DateOnly(2026,8,25),-12345678.90m,25.5m,null]]));
            using var doc=SpreadsheetDocument.Open(path,false);
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            var worksheet=doc.WorkbookPart!.WorksheetParts.Single().Worksheet;
            Cell Cell(string address)=>worksheet.Descendants<Cell>().Single(x=>x.CellReference==address);
            Assert.Equal(CellValues.Number,Cell("A9").DataType!.Value);
            Assert.Equal("-12345678.90",Cell("B9").CellValue!.Text);
            Assert.Equal("25.5",Cell("C9").CellValue!.Text);
            Assert.Equal("—",Cell("D9").InnerText);
            Assert.Contains(doc.WorkbookPart.WorkbookStylesPart!.Stylesheet.Descendants<NumberingFormat>(),x=>x.FormatCode=="0.00\"%\"");
            Assert.Equal("A8:D9",worksheet.GetFirstChild<AutoFilter>()!.Reference!.Value);
            Assert.Equal(8,worksheet.SheetViews!.Elements<SheetView>().Single().Pane!.VerticalSplit!.Value);
        }
        finally{File.Delete(path);}
    }
}
