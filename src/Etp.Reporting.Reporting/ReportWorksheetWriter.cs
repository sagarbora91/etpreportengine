using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Reporting;

/// <summary>
/// Every Excel export and pack. Layout contract (unchanged): metadata in rows 1-6, the applied scope merged in row 7,
/// column headers in row 8 (frozen, with the filter), data from row 9, the totals row last. 1.12 lane U4 (UIUX-OUTPUTS
/// OUT-17..20) adds presentation only: a 14 pt title, grey metadata labels, the Control sentence wrapped across the
/// table, a filled header row, bold totals with a top border, the status as a coloured word, the generation date WITH its
/// local time, widths from the longest cell (long text wraps), and print setup (A4 landscape, fit to one page wide,
/// row 8 repeated on every page, a footer with the report, date and page number). Cell values and number formats are the
/// same as before: the figures do not change.
/// </summary>
internal static class ReportWorksheetWriter
{
    internal const string IndianMoney="[>=10000000]##\\,##\\,##\\,##0.00;[>=100000]##\\,##\\,##0.00;##,##0.00";
    internal const string IndianCount="[>=10000000]##\\,##\\,##\\,##0;[>=100000]##\\,##\\,##0;##,##0";
    internal const string GeneratedFormat="dd mmm yyyy hh:mm";
    internal const string MissingNumberLegend="Empty number = not available (never zero).";
    internal const int HeaderRow=8, MaximumColumnWidth=60;

    // Cell formats (indexes into CellFormats). 0-6 are the original styles and keep their meaning and number formats.
    internal const uint StyleDefault=0,StyleBold=1,StyleMoney=2,StyleDate=3,StylePercent=4,StyleCount=5,StyleWrap=6,
        StyleTitle=7,StyleLabel=8,StyleHeader=9,StyleGenerated=10,StyleTotalText=11,StyleTotalMoney=12,StyleTotalPercent=13,
        StyleTotalCount=14,StyleTotalDate=15,StyleStatusSuccess=16,StyleStatusWarning=17,StyleStatusCritical=18,StyleStatusNeutral=19;

