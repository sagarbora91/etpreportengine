using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Etp.Reporting.Desktop;

/// <summary>
/// Code-built views read spacing and radius from the theme through this class (ETP-DESIGN-SYSTEM.md section 4), so a C#
/// view writes <c>DesignTokens.Gap("Space8")</c> instead of a literal. The fallbacks are the Spacing.xaml values, used
/// only when no theme is loaded (unit tests without an Application).
/// </summary>
public static class DesignTokens
{
    private static readonly Dictionary<string, double> Fallback = new(StringComparer.Ordinal)
    {
        ["Space4"] = 4, ["Space8"] = 8, ["Space12"] = 12, ["Space16"] = 16, ["Space20"] = 20, ["Space24"] = 24, ["Space32"] = 32, ["Space40"] = 40,
        ["InputNarrow"] = 96, ["InputMedium"] = 160, ["InputWide"] = 280, ["ActiveTargetHeight"] = 44, ["ActiveGridRowHeight"] = 44
    };

    /// <summary>A double token (Space4..Space40, InputNarrow/Medium/Wide, ActiveTargetHeight).</summary>
    public static double Value(string key) =>
        Application.Current?.TryFindResource(key) is double value ? value : Fallback.TryGetValue(key, out var fallback) ? fallback : throw new ArgumentException($"Unknown design token {key}.", nameof(key));

    /// <summary>An even thickness from a space token.</summary>
    public static Thickness Gap(string key) => new(Value(key));

    /// <summary>A thickness from up to four space tokens (left, top, right, bottom); null means 0.</summary>
    public static Thickness Gap(string? left, string? top, string? right, string? bottom) =>
        new(left is null ? 0 : Value(left), top is null ? 0 : Value(top), right is null ? 0 : Value(right), bottom is null ? 0 : Value(bottom));

    /// <summary>RadiusSmall (8: pressed or typed in) or RadiusMedium (12: contains).</summary>
    public static CornerRadius Radius(string key) =>
        Application.Current?.TryFindResource(key) is CornerRadius radius ? radius : new CornerRadius(key == "RadiusSmall" ? 8 : 12);
}

/// <summary>
/// An icon from Icons.xaml (I1): a 20 x 20 Path, 1.5 px stroke, round caps. With no stroke key the icon takes the
/// Foreground of the button or control around it (currentColor), so hover, selection and disabled states recolour it.
/// An icon never stands alone: put a word next to it or give its control an automation name (I3).
/// </summary>
public static class DesignIcon
{
    public static Path Create(string iconKey, string? strokeKey = null, double size = 20)
    {
        var path = new Path { Width = size, Height = size, StrokeThickness = 1.5, Stretch = Stretch.None, IsHitTestVisible = false,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center };
        path.SetResourceReference(Path.DataProperty, iconKey);
        if (strokeKey is not null) path.SetResourceReference(Shape.StrokeProperty, strokeKey);
        else path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        return path;
    }

    /// <summary>Icon above its label, for a <c>RailItem</c> button (N2, I4).</summary>
    public static StackPanel Stacked(string iconKey, string label)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var icon = Create(iconKey); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.Margin = new Thickness(0, 0, 0, 2);
        panel.Children.Add(icon);
        var text = new TextBlock { Text = label, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        // The implicit TextBlock style would make it 14; take the RailItem's 12 instead of writing a size here (T1).
        text.SetBinding(TextBlock.FontSizeProperty, new Binding(nameof(Control.FontSize)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        panel.Children.Add(text);
        return panel;
    }

    /// <summary>Label followed by an icon, e.g. a menu button with IconChevronDown (B4).</summary>
    public static StackPanel Trailing(string label, string iconKey)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var icon = Create(iconKey); icon.Margin = new Thickness(4, 0, 0, 0); panel.Children.Add(icon);
        return panel;
    }
}
