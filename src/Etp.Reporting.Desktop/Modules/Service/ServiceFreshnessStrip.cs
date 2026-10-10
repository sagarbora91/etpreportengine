extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ServiceFreshnessChip = EtpApplication::Etp.Reporting.Application.Service.ServiceFreshnessChip;
using ServiceFreshnessColour = EtpApplication::Etp.Reporting.Application.Service.ServiceFreshnessColour;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// Renders the freshness strip (design 3.7): one chip per family group, from the contract's
/// <c>IServiceReportQuery.LoadFreshnessAsync</c> (the rules are <c>ServiceFreshness</c> in the Application layer:
/// amber after 7 days, red after 14, Q14). This class only maps a chip to theme brushes and a control.
/// </summary>
public static class ServiceFreshnessStrip
{
    /// <summary>What the chip prints: "Jobs: last export 03 Oct 2026 (raw)".</summary>
    public static string TextFor(ServiceFreshnessChip chip) => $"{chip.Group}: {chip.Text}";

    /// <summary>The theme brush keys of a colour: background and text.</summary>
    public static (string Background, string Foreground) BrushesFor(ServiceFreshnessColour colour) => colour switch
    {
        ServiceFreshnessColour.Red => ("CriticalSoft", "Critical"),
        ServiceFreshnessColour.Amber => ("WarningSoft", "Warning"),
        ServiceFreshnessColour.Fresh => ("SuccessSoft", "Success"),
        _ => ("SurfaceSecondary", "SecondaryText")
    };

    /// <summary>What assistive technology hears after the text. No day counts: monthly-only families use 38/45 days, the rest 7/14.</summary>
    public static string DescribeColour(ServiceFreshnessColour colour) => colour switch
    {
        ServiceFreshnessColour.Red => "out of date",
        ServiceFreshnessColour.Amber => "getting out of date",
        ServiceFreshnessColour.Fresh => "fresh",
        _ => "no data"
    };

    /// <summary>The chip control: a rounded border whose Tag is the chip, named for assistive technology.</summary>
    public static Border CreateChip(ServiceFreshnessChip chip)
    {
        var (background, foreground) = BrushesFor(chip.Colour);
        var text = new TextBlock { Text = TextFor(chip), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        var border = new Border { Child = text, Tag = chip, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 2, 6, 2), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, background);
        border.SetResourceReference(Border.BorderBrushProperty, foreground);
        AutomationProperties.SetName(border, $"{TextFor(chip)}, {DescribeColour(chip.Colour)}");
        return border;
    }
}
