using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Reports;

/// <summary>Hosts and renders the active visual report without giving the shell rendering ownership.</summary>
public sealed class ReportPresentationControl : ContentControl
{
    public Exception? Show(ReportPresentationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.DailySalesReport is not null)
        {
            Content = new DailySalesReportView(snapshot.DailySalesReport);
            return null;
        }

        if (snapshot.VisualReport is null)
        {
            Content = null;
            return null;
        }

        try
        {
            Content = ReportVisualPresenter.BuildSummary(snapshot.VisualReport);
            return null;
        }
        catch (Exception exception)
        {
            Content = ReportVisualPresenter.BuildFailure(exception.Message);
            return exception;
        }
    }

    public void Clear() => Content = null;
}

public static class ReportVisualPresenter
{
    public static UIElement BuildSummary(VisualReportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var root = new StackPanel();
        var heading = new TextBlock
        {
            Text = "Management summary",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(VisualReportTheme.Navy)
        };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        root.Children.Add(heading);
        root.Children.Add(BuildKpiCards(model, "#F3F6F8", false));
        foreach (var visual in model.Visuals.Take(2)) root.Children.Add(BuildVisual(visual));
        AddControl(model, root, new Thickness(0, 8, 0, 0));
        return root;
    }

    public static UIElement BuildFocusedPreview(VisualReportModel model, IEnumerable? rows)
    {
        ArgumentNullException.ThrowIfNull(model);
        var root = new StackPanel();
        root.Children.Add(BuildKpiCards(model, "#FFFFFF", true));
        foreach (var visual in model.Visuals.Take(2)) root.Children.Add(BuildVisual(visual));
        AddControl(model, root, new Thickness(0, 8, 0, 8));
        root.Children.Add(new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = true,
            IsReadOnly = true,
            MinHeight = 220,
            MaxHeight = 520,
            Margin = new Thickness(0, 8, 0, 0),
            HeadersVisibility = DataGridHeadersVisibility.All
        });
        return root;
    }

    public static UIElement BuildFailure(string message) => new TextBlock
    {
        Text = $"Visual summary unavailable. The detailed report remains valid. {message}",
        Foreground = Brush(VisualReportTheme.Red),
        TextWrapping = TextWrapping.Wrap
    };

    private static UniformGrid BuildKpiCards(VisualReportModel model, string background, bool bordered)
    {
        var cards = new UniformGrid
        {
            Columns = Math.Clamp(model.Kpis.Count, 1, 4),
            Margin = bordered ? new Thickness(0, 0, 0, 10) : new Thickness(0, 8, 0, 8)
        };
        foreach (var kpi in model.Kpis.Take(4))
        {
            var formatted = IndianNumberFormatter.Format(kpi.Value, kpi.Format, kpi.State);
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = kpi.Label, Foreground = Brush("#5D6873") });
            content.Children.Add(new TextBlock
            {
                Text = formatted,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(VisualReportTheme.Navy),
                Margin = new Thickness(0, 4, 0, 0)
            });
            var card = new Border
            {
                Background = Brush(background),
                BorderBrush = bordered ? Brush("#DCE4EF") : null,
                BorderThickness = bordered ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(bordered ? 7 : 6),
                Padding = new Thickness(12),
                Margin = new Thickness(3),
                Child = content
            };
            AutomationProperties.SetName(card, $"{kpi.Label}: {formatted}");
            cards.Children.Add(card);
        }
        return cards;
    }

    private static void AddControl(VisualReportModel model, Panel root, Thickness margin)
    {
        var control = model.Controls.FirstOrDefault();
        if (control is null) return;
        root.Children.Add(new TextBlock
        {
            Text = $"Control {control.Status}: {control.Message}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(control.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase)
                ? VisualReportTheme.Teal
                : VisualReportTheme.Red),
            Margin = margin
        });
    }

    private static FrameworkElement BuildVisual(ReportVisual visual)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 7, 0, 4) };
        panel.Children.Add(new TextBlock { Text = visual.Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        if (visual.Type is ReportVisualType.Line or ReportVisualType.Sparkline)
        {
            panel.Children.Add(BuildLineVisual(visual));
            AddFootnote(visual, panel);
            return panel;
        }

        var points = visual.Series.FirstOrDefault()?.Points.Where(x => x.Value is not null).Take(10).ToArray() ?? [];
        var max = Math.Max(1m, points.Select(x => Math.Abs(x.Value ?? 0)).DefaultIfEmpty(1).Max());
        foreach (var point in points)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            var label = new TextBlock { Text = point.Category, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var track = new Border { Height = 14, Background = Brush("#E7EDF1"), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8, 0, 8, 0) };
            var bar = new Border { Height = 14, Background = Brush(visual.Series[0].Colour), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(2, 420d * (double)(Math.Abs(point.Value ?? 0) / max)) };
            track.Child = bar;
            var value = new TextBlock { Text = IndianNumberFormatter.Format(point.Value, visual.ValueFormat), TextAlignment = TextAlignment.Right };
            Grid.SetColumn(label, 0); Grid.SetColumn(track, 1); Grid.SetColumn(value, 2);
            row.Children.Add(label); row.Children.Add(track); row.Children.Add(value);
            AutomationProperties.SetName(row, $"{point.Category}, {value.Text}");
            panel.Children.Add(row);
        }
        AddFootnote(visual, panel);
        return panel;
    }

    private static FrameworkElement BuildLineVisual(ReportVisual visual)
    {
        const double width = 760, height = 190, inset = 22;
        var canvas = new Canvas { Width = width, Height = height, Background = Brush("#F8FAFB"), HorizontalAlignment = HorizontalAlignment.Left };
        var values = visual.Series.SelectMany(x => x.Points).Where(x => x.Value is not null).Select(x => x.Value!.Value).ToArray();
        var min = values.DefaultIfEmpty(0).Min();
        var max = values.DefaultIfEmpty(1).Max();
        if (max == min) max = min + 1;
        foreach (var (series, seriesIndex) in visual.Series.Select((value, index) => (value, index)))
        {
            var points = series.Points.Where(x => x.Value is not null).ToArray();
            if (points.Length == 0) continue;
            var line = new Polyline { Stroke = Brush(series.Colour), StrokeThickness = 3 };
            for (var i = 0; i < points.Length; i++)
            {
                var x = inset + i * (width - inset * 2) / Math.Max(1, points.Length - 1);
                var y = height - inset - (double)((points[i].Value!.Value - min) / (max - min)) * (height - inset * 2);
                line.Points.Add(new Point(x, y));
                var dot = new Ellipse { Width = 8, Height = 8, Fill = Brush(series.Colour), ToolTip = $"{series.Name} — {points[i].Category}: {IndianNumberFormatter.Format(points[i].Value, visual.ValueFormat)}" };
                Canvas.SetLeft(dot, x - 4); Canvas.SetTop(dot, y - 4); canvas.Children.Add(dot);
            }
            canvas.Children.Add(line);
            AutomationProperties.SetName(line, $"{series.Name} trend with {points.Length} points");
            var legend = new TextBlock { Text = series.Name, Foreground = Brush(series.Colour), FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(legend, inset + seriesIndex * 150); Canvas.SetTop(legend, 3); canvas.Children.Add(legend);
        }
        return canvas;
    }

    private static void AddFootnote(ReportVisual visual, Panel panel)
    {
        if (string.IsNullOrWhiteSpace(visual.Footnote)) return;
        panel.Children.Add(new TextBlock { Text = visual.Footnote, FontSize = 11, Foreground = Brush("#5D6873"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
    }

    private static SolidColorBrush Brush(string colour) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(colour)!;
}
