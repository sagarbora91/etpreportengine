using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Etp.Reporting.Desktop.Modules.Reports;

/// <summary>Filters a separate view of detail rows; report metadata, totals and export rows are unchanged.</summary>
public sealed class ReportDetailFilter : WrapPanel
{
    public TextBox Search { get; } = new() { Width = 210, Margin = new Thickness(0,0,12,0) };
    public CheckBox VarianceOnly { get; } = new() { Content = "Variance only", Margin = new Thickness(0,0,12,0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ListCollectionView rows;
    public ReportDetailFilter(DataGrid grid, IEnumerable? source)
    {
        rows = new ListCollectionView(source?.Cast<object>().ToList() ?? []); grid.ItemsSource = rows;
        var field = new StackPanel(); field.Children.Add(new TextBlock { Text = "Filter detail rows", FontSize = 12 }); field.Children.Add(Search);
        Children.Add(field); Children.Add(VarianceOnly);
        AutomationProperties.SetName(Search, "Filter report detail rows"); AutomationProperties.SetName(VarianceOnly, "Show non-zero variance rows only");
        ToolTip = "Detail filtering does not change summary totals or exported data.";
        Search.TextChanged += (_, _) => Apply(); VarianceOnly.Checked += (_, _) => Apply(); VarianceOnly.Unchecked += (_, _) => Apply();
    }
    private void Apply()
    {
        var text = Search.Text.Trim(); var varianceOnly = VarianceOnly.IsChecked == true;
        rows.Filter = row => (text.Length == 0 || PageSearch.Matches(row, text)) && (!varianceOnly || HasVariance(row));
    }
    public static bool HasVariance(object? row) => row?.GetType().GetProperty("Variance")?.GetValue(row) is decimal variance && variance != 0;
}
