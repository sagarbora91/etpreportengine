using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// The 1.11.0 Health check screen and the one-line System check tile against a fake IHealthCheckStore (lane L8, design 9.1,
/// decisions 33 and 36). All values are synthetic: check ids from HealthCheckId, no names, no paths, no report rows.
/// The fakes complete synchronously, so the views' awaits never leave the STA thread.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class HealthCheckViewTests
{
    private static readonly DateTime ShopHours = new(2026, 10, 11, 10, 30, 0);
    private static readonly DateTime Night = new(2026, 10, 11, 22, 0, 0);

    // ---- navigation and roles ------------------------------------------------------------------------------------

    [Fact]
    public void Both_screens_are_Owner_only_tasks_of_Settings_Database_in_the_Operations_Center()
    {
        foreach (var (id, title, alias) in new[] { ("health-check", "Health check", "self check"), ("error-register", "Error register", "error log") })
        {
            var task = TaskNavigation.Find(id);
            Assert.NotNull(task);
            Assert.Equal("Operations Centre", task.Destination);
            Assert.Equal($"Settings → Database → {title}", task.Path);
            Assert.Equal(3, task.MinimumRole);
            Assert.True(task.IsAllowed(ShellAccess.Owner));
            Assert.False(task.IsAllowed(ShellAccess.StoreManager));
            Assert.False(task.IsAllowed(ShellAccess.Viewer));
            Assert.Contains(alias, task.Aliases!);
            Assert.Contains(task, TaskNavigation.Search(alias, ShellAccess.Owner));
            Assert.DoesNotContain(task, TaskNavigation.Search(alias, ShellAccess.StoreManager));
            Assert.True(new ShellNavigationService().Navigate(task.Route, ShellAccess.Owner).IsAllowed);
            Assert.False(new ShellNavigationService().Navigate(task.Route, ShellAccess.StoreManager).IsAllowed);
            Assert.True(DiagnosticsScreens.Handles(id));
        }
    }

    [Fact]
    public void The_new_database_messages_are_curated_for_every_diagnostics_number()
    {
        foreach (var number in new[] { 51800, 51801, 51802, 51803, 51804, 51805, 51820, 51821, 51822, 51823, 51824 })
        {
            var text = DesktopFriendlyError.Describe(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, "private database detail")));
            Assert.DoesNotContain($"(error {number})", text, StringComparison.Ordinal);
            Assert.DoesNotContain("private database detail", text, StringComparison.Ordinal);
        }
        Assert.Contains("Owner", DesktopFriendlyError.Describe(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(51822, "x"))), StringComparison.Ordinal);
    }

    [Fact]
    public void A_screen_with_no_wiring_says_it_is_not_available()
    {
        RunSta(() =>
        {
            var previous = DiagnosticsScreens.Services;
            try
            {
                DiagnosticsScreens.Services = new DiagnosticsScreenServices();
                var health = Assert.IsType<HealthCheckView>(DiagnosticsScreens.Create("health-check", () => true, null));
                var register = Assert.IsType<ErrorRegisterView>(DiagnosticsScreens.Create("error-register", () => true, null));
                Assert.Equal(HealthCheckView.NotAvailableText, health.StatusText);
                Assert.Equal(ErrorRegisterView.NotAvailableText, register.StatusText);
            }
            finally { DiagnosticsScreens.Services = previous; }
        });
    }

    [Fact]
    public void A_user_who_is_not_the_Owner_sees_no_data_and_cannot_act()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var ran = 0;
            var view = NewView(store, isOwner: () => false, runner: (_, _) => { ran++; return Task.FromResult(1L); });

            view.LoadAsync().GetAwaiter().GetResult();
            view.RunAsync().GetAwaiter().GetResult();
            Assert.False(view.MarkKnownAsync().GetAwaiter().GetResult());
            Assert.False(view.EndKnownAsync().GetAwaiter().GetResult());

            Assert.Equal(HealthCheckView.OwnerOnlyText, view.StatusText);
            Assert.Equal(0, store.LoadRunsCalls);
            Assert.Equal(0, ran);
            Assert.Empty(view.Rows);
            Assert.False(view.RunEnabled);
            Assert.Empty(store.KnownSet);
        });
    }

    // ---- the lines -----------------------------------------------------------------------------------------------

    [Fact]
    public void Lines_show_Fails_first_then_Warnings_Known_and_Skipped_with_passed_lines_hidden_until_asked()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Rpt01, HealthOutcome.Pass), Result(HealthCheckId.Data01, HealthOutcome.Warning),
                Result(HealthCheckId.Ops03, HealthOutcome.Skipped), Result(HealthCheckId.Sec01, HealthOutcome.Fail), Result(HealthCheckId.Db04, HealthOutcome.Known),
                Result(HealthCheckId.Db01, HealthOutcome.Warning));
            var view = NewView(store);

            view.LoadAsync().GetAwaiter().GetResult();

            Assert.Equal(["SEC-01", "DB-01", "DATA-01", "DB-04", "OPS-03"], view.Rows.Select(row => row.Check));
            Assert.Equal(6, view.AllRows.Count);
            Assert.Contains("1 passed line is hidden", view.StatusText, StringComparison.Ordinal);

            view.ShowPassed = true;

            Assert.Equal(["SEC-01", "DB-01", "DATA-01", "DB-04", "OPS-03", "RPT-01"], view.Rows.Select(row => row.Check));
            Assert.Equal(["FAIL", "WARN", "WARN", "KNOWN", "SKIPPED", "PASS"], view.Rows.Select(row => row.Outcome));
        });
    }

    [Fact]
    public void A_line_says_where_to_fix_with_a_button_for_a_task_and_plain_text_for_an_instruction()
    {
        RunSta(() =>
        {
            var opened = new List<string>();
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21),
                Result(HealthCheckId.Db03, HealthOutcome.Fail, fix: "backups"),
                Result(HealthCheckId.Db04, HealthOutcome.Warning, fix: "run ETP setup as administrator"),
                Result(HealthCheckId.Data01, HealthOutcome.Warning, fix: "import-files", unchanged: new DateOnly(2026, 10, 4)));
            var view = NewView(store, navigate: opened.Add);

            view.LoadAsync().GetAwaiter().GetResult();

            var backups = view.Rows.Single(row => row.Check == "DB-03");
            Assert.True(backups.Fix.IsTask);
            Assert.Equal("Settings → Database → Backups", backups.FixText);
            Assert.Equal(System.Windows.Visibility.Visible, backups.FixButtonVisibility);
            var instruction = view.Rows.Single(row => row.Check == "DB-04");
            Assert.False(instruction.Fix.IsTask);
            Assert.Equal("run ETP setup as administrator", instruction.FixText);
            Assert.Equal(System.Windows.Visibility.Visible, instruction.FixTextVisibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, instruction.FixButtonVisibility);
            Assert.EndsWith("(unchanged since 04 Oct)", view.Rows.Single(row => row.Check == "DATA-01").Finding, StringComparison.Ordinal);

            view.OpenFix(backups);
            view.OpenFix(instruction);

            Assert.Equal(["backups"], opened);
        });
    }

    [Fact]
    public void The_headline_names_the_last_nightly_run_with_its_counts_and_the_business_date_it_checked()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 10, 21), new HealthCheckCounts(40, 5, 1, 2, 3), Result(HealthCheckId.Sec01, HealthOutcome.Fail));
            var view = NewView(store);

            view.LoadAsync().GetAwaiter().GetResult();

            Assert.StartsWith("Last nightly run: ", view.HeadlineText, StringComparison.Ordinal);
            Assert.Contains("checked ", view.HeadlineText, StringComparison.Ordinal);
            Assert.EndsWith("1 problem, 5 warnings, 2 known", view.HeadlineText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_database_with_no_run_says_to_run_one_and_a_load_failure_carries_a_Ref()
    {
        RunSta(() =>
        {
            var empty = NewView(new FakeStore());
            empty.LoadAsync().GetAwaiter().GetResult();
            Assert.Equal(HealthCheckView.NoRunText, empty.StatusText);

            var broken = NewView(new FakeStore { FailLoad = true });
            broken.LoadAsync().GetAwaiter().GetResult();
            Assert.Contains("could not be loaded", broken.StatusText, StringComparison.Ordinal);
            Assert.Matches(@"Ref: [0-9A-Fa-f]{12}$", broken.StatusText);
        });
    }

    [Fact]
    public void Each_line_has_its_details_with_the_sub_results_and_never_more_than_the_probe_gave()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Data01, HealthOutcome.Warning, subs:
            [
                new("HEMW", HealthOutcome.Warning, "R025_MISSING", 1m, new Dictionary<string, string> { ["family"] = "R025" }),
                new("TITAN", HealthOutcome.Pass)
            ]));
            var view = NewView(store);
            view.LoadAsync().GetAwaiter().GetResult();

            view.Select(view.Rows.Single());

            Assert.Contains("DATA-01  WARN  finding FINDING", view.DetailsText, StringComparison.Ordinal);
            Assert.Contains("HEMW: WARN R025_MISSING (1) - family R025", view.DetailsText, StringComparison.Ordinal);
            Assert.Contains("TITAN: PASS", view.DetailsText, StringComparison.Ordinal);
        });
    }

    // ---- compare -------------------------------------------------------------------------------------------------

    [Fact]
    public void Compare_with_the_previous_run_by_default_marks_the_lines_that_changed()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 10, 21), Result(HealthCheckId.Data01, HealthOutcome.Pass), Result(HealthCheckId.Db01, HealthOutcome.Warning), Result(HealthCheckId.Db02, HealthOutcome.Pass));
            store.AddRun(2, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Data01, HealthOutcome.Warning), Result(HealthCheckId.Db01, HealthOutcome.Warning), Result(HealthCheckId.Db02, HealthOutcome.Pass));
            var view = NewView(store);

            view.LoadAsync().GetAwaiter().GetResult();

            Assert.Equal(2, view.CurrentRun!.HealthCheckRunId);
            Assert.Equal(1, view.ComparedRun!.HealthCheckRunId);
            Assert.Equal(1, view.CompareChoiceCount);
            Assert.Contains("DATA-01 PASS → WARN", view.ChangesText, StringComparison.Ordinal);
            Assert.DoesNotContain("DB-01", view.ChangesText, StringComparison.Ordinal);
            var changed = view.Rows.Single(row => row.Check == "DATA-01");
            Assert.Equal("PASS", changed.EarlierText);
            Assert.Equal("PASS → WARN", changed.ChangeText);
            Assert.Equal("WARN", view.Rows.Single(row => row.Check == "DB-01").EarlierText);
            Assert.Equal("", view.Rows.Single(row => row.Check == "DB-01").ChangeText);
        });
    }

    [Fact]
    public void Compare_with_a_chosen_earlier_run_reloads_the_earlier_outcomes()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 9, 21), Result(HealthCheckId.Db01, HealthOutcome.Fail));
            store.AddRun(2, HealthCheckTrigger.Nightly, Utc(2026, 10, 10, 21), Result(HealthCheckId.Db01, HealthOutcome.Warning));
            store.AddRun(3, HealthCheckTrigger.Manual, Utc(2026, 10, 11, 21), Result(HealthCheckId.Db01, HealthOutcome.Pass));
            var view = NewView(store, runner: null);
            view.LoadAsync().GetAwaiter().GetResult();
            view.ShowPassed = true;
            Assert.Equal("WARN", view.Rows.Single().EarlierText);

            view.CompareWithAsync(1).GetAwaiter().GetResult();

            Assert.Equal("FAIL", view.Rows.Single().EarlierText);
            Assert.Contains("FAIL → PASS", view.ChangesText, StringComparison.Ordinal);
            Assert.Equal(2, view.CompareChoiceCount);
        });
    }

    [Fact]
    public void Compare_and_order_are_pure()
    {
        var changes = HealthCheckView.Compare(
            [Result("DB-01", HealthOutcome.Pass), Result("DB-02", HealthOutcome.Fail), Result("DB-03", HealthOutcome.Pass)],
            [Result("DB-01", HealthOutcome.Pass), Result("DB-02", HealthOutcome.Warning)]);
        Assert.Equal([new HealthChange("DB-02", HealthOutcome.Warning, HealthOutcome.Fail)], changes);
        Assert.Equal("", HealthCheckView.ChangesLine(null, changes));
    }

    // ---- run -----------------------------------------------------------------------------------------------------

    [Fact]
    public void During_shop_hours_a_manual_run_asks_a_one_line_question_first_and_a_no_starts_nothing()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Db01, HealthOutcome.Pass));
            var asked = new List<string>();
            var ran = 0;
            var view = NewView(store, now: () => ShopHours, confirm: text => { asked.Add(text); return false; }, runner: (_, _) => { ran++; return Task.FromResult(1L); });

            view.RunAsync().GetAwaiter().GetResult();

            Assert.Equal([HealthCheckView.ShopHoursQuestion], asked);
            Assert.Equal("This takes about a minute and reads the database. Run now?", asked[0]);
            Assert.Equal(0, ran);
            Assert.False(view.IsRunning);
            Assert.Equal("The health check was not started.", view.StatusText);
        });
    }

    [Fact]
    public void A_yes_runs_the_check_shows_its_progress_and_then_the_new_run()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 10, 21), Result(HealthCheckId.Db01, HealthOutcome.Pass));
            string? progressSeen = null;
            HealthCheckView? view = null;
            view = NewView(store, now: () => ShopHours, confirm: _ => true, runner: (progress, _) =>
            {
                progress.Report("Reports 7 of 22…");
                progressSeen = view!.ProgressText;
                store.AddRun(2, HealthCheckTrigger.Manual, Utc(2026, 10, 11, 21), Result(HealthCheckId.Db01, HealthOutcome.Warning));
                return Task.FromResult(2L);
            });

            view.RunAsync().GetAwaiter().GetResult();

            Assert.Equal("Reports 7 of 22…", progressSeen);
            Assert.Equal("", view.ProgressText);
            Assert.False(view.IsRunning);
            Assert.Equal(2, view.CurrentRun!.HealthCheckRunId);
            Assert.StartsWith("The health check finished.", view.StatusText, StringComparison.Ordinal);
            Assert.Contains("Shown: manual run", view.HeadlineText, StringComparison.Ordinal);
            Assert.True(view.RunEnabled);
        });
    }

    [Fact]
    public void Outside_shop_hours_a_run_starts_without_asking()
    {
        Assert.True(HealthCheckView.IsShopHours(new DateTime(2026, 10, 11, 9, 0, 0)));
        Assert.True(HealthCheckView.IsShopHours(new DateTime(2026, 10, 11, 20, 59, 0)));
        Assert.False(HealthCheckView.IsShopHours(new DateTime(2026, 10, 11, 8, 59, 0)));
        Assert.False(HealthCheckView.IsShopHours(new DateTime(2026, 10, 11, 21, 0, 0)));
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Manual, Utc(2026, 10, 11, 21), Result(HealthCheckId.Db01, HealthOutcome.Pass));
            var asked = 0;
            var ran = 0;
            var view = NewView(store, now: () => Night, confirm: _ => { asked++; return true; }, runner: (_, _) => { ran++; return Task.FromResult(1L); });

            view.RunAsync().GetAwaiter().GetResult();

            Assert.Equal(0, asked);
            Assert.Equal(1, ran);
        });
    }

    [Fact]
    public void A_cancelled_run_and_a_failed_run_both_leave_the_screen_usable_and_a_failure_carries_a_Ref()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Db01, HealthOutcome.Pass));
            var cancelled = NewView(store, now: () => Night, runner: (_, _) => throw new OperationCanceledException());
            cancelled.RunAsync().GetAwaiter().GetResult();
            Assert.Equal("The health check was cancelled. Nothing was changed.", cancelled.StatusText);
            Assert.False(cancelled.IsRunning);
            Assert.True(cancelled.RunEnabled);

            var failed = NewView(store, now: () => Night, runner: (_, _) => throw new NotSupportedException("private detail"));
            failed.RunAsync().GetAwaiter().GetResult();
            Assert.StartsWith("The health check could not be run.", failed.StatusText, StringComparison.Ordinal);
            Assert.DoesNotContain("private detail", failed.StatusText, StringComparison.Ordinal);
            Assert.Matches(@"Ref: [0-9A-Fa-f]{12}$", failed.StatusText);
            Assert.False(failed.IsRunning);
            Assert.True(failed.RunEnabled);
        });
    }

    [Fact]
    public void Without_a_runner_the_screen_still_shows_runs_but_cannot_start_one()
    {
        RunSta(() =>
        {
            var view = NewView(new FakeStore(), runner: null);
            view.RunAsync().GetAwaiter().GetResult();
            Assert.Equal(HealthCheckView.NotAvailableText, view.StatusText);
            Assert.False(view.RunEnabled);
        });
    }

    // ---- Known ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Mark_as_Known_takes_a_warning_a_safe_reason_and_a_date_within_90_days()
    {
        var today = new DateOnly(2026, 10, 11);
        Assert.Null(HealthCheckView.ValidateKnown("Starting daily entry in November", today.AddDays(20), today));
        Assert.Null(HealthCheckView.ValidateKnown("Accepted", today, today));
        Assert.Null(HealthCheckView.ValidateKnown("Accepted", today.AddDays(90), today));
        Assert.Contains("at most 90 days", HealthCheckView.ValidateKnown("Accepted", today.AddDays(91), today), StringComparison.Ordinal);
        Assert.Contains("past", HealthCheckView.ValidateKnown("Accepted", today.AddDays(-1), today), StringComparison.Ordinal);
        Assert.Contains("Choose the date", HealthCheckView.ValidateKnown("Accepted", null, today), StringComparison.Ordinal);
        Assert.Contains("Enter a reason", HealthCheckView.ValidateKnown("   ", today.AddDays(5), today), StringComparison.Ordinal);
        Assert.Contains("200 characters", HealthCheckView.ValidateKnown(new string('a', 201), today.AddDays(5), today), StringComparison.Ordinal);
        Assert.NotNull(HealthCheckView.ValidateKnown("write to someone@example.invalid", today.AddDays(5), today));
        Assert.NotNull(HealthCheckView.ValidateKnown(@"see C:\Folder\file.xlsx", today.AddDays(5), today));
    }

    [Fact]
    public void The_Known_dialog_answer_is_saved_through_the_store_for_a_Warning_only()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Sec01, HealthOutcome.Fail), Result(HealthCheckId.Data01, HealthOutcome.Warning, code: "R025_MISSING"));
            var view = NewView(store, now: () => Night, askKnown: _ => ("Starting daily entry in November", new DateOnly(2026, 10, 31)));
            view.LoadAsync().GetAwaiter().GetResult();

            view.Select(view.Rows.Single(row => row.Check == "SEC-01"));
            Assert.False(view.KnownEnabled);
            Assert.False(view.MarkKnownAsync().GetAwaiter().GetResult());
            Assert.Contains("Only a warning", view.StatusText, StringComparison.Ordinal);
            Assert.Empty(store.KnownSet);

            view.Select(view.Rows.Single(row => row.Check == "DATA-01"));
            Assert.True(view.KnownEnabled);
            Assert.True(view.MarkKnownAsync().GetAwaiter().GetResult());

            Assert.Equal([("DATA-01", "R025_MISSING", new DateOnly(2026, 10, 31), "Starting daily entry in November")], store.KnownSet);
            Assert.Contains("31 Oct 2026", view.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_refused_or_cancelled_Known_saves_nothing_and_a_store_failure_carries_a_Ref()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Data01, HealthOutcome.Warning));

            var cancelled = NewView(store, now: () => Night, askKnown: _ => null);
            cancelled.LoadAsync().GetAwaiter().GetResult();
            cancelled.Select(cancelled.Rows.Single());
            Assert.False(cancelled.MarkKnownAsync().GetAwaiter().GetResult());

            var tooLong = NewView(store, now: () => Night, askKnown: _ => ("Accepted", new DateOnly(2027, 3, 1)));
            tooLong.LoadAsync().GetAwaiter().GetResult();
            tooLong.Select(tooLong.Rows.Single());
            Assert.False(tooLong.MarkKnownAsync().GetAwaiter().GetResult());
            Assert.Contains("at most 90 days", tooLong.StatusText, StringComparison.Ordinal);
            Assert.Empty(store.KnownSet);

            store.FailKnown = true;
            var failing = NewView(store, now: () => Night, askKnown: _ => ("Accepted", new DateOnly(2026, 10, 20)));
            failing.LoadAsync().GetAwaiter().GetResult();
            failing.Select(failing.Rows.Single());
            Assert.False(failing.MarkKnownAsync().GetAwaiter().GetResult());
            Assert.Matches(@"Ref: [0-9A-Fa-f]{12}$", failing.StatusText);
        });
    }

    [Fact]
    public void A_Known_line_can_be_ended_and_a_Fail_line_has_no_such_button()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Db04, HealthOutcome.Known, code: "AUTO_CLOSE"), Result(HealthCheckId.Sec01, HealthOutcome.Fail));
            var view = NewView(store, now: () => Night);
            view.LoadAsync().GetAwaiter().GetResult();

            view.Select(view.Rows.Single(row => row.Check == "SEC-01"));
            Assert.False(view.EndKnownEnabled);
            view.Select(view.Rows.Single(row => row.Check == "DB-04"));
            Assert.True(view.EndKnownEnabled);
            Assert.True(view.EndKnownAsync().GetAwaiter().GetResult());

            Assert.Equal([("DB-04", "AUTO_CLOSE")], store.KnownCleared);
        });
    }

    [Fact]
    public void Ending_a_line_that_is_Known_through_its_sub_findings_clears_their_Known_rows_too()
    {
        // Review finding 4: DATA-01 DAY_INCOMPLETE was Known through Known rows under its sub codes; clearing only the
        // line code removed nothing while the screen said "ended".
        RunSta(() =>
        {
            var store = new FakeStore();
            store.AddRun(1, HealthCheckTrigger.Nightly, Utc(2026, 10, 11, 21), Result(HealthCheckId.Data01, HealthOutcome.Known, code: "DAY_INCOMPLETE",
                subs: [new("EXPORTS_MISSING", HealthOutcome.Known, "EXPORTS_MISSING"), new("INPUTS_MISSING", HealthOutcome.Known, "INPUTS_MISSING")]));
            var view = NewView(store, now: () => Night);
            view.LoadAsync().GetAwaiter().GetResult();

            view.Select(view.Rows.Single(row => row.Check == "DATA-01"));
            Assert.True(view.EndKnownAsync().GetAwaiter().GetResult());

            Assert.Equal([("DATA-01", "DAY_INCOMPLETE"), ("DATA-01", "EXPORTS_MISSING"), ("DATA-01", "INPUTS_MISSING")], store.KnownCleared);
        });
    }

    // ---- export folder -------------------------------------------------------------------------------------------

    [Fact]
    public void Open_export_folder_opens_an_existing_folder_and_otherwise_says_it_is_missing()
    {
        RunSta(() =>
        {
            var opened = new List<string>();
            var folder = Path.Combine(Path.GetTempPath(), "etp-health-view-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var present = NewView(new FakeStore(), exportFolder: () => folder, openFolder: opened.Add);
                present.OpenExportFolder();
                Assert.Equal([folder], opened);

                var missing = NewView(new FakeStore(), exportFolder: () => Path.Combine(folder, "absent"), openFolder: opened.Add);
                missing.OpenExportFolder();
                Assert.Contains("does not exist yet", missing.StatusText, StringComparison.Ordinal);
                Assert.Single(opened);
            }
            finally { Directory.Delete(folder, true); }
        });
    }

    // ---- the tile ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_tile_is_one_line_with_counts_only_and_never_a_check_name()
    {
        var today = new DateOnly(2026, 10, 11);
        HealthCheckNightlySummary Summary(HealthOutcome overall, int fail, int warning, int businessDay = 10) =>
            new(new DateOnly(2026, 10, businessDay), Utc(2026, 10, 11, 21), overall, new HealthCheckCounts(40, warning, fail, 2, 1));

        Assert.Equal("System check: last night OK", HealthSummaryTile.TextFor(Summary(HealthOutcome.Pass, 0, 0), today));
        Assert.Equal("System check: 2 warnings", HealthSummaryTile.TextFor(Summary(HealthOutcome.Warning, 0, 2), today));
        Assert.Equal("System check: 1 warning", HealthSummaryTile.TextFor(Summary(HealthOutcome.Warning, 0, 1), today));
        Assert.Equal("System check: 1 problem - tell the Owner", HealthSummaryTile.TextFor(Summary(HealthOutcome.Fail, 1, 5), today));
        Assert.Equal("System check: 3 problems - tell the Owner", HealthSummaryTile.TextFor(Summary(HealthOutcome.Fail, 3, 0), today));
        Assert.Equal("System check: 1 problem - tell the Owner", HealthSummaryTile.TextFor(Summary(HealthOutcome.Fail, 0, 0), today));
        Assert.StartsWith("System check: last run ", HealthSummaryTile.TextFor(Summary(HealthOutcome.Pass, 0, 0, businessDay: 5), today), StringComparison.Ordinal);
        Assert.EndsWith("tell the Owner", HealthSummaryTile.TextFor(Summary(HealthOutcome.Pass, 0, 0, businessDay: 5), today), StringComparison.Ordinal);
        Assert.Equal("System check: no nightly check yet", HealthSummaryTile.TextFor(null, today));
        Assert.Equal(HealthOutcome.Fail, HealthSummaryTile.LevelFor(Summary(HealthOutcome.Fail, 1, 0), today));
        Assert.Equal(HealthOutcome.Warning, HealthSummaryTile.LevelFor(Summary(HealthOutcome.Warning, 0, 1), today));
        Assert.Equal(HealthOutcome.Pass, HealthSummaryTile.LevelFor(Summary(HealthOutcome.Pass, 0, 0), today));
    }

    [Fact]
    public void The_tile_reads_the_latest_nightly_summary_for_any_role_and_stays_quiet_when_it_cannot()
    {
        RunSta(() =>
        {
            var store = new FakeStore
            {
                Nightly = new HealthCheckNightlySummary(new DateOnly(2026, 10, 10), Utc(2026, 10, 11, 21), HealthOutcome.Warning, new HealthCheckCounts(45, 2, 0, 1, 3))
            };
            var tile = new HealthSummaryTile(() => store, () => new DateOnly(2026, 10, 11));
            Assert.Equal("System check: not checked yet", tile.Text);

            tile.RefreshAsync().GetAwaiter().GetResult();
            Assert.Equal("System check: 2 warnings", tile.Text);
            Assert.Equal(HealthOutcome.Warning, tile.Level);

            store.FailNightly = true;
            tile.RefreshAsync().GetAwaiter().GetResult();
            Assert.Equal("System check: could not be read", tile.Text);

            var none = new HealthSummaryTile(() => null);
            none.RefreshAsync().GetAwaiter().GetResult();
            Assert.Equal("System check: not available", none.Text);
        });
    }

    // 1.11.2 (UX audit top 3, top 5, accessibility 1): the tile links to the Health check for the Owner and names the top
    // finding; "Where to fix" buttons are named by line; a Ref in a status line can be found for Copy Ref.
    [Fact]
    public void The_tile_names_the_top_finding_and_opens_the_health_check_only_for_the_owner()
    {
        HealthCheckResult Line(string id, HealthOutcome outcome, string text) => new(id, default, outcome, "CODE", text, null, TimeSpan.Zero);
        var results = new[] { Line("A-01", HealthOutcome.Pass, "fine"), Line("B-02", HealthOutcome.Warning, "Staff targets are not set."), Line("C-03", HealthOutcome.Fail, "The backup is two days old.") };
        Assert.Equal("The backup is two days old.", HealthSummaryTile.TopFinding(results));
        Assert.Equal("Staff targets are not set.", HealthSummaryTile.TopFinding(results.Where(x => x.Outcome != HealthOutcome.Fail).ToArray()));
        Assert.Null(HealthSummaryTile.TopFinding([results[0]]));
        Assert.True(HealthSummaryTile.TopFinding([Line("D-04", HealthOutcome.Fail, new string('x', 400))])!.Length <= 140);
        Assert.Equal("System check: 1 problem: The backup is two days old. (click to open the health check)",
            HealthSummaryTile.OwnerText("System check: 1 problem - tell the Owner", "The backup is two days old."));

        RunSta(() =>
        {
            var opened = 0;
            var owner = new HealthSummaryTile(() => null, null, () => true, () => opened++);
            var manager = new HealthSummaryTile(() => null, null, () => false, () => opened++);
            var plain = new HealthSummaryTile(() => null);
            Assert.True(owner.TryOpen());
            Assert.False(manager.TryOpen());
            Assert.False(plain.TryOpen());
            Assert.Equal(1, opened);
        });
    }

    [Fact]
    public void Each_where_to_fix_button_is_named_by_its_line_and_a_ref_is_found_in_a_status_line()
    {
        var result = new HealthCheckResult("BKP-01", default, HealthOutcome.Warning, "OLD", "Old backup.", "backups", TimeSpan.Zero);
        var row = new HealthRow(result, FixTarget.Resolve("backups"), null);
        Assert.Equal("Open where to fix for BKP-01: Settings → Database → Backups", row.FixButtonName);

        Assert.True(DesktopDiagnostics.TryFindReference("Save failed. Ref: 1a2b3c4d5e6f", out var reference));
        Assert.Equal("1A2B3C4D5E6F", reference);
        Assert.False(DesktopDiagnostics.TryFindReference("Ref: 123", out _));
        Assert.False(DesktopDiagnostics.TryFindReference(null, out _));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private static HealthCheckView NewView(
        FakeStore store,
        Func<bool>? isOwner = null,
        Func<IProgress<string>, CancellationToken, Task<long>>? runner = null,
        Action<string>? navigate = null,
        Func<DateTime>? now = null,
        Func<string, bool>? confirm = null,
        Func<DateOnly, (string Reason, DateOnly Until)?>? askKnown = null,
        Func<string?>? exportFolder = null,
        Action<string>? openFolder = null) =>
        new(() => store, runner, isOwner ?? (() => true), navigate, now ?? (() => Night), confirm ?? (_ => true), askKnown ?? (_ => null), exportFolder, openFolder ?? (_ => { }));

    private static DateTimeOffset Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static HealthCheckResult Result(string id, HealthOutcome outcome, string? fix = null, string code = "FINDING", DateOnly? unchanged = null, IReadOnlyList<HealthCheckSubResult>? subs = null) =>
        new(id, HealthCheckId.AreaOf(id), outcome, code, "Synthetic finding.", fix, TimeSpan.FromMilliseconds(300), SubResults: subs, UnchangedSince: unchanged);

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed class FakeStore : IHealthCheckStore
    {
        private readonly List<HealthCheckRunSummary> runs = [];
        private readonly Dictionary<long, IReadOnlyList<HealthCheckResult>> results = [];

        public bool FailLoad { get; set; }
        public bool FailKnown { get; set; }
        public bool FailNightly { get; set; }
        public HealthCheckNightlySummary? Nightly { get; set; }
        public int LoadRunsCalls { get; private set; }
        public List<(string CheckId, string FindingCode, DateOnly Until, string Reason)> KnownSet { get; } = [];
        public List<(string CheckId, string FindingCode)> KnownCleared { get; } = [];

        public void AddRun(long id, HealthCheckTrigger trigger, DateTimeOffset completed, params HealthCheckResult[] lines) =>
            AddRun(id, trigger, completed, new HealthCheckCounts(
                lines.Count(l => l.Outcome == HealthOutcome.Pass), lines.Count(l => l.Outcome == HealthOutcome.Warning), lines.Count(l => l.Outcome == HealthOutcome.Fail),
                lines.Count(l => l.Outcome == HealthOutcome.Known), lines.Count(l => l.Outcome == HealthOutcome.Skipped)), lines);

        public void AddRun(long id, HealthCheckTrigger trigger, DateTimeOffset completed, HealthCheckCounts counts, params HealthCheckResult[] lines)
        {
            runs.Add(new HealthCheckRunSummary(id, trigger, DateOnly.FromDateTime(completed.UtcDateTime.AddDays(-1)), completed.AddSeconds(-60), completed, 60000, "1.11.0", "SYNTHETIC-PC", "Owner",
                counts.Fail > 0 ? HealthOutcome.Fail : counts.Warning > 0 ? HealthOutcome.Warning : HealthOutcome.Pass, counts, null));
            results[id] = lines;
        }

        public Task<long> RecordRunAsync(HealthCheckRun run, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<HealthCheckRunSummary>> LoadRunsAsync(int top = 30, HealthCheckTrigger? trigger = null, CancellationToken cancellationToken = default)
        {
            LoadRunsCalls++;
            if (FailLoad) return Task.FromException<IReadOnlyList<HealthCheckRunSummary>>(new NotSupportedException("private detail"));
            IReadOnlyList<HealthCheckRunSummary> list = runs.OrderByDescending(run => run.CompletedUtc).Where(run => trigger is null || run.Trigger == trigger).Take(top).ToArray();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<HealthCheckResult>> LoadResultsAsync(long healthCheckRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(results.TryGetValue(healthCheckRunId, out var lines) ? lines : []);

        public Task<HealthCheckNightlySummary?> LoadLatestNightlySummaryAsync(CancellationToken cancellationToken = default) =>
            FailNightly ? Task.FromException<HealthCheckNightlySummary?>(new NotSupportedException("private detail")) : Task.FromResult(Nightly);

        public Task<IReadOnlyList<HealthCheckKnownEntry>> LoadKnownAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HealthCheckKnownEntry>>([]);

        public Task SetKnownAsync(string checkId, string findingCode, DateOnly untilDate, string reason, CancellationToken cancellationToken = default)
        {
            if (FailKnown) return Task.FromException(new NotSupportedException("private detail"));
            KnownSet.Add((checkId, findingCode, untilDate, reason));
            return Task.CompletedTask;
        }

        public Task ClearKnownAsync(string checkId, string findingCode, CancellationToken cancellationToken = default)
        {
            KnownCleared.Add((checkId, findingCode));
            return Task.CompletedTask;
        }

        public Task<RolePermissionSnapshot> LoadRolePermissionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
