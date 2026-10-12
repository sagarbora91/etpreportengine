extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceStages = EtpApplication::Etp.Reporting.Application.Service.ServiceStages;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// Token helpers of the Service screens (UI lane U2, ETP-DESIGN-SYSTEM.md): styles and spacing come from the theme by
/// key, never from literals. A key that the running theme does not define falls back to the value written here, which
/// is the rule-book value, so a unit test (no application resources) and the real app lay out the same.
/// </summary>
public static class ServiceUi
{
    public static double Space(string key, double fallback)
    {
        try { return System.Windows.Application.Current?.TryFindResource(key) is double value ? value : fallback; }
        catch (InvalidOperationException) { return fallback; } // asked from a thread that does not own the application
    }

    public static double S4 => Space("Space4", 4);
    public static double S8 => Space("Space8", 8);
    public static double S12 => Space("Space12", 12);
    public static double S16 => Space("Space16", 16);

    /// <summary>Gives the filter controls the active target height (44 touch, 36 desktop) instead of a literal 44.</summary>
    public static void ApplyTargetHeight(Panel panel)
    {
        foreach (var child in panel.Children.OfType<Control>())
            if (child.MinHeight == 0) child.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
    }

    public static Thickness Gap(double left, double top, double right, double bottom) => new(left, top, right, bottom);
    public static Thickness Gap(double all) => new(all);

