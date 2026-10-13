extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Modules.DailyWorkflow;

using DailyStaffTargetSearch = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyStaffTargetSearch;
using SaveDailyStaffTarget = EtpApplication::Etp.Reporting.Application.DailyWorkflow.SaveDailyStaffTarget;

/// <summary>
/// 1.9.9 targets-copy: "Copy from previous month" for staff (CRO) targets. It loads the previous month's targets for the
/// selected store into an editable list; nothing is saved until the Owner presses "Save copied targets", and a target
/// already saved for the month is replaced only after the Owner confirms. Targets are Owner-only (decision D22, 16 Sep 2026).
/// 1.12 U2 (wave plan C-d): "Copy all stores from last month" does the same for every store at once; the staff name shows
/// beside each CRO number; the Replace / Keep / Cancel dialog replaces the Windows Yes / No / Cancel box; the month comes
/// from the header date (From = its first day).
/// </summary>
public partial class DailyWorkflowWorkspaceView
{
    private DataGrid staffTargetCopyGrid = null!;
    private TextBlock staffTargetCopyNote = null!;
    private Button copyStaffTargetsButton = null!;
    private Button copyAllStoresStaffTargetsButton = null!;
    private Button saveCopiedStaffTargetsButton = null!;
    public const string CopyAllStoresStaffTargetsText = "Copy all stores from last month";
    private IReadOnlyList<CopiedTargetRow> pendingStaffTargetCopy = [];
    private DateOnly pendingStaffTargetMonth;
    private string pendingStaffTargetStore = "";

    internal Func<string, TargetOverwriteChoice> ConfirmStaffTargetOverwrite { get; set; } = question =>
        TargetCopy.FromDialog(ReplaceKeepCancelDialog.Ask(Application.Current?.MainWindow, "Replace saved targets?", question));

    internal Func<string, bool> ConfirmDiscardStaffTargetCopy { get; set; } = message =>
        Application.Current?.MainWindow is { } window && ConfirmationSheet.Show(window, "Copy again", message);

    internal IReadOnlyList<CopiedTargetRow> PendingStaffTargetCopy => pendingStaffTargetCopy;

