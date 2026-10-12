using System.Globalization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Etp.Reporting.Reporting.Tests")]

namespace Etp.Reporting.Reporting;

/// <summary>
/// 1.12 lane U4 (decision 45 #4/#7, UIUX-OUTPUTS): the colours of every file ETP hands to people (DSR PDF, pack PDF,
/// Excel exports). PDF and Excel cannot read the WPF resource dictionary, so the values mirror the app's colour tokens in
/// ETP-DESIGN-SYSTEM.md section 1 (with the decision-45 contrast values for grey, amber and red text). One rule on screen,
/// PDF and Excel: green = good / positive growth, red = problem / negative growth, amber = check / missing source,
/// grey = not available or not checked yet.
/// </summary>
public static class OutputPalette
{
    public const string PrimaryText = "#10252D";
    public const string SecondaryText = "#56666B";
    public const string Divider = "#D9E3E3";
    public const string Surface = "#FFFFFF";
    public const string SurfaceSecondary = "#F0F5F5";
    public const string AppBackground = "#F3F7F7";
    public const string Accent = "#008D78";
    public const string AccentSoft = "#E3F3F0";
    public const string Success = "#087C68";
    public const string SuccessSoft = "#E6F4F0";
    public const string Warning = "#8F5700";
    public const string WarningSoft = "#FAF1E3";
    public const string Critical = "#B3303C";
    public const string CriticalSoft = "#FBECEE";
    public const string Information = "#315EBA";
    public const string InformationSoft = "#EAF0FB";

    /// <summary>"#RRGGBB" to Excel's "FFRRGGBB".</summary>
    public static string Argb(string hex) => "FF" + hex.TrimStart('#').ToUpperInvariant();
}

/// <summary>The tone of a status word on an output (same four tones as the app's status chips).</summary>
public enum OutputTone { Neutral, Success, Warning, Critical }

/// <summary>
/// Words, dates and times printed on outputs (UIUX-FORMATS-WORDING R6, part 2.4). Dates are en-IN ("Sept"), never
/// invariant; times are the PC's local time; a check's status is a word for people, never the enum name.
/// </summary>
public static class OutputText
{
    // 1.11.3 integration: the same culture as the shared formatter (U5 IndianFormat), so outputs and screens both say "Sept".
    public static readonly CultureInfo India = IndianFormat.Culture;

    /// <summary>"— / blank = not available (never zero)": the one legend line on every output.</summary>
    public const string MissingLegend = "— = not available (never zero).";

    public static string Date(DateOnly date) => date.ToString("dd MMM yyyy", India);
    public static string DateWithWeekday(DateOnly date) => date.ToString("dddd dd MMM yyyy", India);
    public static string ShortWeekdayDate(DateOnly date) => date.ToString("ddd dd MMM yyyy", India);

    /// <summary>"12 Oct 2026 09:14 IST": the stamp is local time; "IST" only when the PC is on India time.</summary>
    public static string Generated(DateTimeOffset moment)
    {
        var local = moment.ToLocalTime();
        var zone = local.Offset == TimeSpan.FromMinutes(330) ? " IST" : string.Empty;
        return local.ToString("dd MMM yyyy HH:mm", India) + zone;
    }

    /// <summary>The status word for a check (part 2.4): OK, Check, Problem, Cannot run, Not checked yet.</summary>
    public static string StatusWord(string? status) => Normalise(status) switch
    {
        "PASSED" or "PASS" => "OK",
        "WARNING" or "WARN" => "Check",
        "FAILED" or "FAIL" => "Problem",
        "BLOCKED" => "Cannot run",
        "NOTRUN" => "Not checked yet",
        "KNOWN" => "Known",
        "SKIPPED" => "Skipped",
        _ => string.IsNullOrWhiteSpace(status) ? "Not checked yet" : status.Trim()
    };

    public static OutputTone StatusTone(string? status) => Normalise(status) switch
    {
        "PASSED" or "PASS" => OutputTone.Success,
        "WARNING" or "WARN" => OutputTone.Warning,
        "FAILED" or "FAIL" or "BLOCKED" => OutputTone.Critical,
        _ => OutputTone.Neutral
    };

    public static (string Text, string Fill) ToneColours(OutputTone tone) => tone switch
    {
        OutputTone.Success => (OutputPalette.Success, OutputPalette.SuccessSoft),
        OutputTone.Warning => (OutputPalette.Warning, OutputPalette.WarningSoft),
        OutputTone.Critical => (OutputPalette.Critical, OutputPalette.CriticalSoft),
        _ => (OutputPalette.SecondaryText, OutputPalette.SurfaceSecondary)
    };

    /// <summary>A file-name part: letters, digits and hyphens only ("Titan World + Helios" -> "TitanWorld-Helios").</summary>
    public static string FileNamePart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "ETP";
        var words = text.Split(new[] { '+', '/', '\\', ',', ';', '&' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0).ToArray();
        var joined = string.Join("-", words);
        if (joined.Length == 0) return "ETP";
        return joined.Length <= 60 ? joined : joined[..60];
    }

    private static string Normalise(string? status) =>
        (status ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