    public static void Export(string path,ExcelReportMetadata metadata,IReadOnlyList<ReportPackTable> tables)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var document=SpreadsheetDocument.Create(path,SpreadsheetDocumentType.Workbook);
        var book=document.AddWorkbookPart();book.Workbook=new Workbook();
        var styles=book.AddNewPart<WorkbookStylesPart>();styles.Stylesheet=Styles();
        var sheets=book.Workbook.AppendChild(new Sheets());var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);uint id=1;
        var printTitles=new List<DefinedName>();
        foreach(var table in tables)
        {
            var data=table.Data;
            if(data.Columns.Count==0||data.Rows.Any(x=>x.Count!=data.Columns.Count)||(data.Totals is not null&&data.Totals.Count!=data.Columns.Count))throw new ArgumentException("Report rows must match columns.");
            var part=book.AddNewPart<WorksheetPart>();var rows=new SheetData();uint row=1;
            var last=Column(data.Columns.Count);
            rows.Append(Row(row++,[Text(table.Name,StyleTitle)]));
            rows.Append(Row(row++,[Text("Period",StyleLabel),Value(metadata.DateFrom,"date"),Value(metadata.DateTo,"date")]));
            rows.Append(Row(row++,[Text("Status",StyleLabel),Text(OutputText.StatusWord(table.Status),StatusStyle(table.Status))]));
            var control=Row(row++,[Text("Control",StyleLabel),Text(table.Message,data.Columns.Count>2?StyleWrap:StyleDefault)]);
            var mergeControl=data.Columns.Count>2&&!string.IsNullOrWhiteSpace(table.Message);
            if(mergeControl){var lines=EstimatedLines(table.Message,Enumerable.Range(1,data.Columns.Count-1).Sum(i=>ColumnWidth(i,data)));if(lines>1){control.Height=15*lines;control.CustomHeight=true;}}
            rows.Append(control);
            rows.Append(Row(row++,[Text("Rule",StyleLabel),Text(metadata.RuleVersion)]));
            rows.Append(Row(row++,[Text("Generated",StyleLabel),Number(metadata.GeneratedUtc.LocalDateTime.ToOADate(),StyleGenerated),Text("local time · "+MissingNumberLegend,StyleLabel)]));
            if (!string.IsNullOrWhiteSpace(metadata.AppliedScope))
            {
                var scopeRow = Row(row++, [Text(metadata.AppliedScope, StyleWrap)]);
                scopeRow.Height = 45; scopeRow.CustomHeight = true; rows.Append(scopeRow);
            }
            row=HeaderRow;rows.Append(Row(row++,data.Columns.Select(x=>Text(x.Header,StyleHeader))));
            var wraps=data.Columns.Select((x,i)=>x.NumberFormat=="General"&&LongestText(i,data)>MaximumColumnWidth).ToArray();
            var statusColumns=data.Columns.Select(x=>x.Header.Trim().EndsWith("Status",StringComparison.OrdinalIgnoreCase)&&x.NumberFormat=="General").ToArray();
            foreach(var record in data.Rows)rows.Append(Row(row++,record.Select((x,i)=>DataCell(x,data.Columns[i].NumberFormat,wraps[i],statusColumns[i],false))));
            var lastData=row-1;if(data.Totals is not null)rows.Append(Row(row++,data.Totals.Select((x,i)=>DataCell(x,data.Columns[i].NumberFormat,false,false,true))));
            var merges=new List<MergeCell>();
            if(mergeControl)merges.Add(new MergeCell{Reference=$"B4:{last}4"});
            if (!string.IsNullOrWhiteSpace(metadata.AppliedScope) && data.Columns.Count > 1)merges.Add(new MergeCell { Reference = $"A7:{last}7" });
            part.Worksheet=new Worksheet(
                new SheetProperties(new PageSetupProperties{FitToPage=true}),
                new SheetViews(new SheetView(new Pane{VerticalSplit=8,TopLeftCell="A9",ActivePane=PaneValues.BottomLeft,State=PaneStateValues.Frozen}){WorkbookViewId=0}),
                new Columns(data.Columns.Select((x,i)=>new Column{Min=(uint)i+1,Max=(uint)i+1,Width=ColumnWidth(i,data),CustomWidth=true})),rows,
                new AutoFilter{Reference=$"A8:{last}{Math.Max(8,lastData)}"});
            if(merges.Count>0)part.Worksheet.Append(new MergeCells(merges){Count=(uint)merges.Count});
            part.Worksheet.Append(new PrintOptions{HorizontalCentered=false},
                new PageMargins{Left=0.4,Right=0.4,Top=0.5,Bottom=0.6,Header=0.3,Footer=0.3},
                new PageSetup{PaperSize=9,Orientation=OrientationValues.Landscape,FitToWidth=1,FitToHeight=0},
                new HeaderFooter(new OddFooter(Footer(table.Name,metadata))));
            var raw=new string(table.Name.Where(x=>!"[]:*?/\\".Contains(x)).ToArray());if(raw.Length==0)raw="Report";raw=raw[..Math.Min(raw.Length,25)];var name=raw;var suffix=1;while(!names.Add(name))name=$"{raw} {++suffix}";
            sheets.Append(new Sheet{Id=book.GetIdOfPart(part),SheetId=id++,Name=name});
            printTitles.Add(new DefinedName($"'{name.Replace("'","''",StringComparison.Ordinal)}'!$8:$8"){Name="_xlnm.Print_Titles",LocalSheetId=id-2});
        }
        if(printTitles.Count>0)book.Workbook.Append(new DefinedNames(printTitles));
        book.Workbook.Save();
    }

    /// <summary>Footer: report name left, period centre, "Page n of m" right (Excel codes; a literal "&amp;" is doubled).</summary>
    internal static string Footer(string reportName,ExcelReportMetadata metadata)
    {
        static string Escape(string text)=>text.Replace("&","&&",StringComparison.Ordinal);
        var period=metadata.DateFrom==metadata.DateTo?OutputText.Date(metadata.DateFrom):$"{OutputText.Date(metadata.DateFrom)} – {OutputText.Date(metadata.DateTo)}";
        return $"&L{Escape(reportName)}&C{Escape(period)}&RPage &P of &N";
    }

    /// <summary>Width from the longest cell (header, values, totals), capped at 60; numbers 12-24 wide.</summary>
    internal static double ColumnWidth(int index,ExcelReportData data)
    {
        var column=data.Columns[index];
        if(column.NumberFormat!="General")
        {
            var digits=Values(index,data).Select(x=>x switch{decimal or int or long or double or float=>Math.Abs(Convert.ToDecimal(x,CultureInfo.InvariantCulture)).ToString("#,##0.00",CultureInfo.InvariantCulture).Length+1,_=>0}).DefaultIfEmpty(0).Max();
            return Math.Clamp(Math.Max(column.Header.Length+4,digits+4),12,24);
        }
        return Math.Clamp(Math.Max(column.Header.Length+4,LongestText(index,data)+2),10,MaximumColumnWidth);
    }

    private static int LongestText(int index,ExcelReportData data)=>
        Values(index,data).Select(x=>x switch{null=>1,DateOnly or DateTime=>11,_=>Convert.ToString(x,CultureInfo.InvariantCulture)?.Length??0}).DefaultIfEmpty(0).Max();

    private static IEnumerable<object?> Values(int index,ExcelReportData data)
    {
        foreach(var record in data.Rows)yield return record[index];
        if(data.Totals is not null)yield return data.Totals[index];
    }

    private static int EstimatedLines(string text,double width)=>Math.Clamp((int)Math.Ceiling(text.Length/Math.Max(20,width*1.1)),1,8);

    private static Stylesheet Styles()
    {
        static Fill MakeFill(string hex)=>new(new PatternFill(new ForegroundColor{Rgb=OutputPalette.Argb(hex)}){PatternType=PatternValues.Solid});
        static Font MakeFont(double size,bool bold=false,string? colour=null)
        {
            var font=new Font();if(bold)font.Append(new Bold());font.Append(new FontSize{Val=size});
            if(colour is not null)font.Append(new Color{Rgb=OutputPalette.Argb(colour)});return font;
        }
        static Border MakeBorder(bool top,bool bottom,string hex)=>new(new LeftBorder(),new RightBorder(),
            top?new TopBorder(new Color{Rgb=OutputPalette.Argb(hex)}){Style=BorderStyleValues.Thin}:new TopBorder(),
            bottom?new BottomBorder(new Color{Rgb=OutputPalette.Argb(hex)}){Style=BorderStyleValues.Medium}:new BottomBorder(),new DiagonalBorder());
        static CellFormat MakeFormat(uint numberFormat=0,uint font=0,uint fill=0,uint border=0,bool wrap=false,bool centre=false)
        {
            var format=new CellFormat{NumberFormatId=numberFormat,FontId=font,FillId=fill,BorderId=border,
                ApplyNumberFormat=numberFormat!=0,ApplyFont=font!=0,ApplyFill=fill!=0,ApplyBorder=border!=0};
            if(wrap||centre)
            {
                var alignment=new Alignment{Vertical=centre?VerticalAlignmentValues.Center:VerticalAlignmentValues.Top};
                if(wrap)alignment.WrapText=true;
                format.Append(alignment);format.ApplyAlignment=true;
            }
            return format;
        }
        // Fonts: 0 normal, 1 bold, 2 title 14, 3 grey label, 4..7 status words (bold, tone colour).
        // Fills: 0 none, 1 gray125 (required), 2 header, 3..6 status tone fills.
        // Borders: 0 none, 1 header bottom, 2 totals top.
        return new(
            new NumberingFormats(new NumberingFormat{NumberFormatId=164,FormatCode=IndianMoney},new NumberingFormat{NumberFormatId=165,FormatCode="dd mmm yyyy"},new NumberingFormat{NumberFormatId=166,FormatCode="0.00\"%\""},new NumberingFormat{NumberFormatId=167,FormatCode=IndianCount},new NumberingFormat{NumberFormatId=168,FormatCode=GeneratedFormat}){Count=5},
            new Fonts(MakeFont(11),MakeFont(11,true),MakeFont(14,true,OutputPalette.PrimaryText),MakeFont(11,false,OutputPalette.SecondaryText),
                MakeFont(11,true,OutputPalette.Success),MakeFont(11,true,OutputPalette.Warning),MakeFont(11,true,OutputPalette.Critical),MakeFont(11,true,OutputPalette.SecondaryText)){Count=8},
            new Fills(new Fill(new PatternFill{PatternType=PatternValues.None}),new Fill(new PatternFill{PatternType=PatternValues.Gray125}),MakeFill(OutputPalette.Divider),
                MakeFill(OutputPalette.SuccessSoft),MakeFill(OutputPalette.WarningSoft),MakeFill(OutputPalette.CriticalSoft),MakeFill(OutputPalette.SurfaceSecondary)){Count=7},
            new Borders(new Border(),MakeBorder(false,true,OutputPalette.SecondaryText),MakeBorder(true,false,OutputPalette.PrimaryText)){Count=3},
            new CellStyleFormats(new CellFormat()){Count=1},
            new CellFormats(
                new CellFormat(),new CellFormat{FontId=1,ApplyFont=true},new CellFormat{NumberFormatId=164,ApplyNumberFormat=true},new CellFormat{NumberFormatId=165,ApplyNumberFormat=true},new CellFormat{NumberFormatId=166,ApplyNumberFormat=true},new CellFormat{NumberFormatId=167,ApplyNumberFormat=true},new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top }) { ApplyAlignment = true },
                MakeFormat(font:2),MakeFormat(font:3),MakeFormat(font:1,fill:2,border:1,wrap:true,centre:true),MakeFormat(numberFormat:168),
                MakeFormat(font:1,border:2),MakeFormat(164,1,0,2),MakeFormat(166,1,0,2),MakeFormat(167,1,0,2),MakeFormat(165,1,0,2),
                MakeFormat(font:4,fill:3),MakeFormat(font:5,fill:4),MakeFormat(font:6,fill:5),MakeFormat(font:7,fill:6)){Count=20},
            new CellStyles(new CellStyle{Name="Normal",FormatId=0,BuiltinId=0}){Count=1});
    }

    private static uint StatusStyle(string? status)=>OutputText.StatusTone(status) switch
    {
        OutputTone.Success=>StyleStatusSuccess,OutputTone.Warning=>StyleStatusWarning,OutputTone.Critical=>StyleStatusCritical,_=>StyleStatusNeutral
    };

    private static Cell Text(string? text,uint style=StyleDefault)=>new(){DataType=CellValues.InlineString,InlineString=new InlineString(new Text(text??"")),StyleIndex=style};

    private static Cell DataCell(object? value,string format,bool wrap,bool status,bool total)
    {
        if(status&&value is string word&&OutputText.StatusWord(word)!=word.Trim())return Text(OutputText.StatusWord(word),StatusStyle(word));
        var cell=Value(value,format);
        if(total)cell.StyleIndex=(cell.StyleIndex?.Value??StyleDefault) switch{StyleMoney=>StyleTotalMoney,StylePercent=>StyleTotalPercent,StyleCount=>StyleTotalCount,StyleDate=>StyleTotalDate,_=>StyleTotalText};
        else if(wrap&&cell.DataType?.Value==CellValues.InlineString)cell.StyleIndex=StyleWrap;
        return cell;
    }

    // RA-EXPORT-14 (1.10.1): a missing number is an empty cell (never the text "—" inside a numeric column, which breaks SUM and sorting);
    // General (text/date) columns keep "—". Whole numbers always use the count style, whatever the column format.
    private static Cell Value(object? value,string format)=>value switch
    {
        null when format!="General"=>new Cell{StyleIndex=format.Contains('%')?StylePercent:format=="#,##0"?StyleCount:StyleMoney},
        null=>Text("—"),DateOnly d=>Number(d.ToDateTime(TimeOnly.MinValue).ToOADate(),StyleDate),DateTime d=>Number(d.ToOADate(),StyleDate),
        int or long=>Number(value,format.Contains('%')?StylePercent:StyleCount),decimal or double or float=>Number(value,format.Contains('%')?StylePercent:format=="#,##0"?StyleCount:StyleMoney),_=>Text(Convert.ToString(value,CultureInfo.InvariantCulture))
    };
    private static Cell Number(object value,uint style)=>new(){DataType=CellValues.Number,CellValue=new CellValue(Convert.ToString(value,CultureInfo.InvariantCulture)!),StyleIndex=style};
    private static Row Row(uint index,IEnumerable<Cell> cells){var r=new Row{RowIndex=index};var i=1;foreach(var c in cells){c.CellReference=Column(i++)+index;r.Append(c);}return r;}
    private static string Column(int n){var s="";while(n>0){n--;s=(char)('A'+n%26)+s;n/=26;}return s;}
}