    private void InitializeStaffTargetCopy()
    {
        copyStaffTargetsButton = new Button { Content = "Copy from previous month", Margin = DesignTokens.Gap("Space8", null, null, null) };
        System.Windows.Automation.AutomationProperties.SetName(copyStaffTargetsButton, "Copy staff targets from previous month");
        copyStaffTargetsButton.Click += async (_, _) => await CopyStaffTargetsFromPreviousMonthAsync();
        copyAllStoresStaffTargetsButton = new Button { Content = CopyAllStoresStaffTargetsText, Margin = DesignTokens.Gap("Space8", null, null, null) };
        System.Windows.Automation.AutomationProperties.SetName(copyAllStoresStaffTargetsButton, "Copy staff targets of all stores from last month");
        copyAllStoresStaffTargetsButton.Click += async (_, _) => await CopyStaffTargetsFromPreviousMonthAsync(allStores: true);
        var staffTargetRow = (Panel)SaveStaffTargetButton.Parent;
        staffTargetRow.Children.Insert(staffTargetRow.Children.IndexOf(SaveStaffTargetButton) + 1, copyStaffTargetsButton);
        staffTargetRow.Children.Insert(staffTargetRow.Children.IndexOf(copyStaffTargetsButton) + 1, copyAllStoresStaffTargetsButton);

        staffTargetCopyNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = DesignTokens.Gap(null, "Space4", null, null) };
        staffTargetCopyGrid = CopiedTargetGrid.Create("Staff targets copied from the previous month (not saved)", withCro: true);
        saveCopiedStaffTargetsButton = new Button
        {
            Content = "Save copied targets", Margin = DesignTokens.Gap(null, "Space4", null, null),
            HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed,
        };
        saveCopiedStaffTargetsButton.SetResourceReference(StyleProperty, "PrimaryButton");
        System.Windows.Automation.AutomationProperties.SetName(saveCopiedStaffTargetsButton, "Save copied staff targets");
        saveCopiedStaffTargetsButton.Click += async (_, _) => await SaveCopiedStaffTargetsAsync();
        StaffTargetCopyHost.Children.Add(staffTargetCopyNote);
        StaffTargetCopyHost.Children.Add(staffTargetCopyGrid);
        StaffTargetCopyHost.Children.Add(saveCopiedStaffTargetsButton);
    }

    private void RefreshStaffTargetCopyAccess(DailyWorkflowWorkspaceAccess current)
    {
        // InitializeComponent raises scope events (and so this refresh) before the copy controls exist.
        if (copyStaffTargetsButton is null) return;
        // D22: every staff-target edit is Owner-only, so the row's inputs follow the Save button.
        foreach (var input in new Control[] { StaffTargetCroInput, StaffTargetFromInput, StaffTargetToInput, StaffTargetValueInput, StaffTargetReasonInput })
            input.IsEnabled = current.CanAdminister;
        copyStaffTargetsButton.IsEnabled = current.CanAdminister;
        copyAllStoresStaffTargetsButton.IsEnabled = current.CanAdminister;
        saveCopiedStaffTargetsButton.IsEnabled = current.CanAdminister;
        var reason = current.CanAdminister ? null : StaffTargetsNeedOwnerMessage;
        copyStaffTargetsButton.ToolTip = reason;
        copyAllStoresStaffTargetsButton.ToolTip = reason;
        saveCopiedStaffTargetsButton.ToolTip = reason;
        ToolTipService.SetShowOnDisabled(copyStaffTargetsButton, true);
        ToolTipService.SetShowOnDisabled(copyAllStoresStaffTargetsButton, true);
        ToolTipService.SetShowOnDisabled(saveCopiedStaffTargetsButton, true);
    }

    public async Task CopyStaffTargetsFromPreviousMonthAsync(bool allStores = false)
    {
        if (!allStores && string.IsNullOrWhiteSpace(StoreCode)) { Publish("Choose one store in the header."); return; }
        // C-d: the month comes from the header date (the From box follows it); an Owner who changed From copies into that month.
        var target = TargetCopy.MonthStart(DateOnly.FromDateTime(StaffTargetFromInput.SelectedDate ?? BusinessDateInput.SelectedDate ?? DateTime.Today));
        var previous = TargetCopy.PreviousMonth(target);
        try { RequireOwnerAccess(); }
        catch (Exception exception) { PublishFailure(exception, "STAFF_TARGET_COPY_FAILED", "Staff targets were not copied", StaffTargetsNeedOwnerMessage); return; }
        if (pendingStaffTargetCopy.Count > 0 && !ConfirmDiscardStaffTargetCopy(
                $"Copied staff targets for {TargetCopy.MonthLabel(pendingStaffTargetMonth)} are not saved yet. Replace them with a new copy?"))
            return;
        if (!BeginOperation()) return;
        try
        {
            string? store = allStores ? null : SelectedScope().StoreCode;
            var saved = await queryFactory().LoadStaffTargetsAsync(
                new DailyStaffTargetSearch(previous, TargetCopy.MonthEnd(target), store is null ? null : [store]));
            var plan = TargetCopy.Plan(
                saved.Where(row => store is null || string.Equals(row.StoreCode, store, StringComparison.OrdinalIgnoreCase))
                    .Select(row => new TargetCopySource(row.StoreCode, row.CroNumber, row.PeriodStart, row.TargetSales)),
                target);
            if (plan.Count > 0) plan = TargetCopy.WithStaffNames(plan, await StaffNameDirectory.TryLoadAsync());
            var scopeText = store ?? "all stores";
            ShowStaffTargetCopy(plan, scopeText, target);
            Publish(plan.Count == 0
                ? $"No staff targets are saved for {scopeText} in {TargetCopy.MonthLabel(previous)}. Nothing was copied."
                : $"Copied {plan.Count} staff target(s) for {scopeText} from {TargetCopy.MonthLabel(previous)} into {TargetCopy.MonthLabel(target)}. Nothing is saved yet: check the values, then press Save copied targets.");
        }
        catch (Exception exception) { PublishFailure(exception, "STAFF_TARGET_COPY_FAILED", "Staff targets were not copied", StaffTargetsNeedOwnerMessage); }
        finally { EndOperation(); }
    }

    public async Task SaveCopiedStaffTargetsAsync()
    {
        if (pendingStaffTargetCopy.Count == 0) { Publish("Press Copy from previous month first."); return; }
        if (!BeginOperation()) return;
        try
        {
            RequireOwnerAccess();
            staffTargetCopyGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var resolved = TargetCopy.Resolve(pendingStaffTargetCopy);
            if (resolved.Any(row => row.ReplacesExisting))
            {
                var choice = ConfirmStaffTargetOverwrite(TargetCopy.OverwriteQuestion(resolved, pendingStaffTargetMonth));
                if (choice == TargetOverwriteChoice.Cancel) { Publish("Nothing was saved. The copied staff targets are still shown."); return; }
                resolved = TargetCopy.Apply(resolved, choice);
            }
            var month = TargetCopy.MonthLabel(pendingStaffTargetMonth);
            var reason = string.IsNullOrWhiteSpace(StaffTargetReasonInput.Text)
                ? $"Copied from {TargetCopy.MonthLabel(TargetCopy.PreviousMonth(pendingStaffTargetMonth))}"
                : StaffTargetReasonInput.Text.Trim();
            var commands = commandsFactory();
            var saved = 0;
            foreach (var row in resolved)
            {
                // Each row keeps its own store ("all stores" copies several).
                await commands.SaveStaffTargetAsync(new SaveDailyStaffTarget(
                    row.StoreCode, row.CroNumber!, pendingStaffTargetMonth, TargetCopy.MonthEnd(pendingStaffTargetMonth),
                    row.TargetSales, Environment.UserName, reason));
                saved++;
            }
            ShowStaffTargetCopy([], pendingStaffTargetStore, pendingStaffTargetMonth);
            if (saved == 0) { Publish($"No staff target for {month} needed changing. Nothing was saved."); return; }
            InvalidatePack();
            StaffTargetReasonInput.Clear();
            await recordAuditAsync("StaffTarget", "Succeeded", $"{saved} staff target(s) copied from the previous month and saved");
            await RelayDashboardRefreshAsync();
            Publish($"Saved {saved} staff target(s) for {month}.");
        }
        catch (Exception exception) { PublishFailure(exception, "STAFF_TARGET_COPY_SAVE_FAILED", "Copied staff targets were not all saved", StaffTargetsNeedOwnerMessage); }
        finally { EndOperation(); }
    }

    private void ShowStaffTargetCopy(IReadOnlyList<CopiedTargetRow> rows, string store, DateOnly target)
    {
        pendingStaffTargetCopy = rows;
        pendingStaffTargetStore = store;
        pendingStaffTargetMonth = target;
        staffTargetCopyGrid.ItemsSource = rows;
        var visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        staffTargetCopyGrid.Visibility = visibility;
        staffTargetCopyNote.Visibility = visibility;
        saveCopiedStaffTargetsButton.Visibility = visibility;
        staffTargetCopyNote.Text = rows.Count > 0
            ? $"Copied from {TargetCopy.MonthLabel(TargetCopy.PreviousMonth(target))} for {store}, {TargetCopy.MonthLabel(target)} - not saved. Edit a value or clear it to skip that CRO; a target already saved for {TargetCopy.MonthLabel(target)} is replaced only after you confirm."
            : "";
    }
}
