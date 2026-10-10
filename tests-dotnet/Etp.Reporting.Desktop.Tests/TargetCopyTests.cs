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

/// <summary>1.9.9 targets-copy: "Copy from previous month" for monthly store targets and staff (CRO) targets.</summary>
[Collection(WpfViewCollection.Name)]
public sealed class TargetCopyTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly September = new(2026, 9, 1);

    [Fact]
    public void Plan_takes_the_previous_month_rows_and_shows_the_saved_value_of_the_month_being_filled()
    {
        var plan = TargetCopy.Plan(
        [
            new("TST02", null, September, 2000m),
            new("TST01", null, September, 1000.5m),
            new("TST02", null, October, 1500m),
            new("TST03", null, new(2026, 8, 1), 900m),
            new("TST04", null, October, 700m),
        ], new DateOnly(2026, 10, 17), Invariant);

        Assert.Equal(["TST01", "TST02"], plan.Select(row => row.StoreCode));
        Assert.Equal("1000.5", plan[0].NewTarget);
        Assert.Null(plan[0].CurrentTarget);
        Assert.Equal(2000m, plan[1].PreviousTarget);
        Assert.Equal(1500m, plan[1].CurrentTarget);
    }

    [Fact]
    public void Plan_for_january_copies_december_of_the_previous_year_and_keys_staff_rows_by_cro()
    {
        var plan = TargetCopy.Plan(
        [
            new("TST01", "101", new(2026, 12, 1), 10m),
            new("TST01", "102", new(2026, 12, 1), 20m),
            new("TST01", "102", new(2027, 1, 1), 25m),
        ], new DateOnly(2027, 1, 5), Invariant);

        Assert.Equal(["101", "102"], plan.Select(row => row.CroNumber));
        Assert.Null(plan[0].CurrentTarget);
        Assert.Equal(25m, plan[1].CurrentTarget);
        Assert.Equal(new DateOnly(2026, 12, 1), TargetCopy.PreviousMonth(new DateOnly(2027, 1, 31)));
        Assert.Equal(new DateOnly(2027, 2, 28), TargetCopy.MonthEnd(new DateOnly(2027, 2, 10)));
    }

    [Fact]
    public void Resolve_skips_blank_and_unchanged_rows_and_flags_rows_that_replace_a_saved_target()
    {
        var rows = new[]
        {
            new CopiedTargetRow("TST01", null, 1000m, null, "1000"),
            new CopiedTargetRow("TST02", null, 2000m, 1500m, "2000"),
            new CopiedTargetRow("TST03", null, 3000m, 3000m, "3000"),
            new CopiedTargetRow("TST04", null, 4000m, null, " "),
        };

        var resolved = TargetCopy.Resolve(rows, Invariant);

        Assert.Equal([("TST01", 1000m, false), ("TST02", 2000m, true)],
            resolved.Select(row => (row.StoreCode, row.TargetSales, row.ReplacesExisting)));
        Assert.Equal(resolved, TargetCopy.Apply(resolved, TargetOverwriteChoice.ReplaceExisting));
        Assert.Equal(["TST01"], TargetCopy.Apply(resolved, TargetOverwriteChoice.KeepExisting).Select(row => row.StoreCode));
        Assert.Empty(TargetCopy.Apply(resolved, TargetOverwriteChoice.Cancel));
        Assert.Contains("TST02", TargetCopy.OverwriteQuestion(resolved, October));
        Assert.Contains("Oct 2026", TargetCopy.OverwriteQuestion(resolved, October));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    public void Resolve_refuses_an_invalid_or_negative_value_and_names_the_row(string value)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            TargetCopy.Resolve([new CopiedTargetRow("TST01", "101", 10m, null, value)], Invariant));
        Assert.Contains("TST01 CRO 101", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Monthly_target_copy_buttons_are_owner_only(bool owner)
    {
        RunSta(() =>
        {
            var view = new EveningMastersView(() => throw new InvalidOperationException("No database call expected."), () => owner, () => true);
            Assert.Equal(owner, FindButton(view, "Copy from previous month").IsEnabled);
            Assert.Equal(owner, FindButton(view, "Save copied targets").IsEnabled);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Non_owner_monthly_copy_loads_nothing()
    {
        RunSta(() =>
        {
            var loads = 0;
            var view = new EveningMastersView(() => throw new InvalidOperationException("No database call expected."), () => false, () => true)
            { TargetLoader = () => { loads++; return Task.FromResult<IReadOnlyList<MonthlyTargetRow>>([]); } };
            Click(FindButton(view, "Copy from previous month"));
            Assert.Equal(0, loads);
            Assert.Contains("Owner permission is required", view.StatusText);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(TargetOverwriteChoice.Cancel, "")]
    [InlineData(TargetOverwriteChoice.KeepExisting, "TST01=1100")]
    [InlineData(TargetOverwriteChoice.ReplaceExisting, "TST01=1100,TST02=2000")]
    public void Monthly_copy_prefills_without_saving_and_replaces_a_saved_target_only_when_confirmed(TargetOverwriteChoice choice, string expected)
    {
        RunSta(() =>
        {
            var saved = new List<MonthlyTargetRow>
            {
                new("TST01", September, 1000m), new("TST02", September, 2000m), new("TST02", October, 1500m),
            };
            var writes = new List<MonthlyTargetRow>();
            var questions = new List<string>();
            var view = new EveningMastersView(() => throw new InvalidOperationException("No database call expected."), () => true, () => true)
            {
                TargetLoader = () => Task.FromResult<IReadOnlyList<MonthlyTargetRow>>(saved.ToArray()),
                TargetSaver = row => { writes.Add(row); return Task.CompletedTask; },
                ConfirmOverwrite = question => { questions.Add(question); return choice; },
            };
            Find<DatePicker>(view, "Target month").SelectedDate = new DateTime(2026, 10, 12);

            Click(FindButton(view, "Copy from previous month"));

            Assert.Empty(writes);
            Assert.Contains("Nothing is saved yet", view.StatusText);
            var grid = Find<DataGrid>(view, "Monthly targets copied from the previous month (not saved)");
            Assert.Equal(Visibility.Visible, grid.Visibility);
            var rows = grid.ItemsSource.Cast<CopiedTargetRow>().ToArray();
            Assert.Equal(["TST01", "TST02"], rows.Select(row => row.StoreCode));
            Assert.Equal(1500m, rows[1].CurrentTarget);
            rows[0].NewTarget = 1100m.ToString(CultureInfo.CurrentCulture);

            Click(FindButton(view, "Save copied targets"));

            Assert.Single(questions);
            Assert.Equal(expected, string.Join(",", writes.Select(row => $"{row.StoreCode}={row.TargetSales:0}")));
            Assert.All(writes, row => Assert.Equal(October, row.Month));
            Assert.Equal(choice == TargetOverwriteChoice.Cancel ? Visibility.Visible : Visibility.Collapsed, grid.Visibility);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Monthly_copy_with_no_previous_month_rows_says_so()
    {
        RunSta(() =>
        {
            var view = new EveningMastersView(() => throw new InvalidOperationException("No database call expected."), () => true, () => true)
            { TargetLoader = () => Task.FromResult<IReadOnlyList<MonthlyTargetRow>>([new("TST01", October, 10m)]) };
            Find<DatePicker>(view, "Target month").SelectedDate = new DateTime(2026, 10, 1);
            Click(FindButton(view, "Copy from previous month"));
            Assert.Contains("No monthly targets are saved for Sep 2026", view.StatusText);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Staff_target_copy_is_owner_only_and_a_store_manager_loads_nothing()
    {
        RunSta(async () =>
        {
            var query = new StaffTargetQuery([]);
            var view = CreateStaffView(query, new RecordingCommands(), new(true, true, false));
            view.RefreshAccessState();
            Assert.False(FindButton(view, "Copy staff targets from previous month").IsEnabled);
            await view.CopyStaffTargetsFromPreviousMonthAsync();
            Assert.Equal(0, query.Calls);
            Assert.Contains("Owner permission is required", view.StatusText);
        });
    }

    [Theory]
    [InlineData(TargetOverwriteChoice.Cancel, "")]
    [InlineData(TargetOverwriteChoice.KeepExisting, "101=1200")]
    [InlineData(TargetOverwriteChoice.ReplaceExisting, "101=1200,102=2000")]
    public void Staff_target_copy_prefills_without_saving_and_replaces_a_saved_target_only_when_confirmed(TargetOverwriteChoice choice, string expected)
    {
        RunSta(async () =>
        {
            var modified = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
            var query = new StaffTargetQuery(
            [
                new("TST01", "101", September, new(2026, 9, 30), 1000m, modified, "owner"),
                new("TST01", "102", September, new(2026, 9, 30), 2000m, modified, "owner"),
                new("TST01", "102", October, new(2026, 10, 31), 1500m, modified, "owner"),
            ]);
            var commands = new RecordingCommands();
            var view = CreateStaffView(query, commands, new(true, true, true));
            view.ConfirmStaffTargetOverwrite = _ => choice;
            Find<DatePicker>(view, "Staff target start date").SelectedDate = new DateTime(2026, 10, 1);
            Assert.True(FindButton(view, "Copy staff targets from previous month").IsEnabled);

            await view.CopyStaffTargetsFromPreviousMonthAsync();

            Assert.Empty(commands.Saved);
            Assert.Equal(September, query.LastSearch!.PeriodStart);
            Assert.Equal(new DateOnly(2026, 10, 31), query.LastSearch.PeriodEnd);
            Assert.Equal(["TST01"], query.LastSearch.StoreCodes);
            Assert.Contains("Nothing is saved yet", view.StatusText);
            var rows = view.PendingStaffTargetCopy;
            Assert.Equal(["101", "102"], rows.Select(row => row.CroNumber));
            Assert.Equal(1500m, rows[1].CurrentTarget);
            rows[0].NewTarget = 1200m.ToString(CultureInfo.CurrentCulture);

            await view.SaveCopiedStaffTargetsAsync();

            Assert.Equal(expected, string.Join(",", commands.Saved.Select(row => $"{row.CroNumber}={row.TargetSales:0}")));
            Assert.All(commands.Saved, row =>
            {
                Assert.Equal("TST01", row.StoreCode);
                Assert.Equal(October, row.PeriodStart);
                Assert.Equal(new DateOnly(2026, 10, 31), row.PeriodEnd);
                Assert.Equal("Copied from Sep 2026", row.Reason);
            });
            Assert.Equal(choice == TargetOverwriteChoice.Cancel ? 2 : 0, view.PendingStaffTargetCopy.Count);
        });
    }

    private static DailyWorkflowWorkspaceView CreateStaffView(IDailyWorkflowQuery query, IDailyWorkflowCommands commands, DailyWorkflowWorkspaceAccess access) => new(
        new DailyWorkflowPresentationSession(), () => "Integrated Security=True", _ => query, _ => commands,
        _ => throw new InvalidOperationException("No pack expected."), () => access, (_, _, _) => Task.CompletedTask,
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
