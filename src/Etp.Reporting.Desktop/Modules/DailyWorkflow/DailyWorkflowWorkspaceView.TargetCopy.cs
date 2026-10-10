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
/// </summary>
public partial class DailyWorkflowWorkspaceView
{
    private DataGrid staffTargetCopyGrid = null!;
    private TextBlock staffTargetCopyNote = null!;
    private Button copyStaffTargetsButton = null!;
    private Button saveCopiedStaffTargetsButton = null!;
    private IReadOnlyList<CopiedTargetRow> pendingStaffTargetCopy = [];
    private DateOnly pendingStaffTargetMonth;
    private string pendingStaffTargetStore = "";

    internal Func<string, TargetOverwriteChoice> ConfirmStaffTargetOverwrite { get; set; } = question =>
        MessageBox.Show(question, "Replace saved targets?", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => TargetOverwriteChoice.ReplaceExisting,
            MessageBoxResult.No => TargetOverwriteChoice.KeepExisting,
            _ => TargetOverwriteChoice.Cancel,
        };

    internal Func<string, bool> ConfirmDiscardStaffTargetCopy { get; set; } = message =>
        MessageBox.Show(message, "Copy from previous month", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    internal IReadOnlyList<CopiedTargetRow> PendingStaffTargetCopy => pendingStaffTargetCopy;

    private void InitializeStaffTargetCopy()
    {
        copyStaffTargetsButton = new Button { Content = "Copy from previous month", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(6, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(copyStaffTargetsButton, "Copy staff targets from previous month");
        copyStaffTargetsButton.Click += async (_, _) => await CopyStaffTargetsFromPreviousMonthAsync();
        ((Panel)SaveStaffTargetButton.Parent).Children.Add(copyStaffTargetsButton);

        staffTargetCopyNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        staffTargetCopyGrid = CopiedTargetGrid.Create("Staff targets copied from the previous month (not saved)", withCro: true);
        saveCopiedStaffTargetsButton = new Button
        {
            Content = "Save copied targets", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 4, 0, 0),
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
        copyStaffTargetsButton.IsEnabled = current.CanAdminister;
        saveCopiedStaffTargetsButton.IsEnabled = current.CanAdminister;
        var reason = current.CanAdminister ? null : "Owner permission is required to copy targets.";
        copyStaffTargetsButton.ToolTip = reason;
        saveCopiedStaffTargetsButton.ToolTip = reason;
    }

    public async Task CopyStaffTargetsFromPreviousMonthAsync()
    {
        if (string.IsNullOrWhiteSpace(StoreCode)) { Publish("Choose one store in the header."); return; }
        var target = TargetCopy.MonthStart(DateOnly.FromDateTime(StaffTargetFromInput.SelectedDate ?? DateTime.Today));
        var previous = TargetCopy.PreviousMonth(target);
        try { RequireOwnerAccess(); }
        catch (Exception exception) { PublishFailure(exception, "STAFF_TARGET_COPY_FAILED", "Staff targets were not copied", "Owner permission is required."); return; }
        if (pendingStaffTargetCopy.Count > 0 && !ConfirmDiscardStaffTargetCopy(
                $"Copied staff targets for {TargetCopy.MonthLabel(pendingStaffTargetMonth)} are not saved yet. Replace them with a new copy?"))
            return;
        if (!BeginOperation()) return;
        try
        {
            var store = SelectedScope().StoreCode;
            var saved = await queryFactory(connectionString()).LoadStaffTargetsAsync(
                new DailyStaffTargetSearch(previous, TargetCopy.MonthEnd(target), [store]));
            var plan = TargetCopy.Plan(
                saved.Where(row => string.Equals(row.StoreCode, store, StringComparison.OrdinalIgnoreCase))
                    .Select(row => new TargetCopySource(row.StoreCode, row.CroNumber, row.PeriodStart, row.TargetSales)),
                target);
            ShowStaffTargetCopy(plan, store, target);
            Publish(plan.Count == 0
                ? $"No staff targets are saved for {store} in {TargetCopy.MonthLabel(previous)}. Nothing was copied."
                : $"Copied {plan.Count} staff target(s) for {store} from {TargetCopy.MonthLabel(previous)} into {TargetCopy.MonthLabel(target)}. Nothing is saved yet: check the values, then press Save copied targets.");
        }
        catch (Exception exception) { PublishFailure(exception, "STAFF_TARGET_COPY_FAILED", "Staff targets were not copied", "Owner permission is required."); }
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
            var commands = commandsFactory(connectionString());
            var saved = 0;
            foreach (var row in resolved)
            {
                await commands.SaveStaffTargetAsync(new SaveDailyStaffTarget(
                    pendingStaffTargetStore, row.CroNumber!, pendingStaffTargetMonth, TargetCopy.MonthEnd(pendingStaffTargetMonth),
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
        catch (Exception exception) { PublishFailure(exception, "STAFF_TARGET_COPY_SAVE_FAILED", "Copied staff targets were not all saved", "Owner permission is required."); }
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
