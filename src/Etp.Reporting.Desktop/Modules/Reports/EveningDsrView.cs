using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Reports;

public sealed class EveningDsrView : TabControl
{
    public EveningDsrView(DailySalesReportDocument document)
    {
        Background=Brushes.White;
        System.Windows.Automation.AutomationProperties.SetName(this,"Evening DSR stores");
        foreach(var sheet in document.EveningSheets)
        {
            var panel=new StackPanel();
            var heading = new TextBlock{Text=$"{sheet.StoreName} · {document.BusinessDate:dd MMM yyyy}",FontSize=19,FontWeight=FontWeights.Bold,Margin=new(6)};
            System.Windows.Automation.AutomationProperties.SetHeadingLevel(heading,System.Windows.Automation.AutomationHeadingLevel.Level2);
            panel.Children.Add(heading);
            panel.Children.Add(new TextBlock{Text=$"Store target {Money(sheet.StoreTarget)}   Day target {Money(sheet.DayTarget)}\nMTD balance {Money(sheet.Balance)}   Required daily sales {Money(sheet.RequiredAds)}",TextWrapping=TextWrapping.Wrap,Margin=new(6)});
            var grid=new Grid{MinWidth=680};grid.ColumnDefinitions.Add(new(){Width=new GridLength(150)});for(var i=0;i<6;i++)grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var all=new List<string[]>{new[]{"Metric","FTD","LY","Growth %","MTD","YTD","LY YTD"}};
            all.AddRange(sheet.Rows.Select(r=>new[]{r.Metric,Format(r.Ftd,r.Format),Format(r.Ly,r.Format),Format(r.Growth,"percent"),Format(r.Mtd,r.Format),Format(r.Ytd,r.Format),Format(r.LyYtd,r.Format)}));
            for(var row=0;row<all.Count;row++)
            {
                grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
                for(var col=0;col<7;col++)
                {
                    var text=new TextBlock{Text=all[row][col],FontSize=12,TextWrapping=TextWrapping.Wrap,TextAlignment=col==0?TextAlignment.Left:TextAlignment.Right,Padding=new(5),FontWeight=row==0?FontWeights.Bold:FontWeights.Normal};
                    if(row>0&&col==3&&sheet.Rows[row-1].Growth is decimal growth)text.Foreground=growth<0?Brushes.Firebrick:Brushes.DarkGreen;
                    var cell=new Border{Child=text,Background=row%2==0?Brushes.AliceBlue:Brushes.White,BorderBrush=Brushes.LightGray,BorderThickness=new(0,0,0,1)};Grid.SetRow(cell,row);Grid.SetColumn(cell,col);grid.Children.Add(cell);
                }
            }
            panel.Children.Add(grid);panel.Children.Add(new TextBlock{Text="Day target = monthly ÷ days in month. Balance = target − MTD. Required daily sales includes today. — means unavailable; manual totals use available entries.",TextWrapping=TextWrapping.Wrap,Margin=new(6)});
            Items.Add(new TabItem{Header=sheet.StoreName,MinHeight=44,Content=new ScrollViewer{Content=panel,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        }
    }
    private static string Money(decimal? value)=>value is null?"—":value.Value.ToString("₹#,##0.00",CultureInfo.GetCultureInfo("en-IN"));
    private static string Format(decimal? n,string format)=>n is null?"—":n.Value.ToString(format=="currency"?"N2":format=="ratio"?"0.00":"0.##",CultureInfo.GetCultureInfo("en-IN"))+(format=="percent"?"%":"");
}
