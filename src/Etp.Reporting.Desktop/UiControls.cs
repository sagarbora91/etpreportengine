using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>"Nothing here yet": a SurfaceCard with a CardTitleText heading, body text and an optional next step.
/// The next step is AccentDark text (Accent as text is 4.1:1, CS-04). Every brush is a theme reference.</summary>
public sealed class EmptyState : Border
{
    public EmptyState(string title, string message, string? action = null)
    {
        SetResourceReference(StyleProperty, "SurfaceCard");
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520 };
        panel.Children.Add(Line(title, "SectionTitleText", null, null));
        panel.Children.Add(Line(message, "BodyText", "SecondaryText", "Space8"));
        if (!string.IsNullOrWhiteSpace(action)) { var next = Line(action, "BodyText", "AccentDark", "Space12"); next.FontWeight = FontWeights.SemiBold; panel.Children.Add(next); }
        Child = panel; AutomationProperties.SetName(this, $"{title}. {message}");
    }

    private static TextBlock Line(string text, string style, string? colour, string? spaceAbove)
    {
        var line = new TextBlock { Text = text, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(StyleProperty, style);
        if (colour is not null) line.SetResourceReference(TextBlock.ForegroundProperty, colour);
        if (spaceAbove is not null) line.Margin = DesignTokens.Gap(null, spaceAbove, null, null);
        return line;
    }
}

/// <summary>"Working on it": an InsetCard with an indeterminate progress bar and the message in body text.</summary>
public sealed class LoadingState : Border
{
    public LoadingState(string message)
    {
        SetResourceReference(StyleProperty, "InsetCard");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new ProgressBar { Width = 96, IsIndeterminate = true, VerticalAlignment = VerticalAlignment.Center, Margin = DesignTokens.Gap(null, null, "Space12", null) });
        var text = new TextBlock { Text = message, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(StyleProperty, "BodyText");
        panel.Children.Add(text);
        Child = panel; AutomationProperties.SetName(this, message);
    }
}
