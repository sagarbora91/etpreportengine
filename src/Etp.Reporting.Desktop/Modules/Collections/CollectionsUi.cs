using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>Builders the four Collections screens share. Every size, space, colour and type style is a theme token
/// (ETP-DESIGN-SYSTEM.md sections 2-8): no literal colour, font size, radius or fixed target height appears here.</summary>
internal static class CollectionsUi
{
    public static TextBlock Text(string text, string styleKey)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return block;
    }

    public static Button MakeButton(string content, string? styleKey = null, string? automationName = null)
    {
        var button = new Button { Content = content, Margin = DesignTokens.Gap(null, null, "Space8", "Space8") };
        button.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        if (styleKey is not null) button.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        if (automationName is not null) AutomationProperties.SetName(button, automationName);
        return button;
    }

    /// <summary>Disables a button and puts the reason in its tooltip, shown even while disabled (B6).</summary>
    public static void SetEnabled(Button button, bool enabled, string disabledReason)
    {
        button.IsEnabled = enabled;
        ToolTipService.SetShowOnDisabled(button, true);
        button.ToolTip = enabled || string.IsNullOrEmpty(disabledReason) ? null : disabledReason;
    }

    public static WrapPanel Toolbar() => new() { Orientation = Orientation.Horizontal };

    /// <summary>A SurfaceCard with a section title; the content sits under it.</summary>
    public static Border Card(string title, UIElement content, string? caption = null)
    {
        var stack = new StackPanel();
        var heading = Text(title, "SectionTitleText");
        heading.Margin = DesignTokens.Gap(null, null, null, "Space4");
        stack.Children.Add(heading);
        if (caption is not null)
        {
            var line = Text(caption, "CaptionText");
            line.Margin = DesignTokens.Gap(null, null, null, "Space12");
            stack.Children.Add(line);
        }
        stack.Children.Add(content);
        var card = new Border { Child = stack, Margin = DesignTokens.Gap(null, null, null, "Space24") };
        card.SetResourceReference(FrameworkElement.StyleProperty, "SurfaceCard");
        AutomationProperties.SetName(card, title);
        return card;
    }

    /// <summary>A field: its label (FieldLabelText) over the input, Space8 apart (template C).</summary>
    public static StackPanel Field(string label, FrameworkElement input)
    {
        var stack = new StackPanel { Margin = DesignTokens.Gap(null, null, "Space16", "Space8") };
        stack.Children.Add(Text(label, "FieldLabelText"));
        stack.Children.Add(input);
        AutomationProperties.SetName(input, label);
        return stack;
    }

    public static TextBox Input(string widthToken, int maxLength, bool multiline = false)
    {
        var box = new TextBox { Width = DesignTokens.Value(widthToken), MaxLength = maxLength };
        if (multiline)
        {
            box.AcceptsReturn = true; box.TextWrapping = TextWrapping.Wrap; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.Height = DesignTokens.Value("ActiveTargetHeight") * 4;
        }
        else
        {
            box.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
            box.VerticalContentAlignment = VerticalAlignment.Center;
        }
        return box;
    }

    /// <summary>A grid for a list or a short preview: read-only, columns declared by the caller.</summary>
    public static DataGrid Grid(string name)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column };
        AutomationProperties.SetName(grid, name);
        return grid;
    }

    /// <summary>A grid shown inside a card: it shows at most six rows and scrolls itself after that (G6); its height is set from
    /// the row count and the active row height, never a fixed pixel maximum.</summary>
    public static void SizePreview(DataGrid grid, int rows)
    {
        var shown = Math.Clamp(rows, 1, 6);
        grid.Height = DesignTokens.Value("ActiveGridRowHeight") * shown + DesignTokens.Value("ActiveTargetHeight") + DesignTokens.Value("Space4");
        grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    public static void AddText(DataGrid grid, string header, string path, bool number = false, double minWidth = 110, bool star = false)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, number ? TextAlignment.Right : TextAlignment.Left));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header, Binding = new Binding(path) { Mode = BindingMode.OneWay }, ElementStyle = style, MinWidth = minWidth, IsReadOnly = true,
            Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto
        });
    }

    /// <summary>A column that shows a <see cref="StatusChip"/>: the word and the tone come from the row (ST1, ST2).</summary>
    public static void AddChip(DataGrid grid, string header, Func<object, (string Word, StatusTone Tone)> pick, double minWidth = 140)
    {
        var factory = new FrameworkElementFactory(typeof(ContentControl));
        factory.SetBinding(ContentControl.ContentProperty, new Binding(".") { Converter = new ChipConverter(pick), Mode = BindingMode.OneWay });
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetValue(Control.IsTabStopProperty, false);
        grid.Columns.Add(new DataGridTemplateColumn { Header = header, CellTemplate = new DataTemplate { VisualTree = factory }, MinWidth = minWidth, IsReadOnly = true });
    }

    private sealed class ChipConverter(Func<object, (string Word, StatusTone Tone)> pick) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null) return new TextBlock();
            var (word, tone) = pick(value);
            return new StatusChip(word, tone);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
