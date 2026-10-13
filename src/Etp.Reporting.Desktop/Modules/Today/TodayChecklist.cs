extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using DailyReadinessDestination = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessDestination;
using DailyReadinessItemKind = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessItemKind;
using DailyReadinessItem = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessItem;
using DailyReadinessQuery = EtpApplication::Etp.Reporting.Application.DailyReadiness.IDailyReadinessQuery;
using DailyReadinessRequest = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessRequest;
using DailyReadinessResult = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessResult;
using NoSaleDay = EtpApplication::Etp.Reporting.Application.NoSaleDays.NoSaleDay;

namespace Etp.Reporting.Desktop.Modules.Today;

/// <summary>
/// One line of the Today "What's missing" checklist: the readiness message, the button that opens
/// the screen where the item is fixed, and whether the signed-in role may open that screen.
/// </summary>
public sealed record TodayChecklistLine(string Message, string ActionLabel, string TaskId, bool CanOpen, string? Tooltip,
    string StoreCode = "", string ShopName = "", string PlainMessage = "", TodayChecklistSection Section = TodayChecklistSection.EnterNow, string Code = "");

/// <summary>
/// 1.11.2 (UX audit top 6): who can act on a line. Exports come from the shop PC, so the Owner can only wait for them
/// (or ask); manual inputs the Owner can type in himself; targets are a monthly job, kept apart from the day's files.
/// </summary>
public enum TodayChecklistSection
{
    /// <summary>A Titan or Helios export that has not arrived.</summary>
    WaitingForShop,
    /// <summary>A manual input (walk-ins, cash) the Owner can enter now, or leave for the shop daily sheet.</summary>
    EnterNow,
    /// <summary>A monthly or staff target.</summary>
    ThisMonth
}

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
        DailyReadinessDestination.ImportIntake => "Open Import",
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

    public static IReadOnlyList<TodayChecklistLine> Lines(DailyReadinessResult result, ShellAccess access, Func<string, string?>? shopName = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(access);
        return result.Missing.Select(item => Line(item, access, shopName)).ToArray();
    }

    /// <summary>The plain report name for an export code in a missing-file line (the readiness message speaks in codes).</summary>
    public static string ExportName(string code) => code switch
    {
        "R025" => "Sales lines",
        "R022" => "Invoice tenders",
        "R011" => "Closing stock",
        "R030" => "Stock ledger",
        _ => code.Contains("SERVICE", StringComparison.OrdinalIgnoreCase) ? "Service files" : code
    };

    public static string InputName(string code) => code switch
    {
        "WALK_INS" => "Walk-ins",
        "OPENING_CASH" => "Opening cash",
        "EXPENSES" => "Expenses",
        "CASH_DEPOSIT" => "Cash deposit",
        _ => code
    };

    public static TodayChecklistSection SectionFor(DailyReadinessItemKind kind) => kind switch
    {
        DailyReadinessItemKind.MissingExport => TodayChecklistSection.WaitingForShop,
        DailyReadinessItemKind.MissingManualInput => TodayChecklistSection.EnterNow,
        _ => TodayChecklistSection.ThisMonth
    };

    /// <summary>
    /// The line in words, without codes and without the shop (the panel groups by shop; a target line, which is listed
    /// under "This month", names its shop). The original readiness message stays in <see cref="TodayChecklistLine.Message"/>.
    /// </summary>
    public static string PlainMessage(DailyReadinessItem item, string shopName) => item.Kind switch
    {
        DailyReadinessItemKind.MissingExport => $"{ExportName(item.Code)} - waiting for the shop's file",
        DailyReadinessItemKind.MissingManualInput => $"{InputName(item.Code)} - not entered yet. You can enter it, or it arrives with the shop's daily sheet",
        DailyReadinessItemKind.MissingMonthlyTarget => $"Monthly sales target for {shopName} is not set",
        DailyReadinessItemKind.MissingStaffTargets => $"Staff targets for {shopName} are not set",
        _ => item.Message
    };

    /// <summary>The shop name for its code, or the code when the shop list does not know it.</summary>
    public static string ShopName(string storeCode, Func<string, string?>? shopNames)
    {
        if (string.IsNullOrWhiteSpace(storeCode)) return "";
        var name = shopNames?.Invoke(storeCode);
        return string.IsNullOrWhiteSpace(name) ? storeCode : name!;
    }

    private static TodayChecklistLine Line(DailyReadinessItem item, ShellAccess access, Func<string, string?>? shopNames)
    {
        var shop = ShopName(item.StoreCode, shopNames);
        var taskId = TaskFor(item.Destination);
        var task = TaskNavigation.Find(taskId) ?? throw new InvalidOperationException($"Task {taskId} is not registered.");
        var allowed = task.IsAllowed(access);
        var path = item.Destination == DailyReadinessDestination.SettingsMonthlyTargets ? task.Path + " → " + MonthlyTargetsTab : task.Path;
        return new(item.Message, ActionLabel(item.Destination), taskId, allowed,
            allowed ? "Open " + path : $"{path} needs a different role. Ask the Owner.",
            item.StoreCode, shop, PlainMessage(item, shop), SectionFor(item.Kind), item.Code);
    }

    /// <summary>
    /// 1.12 U1 (decision 52): a shop whose sales lines file has not arrived can be marked as a no-sale day from its group,
    /// by an Owner or a Store Manager. N1's procedure still refuses a day that has sales, a locked day and a future day.
    /// </summary>
    public static bool CanMarkNoSale(IEnumerable<TodayChecklistLine> shopLines, ShellAccess access) =>
        access.CanImport && shopLines.Any(line => line.Section == TodayChecklistSection.WaitingForShop
            && string.Equals(line.Code, "R025", StringComparison.OrdinalIgnoreCase) && line.StoreCode.Length > 0);

    /// <summary>The line for a marked store-day (its exports and entries are no longer asked for): who marked it and when.</summary>
    public static string NoSaleLine(NoSaleDay mark) =>
        $"No-sale day (marked by {mark.MarkedBy} on {mark.MarkedUtc.ToLocalTime().ToString("dd MMM", Etp.Reporting.Reporting.IndianFormat.Culture)})";

    public static string NoSaleHeading(DateOnly businessDate, int stores) =>
        stores == 1 ? $"No-sale day for {businessDate:dd MMM yyyy}: 1 store" : $"No-sale days for {businessDate:dd MMM yyyy}: {stores} stores";

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
