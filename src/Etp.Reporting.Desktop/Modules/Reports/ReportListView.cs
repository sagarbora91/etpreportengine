using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Etp.Reporting.Desktop.Modules.Reports;

public sealed class ReportListView : UserControl
{
    public ReportListView(ShellAccess access, Action<TaskDestination> open)
    {
        var list = CreateTileGrid();
        foreach (var task in TaskNavigation.All.Where(t => t.ReportCode is not null && t.IsAllowed(access)).OrderBy(t => t.Title))
            list.Children.Add(CreateTile(task, open));
        Content = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // Fit as many columns as stay readable instead of stepping straight from three
        // to one. A 1366x768 screen at 125% leaves about 980 DIP here, which fell just
        // under the old 1000 threshold and dropped the whole catalogue into one column.
        list.Columns = ColumnsFor(ActualWidth);
        SizeChanged += (_,_) => list.Columns = ColumnsFor(ActualWidth);
    }

    /// <summary>The tile grid the catalogue and Favourites (RA-UI-21) share; columns follow <see cref="ColumnsFor"/>.</summary>
    internal static UniformGrid CreateTileGrid() => new() { Columns = 3, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Top };

    /// <summary>One report tile: a touch-height button named after the report, the same on the catalogue and on Favourites.</summary>
    internal static Button CreateTile(TaskDestination task, Action<TaskDestination> open)
    {
        var button = new Button { Content = task.Title, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 44, Margin = new Thickness(2), Padding = new Thickness(12,4,12,4) };
        AutomationProperties.SetName(button, task.Title);
        button.Click += (_,_) => open(task);
        return button;
    }

    /// <summary>Report names need roughly 330 DIP to stay readable; never fewer than one
    /// column, never more than three so the titles do not become a wall of short strips.
    /// 330 keeps two columns on the 816x480 touch case once the workspace inset is taken
    /// off, which is the narrowest layout the plan asks the catalogue to survive.</summary>
    internal const double MinimumColumnWidth = 330d;

    internal static int ColumnsFor(double availableWidth) =>
        Math.Clamp((int)(availableWidth / MinimumColumnWidth), 1, 3);
}
