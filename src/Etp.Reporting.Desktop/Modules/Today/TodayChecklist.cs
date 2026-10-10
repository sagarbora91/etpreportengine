extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using DailyReadinessDestination = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessDestination;
using DailyReadinessItem = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessItem;
using DailyReadinessQuery = EtpApplication::Etp.Reporting.Application.DailyReadiness.IDailyReadinessQuery;
using DailyReadinessRequest = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessRequest;
using DailyReadinessResult = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessResult;

namespace Etp.Reporting.Desktop.Modules.Today;

/// <summary>
/// One line of the Today "What's missing" checklist: the readiness message, the button that opens
/// the screen where the item is fixed, and whether the signed-in role may open that screen.
/// </summary>
public sealed record TodayChecklistLine(string Message, string ActionLabel, string TaskId, bool CanOpen, string? Tooltip);

/// <summary>
/// 1.9.9 Today checklist. Maps IDailyReadinessQuery results to checklist lines and each
/// destination to a task id in <see cref="TaskNavigation"/>. Holds no WPF state, so it is tested
/// without a window.
/// </summary>
public static class TodayChecklist
{
    /// <summary>The Monthly targets tab inside Settings > Brands and targets (EveningMastersView).</summary>
    public const string MonthlyTargetsTab = "Monthly targets";

    /// <summary>
    /// A query factory for a build whose composition root has no readiness query. The panel then
    /// stays hidden instead of failing (the same pattern as ServiceScreens.Unavailable).
    /// </summary>
    public static DailyReadinessQuery Unavailable() =>
        throw new InvalidOperationException("The daily readiness query is not available in this build.");

    public static string TaskFor(DailyReadinessDestination destination) => destination switch
    {
        DailyReadinessDestination.ImportIntake => "import-files",
        DailyReadinessDestination.TodayWalkIns => "walk-ins",
        DailyReadinessDestination.TodayCashEntries => "cash-input",
        DailyReadinessDestination.SettingsMonthlyTargets => "masters",
        DailyReadinessDestination.SettingsStaffTargets => "staff-target",
        _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, "No Today checklist screen for this destination.")
    };

    public static string ActionLabel(DailyReadinessDestination destination) => destination switch
    {
        DailyReadinessDestination.ImportIntake => "Import",
        DailyReadinessDestination.TodayWalkIns => "Enter walk-ins",
        DailyReadinessDestination.TodayCashEntries => "Enter cash",
        DailyReadinessDestination.SettingsMonthlyTargets => "Set monthly target",
        DailyReadinessDestination.SettingsStaffTargets => "Set staff targets",
        _ => "Open"
    };

    /// <summary>
    /// The request for the shell scope. The header store tag is "" for All stores: every active
    /// Retail store plus the Service Centre's raw pack. One store checks that store only.
    /// </summary>
    public static DailyReadinessRequest Request(DateTime businessDate, string? headerStore) =>
        string.IsNullOrWhiteSpace(headerStore)
            ? new(DateOnly.FromDateTime(businessDate), [], IncludeServiceCentre: true)
            : new(DateOnly.FromDateTime(businessDate), [headerStore.Trim()], IncludeServiceCentre: false);

    public static IReadOnlyList<TodayChecklistLine> Lines(DailyReadinessResult result, ShellAccess access)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(access);
        return result.Missing.Select(item => Line(item, access)).ToArray();
    }

    private static TodayChecklistLine Line(DailyReadinessItem item, ShellAccess access)
    {
        var taskId = TaskFor(item.Destination);
        var task = TaskNavigation.Find(taskId) ?? throw new InvalidOperationException($"Task {taskId} is not registered.");
        var allowed = task.IsAllowed(access);
        var path = item.Destination == DailyReadinessDestination.SettingsMonthlyTargets ? task.Path + " → " + MonthlyTargetsTab : task.Path;
        return new(item.Message, ActionLabel(item.Destination), taskId, allowed,
            allowed ? "Open " + path : $"{path} needs a different role. Ask the Owner.");
    }

    public static string Heading(DateOnly businessDate, int count) =>
        count == 1 ? $"What's missing for {businessDate:dd MMM yyyy}: 1 item" : $"What's missing for {businessDate:dd MMM yyyy}: {count} items";

    /// <summary>
    /// After Settings > Brands and targets opens, select its Monthly targets tab. The tab is found by
    /// header so the masters view needs no change. Returns false when the tab is not on screen
    /// (for example when navigation was cancelled at a draft prompt).
    /// </summary>
    public static bool SelectTab(DependencyObject? root, string header)
    {
        if (root is null) return false;
        var tab = Descendants(root).OfType<TabItem>().FirstOrDefault(x => string.Equals(x.Header?.ToString(), header, StringComparison.Ordinal));
        if (tab is null) return false;
        tab.IsSelected = true;
        return true;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
