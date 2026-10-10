extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ServiceFamilyFreshness = EtpApplication::Etp.Reporting.Application.Service.ServiceFamilyFreshness;
using ServiceSourceKinds = EtpApplication::Etp.Reporting.Application.Service.ServiceSourceKinds;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>How old a family group's latest export is (decision 25, Q14: amber after 7 days, red after 14).</summary>
public enum ServiceFreshnessLevel { NoExport, Fresh, Amber, Red }

/// <summary>
/// One chip of the freshness strip: a family group, the date of its latest export, the export's source kind and
/// the colour level. <c>Text</c> is what the chip shows, for example "Jobs: last export 03 Oct 2026 (raw)".
/// </summary>
public sealed record ServiceFreshnessChip(string Group, DateOnly? SnapshotDate, string SourceKind, int? AgeDays, ServiceFreshnessLevel Level)
{
    public string Text => SnapshotDate is null
        ? $"{Group}: no export yet"
        : $"{Group}: last export {SnapshotDate:dd MMM yyyy}" + (SourceKind.Length == 0 ? "" : $" ({SourceKind})");
}

/// <summary>
/// The freshness strip (design 3.7): one chip per family group with its latest snapshot date and source kind. Built in
/// C# from the latest reading per family (no new SQL). The "Status views" chip shows the oldest of the ten status
/// lists, because the Pending board and Service Today are only as fresh as the stalest status list.
/// </summary>
public static class ServiceFreshnessStrip
{
    public const int AmberAfterDays = 7;
    public const int RedAfterDays = 14;

    /// <summary>The family groups in strip order, with their report codes and whether the oldest family dates the chip.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<string> Families, bool Oldest)> Groups { get; } =
    [
        ("Jobs", ["S002", "S036", "S037"], false),
        ("Status views", ["S014", "S015", "S016", "S017", "S018", "S031", "S032", "S033", "S034", "S035"], true),
        ("Pending lists", ["S009", "S010"], false),
        ("SRN", ["S011", "S012", "S013"], false),
        ("Money", ["S003", "S004"], false),
        ("Claims", ["S023", "S024", "S025", "S026", "S039", "S040", "S041"], false),
        ("Parts", ["S006", "S007", "S008"], false),
        ("Tests", ["S030"], false),
        ("Deftran", ["S029"], false)
    ];

    /// <summary>One chip per group, in strip order; <paramref name="today"/> dates the colours.</summary>
    public static IReadOnlyList<ServiceFreshnessChip> Build(IReadOnlyList<ServiceFamilyFreshness> families, DateOnly today)
    {
        var latest = families.GroupBy(family => family.ReportCode.Trim().ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.OrderByDescending(family => family.SnapshotDate).First());
        return Groups.Select(group =>
        {
            var present = group.Families.Where(latest.ContainsKey).Select(code => latest[code]).ToArray();
            if (present.Length == 0) return new ServiceFreshnessChip(group.Group, null, "", null, ServiceFreshnessLevel.NoExport);
            var dated = group.Oldest ? present.MinBy(family => family.SnapshotDate)! : present.MaxBy(family => family.SnapshotDate)!;
            var age = today.DayNumber - dated.SnapshotDate.DayNumber;
            return new ServiceFreshnessChip(group.Group, dated.SnapshotDate, DescribeSourceKind(dated.SourceKind), age, LevelFor(age));
        }).ToArray();
    }

    public static ServiceFreshnessLevel LevelFor(int ageDays) =>
        ageDays > RedAfterDays ? ServiceFreshnessLevel.Red : ageDays > AmberAfterDays ? ServiceFreshnessLevel.Amber : ServiceFreshnessLevel.Fresh;

    /// <summary>"consolidated" or "raw" as the strip prints them; an unknown kind prints nothing.</summary>
    public static string DescribeSourceKind(string? sourceKind) => (sourceKind ?? "").Trim().ToUpperInvariant() switch
    {
        ServiceSourceKinds.Consolidated => "consolidated",
        ServiceSourceKinds.Raw => "raw",
        _ => ""
    };

    /// <summary>The theme brush keys of a level: background and text.</summary>
    public static (string Background, string Foreground) BrushesFor(ServiceFreshnessLevel level) => level switch
    {
        ServiceFreshnessLevel.Red => ("CriticalSoft", "Critical"),
        ServiceFreshnessLevel.Amber => ("WarningSoft", "Warning"),
        ServiceFreshnessLevel.Fresh => ("SuccessSoft", "Success"),
        _ => ("SurfaceSecondary", "SecondaryText")
    };

    /// <summary>The chip control: a rounded border whose Tag is the chip, named for assistive technology.</summary>
    public static Border CreateChip(ServiceFreshnessChip chip)
    {
        var (background, foreground) = BrushesFor(chip.Level);
        var text = new TextBlock { Text = chip.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        var border = new Border { Child = text, Tag = chip, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 2, 6, 2), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, background);
        border.SetResourceReference(Border.BorderBrushProperty, foreground);
        var level = chip.Level switch { ServiceFreshnessLevel.Red => "older than 14 days", ServiceFreshnessLevel.Amber => "older than 7 days", ServiceFreshnessLevel.Fresh => "fresh", _ => "no export" };
        AutomationProperties.SetName(border, $"{chip.Text}, {level}");
        return border;
    }
}
