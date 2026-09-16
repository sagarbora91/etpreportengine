using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Etp.Reporting.Desktop.Modules.Reports;

public sealed class ReportListView : UserControl
{
    public ReportListView(ShellAccess access, Action<TaskDestination> open)
    {
        var list = new UniformGrid { Columns = 2, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Top };
        foreach (var task in TaskNavigation.All.Where(t => t.ReportCode is not null && t.IsAllowed(access)).OrderBy(t => t.Title))
        {
            var button = new Button { Content = task.Title, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 44, Margin = new Thickness(2), Padding = new Thickness(12,4,12,4) };
            AutomationProperties.SetName(button, task.Title);
            button.Click += (_,_) => open(task); list.Children.Add(button);
        }
        Content = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SizeChanged += (_,_) => list.Columns = ActualWidth < 1000 ? 1 : 2;
    }
}
