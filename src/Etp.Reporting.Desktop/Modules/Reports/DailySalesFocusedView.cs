using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Etp.Reporting.Reporting;
namespace Etp.Reporting.Desktop.Modules.Reports;

public sealed class DailySalesFocusedView : TabControl
{
    public DailySalesFocusedView(DailySalesReportDocument report)
    {
        var original = new DailySalesReportView(report);
        var metrics = original.Children.OfType<UniformGrid>().Single(); original.Children.Remove(metrics); metrics.Columns=3;
        Add("Summary",metrics);
        foreach(var store in report.Stores)
        {
            var content=new StackPanel(); content.Children.Add(new StorePeriodCard(store)); content.Children.Add(new OperationalMetricTable(store)); Add(store.DisplayName,content);
        }
        var services=new StackPanel(); services.Children.Add(new ServiceSummaryCard(report.Service)); services.Children.Add(new TargetProgressCard(report.Targets)); Add("Service & targets",services);
    }
    private void Add(string title,UIElement content) => Items.Add(new TabItem { Header=title,Content=new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled } });
}
