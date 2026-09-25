using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Reports;

internal static class ManagementTrendChart
{
    public static StackPanel Create(ExcelReportData data)
    {
        var dateColumn = data.Columns.Select((column, index) => (column, index)).Single(item => item.column.Header == "Date").index;
        var salesColumn = data.Columns.Select((column, index) => (column, index)).Single(item => item.column.Header == "Net Sales").index;
        var panel = new StackPanel { Name = "ManagementTrendChartPanel", Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(panel, "Management sales trend chart");
        // Sales are additive across stores for the same date. The detail table and
        // exports retain every original row and all seven management-trend fields.
        var points = data.Rows.GroupBy(row => (DateOnly)row[dateColumn]!)
            .Select(group => new { Date = group.Key, Sales = group.Sum(row => Convert.ToDecimal(row[salesColumn], CultureInfo.InvariantCulture)) })
            .OrderBy(point => point.Date).TakeLast(31).ToArray();
        var maximum = Math.Max(1m, points.Select(point => Math.Abs(point.Sales)).DefaultIfEmpty(1m).Max());
        foreach (var point in points)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(95) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(120) });
            var label = new TextBlock { Text = point.Date.ToString("dd MMM"), VerticalAlignment = VerticalAlignment.Center };
            var bar = new Border { Background = point.Sales < 0 ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(23, 107, 135)), Height = 14, HorizontalAlignment = HorizontalAlignment.Left, Width = 480d * (double)(Math.Abs(point.Sales) / maximum) };
            var value = new TextBlock { Text = point.Sales.ToString("N2"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(label, 0); Grid.SetColumn(bar, 1); Grid.SetColumn(value, 2);
            row.Children.Add(label); row.Children.Add(bar); row.Children.Add(value); panel.Children.Add(row);
        }
        return panel;
    }
}
