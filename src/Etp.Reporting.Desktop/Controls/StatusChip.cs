using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>The five status tones of the 1.12 design system (ETP-DESIGN-SYSTEM.md section 8). Colour is the second
/// channel: every tone is shown with a word (ST1), and the same word always has the same tone across screens (ST2).</summary>
public enum StatusTone
{
    /// <summary>No data, Not run, Skipped, Not checked yet.</summary>
    Neutral,
    /// <summary>Accepted, Imported, Fresh, OK, Locked, Entered, Passed.</summary>
    Success,
    /// <summary>Warning, Check, Getting old, Partial, EDD passed, Over 15 days, Not entered (optional).</summary>
    Warning,
    /// <summary>Refused, Failed, Problem, Missing, Blocked, Out of date, Not entered (required).</summary>
    Critical,
    /// <summary>Read only, Demo store, Scheduled, Consolidated, Raw.</summary>
    Information
}

/// <summary>Theme keys and spoken meaning of a <see cref="StatusTone"/>; the one map every chip, notice and tile uses.</summary>
public static class StatusTones
{
    /// <summary>The brush keys of a tone: soft fill, tone border, tone text. Neutral is a Divider-edged grey chip.</summary>
    public static (string Fill, string Border, string Text) BrushesFor(StatusTone tone) => tone switch
    {
        StatusTone.Success => ("SuccessSoft", "Success", "Success"),
        StatusTone.Warning => ("WarningSoft", "Warning", "Warning"),
        StatusTone.Critical => ("CriticalSoft", "Critical", "Critical"),
        StatusTone.Information => ("InformationSoft", "Information", "Information"),
        _ => ("SurfaceSecondary", "Divider", "SecondaryText")
    };

    /// <summary>The solid tone key for a tile stripe (K1); Neutral means "no stripe" (Divider).</summary>
    public static string StripeKey(StatusTone tone) => tone == StatusTone.Neutral ? "Divider" : tone.ToString();

    /// <summary>The Notice style of a tone (section 5); Neutral is the plain Notice.</summary>
    public static string NoticeStyleKey(StatusTone tone) => tone == StatusTone.Neutral ? "Notice" : "Notice" + tone;

    /// <summary>What assistive technology hears after the word (ST1: "word, tone meaning").</summary>
    public static string Describe(StatusTone tone) => tone switch
    {
        StatusTone.Success => "good",
        StatusTone.Warning => "needs a look",
        StatusTone.Critical => "problem",
        StatusTone.Information => "information",
        _ => "no status"
    };

    /// <summary>A theme key read as a tone: Success, Warning, Critical and Information are tones; every other key
    /// (PrimaryText, Accent, Divider, SecondaryText, hex) is Neutral, so it can never become a decorative stripe (K1).</summary>
    public static StatusTone FromKey(string? key) => key switch
    {
        "Success" => StatusTone.Success,
        "Warning" => StatusTone.Warning,
        "Critical" => StatusTone.Critical,
        "Information" => StatusTone.Information,
        _ => StatusTone.Neutral
    };
}

/// <summary>
/// The one status chip (ETP-DESIGN-SYSTEM.md section 8, promoted from <c>ServiceFreshnessStrip.CreateChip</c>): soft fill,
/// 1 px tone border, tone text, radius 8, padding 8,3, caption size, MinHeight 24; not a tab stop. Every brush is a
/// resource reference, so high contrast and token changes reach it.
/// </summary>
public sealed class StatusChip : Border
{
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private StatusTone tone;

    public StatusChip(string text, StatusTone tone = StatusTone.Neutral)
    {
        label.SetResourceReference(StyleProperty, "CaptionText");
        Child = label; Focusable = false; MinHeight = 24; BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 3, 8, 3); VerticalAlignment = VerticalAlignment.Center; HorizontalAlignment = HorizontalAlignment.Left;
        SetResourceReference(CornerRadiusProperty, "RadiusSmall");
        Text = text; Tone = tone;
    }

    /// <summary>The word the chip carries.</summary>
    public string Text
    {
        get => label.Text;
        set { label.Text = value; Describe(); }
    }

    public StatusTone Tone
    {
        get => tone;
        set
        {
            tone = value;
            var (fill, border, text) = StatusTones.BrushesFor(value);
            SetResourceReference(BackgroundProperty, fill);
            SetResourceReference(BorderBrushProperty, border);
            label.SetResourceReference(TextBlock.ForegroundProperty, text);
            Describe();
        }
    }

    private void Describe() => AutomationProperties.SetName(this, tone == StatusTone.Neutral ? label.Text : $"{label.Text}, {StatusTones.Describe(tone)}");
}
