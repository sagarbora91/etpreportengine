using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Reporting;

internal static class ReportWorksheetWriter
{
    internal const string IndianMoney="[>=10000000]##\\,##\\,##\\,##0.00;[>=100000]##\\,##\\,##0.00;##,##0.00";
    public static void Export(string path,ExcelReportMetadata metadata,IReadOnlyList<ReportPackTable> tables)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var document=SpreadsheetDocument.Create(path,SpreadsheetDocumentType.Workbook);
        var book=document.AddWorkbookPart();book.Workbook=new Workbook();
        var styles=book.AddNewPart<WorkbookStylesPart>();styles.Stylesheet=Styles();
        var sheets=book.Workbook.AppendChild(new Sheets());var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);uint id=1;
        foreach(var table in tables)
        {
            var data=table.Data;
            if(data.Columns.Count==0||data.Rows.Any(x=>x.Count!=data.Columns.Count)||(data.Totals is not null&&data.Totals.Count!=data.Columns.Count))throw new ArgumentException("Report rows must match columns.");
            var part=book.AddNewPart<WorksheetPart>();var rows=new SheetData();uint row=1;
            rows.Append(Row(row++,[Text(table.Name,1)]));
            rows.Append(Row(row++,[Text("Period"),Value(metadata.DateFrom,"date"),Value(metadata.DateTo,"date")]));
            rows.Append(Row(row++,[Text("Status"),Text(table.Status)]));
            rows.Append(Row(row++,[Text("Control"),Text(table.Message)]));
            rows.Append(Row(row++,[Text("Rule"),Text(metadata.RuleVersion)]));
            rows.Append(Row(row++,[Text("Generated"),Value(metadata.GeneratedUtc.LocalDateTime,"date")]));
            if (!string.IsNullOrWhiteSpace(metadata.AppliedScope))
            {
                var scopeRow = Row(row++, [Text(metadata.AppliedScope, 6)]);
                scopeRow.Height = 45; scopeRow.CustomHeight = true; rows.Append(scopeRow);
            }
            row=8;rows.Append(Row(row++,data.Columns.Select(x=>Text(x.Header,1))));
            foreach(var record in data.Rows)rows.Append(Row(row++,record.Select((x,i)=>Value(x,data.Columns[i].NumberFormat))));
            var lastData=row-1;if(data.Totals is not null)rows.Append(Row(row++,data.Totals.Select((x,i)=>Value(x,data.Columns[i].NumberFormat))));
            part.Worksheet=new Worksheet(new SheetViews(new SheetView(new Pane{VerticalSplit=8,TopLeftCell="A9",ActivePane=PaneValues.BottomLeft,State=PaneStateValues.Frozen}){WorkbookViewId=0}),
                new Columns(data.Columns.Select((x,i)=>new Column{Min=(uint)i+1,Max=(uint)i+1,Width=x.NumberFormat=="General"?Math.Clamp(x.Header.Length+8,18,45):21,CustomWidth=true})),rows,new AutoFilter{Reference=$"A8:{Column(data.Columns.Count)}{Math.Max(8,lastData)}"});
            if (!string.IsNullOrWhiteSpace(metadata.AppliedScope) && data.Columns.Count > 1)
                part.Worksheet.Append(new MergeCells(new MergeCell { Reference = $"A7:{Column(data.Columns.Count)}7" }));
            var raw=new string(table.Name.Where(x=>!"[]:*?/\\".Contains(x)).ToArray());if(raw.Length==0)raw="Report";raw=raw[..Math.Min(raw.Length,25)];var name=raw;var suffix=1;while(!names.Add(name))name=$"{raw} {++suffix}";
            sheets.Append(new Sheet{Id=book.GetIdOfPart(part),SheetId=id++,Name=name});
        }
        book.Workbook.Save();
    }
    private static Stylesheet Styles()=>new(
        new NumberingFormats(new NumberingFormat{NumberFormatId=164,FormatCode=IndianMoney},new NumberingFormat{NumberFormatId=165,FormatCode="dd mmm yyyy"},new NumberingFormat{NumberFormatId=166,FormatCode="0.00\"%\""},new NumberingFormat{NumberFormatId=167,FormatCode="[>=10000000]##\\,##\\,##\\,##0;[>=100000]##\\,##\\,##0;##,##0"}),
        new Fonts(new Font(new FontSize{Val=11}),new Font(new Bold(),new FontSize{Val=11})),
        new Fills(new Fill(new PatternFill{PatternType=PatternValues.None}),new Fill(new PatternFill{PatternType=PatternValues.Gray125})),new Borders(new Border()),new CellStyleFormats(new CellFormat()),
        new CellFormats(new CellFormat(),new CellFormat{FontId=1,ApplyFont=true},new CellFormat{NumberFormatId=164,ApplyNumberFormat=true},new CellFormat{NumberFormatId=165,ApplyNumberFormat=true},new CellFormat{NumberFormatId=166,ApplyNumberFormat=true},new CellFormat{NumberFormatId=167,ApplyNumberFormat=true},new CellFormat(new Alignment { WrapText = true }) { ApplyAlignment = true }),new CellStyles(new CellStyle{Name="Normal",FormatId=0,BuiltinId=0}));
    private static Cell Text(string? text,uint style=0)=>new(){DataType=CellValues.InlineString,InlineString=new InlineString(new Text(text??"")),StyleIndex=style};
    private static Cell Value(object? value,string format)=>value switch
    {
        null=>Text("—"),DateOnly d=>Number(d.ToDateTime(TimeOnly.MinValue).ToOADate(),3),DateTime d=>Number(d.ToOADate(),3),
        decimal or double or float or int or long=>Number(value,format.Contains('%')?4u:format=="#,##0"?5u:2u),_=>Text(Convert.ToString(value,CultureInfo.InvariantCulture))
    };
    private static Cell Number(object value,uint style)=>new(){DataType=CellValues.Number,CellValue=new CellValue(Convert.ToString(value,CultureInfo.InvariantCulture)!),StyleIndex=style};
    private static Row Row(uint index,IEnumerable<Cell> cells){var r=new Row{RowIndex=index};var i=1;foreach(var c in cells){c.CellReference=Column(i++)+index;r.Append(c);}return r;}
    private static string Column(int n){var s="";while(n>0){n--;s=(char)('A'+n%26)+s;n/=26;}return s;}
}
