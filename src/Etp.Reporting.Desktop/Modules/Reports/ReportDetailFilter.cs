using System.Collections;
using System.ComponentModel;
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
    private string focus="All";
    public ReportDetailFilter(DataGrid grid, IEnumerable? source)
    {
        rows = new ListCollectionView(source?.Cast<object>().ToList() ?? []); grid.ItemsSource = rows;
        var label = new TextBlock { Text = "Filter detail rows", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var field = new StackPanel(); field.Children.Add(label); field.Children.Add(Search);
        Children.Add(field); Children.Add(VarianceOnly);
        AutomationProperties.SetName(Search, "Filter report detail rows"); AutomationProperties.SetName(VarianceOnly, "Show non-zero variance rows only");
        ToolTip = "Detail filtering does not change summary totals or exported data.";
        ConfigureVarianceOption(VarianceOnly, RowType(source));
        Search.TextChanged += (_, _) => Apply(); VarianceOnly.Checked += (_, _) => Apply(); VarianceOnly.Unchecked += (_, _) => Apply();
        SizeChanged += (_, _) =>
        {
            var compact = ActualWidth > 0 && ActualWidth < 1000;
            field.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
            label.Text = compact ? "Find" : "Filter detail rows";
            label.Margin = compact ? new Thickness(0,0,8,0) : new Thickness(0);
            Search.Width = compact ? 170 : 210;
        };
        if(rows.SourceCollection.Cast<object>().FirstOrDefault()?.GetType().GetProperty("Area") is not null)
            foreach(var name in new[]{"All","Source","Unmapped","Stock","Staff","Tender","Cash"})
            {
                var chip=new Button{Content=name,MinHeight=44,Margin=new Thickness(3)};
                chip.Click+=(_,_)=>{focus=name;Apply();};Children.Add(chip);
            }
    }
    private void Apply()
    {
        var text = Search.Text.Trim(); var varianceOnly = VarianceOnly.IsChecked == true;
        rows.Filter = row => (text.Length == 0 || PageSearch.Matches(row, text)) && (!varianceOnly || HasVariance(row)) && MatchesFocus(row);
    }
    private bool MatchesFocus(object row)
    {
        var area=row.GetType().GetProperty("Area")?.GetValue(row)?.ToString()??"";
        var code=row.GetType().GetProperty("Code")?.GetValue(row)?.ToString()??"";
        return focus switch {"All"=>true,"Unmapped"=>code.Contains("MISSING")||code.Contains("AMBIGUOUS")||code.Contains("UNMAPPED"),_=>area.Contains(focus,StringComparison.OrdinalIgnoreCase)};
    }
    /// <summary>RA-UI-16 / RA-EXPORT-09 (9 Oct 2026): the filter reads the row type's variance column (<see cref="ReportGridColumns.VarianceProperty"/>), not a property that happens to be called Variance.</summary>
    public static bool HasVariance(object? row) =>
        row is not null && ReportGridColumns.VarianceProperty(row.GetType()) is { } name && row.GetType().GetProperty(name)?.GetValue(row) is decimal variance && variance != 0;

    internal const string NoVarianceHint = "This report has no variance column";

    /// <summary>The row type of a grid source: the first row's, else the element type of a typed empty list; null for an untyped or null source.</summary>
    public static Type? RowType(IEnumerable? source)
    {
        if (source is ICollectionView view) source = view.SourceCollection;
        if (source is null) return null;
        if (source.Cast<object>().FirstOrDefault() is { } row) return row.GetType();
        var type = source.GetType();
        return new[] { type }.Concat(type.GetInterfaces())
            .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0] is { } element && element != typeof(object) ? element : null;
    }

    /// <summary>"Variance only" is enabled only for a row type with a variance column; otherwise it is unticked, disabled and explains why.</summary>
    public static void ConfigureVarianceOption(CheckBox option, Type? rowType)
    {
        var supported = ReportGridColumns.VarianceProperty(rowType) is not null;
        if (!supported && option.IsChecked == true) option.IsChecked = false;
        option.IsEnabled = supported;
        option.ToolTip = supported ? null : NoVarianceHint;
        ToolTipService.SetShowOnDisabled(option, true);
    }
}
