using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U2 (wave plan C-d, month start): the staff-target period follows the header month, the Replace / Keep / Cancel dialog
/// replaces the Windows box, "Copy all stores from last month" for monthly and staff targets (staff Owner-only, decision 27),
/// staff names beside CRO numbers, and "Save copied targets" highlighted until it is pressed. Synthetic store codes only.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class TargetCopyMonthStartTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly September = new(2026, 9, 1);

    [Theory]
    [InlineData("2026-11-01", "2026-11-01", "2026-11-30")]
    [InlineData("2026-10-31", "2026-10-01", "2026-10-31")]
    [InlineData("2027-01-01", "2027-01-01", "2027-01-31")]
    [InlineData("2026-12-31", "2026-12-01", "2026-12-31")]
    [InlineData("2028-02-29", "2028-02-01", "2028-02-29")]
    [InlineData("2027-02-15", "2027-02-01", "2027-02-28")]
    public void The_staff_target_period_is_the_header_dates_month(string header, string from, string to)
    {
        var (start, end) = TargetCopy.DefaultPeriod(DateOnly.Parse(header, CultureInfo.InvariantCulture));
        Assert.Equal((DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture)), (start, end));
    }

    [Fact]
    public void The_close_day_screen_opens_the_staff_target_period_on_the_header_month_and_follows_a_new_header_date()
    {
        RunSta(() =>
        {
            var view = CreateStaffView(new StaffTargetQuery([]), new RecordingCommands(), new(true, true, true));
            view.BusinessDate = new DateTime(2026, 11, 1);
            Assert.Equal(new DateTime(2026, 11, 1), Find<DatePicker>(view, "Staff target start date").SelectedDate);
            Assert.Equal(new DateTime(2026, 11, 30), Find<DatePicker>(view, "Staff target end date").SelectedDate);
            view.BusinessDate = new DateTime(2026, 10, 31);
            Assert.Equal(new DateTime(2026, 10, 1), Find<DatePicker>(view, "Staff target start date").SelectedDate);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(ReplaceKeepCancelChoice.Replace, TargetOverwriteChoice.ReplaceExisting)]
    [InlineData(ReplaceKeepCancelChoice.Keep, TargetOverwriteChoice.KeepExisting)]
    [InlineData(ReplaceKeepCancelChoice.Cancel, TargetOverwriteChoice.Cancel)]
    public void The_dialog_answer_maps_to_the_overwrite_choice(ReplaceKeepCancelChoice choice, TargetOverwriteChoice expected) =>
        Assert.Equal(expected, TargetCopy.FromDialog(choice));

    [Fact]
    public void The_question_names_the_rows_and_no_longer_speaks_of_yes_and_no()
    {
        var question = TargetCopy.OverwriteQuestion([new("TST02", null, 2000m, true), new("TST01", null, 1000m, false)], October);
        Assert.Contains("TST02", question);
        Assert.DoesNotContain("TST01", question);
        Assert.DoesNotContain("Yes:", question);
        Assert.DoesNotContain("No:", question);
    }

    [Fact]
    public void The_replace_keep_cancel_dialog_is_L3_with_cancel_first_and_replace_primary_last()
    {
        RunSta(() =>
        {
            var dialog = new ReplaceKeepCancelDialog(null, "Replace saved targets?", "2 target(s) are already saved.");
            Assert.Equal(480, dialog.Width);
            var actions = (WrapPanel)dialog.ReplaceButton.Parent;
            Assert.Equal([ReplaceKeepCancelDialog.CancelText, ReplaceKeepCancelDialog.KeepText, ReplaceKeepCancelDialog.ReplaceText],
                actions.Children.OfType<Button>().Select(button => (string)button.Content));
            Assert.True(dialog.CancelButton.IsCancel);
            Assert.Equal(ReplaceKeepCancelChoice.Cancel, dialog.Choice);
            dialog.KeepButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(ReplaceKeepCancelChoice.Keep, dialog.Choice);
            dialog.ReplaceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(ReplaceKeepCancelChoice.Replace, dialog.Choice);
            dialog.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Staff_names_show_beside_the_cro_numbers_they_belong_to()
    {
        var rows = TargetCopy.WithStaffNames(
            [new CopiedTargetRow("TST01", "101", 10m, null, "10"), new CopiedTargetRow("TST02", "101", 20m, null, "20"), new CopiedTargetRow("TST01", null, 5m, null, "5")],
            [new StaffNameEntry("tst01", "101", "Synthetic A"), new StaffNameEntry("TST03", "101", "Synthetic C")]);

        Assert.Equal(["Synthetic A", null, null], rows.Select(row => row.StaffName));
        Assert.Equal("10", rows[0].NewTarget);
    }

    [Fact]
    public void Monthly_copy_of_all_stores_highlights_save_until_it_is_pressed()
    {
        RunSta(() =>
        {
            var saved = new List<MonthlyTargetRow> { new("TST01", September, 1000m), new("TST02", September, 2000m) };
            var view = new EveningMastersView(EveningMastersServices.Unavailable, () => true, () => true)
            {
                TargetLoader = () => Task.FromResult<IReadOnlyList<MonthlyTargetRow>>(saved.ToArray()),
                TargetSaver = row => { saved.Add(row); return Task.CompletedTask; }
            };
            Find<DatePicker>(view, "Target month").SelectedDate = new DateTime(2026, 10, 3);
            Assert.False(view.SaveCopiedIsHighlighted);

            Click(FindButton(view, EveningMastersView.CopyAllStoresText));
            Assert.True(view.SaveCopiedIsHighlighted);
            Assert.Contains("all stores", view.StatusText);

            Click(FindButton(view, EveningMastersView.SaveCopiedText));
            Assert.False(view.SaveCopiedIsHighlighted);
            Assert.Equal(2, saved.Count(row => row.Month == October));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(TargetOverwriteChoice.KeepExisting, "TST01/101=1000,TST01/103=3000")]
    [InlineData(TargetOverwriteChoice.ReplaceExisting, "TST01/101=1000,TST02/102=2000,TST01/103=3000")]
    public void Staff_copy_of_all_stores_saves_each_row_under_its_own_store_and_keep_leaves_the_existing_one(TargetOverwriteChoice choice, string expected)
    {
        RunSta(async () =>
        {
            var modified = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
            var query = new StaffTargetQuery(
            [
                new("TST01", "101", September, new(2026, 9, 30), 1000m, modified, "owner"),
                new("TST02", "102", September, new(2026, 9, 30), 2000m, modified, "owner"),
                new("TST01", "103", September, new(2026, 9, 30), 3000m, modified, "owner"),
                new("TST02", "102", October, new(2026, 10, 31), 1800m, modified, "owner"),
            ]);
            var commands = new RecordingCommands();
            var view = CreateStaffView(query, commands, new(true, true, true));
            view.ConfirmStaffTargetOverwrite = _ => choice;
            view.BusinessDate = new DateTime(2026, 10, 9);

            await view.CopyStaffTargetsFromPreviousMonthAsync(allStores: true);

            Assert.Null(query.LastSearch!.StoreCodes);
            Assert.Equal(3, view.PendingStaffTargetCopy.Count);
            Assert.Empty(commands.Saved);

            await view.SaveCopiedStaffTargetsAsync();

            Assert.Equal(expected, string.Join(",", commands.Saved.OrderBy(row => row.CroNumber, StringComparer.Ordinal)
                .Select(row => $"{row.StoreCode}/{row.CroNumber}={row.TargetSales:0}")));
            Assert.All(commands.Saved, row => Assert.Equal((October, new DateOnly(2026, 10, 31)), (row.PeriodStart, row.PeriodEnd)));
        });
    }

    [Fact]
    public void A_store_manager_cannot_copy_staff_targets_of_all_stores()
    {
        RunSta(async () =>
        {
            var query = new StaffTargetQuery([]);
            var view = CreateStaffView(query, new RecordingCommands(), new(true, true, false));
            view.RefreshAccessState();
            Assert.False(FindButton(view, "Copy staff targets of all stores from last month").IsEnabled);
            await view.CopyStaffTargetsFromPreviousMonthAsync(allStores: true);
            Assert.Equal(0, query.Calls);
            Assert.Contains(DailyWorkflowWorkspaceView.StaffTargetsNeedOwnerMessage, view.StatusText);
        });
    }

    private static DailyWorkflowWorkspaceView CreateStaffView(IDailyWorkflowQuery query, IDailyWorkflowCommands commands, DailyWorkflowWorkspaceAccess access) => new(
        new DailyWorkflowPresentationSession(), () => query, () => commands,
        () => throw new InvalidOperationException("No pack expected."), () => access, (_, _, _) => Task.CompletedTask,
        (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask)
        { StoreCode = "TST01", BusinessDate = new DateTime(2026, 10, 9) };

    private sealed class StaffTargetQuery(IReadOnlyList<DailyStaffSalesTarget> rows) : IDailyWorkflowQuery
    {
        public int Calls { get; private set; }
        public DailyStaffTargetSearch? LastSearch { get; private set; }
        public Task<DailyWorkflowState> LoadAsync(DailyWorkflowScope scope, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No readiness load expected.");
        public Task<IReadOnlyList<DailyManualStockCount>> LoadStockCountsAsync(DailyWorkflowScope scope, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DailyManualStockCount>>([]);
        public Task<IReadOnlyList<DailyStaffSalesTarget>> LoadStaffTargetsAsync(DailyStaffTargetSearch search, CancellationToken cancellationToken = default)
        { Calls++; LastSearch = search; return Task.FromResult(rows); }
    }

    private sealed class RecordingCommands : IDailyWorkflowCommands
    {
        public List<SaveDailyStaffTarget> Saved { get; } = [];
        public Task SaveManualInputAsync(SaveDailyManualInput command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveStockCountAsync(SaveDailyStockCount command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveStaffTargetAsync(SaveDailyStaffTarget command, CancellationToken cancellationToken = default) { Saved.Add(command); return Task.CompletedTask; }
        public Task FinaliseAsync(FinaliseDailyWorkflow command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReopenAsync(ReopenDailyWorkflow command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button FindButton(DependencyObject root, string name) =>
        Descendants(root).OfType<Button>().Single(button => Equals(button.Content, name) || AutomationProperties.GetName(button) == name);

    private static T Find<T>(DependencyObject root, string automationName) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(item => AutomationProperties.GetName(item) == automationName);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
