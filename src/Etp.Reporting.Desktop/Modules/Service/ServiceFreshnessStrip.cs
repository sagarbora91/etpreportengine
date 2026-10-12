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

    /// <summary>
    /// The colour a chip is drawn in: a group with a never-exported family is at least amber (SVC-29), because a missing
    /// export must not sit in the "fresh" colour. The chip's own <see cref="ServiceFreshnessChip.Colour"/> is unchanged.
    /// </summary>
    public static ServiceFreshnessColour EffectiveColour(ServiceFreshnessChip chip) =>
        chip.Colour == ServiceFreshnessColour.Fresh && chip.NeverExported is { Count: > 0 } ? ServiceFreshnessColour.Amber : chip.Colour;

    /// <summary>True when the chip stays inline on the freshness line: amber, red, or no data (SVC-01).</summary>
    public static bool NeedsLook(ServiceFreshnessChip chip) => EffectiveColour(chip) != ServiceFreshnessColour.Fresh;

    /// <summary>The state word a chip that is not fresh carries in its text (SVC-30), so the colour is never the only channel.</summary>
    public static string StateWord(ServiceFreshnessChip chip) => EffectiveColour(chip) switch
    {
        ServiceFreshnessColour.Red => "out of date",
        ServiceFreshnessColour.Amber => chip.NeverExported is { Count: > 0 } && chip.Colour == ServiceFreshnessColour.Fresh
            ? "file not yet exported" : "getting out of date",
        ServiceFreshnessColour.NoData => "no data",
        _ => ""
    };

    /// <summary>What an inline chip prints: the text plus the state word.</summary>
    public static string InlineTextFor(ServiceFreshnessChip chip)
    {
        var word = StateWord(chip);
        return word.Length == 0 || TextFor(chip).Contains(word, StringComparison.OrdinalIgnoreCase) ? TextFor(chip) : $"{TextFor(chip)} - {word}";
    }

    /// <summary>The cadence tooltip: why two chips with the same date can differ in colour (SVC-30).</summary>
    public const string CadenceText = "Daily and raw lists turn amber after 7 days and red after 14. Monthly-only groups turn amber after 38 days and red after 45.";

    /// <summary>"All 9 groups fresh" or "8 of 9 groups fresh": the one summary chip when the strip is collapsed.</summary>
    public static string SummaryText(IReadOnlyList<ServiceFreshnessChip> chips)
    {
        var fresh = chips.Count(chip => !NeedsLook(chip));
        return fresh == chips.Count ? $"All {chips.Count} groups fresh" : $"{fresh} of {chips.Count} groups fresh";
    }

    /// <summary>The colour of the summary chip: the worst colour in the strip.</summary>
    public static ServiceFreshnessColour SummaryColour(IReadOnlyList<ServiceFreshnessChip> chips)
    {
        var colours = chips.Select(EffectiveColour).ToArray();
        if (colours.Contains(ServiceFreshnessColour.Red)) return ServiceFreshnessColour.Red;
        if (colours.Contains(ServiceFreshnessColour.Amber)) return ServiceFreshnessColour.Amber;
        if (colours.Contains(ServiceFreshnessColour.NoData)) return ServiceFreshnessColour.NoData;
        return ServiceFreshnessColour.Fresh;
    }

    /// <summary>The chip control: a rounded border whose Tag is the chip, named for assistive technology.</summary>
    public static Border CreateChip(ServiceFreshnessChip chip, bool includeState = false)
    {
        var colour = EffectiveColour(chip);
        var (background, foreground) = BrushesFor(colour);
        var shown = includeState ? InlineTextFor(chip) : TextFor(chip);
        return Build(shown, background, foreground, $"{TextFor(chip)}, {DescribeColour(colour)}", chip);
    }

    /// <summary>The summary chip of the collapsed strip.</summary>
    public static Border CreateSummaryChip(IReadOnlyList<ServiceFreshnessChip> chips)
    {
        var colour = SummaryColour(chips);
        var (background, foreground) = BrushesFor(colour);
        var text = SummaryText(chips);
        return Build(text, background, foreground, $"{text}, {DescribeColour(colour)}", null);
    }

    private static Border Build(string text, string background, string foreground, string automationName, object? tag)
    {
        var block = ServiceUi.Caption(text);
        block.VerticalAlignment = VerticalAlignment.Center;
        block.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        var border = new Border
        {
            Child = block, Tag = tag, BorderThickness = new Thickness(1), MinHeight = 24, ToolTip = CadenceText,
            Padding = ServiceUi.Gap(ServiceUi.S8, ServiceUi.S4 - 1, ServiceUi.S8, ServiceUi.S4 - 1),
            Margin = ServiceUi.Gap(0, ServiceUi.S4 / 2, ServiceUi.S8, ServiceUi.S4 / 2)
        };
        border.SetResourceReference(Border.CornerRadiusProperty, "RadiusSmall");
        border.SetResourceReference(Border.BackgroundProperty, background);
        border.SetResourceReference(Border.BorderBrushProperty, foreground);
        AutomationProperties.SetName(border, automationName);
        return border;
    }
}