    public static T Styled<T>(T element, string styleKey) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return element;
    }

    public static TextBlock Text(string text, string styleKey, string? brushKey = null)
    {
        var block = Styled(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, styleKey);
        if (brushKey is not null) block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return block;
    }

    public static TextBlock Caption(string text) => Text(text, "CaptionText");
    public static TextBlock Body(string text) => Text(text, "BodyText");
    public static TextBlock CardTitle(string text) => Text(text, "CardTitleText");
    public static TextBlock CardTitle(string text, Thickness margin) { var block = CardTitle(text); block.Margin = margin; return block; }
    public static TextBlock SectionTitle(string text) => Text(text, "SectionTitleText");

    /// <summary>A button of a named theme style (PrimaryButton, QuietButton) at the active target height.</summary>
    public static Button MakeButton(string content, string? styleKey = null)
    {
        var button = new Button { Content = content };
        button.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        if (styleKey is not null) button.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return button;
    }

    /// <summary>
    /// A secondary grid as a section that is collapsed by default: its title and a status caption (the count) stay visible in
    /// the header, the grid opens on click (SVC-24, SVC-27: never three grids stacked above the fold).
    /// </summary>
    public static Expander CollapsedSection(TextBlock title, TextBlock statusCaption, params UIElement[] content)
    {
        statusCaption.Margin = Gap(S12, 0, 0, 0);
        statusCaption.VerticalAlignment = VerticalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Center;
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(title);
        header.Children.Add(statusCaption);
        var body = new StackPanel { Margin = Gap(0, S4, 0, 0) };
        foreach (var element in content) body.Children.Add(element);
        var section = new Expander { Header = header, Content = body, IsExpanded = false, Margin = Gap(0, S8, 0, 0) };
        AutomationProperties.SetName(section, title.Text);
        return section;
    }

    /// <summary>The (i) mark of a screen: the paragraph that used to sit under the title now sits behind this tooltip (SVC-02).</summary>
    public static Border InfoMark(string text)
    {
        var glyph = Text("i", "CaptionText", "SecondaryText");
        glyph.TextAlignment = TextAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
        var mark = new Border
        {
            Child = glyph, Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1),
            Focusable = true, ToolTip = text, VerticalAlignment = VerticalAlignment.Center, Margin = Gap(Space("Space8", 8), 0, 0, 0)
        };
        mark.SetResourceReference(Border.BorderBrushProperty, "Divider");
        ToolTipService.SetShowDuration(mark, 60000);
        ToolTipService.SetInitialShowDelay(mark, 200);
        AutomationProperties.SetName(mark, "About this screen");
        AutomationProperties.SetHelpText(mark, text);
        return mark;
    }

    // ---- Stripe rule (SVC-06, section 4 rule 2): state only. "Divider" = information, no state. ----
    public const string StripeNone = "Divider";
    public const string StripeFine = "Success";
    public const string StripeLookThisWeek = "Warning";
    public const string StripeActToday = "Critical";

    // ---- Stage palette (SVC-12, section 4 rule 3). New tokens Stage<Group> / Stage<Group>Soft; fallbacks are existing tokens. ----
    public sealed record StageStyle(int Number, string Group, string StrongKey, string SoftKey, string FallbackKey);

    private static Brush? Find(string key)
    {
        try { return System.Windows.Application.Current?.TryFindResource(key) as Brush; }
        catch (InvalidOperationException) { return null; }
    }

    private static readonly Dictionary<string, StageStyle> Styles = new(StringComparer.OrdinalIgnoreCase)
    {
        [ServiceStages.Booked] = new(1, "Intake", "StageIntake", "StageIntakeSoft", "SecondaryText"),
        [ServiceStages.OnBench] = new(2, "Workshop", "StageWorkshop", "StageWorkshopSoft", "Warning"),
        [ServiceStages.IndentRaised] = new(3, "Workshop", "StageWorkshop", "StageWorkshopSoft", "Warning"),
        [ServiceStages.SrnOut] = new(4, "Out of centre", "StageOut", "StageOutSoft", "Information"),
        [ServiceStages.ReadyForDelivery] = new(5, "Closing", "StageClosing", "StageClosingSoft", "Accent"),
        [ServiceStages.InTransitBack] = new(6, "Out of centre", "StageOut", "StageOutSoft", "Information"),
        [ServiceStages.DcIssued] = new(7, "Claim pending", "StageClaim", "StageClaimSoft", "AccentDark"),
        [ServiceStages.RaIssued] = new(8, "Claim pending", "StageClaim", "StageClaimSoft", "AccentDark"),
        [ServiceStages.Rwr] = new(9, "Closed", "StageClosed", "StageClosedSoft", "SecondaryText"),
        [ServiceStages.Delivered] = new(10, "Closed", "StageClosed", "StageClosedSoft", "SecondaryText")
    };

    public static StageStyle StageFor(string stage) =>
        Styles.TryGetValue(stage, out var style) ? style : new(0, "Other", "StageClosed", "StageClosedSoft", "SecondaryText");

    /// <summary>The number printed in the stage dot: "1".."8" in lifecycle order of the board stages.</summary>
    public static string StageNumber(string stage) => StageFor(stage).Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The numbered dot of a stage (lifecycle number on the stage colour). Never red or green: lateness is red, stage is not.</summary>
    public static Border StageDot(string stage)
    {
        var style = StageFor(stage);
        var number = Text(StageNumber(stage), "CaptionText");
        number.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        number.TextAlignment = TextAlignment.Center; number.VerticalAlignment = VerticalAlignment.Center; number.FontWeight = FontWeights.SemiBold;
        var dot = new Border { Child = number, Width = 20, Height = 20, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center, Tag = stage };
        SetStageBrush(dot, Border.BackgroundProperty, style);
        AutomationProperties.SetName(dot, $"Stage {StageNumber(stage)}");
        return dot;
    }

    /// <summary>References the stage token, or its fallback token when the theme does not define it yet.</summary>
    public static void SetStageBrush(FrameworkElement element, DependencyProperty property, StageStyle style) =>
        element.SetResourceReference(property, Find(style.StrongKey) is null ? style.FallbackKey : style.StrongKey);

    /// <summary>One brush for a stage, resolved now (group header converters have no element to hang a reference on).</summary>
    public static Brush StageBrush(string stage)
    {
        var style = StageFor(stage);
        return Find(style.StrongKey) ?? Find(style.FallbackKey) ?? new SolidColorBrush();
    }
}
