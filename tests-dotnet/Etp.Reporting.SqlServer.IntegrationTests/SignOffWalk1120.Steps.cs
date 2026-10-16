using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Morning;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Diagnostics;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Desktop.Modules.Collections;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.HealthCheck;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.Morning;
using Etp.Reporting.Desktop.Modules.NoSaleDays;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Modules.Today;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Collections;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.Morning;
using Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed partial class PhaseFiveFullWindowCaptureTests
{
    private sealed partial class SignOffWalk
    {
        private IReadOnlyList<HealthRow> healthRows = [];

        private async Task RunStepsAsync()
        {
            // ---------------------------------------------------------------- Owner: start-up, Morning, health
            VmStep("1", "Run the 1.12.0 installer over 1.11.4 on the VM: setup's verified backup and a clean finish can only be seen there.");
            await StepAsync("2", TitleAsync);
            await StepAsync("3", HealthCheckAsync, minutes: 10);
            await StepAsync("3b", ErrorRegisterAsync);
            await StepAsync("4", PlanAsync);
            VmStep("5", "Delayed SQL start, a VM restart and the automation task's wait lines are VM-only (the walk never stops SQL Server or restarts the PC).");
            await StepAsync("6", ObjectsCheckAsync);
            await StepAsync("7", MorningAsync);
            await StepAsync("8", FinaliseAllAsync);
            VmStep("9", "Publish shop results now starts the Windows task 'ETP Publish Shop Results'. The walk never starts a scheduled task on this PC (it would publish the live shop results). Press it on the VM.");
            await StepAsync("10", PinsAsync);
            await StepAsync("11", OtherRolesLandingAsync);

            // ---------------------------------------------------------------- no-sale days
            await StepAsync("12", MarkNoSaleAsync);
            await StepAsync("13", FinaliseNoSaleAsync);
            await StepAsync("13b", ExportsAsync);
            await StepAsync("14", WithdrawNoSaleAsync);
            await StepAsync("15", NoSaleRefusalsAsync);

            // ---------------------------------------------------------------- daily sheets, Fix buttons, month start
            await StepAsync("16", AutomationRunAsync, minutes: 6);
            await StepAsync("17", FixButtonsAsync, minutes: 6);
            await StepAsync("18", MonthStartAsync);
            await StepAsync("19", StaffTargetsAsync);

            // ---------------------------------------------------------------- Tally
            await StepAsync("20", TallyProfileAsync);
            VmStep("21", "Write masters and importing them needs TallyPrime Educational on the VM.");
            VmStep("22", "Prepare, Save, Approve and Write file need a prepared batch and Tally on the VM.");
            VmStep("23", "Send and the duplicate refusal need Tally on port 9000 (VM). Step 25 below shows whether Send is offered on this PC.");
            VmStep("24", "Read back and Compare need Tally's Day Book (VM).");
            await StepAsync("25", TallyTestPcAsync);

            // ---------------------------------------------------------------- Collections
            await StepAsync("26", CollectionsSetupAsync);
            await StepAsync("27", BankFilesAsync);
            await StepAsync("28", DepositsAsync);
            await StepAsync("29", StatementAsync);

            // ---------------------------------------------------------------- software update
            await StepAsync("30", UpdateScreenAsync);
            await StepAsync("31", UpdateBannerAsync);
            VmStep("32", "Install now (drafts, an import, the backup, setup, the restart) needs a signed 1.12.1 test installer and the VM.");
            VmStep("33", "The failing-migration update and the declined administrator prompt need the test build on the VM.");

            // ---------------------------------------------------------------- shop results and Service
            VmStep("34", "Step 16 shows the automation run writing a shop's pending result file (into the walk's own folder). Publish and the shop window, and the folder without the automation account's access, are VM steps.");
            await StepAsync("35", Db06Async);
            await StepAsync("36", ServiceAsync, minutes: 6);
            VmStep("37", "A second Service Centre through the fixture import and its removal: VM (the gate's ServiceCentreCatalogSqlTests covers SC999 in SQL).");

            // ---------------------------------------------------------------- backup disk, help, all roles
            VmStep("38", "The disk copy task to G: needs the second virtual disk and the elevated task registration (VM).");
            VmStep("39", "OPS-11 with G: attached and detached needs the VM's G: disk.");
            VmStep("40", "31 test copies and the 30 + monthly retention need the VM's G: disk.");
            await StepAsync("42", HelpAsync);
            await StepAsync("41", AllRolesScanAsync);
            VmStep("43", "Rollback rehearsal (reinstall 1.11.4 and restore the pre-migration backup) is VM-only.");

            // ---------------------------------------------------------------- your judgement
            await StepAsync("X1", step => { step.Sagar("Look at the Morning pictures of step 7 (and 8, 18): is the verdict line, the three chips and the store cards easy to read at a glance?"); step.Pictures.AddRange(PicturesOf("7", "8", "18")); return Task.CompletedTask; });
            await StepAsync("X2", step => { step.Sagar("Look at the Today pictures (steps 11 and 12): is the Today drop-down and the header date and store acceptable? The walk cannot open a drop-down list into a picture."); step.Pictures.AddRange(PicturesOf("11", "12")); return Task.CompletedTask; });
            await StepAsync("X3", PrintedSheetAsync);
        }

        private IEnumerable<string> PicturesOf(params string[] ids) =>
            steps.Where(step => ids.Contains(step.Id)).SelectMany(step => step.Pictures).ToArray();

        // ================================================================== 1. upgrade

        private async Task TitleAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            var title = titles[Owner];
            step.Note($"Window title: \"{title}\".");
            step.Expect(title.StartsWith("ETP Reporting Engine 1.12.0", StringComparison.Ordinal),
                "The title starts with \"ETP Reporting Engine 1.12.0\" (a build of the released code adds its commit in brackets).",
                "The title does not start with \"ETP Reporting Engine 1.12.0\".");
            step.Note("The pictures are drawn without the window frame, so the title bar itself is not in them; the line above is the window's own title.");
            await ShotAsync(step, "first-screen");
            step.Vm("Look at the title bar of the installed ETP on the VM.");
        }

        private async Task HealthCheckAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            await GoAsync(DiagnosticsScreens.HealthCheckTask);
            var store = new SqlServerHealthCheckStore(connectionString);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var view = new HealthCheckView(() => store, RunHealthAsync, () => true, navigate: null, confirm: _ => true,
                askKnown: _ => ("Synthetic walk: accepted to test Known", today.AddDays(7)));
            Swap(view);
            await view.LoadAsync();
            step.Note("The walk runs the same 56 probes as Run health check on the walk database, but does not write the nightly-style export file into ProgramData (the live PC's folder).");
            await view.RunAsync().WaitAsync(TimeSpan.FromMinutes(8));
            view.ShowPassed = true;
            await WaitIdleAsync();
            healthRows = view.AllRows;
            step.Note("Verdict: " + view.HeadlineText + (view.StatusText.Length > 0 ? " / " + view.StatusText : ""));
            step.Expect(healthRows.Count == 56, $"The result has {healthRows.Count} checks.", $"The result has {healthRows.Count} checks, not 56.");
            foreach (var id in new[] { "DB-02", "DB-07" })
            {
                var row = healthRows.FirstOrDefault(x => x.Check == id);
                step.Expect(row?.Result.Outcome == HealthOutcome.Pass, $"{id} is {row?.Outcome}: {Short(row?.Finding, 160)}",
                    row is null ? $"{id} is not in the result." : $"{id} is {row.Outcome}: {Short(row.Finding, 200)}");
            }
            var counts = healthRows.GroupBy(x => x.Outcome).Select(g => $"{g.Key} {g.Count()}");
            step.Note("Lines: " + string.Join(", ", counts) + ".");
            foreach (var row in healthRows.Where(x => x.Result.Outcome is HealthOutcome.Fail or HealthOutcome.Warning))
                step.Note($"{row.Outcome}: {row.Check} - {Short(row.Finding, 180)}");
            step.Note("On a throw-away database several lines are expected to be Problem or Check (no backups, no automation account, no nightly run, the walk's own data). Write down the VM's numbers, not these.");
            await ShotAsync(step, "health-check");

            // Known: accept one warning.
            var warning = view.AllRows.FirstOrDefault(x => x.Result.Outcome == HealthOutcome.Warning);
            if (warning is null) step.Note("No Check line to accept as Known.");
            else
            {
                view.Select(warning);
                var known = await view.MarkKnownAsync();
                await WaitIdleAsync();
                step.Expect(known, $"{warning.Check} accepted as Known for 7 days: {Short(view.StatusText, 160)}", $"Accept as Known did not save: {Short(view.StatusText, 200)}");
                await ShotAsync(step, "known");
            }
            step.Vm("Run it on the VM after the installer and write down Problem ___ Check ___ there.");
        }

        /// <summary>The Manual run without the export file (DesktopCompositionRoot.RunHealthCheckAsync writes into ProgramData).</summary>
        private async Task<long> RunHealthAsync(IProgress<string> progress, CancellationToken token)
        {
            var health = new SqlServerHealthCheckStore(connectionString);
            var register = new SqlServerErrorRegisterStore(connectionString);
            var version = DesktopCompositionRoot.HealthApplicationVersion();
            ErrorRegisterIngestRun? ingest = null;
            try { ingest = await new ErrorRegisterIngest(register, version, loadKnownEntries: t => register.LoadRegisterAsync(cancellationToken: t)).RunAsync(token); }
            catch (Exception exception) when (exception is not OperationCanceledException) { current?.Note("Reading this account's log failed: " + exception.Message); }
            var probes = DesktopCompositionRoot.CreateHealthProbes(connectionString, MigrationsDirectory, health, DesktopCompositionRoot.TryLoadAutomationPrincipal(),
                new ErrorProbes(t => register.LoadRegisterAsync(cancellationToken: t), () => ingest));
            var runner = new HealthCheckRunner(probes, new HealthCheckRunnerOptions
            {
                Store = health,
                LoadRetailStoreCodes = async t => await new StoreCatalogRepository(connectionString).ActiveCodesAsync(t),
                AppVersion = version,
                MachineName = Environment.MachineName,
                RunByRole = "Owner",
                ProbeFailed = (check, exception) => current?.Note($"Probe {check} threw: {exception.GetType().Name}: {Short(exception.Message, 160)}")
            });
            var run = await runner.RunAsync(HealthCheckTrigger.Manual, token);
            progress.Report("Recorded.");
            return await DesktopCompositionRoot.RecordHealthRunAsync(health, run, token);
        }

        private async Task ErrorRegisterAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            var reference = DesktopDiagnostics.Record(new InvalidOperationException("Synthetic sign-off walk error"), "SignOffWalk", "WALK_SYNTHETIC_ERROR");
            step.Note($"The walk logged one synthetic error; its message would say \"Ref: {reference}\".");
            await GoAsync(DiagnosticsScreens.ErrorRegisterTask);
            var register = (ErrorRegisterView)DiagnosticsScreens.Create(DiagnosticsScreens.ErrorRegisterTask, () => true, null);
            Swap(register);
            await register.RefreshAsync();
            var found = await register.FindAsync(reference);
            await WaitIdleAsync();
            step.Expect(found && register.SelectedRow is not null, $"Typing the Ref finds its entry: {Short(register.DetailHeaderText, 160)}",
                $"The Ref was not found: {Short(register.StatusText, 200)}");
            await ShotAsync(step, "error-register-ref");
        }

        private async Task PlanAsync(WalkStep step)
        {
            var plan = await DatabaseChangePlanner.PlanAsync(connectionString, MigrationsDirectory);
            step.Expect(!plan.HasChanges, "The database change plan (what --plan-database-changes prints) says nothing is pending.",
                "The plan lists pending changes: " + Short(plan.ToJson(), 500));
            step.Vm("Run Etp.Reporting.Desktop.exe --plan-database-changes on the VM: the command reads the installed PC's automation target, which the walk does not use.");
        }

        // ================================================================== 2. starting up

        private async Task ObjectsCheckAsync(WalkStep step)
        {
            var copy = Path.Combine(temp, "database-copy");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "database"), copy);
            var viewFile = Directory.EnumerateFiles(Path.Combine(copy, "objects"), "*.sql", SearchOption.AllDirectories)
                .FirstOrDefault(file => Regex.IsMatch(File.ReadAllText(file), @"CREATE\s+(OR\s+ALTER\s+)?VIEW", RegexOptions.IgnoreCase))
                ?? throw new InvalidOperationException("No view file in database\\objects.");
            await File.AppendAllTextAsync(viewFile, Environment.NewLine + "-- changed by hand (sign-off walk)" + Environment.NewLine);
            step.Note($"Changed {Path.GetFileName(viewFile)} in a copy of the install's database folder.");
            var refusal = await StartupObjectsCheck.RefusalAsync(connectionString, Path.Combine(copy, "migrations"), exception => step.Note("The check was skipped: " + exception.Message));
            step.Expect(refusal?.StartsWith("Database objects are not up to date", StringComparison.Ordinal) == true,
                $"ETP stops with: \"{refusal}\"", $"ETP did not stop as expected; it said: \"{refusal ?? "(nothing)"}\"");
            var again = await StartupObjectsCheck.RefusalAsync(connectionString, MigrationsDirectory);
            step.Expect(again is null, "With the file as shipped, ETP opens (no refusal).", $"With the file as shipped ETP still refuses: \"{again}\"");
            step.Note("No picture: this is the start-up message, checked as text.");
            step.Vm("The Store Manager part (a login that cannot read object definitions opens anyway) needs a second Windows account: check it on the VM.");
        }

        private static void CopyDirectory(string from, string to)
        {
            foreach (var directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(directory.Replace(from, to, StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(from, to, StringComparison.OrdinalIgnoreCase), overwrite: true);
        }

        // ================================================================== 3. Morning

        private async Task<MorningView> MorningOnAsync(DateOnly day)
        {
            await GoAsync("morning", day);
            return await WaitForAsync(() => Window.FocusedWorkspaceHost.Content is MorningView { IsBusy: false, Summary: { } summary } view && summary.BusinessDate == day ? view : null,
                90, "the Morning summary");
        }

        private async Task MorningAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            step.Expect(landing[Owner] == "morning", "As Owner, ETP opened on Morning.", $"As Owner, ETP opened on \"{landing[Owner]}\", not Morning.");
            var view = await MorningOnAsync(MorningDay);
            var summary = view.Summary!;
            step.Note($"Business day {MorningDay:dd MMM yyyy}. Verdict: \"{summary.Verdict}\" - {summary.VerdictDetail}");
            step.Expect(!string.IsNullOrWhiteSpace(summary.Verdict), "There is a verdict line.", "The verdict line is empty.");
            var finalise = ContentText(view.FinaliseButton);
            step.Expect(view.FinaliseButton.Visibility == Visibility.Visible && finalise.StartsWith("Finalise", StringComparison.Ordinal),
                $"The button says \"{finalise}\".", $"No Finalise button (\"{finalise}\", {view.FinaliseButton.Visibility}).");
            var chips = summary.Chips.Select(chip => chip.Label).ToArray();
            step.Expect(new[] { "System check", "Shop files", "Service" }.All(chips.Contains), $"Chips: {string.Join(", ", summary.Chips.Select(c => $"{c.Label} ({c.Word})"))}.",
                $"Chips are {string.Join(", ", chips)}, not System check, Shop files, Service.");
            step.Note($"{summary.Stores.Count} store cards: {string.Join(", ", summary.Stores.Select(card => $"{card.StoreName} ({card.StatusWord})"))}.");
            var labels = summary.Stores.SelectMany(card => card.Lines.Select(line => line.Label)).Distinct().ToArray();
            step.Expect(new[] { "Files", "Entries", "Checks", "Sent back" }.All(labels.Contains), $"Each card has the lines {string.Join(" / ", labels)}.",
                $"The card lines are {string.Join(" / ", labels)}, not Files / Entries / Checks / Sent back.");
            step.Note("The footer and the System check / Shop files chips read this PC's own backup and shop-file state, not the walk database.");
            await ShotAsync(step, "morning");
        }

        private async Task FinaliseAllAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            var view = await MorningOnAsync(MorningDay);
            var ready = view.Summary!.ReadyStoreCodes.ToArray();
            step.Note($"Ready before: {string.Join(", ", ready)}.");
            step.Expect(ready.Contains("WALKA") && ready.Contains("WALKB") && !ready.Contains("WALKC"),
                "WALKA and WALKB are ready, WALKC is blocked.", "The ready stores are not WALKA and WALKB only.");
            string? asked = null;
            view.Confirm = (_, title, detail) => { asked = $"{title}: {detail}"; return true; };
            await view.FinaliseAllAsync();
            await WaitIdleAsync(800);
            step.Note("The sheet asked: " + (asked ?? "(no question)"));
            step.Expect(asked is not null && asked.Contains("Walk test shop A", StringComparison.Ordinal) && asked.Contains("Walk test shop B", StringComparison.Ordinal),
                "The sheet names both ready stores; the walk confirmed it.", "The sheet did not name both stores.");
            foreach (var store in new[] { "WALKA", "WALKB" })
                step.Expect(await DayStatusAsync(store, MorningDay) == "LOCKED", $"{store} is locked.", $"{store} is {await DayStatusAsync(store, MorningDay)}, not locked.");
            step.Expect(await DayStatusAsync("WALKC", MorningDay) != "LOCKED", "WALKC stays open.", "WALKC was locked.");
            var blocked = view.Summary?.Stores.FirstOrDefault(card => card.StoreCode == "WALKC");
            step.Expect(blocked is { Blockers.Count: > 0 }, $"WALKC's card says why: {string.Join("; ", blocked?.Blockers.Take(3) ?? [])}", "WALKC's card gives no reason.");
            step.Note("The real confirmation sheet is a modal dialog; the walk answers it in code, so the picture shows the page after the answer.");
            await ShotAsync(step, "after-finalise");
        }

        private async Task PinsAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            const string pinned = "import-history";
            await GoAsync(pinned);
            Click(Window.PinButton);
            await WaitIdleAsync();
            step.Note("Star pressed on Import > History: " + Window.ApplicationStatus.Text);
            await ShotAsync(step, "pinned");
            await OpenAsync(Owner, fresh: true);
            step.Expect(UiPreferenceStore.Load().PinnedTaskIds.Contains(pinned), "After closing and reopening ETP the screen is still pinned.", "After reopening, the pin is gone.");
            await GoAsync(pinned);
            step.Expect(AutomationProperties.GetName(Window.PinButton) == "Unpin this screen", "Its star shows as pinned.", $"Its star says \"{AutomationProperties.GetName(Window.PinButton)}\".");
            foreach (var task in new[] { "report-dsr", "readiness", "health-check", "masters" }) await GoAsync(task);
            var preferences = Window.UiPreferencesSnapshot;
            var pins = preferences.PinnedForSearch().ToArray();
            var recents = preferences.RecentTaskIds.ToArray();
            var list = TaskNavigation.Search("", Window.CurrentShellAccess, pins, recents).Select(task => task.Id).ToArray();
            var expectedPinned = list.TakeWhile(pins.Contains).ToArray();
            var afterPins = list.Skip(expectedPinned.Length).ToArray();
            var recentPart = afterPins.TakeWhile(recents.Contains).ToArray();
            step.Note($"Ctrl+K with nothing typed lists first: {string.Join(", ", list.Take(10))} ...");
            step.Expect(expectedPinned.Contains(pinned) && expectedPinned.Length == pins.Count(id => list.Contains(id)), "Pinned screens come first.", "Pinned screens are not first.");
            step.Expect(recentPart.Length is > 0 and <= 8 && recents.Where(id => !pins.Contains(id)).Take(8).All(recentPart.Contains),
                $"Then the recent screens ({recentPart.Length}, at most 8).", $"The recent part is not right: {string.Join(", ", recentPart)}.");
            step.Expect(list.Length > expectedPinned.Length + recentPart.Length, "Then the rest.", "Nothing follows the recents.");
            await ShotSearchAsync(step);
        }

        /// <summary>Opens Ctrl+K and draws its list (the list lives in a pop-up the window picture cannot see).</summary>
        private async Task ShotSearchAsync(WalkStep step)
        {
            var navigator = (TaskNavigator?)typeof(MainWindow).GetField("taskNavigator", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(Window);
            if (navigator is null) { step.Note("Ctrl+K could not be opened for a picture."); return; }
            var before = application!.Windows.OfType<Window>().ToHashSet();
            navigator.FocusMasterSearch();
            foreach (var opened in application.Windows.OfType<Window>().Where(w => !before.Contains(w))) allowedWindows.Add(opened);
            await Task.Delay(300);
            try
            {
                if (typeof(TaskNavigator).GetField("masterResults", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(navigator) is ListBox results
                    && results.Parent is Border border)
                {
                    border.Child = null;
                    try { ShotElement(step, "ctrl-k", results, 520); }
                    finally { border.Child = results; }
                }
            }
            finally
            {
                typeof(TaskNavigator).GetMethod("CloseMasterSearch", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(navigator, null);
                await WaitIdleAsync();
            }
        }

        private async Task OtherRolesLandingAsync(WalkStep step)
        {
            foreach (var who in new[] { Manager, Viewer })
            {
                await OpenAsync(who);
                step.Expect(landing[who] == "report-dsr", $"{RoleWord(who)} opens on Today > Sales.", $"{RoleWord(who)} opens on \"{landing[who]}\".");
                var search = TaskNavigation.Search("morning", Window.CurrentShellAccess).Concat(TaskNavigation.Search("", Window.CurrentShellAccess)).Select(task => task.Id);
                step.Expect(!search.Contains("morning"), $"\"Morning\" is not in Ctrl+K for {RoleWord(who)}.", $"Ctrl+K offers Morning to {RoleWord(who)}.");
                await ShotAsync(step, "landing");
            }
        }

        // ================================================================== 4. no-sale days

        private async Task<string> TodayAsync(DateOnly day, string store)
        {
            await GoAsync("report-dsr", day, store);
            await Window.TodayChecklistPanel.RefreshAsync(day.ToDateTime(TimeOnly.MinValue), store);
            await WaitIdleAsync();
            return string.Join(" | ", Window.TodayChecklistPanel.Lines.Where(line => line.StoreCode is "" || line.StoreCode == store).Select(line => line.PlainMessage.Length > 0 ? line.PlainMessage : line.Message));
        }

        // By its automation name ("Mark as no-sale day for Walk test shop C"), so a button of another shop is never pressed.
        private Button? NoSaleButton(string label, string store) =>
            FindButtonByName(Window, $"{label} for Walk test shop {store[^1]}") ?? FindButtonByName(Window, $"{label} for {store}");

        /// <summary>The button on Today (the checklist) or, when Today has none, on Close day (its verdict band).</summary>
        private async Task<Button?> NoSaleButtonAsync(WalkStep step, string label, DateOnly day, string store)
        {
            if (NoSaleButton(label, store) is { } onToday) { step.Ok($"Today shows \"{label}\"."); return onToday; }
            step.Note($"Today shows no \"{label}\" button; the walk looks on Close day.");
            await GoAsync("readiness", day, store);
            await WaitIdleAsync(1500);
            var onCloseDay = NoSaleButton(label, store);
            step.Expect(onCloseDay is not null, $"Close day shows \"{label}\".", $"Neither Today nor Close day shows \"{label}\".");
            return onCloseDay;
        }

        private async Task<(bool Saved, string Message)> AnswerNoSaleDialogAsync(WalkStep step, string reason, string pictureName, bool pictureAfter = false)
        {
            var dialog = pendingNoSaleDialog ?? throw new InvalidOperationException("The no-sale dialog did not open.");
            pendingNoSaleDialog = null;
            if (dialog.ReasonChoices.Count > 0) dialog.ReasonChoices[0].IsChecked = true;
            dialog.ReasonInput.Text = reason;
            if (!pictureAfter) ShotDialog(step, pictureName, dialog);
            var saved = await dialog.SubmitAsync();
            if (pictureAfter) ShotDialog(step, pictureName, dialog);
            return (saved, dialog.Message.Text);
        }

        private async Task MarkNoSaleAsync(WalkStep step)
        {
            await OpenAsync(Manager);
            var before = await TodayAsync(NoSaleDay, "WALKC");
            step.Note($"Today, {NoSaleDay:dd MMM yyyy}, WALKC, as Store Manager. Checklist: {Short(before, 400)}");
            await ShotAsync(step, "today-before");
            var mark = await NoSaleButtonAsync(step, NoSaleDayActions.MarkLabel, NoSaleDay, "WALKC");
            if (mark is null) return;
            Click(mark!);
            var (saved, message) = await AnswerNoSaleDialogAsync(step, "Synthetic walk: test day, the shop was closed", "mark-dialog");
            step.Expect(saved, "Marked with a reason.", "The mark was not saved: " + message);
            var after = await TodayAsync(NoSaleDay, "WALKC");
            step.Note("Checklist after: " + Short(after, 400));
            step.Expect(after.Length < before.Length || Window.TodayChecklistPanel.NoSaleDays.Any(x => x.StoreCode == "WALKC"),
                "The checklist line for WALKC goes away (the day shows as a no-sale day).", "The checklist still asks for WALKC's files.");
            await ShotAsync(step, "today-after");
            var readiness = await new SqlServerDailyReadinessQuery(connectionString).LoadAsync(new DailyReadinessRequest(NoSaleDay, ["WALKC"], false));
            var data01 = DataProbes.EvaluateCompleteness(NoSaleDay, readiness, TimeSpan.Zero);
            step.Expect(data01.Outcome == HealthOutcome.Pass && data01.Explanation.EndsWith("(1 store-day marked no-sale)", StringComparison.Ordinal),
                $"DATA-01 for that day: {data01.Outcome}, \"{data01.Explanation}\".", $"DATA-01 for that day: {data01.Outcome}, \"{data01.Explanation}\".");
            step.Note("DATA-01 is checked for the walk's test day directly (the nightly run checks the last business day).");
        }

        private async Task FinaliseNoSaleAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            await GoAsync("readiness", NoSaleDay, "WALKC");
            var state = await new DailyReportingWorkflowRepository(connectionString).LoadAsync("WALKC", NoSaleDay);
            step.Expect(state.CanFinalise, "Close day for that day is Ready.", $"Close day is not ready: {state.StatusMessage}");
            await ShotAsync(step, "close-day");
            var results = await DesktopCompositionRoot.FinaliseAllAsync(factory, NoSaleDay, ["WALKC"], null, CancellationToken.None);
            step.Note("Finalise (Close day's own steps: pack, lock, audit): " + string.Join("; ", results.Select(r => $"{r.StoreCode}: {r.Message}")));
            step.Expect(await DayStatusAsync("WALKC", NoSaleDay) == "LOCKED", "The day locks.", "The day did not lock.");
            // The DSR covers every active shop; the other shops have no files on the walk's test day, so for this picture only
            // WALKC is active (as on a day where every other shop is fine). They are made active again straight after.
            var others = (await TextAsync("SELECT STRING_AGG(store_code,',') FROM dbo.stores WHERE is_active=1 AND service_job_prefix IS NULL AND store_code<>'WALKC'") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            string text;
            try
            {
                if (others.Length > 0) await ScalarAsync($"UPDATE dbo.stores SET is_active=0 WHERE store_code IN ({string.Join(",", others.Select(code => $"'{code}'"))})");
                step.Note($"For the DSR picture the other shops ({string.Join(", ", others)}) were made inactive for a moment.");
                await OpenAsync(Owner, fresh: true);
                await GoAsync("report-dsr", NoSaleDay);
                await Window.reportsWorkspaceView.RunReportAsync("dsr");
                await WaitIdleAsync(800);
                text = await ShotAsync(step, "dsr");
            }
            finally
            {
                if (others.Length > 0) await ScalarAsync($"UPDATE dbo.stores SET is_active=1 WHERE store_code IN ({string.Join(",", others.Select(code => $"'{code}'"))})");
                await OpenAsync(Owner, fresh: true);
            }
            var statusLine = text.Split(Environment.NewLine).FirstOrDefault(line => line.StartsWith("OK:", StringComparison.Ordinal));
            step.Expect(statusLine is not null, $"The Daily Sales Report status line: \"{Short(statusLine, 200)}\"", "No status line starting \"OK:\" on the Daily Sales Report.");
            var noSales = text.Split(Environment.NewLine).FirstOrDefault(line => line.Contains("No sales (marked by", StringComparison.Ordinal));
            step.Expect(noSales is not null, $"The store's matrix says \"{Short(noSales, 160)}\".", "The matrix does not say \"No sales (marked by ...)\".");
            await GoAsync("audit");
            await Window.RefreshDashboardAsync();
            await WaitIdleAsync();
            var audit = await ShotAsync(step, "audit-trail");
            var rows = Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='NoSaleDayMarked'"), English);
            step.Expect(rows > 0, $"The audit trail has {rows} NoSaleDayMarked row(s).", "No NoSaleDayMarked audit row.");
            if (!audit.Contains("NoSaleDay", StringComparison.OrdinalIgnoreCase) && !audit.Contains("no-sale", StringComparison.OrdinalIgnoreCase))
                step.Note("The audit trail picture may show only the latest rows; the row is in the database (above).");
        }

        private async Task ExportsAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            var pack = await new DailyReportingPackService(connectionString).GenerateAsync(salesStore, salesDay, "Synthetic walk");
            step.Note($"Store pack for {salesStore} on {salesDay:dd MMM yyyy} (synthetic sales fixture): {pack.Status} - {Short(pack.Message, 200)}");
            var exporter = new ReportExportCoordinator();
            var pdf = $"S13b-store-pack-{salesStore}-{salesDay:yyyyMMdd}.pdf";
            var xlsx = Path.ChangeExtension(pdf, ".xlsx");
            await exporter.ExportPackPdfAsync(Path.Combine(folder, pdf), pack.Document);
            await exporter.ExportPackExcelAsync(Path.Combine(folder, xlsx), pack.Document);
            foreach (var file in new[] { pdf, xlsx })
            {
                var info = new FileInfo(Path.Combine(folder, file));
                step.Expect(info.Exists && info.Length > 1000, $"{file} written ({info.Length / 1024} KB).", $"{file} was not written.");
                step.Files.Add(file);
            }
            await GoAsync("report-dsr", salesDay);
            await Window.reportsWorkspaceView.RunReportAsync("dsr");
            await WaitIdleAsync(800);
            await ShotAsync(step, "dsr-sales-day");
            step.Sagar("Open the PDF on your phone (send it to yourself) and check it reads well. Real DSR figures can only be judged on the live data on go-live day; these figures are invented.");
        }

        private async Task WithdrawNoSaleAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            await new DailyReportingWorkflowRepository(connectionString).ReopenAsync("WALKC", NoSaleDay, Environment.UserName, "Synthetic walk: reopen to test Withdraw");
            step.Expect(await DayStatusAsync("WALKC", NoSaleDay) != "LOCKED", "The Owner reopened the day (the procedure Close day's Reopen uses).", "The day is still locked.");
            var before = await TodayAsync(NoSaleDay, "WALKC");
            var withdraw = await NoSaleButtonAsync(step, NoSaleDayActions.WithdrawLabel, NoSaleDay, "WALKC");
            if (withdraw is null) { await ShotAsync(step, "today"); return; }
            Click(withdraw!);
            var (saved, message) = await AnswerNoSaleDialogAsync(step, "Synthetic walk: the sales file arrived", "withdraw-dialog");
            step.Expect(saved, "Withdrawn with a reason.", "The withdraw was not saved: " + message);
            var after = await TodayAsync(NoSaleDay, "WALKC");
            step.Note("Checklist after: " + Short(after, 400));
            step.Expect(after.Length > before.Length || NoSaleButton(NoSaleDayActions.MarkLabel, "WALKC") is not null, "The checklist line comes back.", "The checklist line did not come back.");
            await ShotAsync(step, "today-after-withdraw");
        }

        private async Task NoSaleRefusalsAsync(WalkStep step)
        {
            await OpenAsync(Manager);
            var cases = new (string Store, DateOnly Day, string Words, string What)[]
            {
                (salesStore, salesDay, "already holds sales", "a day that has invoices"),
                ("WALKC", DateOnly.FromDateTime(DateTime.Today).AddDays(2), "after today", "a future date"),
                ("WALKB", EntryDay, "other than zero", "a day with a non-zero daily entry")
            };
            var first = true;
            foreach (var (store, day, words, what) in cases)
            {
                noSaleActions!.Open(NoSaleDayDialogMode.Mark, store, day, null);
                var (saved, message) = await AnswerNoSaleDialogAsync(step, "Synthetic walk: refusal test", "refused-" + what.Replace(' ', '-'), pictureAfter: first);
                first = false;
                var plain = !Regex.IsMatch(message, @"\b5\d{4}\b|SQL|exception|error code", RegexOptions.IgnoreCase);
                step.Expect(!saved && message.Contains(words, StringComparison.OrdinalIgnoreCase) && plain,
                    $"Marking {what} is refused in plain words: \"{message}\"", $"Marking {what}: saved={saved}, message \"{message}\".");
            }
            await OpenAsync(Viewer);
            await TodayAsync(NoSaleDay, "WALKC");
            step.Expect(NoSaleButton(NoSaleDayActions.MarkLabel, "WALKC") is null && NoSaleButton(NoSaleDayActions.WithdrawLabel, "WALKC") is null,
                "As Viewer there is no no-sale button.", "A Viewer sees a no-sale button.");
            await ShotAsync(step, "viewer-today");
        }

        // ================================================================== 5. Fix buttons and month start

        private string? automationRoot;

        private async Task AutomationRunAsync(WalkStep step)
        {
            var root = automationRoot = Path.Combine(temp, "automation");
            var inbound = Path.Combine(root, "In");
            foreach (var part in new[] { "In", "Done", "Failed", "Reports" }) Directory.CreateDirectory(Path.Combine(root, part));
            await new Phase2OperationsRepository(connectionString).SaveWatchFolderSettingsAsync(new WatchFolderSettings(
                inbound, Path.Combine(root, "Done"), Path.Combine(root, "Failed"), Path.Combine(root, "Reports"), true, DateTime.MinValue, "Synthetic walk"),
                "Synthetic walk watch folder");

            // Step 17's day: the first sheet is taken, then the day is finalised.
            var first = await WriteSheetAsync(Path.Combine(root, "sheets", "first"), SheetStore, LockedDay, LockedDay, 3, "Synthetic staff");
            await new FolderImportService(new SqlServerImportPersistenceUseCase(connectionString)).RunAsync(first, new("Synthetic Owner"));
            await ScalarAsync($"UPDATE dbo.daily_reporting_days SET status='LOCKED',finalised_by=SUSER_SNAME(),finalised_utc=SYSUTCDATETIME() WHERE store_code='{SheetStore}' AND business_date='{LockedDay:yyyyMMdd}'");

            var wrong = await WriteSheetAsync(Path.Combine(root, "sheets", "wrong"), SheetStore, WrongNameDay, WrongNameDay.AddDays(1), 4, "Synthetic staff");
            var noName = await WriteSheetAsync(Path.Combine(root, "sheets", "nofilledby"), SheetStore, NoFilledByDay, NoFilledByDay, 2, null);
            var late = await WriteSheetAsync(Path.Combine(root, "sheets", "late"), SheetStore, LockedDay, LockedDay, 7, "Synthetic staff");
            step.Note($"Sheet dated {WrongNameDay:dd MMM} saved as {Path.GetFileName(wrong)}; a sheet without Filled by ({Path.GetFileName(noName)}); a corrected sheet for the finalised {LockedDay:dd MMM} ({Path.GetFileName(late)}).");
            ZipShopFile(wrong, inbound, $"HELIOS-{WrongNameDay:yyyy-MM-dd}.zip");
            ZipShopFile(noName, inbound, $"HELIOS-{NoFilledByDay:yyyy-MM-dd}.zip");
            ZipShopFile(late, inbound, $"HELIOS-{LockedDay:yyyy-MM-dd}.zip");

            var run = await new AutomatedOperationsService(connectionString, shopResultWriter: new ShopResultWriter(walkPending)).RunOnceAsync();
            step.Note($"The automation run (in this process, on the walk database and folders): {run.SourcesProcessed} processed, {run.SourcesFailed} failed.");

            var wrongIn = await WalkInsAsync(SheetStore, WrongNameDay);
            step.Expect(wrongIn == 4m, $"The sheet was imported under its own date ({WrongNameDay:dd MMM}: walk-ins {wrongIn}).",
                $"Nothing (or something else) was imported for {WrongNameDay:dd MMM}: walk-ins {wrongIn?.ToString(English) ?? "none"}.");
            var results = Directory.GetFiles(walkPending, "*.json");
            foreach (var file in results.Order(StringComparer.Ordinal))
            {
                var words = JsonStrings(await File.ReadAllTextAsync(file)).Where(text => text.Contains(' ', StringComparison.Ordinal)).Distinct().ToArray();
                step.Note($"Shop result file {Path.GetFileName(file)}: {Short(string.Join(" / ", words), 500)}");
                step.Files.Add(CopyIntoReport(file, "S16-" + Path.GetFileName(file)));
                var office = words.Where(text => text.Contains("folder", StringComparison.OrdinalIgnoreCase) || text.Contains("Settings", StringComparison.Ordinal)).ToArray();
                step.Expect(office.Length == 0, $"{Path.GetFileName(file)} uses no office words (folder, Settings).", $"{Path.GetFileName(file)} has office words: {Short(string.Join(" / ", office), 300)}");
            }
            var wrongResult = results.FirstOrDefault(file => Path.GetFileName(file).StartsWith($"HELIOS-{WrongNameDay:yyyy-MM-dd}", StringComparison.Ordinal));
            step.Expect(wrongResult is not null && (await File.ReadAllTextAsync(wrongResult)).Contains("date", StringComparison.OrdinalIgnoreCase),
                "The shop's result file for that sheet carries the date warning.", "No shop result file with the date warning for that sheet.");
            var noNameIn = await WalkInsAsync(SheetStore, NoFilledByDay);
            step.Expect(noNameIn is null, "The sheet with an empty Filled by was refused (nothing imported).", $"The sheet without Filled by was imported (walk-ins {noNameIn}).");

            await OpenAsync(Owner);
            foreach (var day in new[] { WrongNameDay, NoFilledByDay })
            {
                await GoAsync("import-history", day, SheetStore);
                if (Window.importHistoryView is { } history) await history.ActivateAsync(new ImportHistoryScope(day, day, SheetStore));
                await WaitIdleAsync();
                await ShotAsync(step, $"import-history-{day:MMdd}");
            }
        }

        private string CopyIntoReport(string source, string name)
        {
            File.Copy(source, Path.Combine(folder, name), overwrite: true);
            return name;
        }

        private static IEnumerable<string> JsonStrings(string json)
        {
            using var document = JsonDocument.Parse(json);
            var found = new List<string>();
            void Walk(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String: found.Add(element.GetString() ?? ""); break;
                    case JsonValueKind.Object: foreach (var property in element.EnumerateObject()) Walk(property.Value); break;
                    case JsonValueKind.Array: foreach (var item in element.EnumerateArray()) Walk(item); break;
                }
            }
            Walk(document.RootElement);
            return found;
        }

        private async Task<(ImportProblemsView View, ImportProblemRow? Row)> ProblemsAsync()
        {
            // Import > Problems (task "conflicts"; the Fix link id "import-problems" opens the same screen).
            await GoAsync("conflicts", LockedDay, SheetStore);
            var view = await WaitForAsync(() => Window.FocusedWorkspaceHost.Content is ImportProblemsView { HasLoaded: true } v ? v : null, 45, "Import > Problems");
            ImportProblemRow? Row() => view.GridRows.FirstOrDefault(row => row.File.Contains(LockedDay.ToString("yyyy-MM-dd", English), StringComparison.Ordinal)
                || row.Problem.Source is { StoreCode: SheetStore } source && source.PeriodStart == LockedDay);
            await WaitForAsync(() => Row() is { Fixes.Count: > 0 }, 20);
            await WaitIdleAsync();
            return (view, Row());
        }

        private async Task FixButtonsAsync(WalkStep step)
        {
            var still = await WalkInsAsync(SheetStore, LockedDay);
            step.Expect(still == 3m, $"The corrected sheet for the finalised {LockedDay:dd MMM} was refused (walk-ins stay {still}).", $"Walk-ins for the finalised day are {still}.");
            foreach (var who in new[] { Manager, Viewer, Owner })
            {
                await OpenAsync(who);
                var (view, row) = await ProblemsAsync();
                var labels = row?.Fixes.Select(fix => fix.Label).ToArray() ?? [];
                step.Note($"{RoleWord(who)} sees for {row?.File ?? "(no row)"}: {(labels.Length == 0 ? "no Fix buttons" : string.Join(", ", labels))}.");
                switch (who)
                {
                    case Owner:
                        step.Expect(labels.Contains("Reopen day and re-apply"), "The Owner is offered Reopen day and re-apply.", "The Owner is not offered Reopen day and re-apply.");
                        break;
                    case Manager:
                        step.Expect(!labels.Contains("Reopen day and re-apply") && labels.Any(l => l.StartsWith("Retry", StringComparison.Ordinal))
                                && labels.Any(l => l.Contains("shop", StringComparison.OrdinalIgnoreCase)) && labels.Any(l => l.Contains("folder", StringComparison.OrdinalIgnoreCase)),
                            "The Store Manager sees Retry / Tell the shop / Open folder.", "The Store Manager's buttons are not Retry / Tell the shop / Open folder.");
                        break;
                    default:
                        step.Expect(labels.Length == 1 && labels[0].Contains("Ref", StringComparison.Ordinal), "The Viewer sees only Copy Ref.", "The Viewer's buttons are not Copy Ref only.");
                        break;
                }
                await ShotAsync(step, "problems");
                if (who != Owner || row is null) continue;

                var fix = row.Fixes.FirstOrDefault(f => f.Kind == ImportFixKind.ReopenDayAndReapply);
                if (fix is null) continue;
                view.FixHandlers.AskReopen = request =>
                {
                    step.Note($"The reopen sheet asked about {request.FileName} ({request.StoreCode}, {request.PeriodStart:dd MMM}); the walk gave the reason.");
                    return new ReopenAndReapplyAnswer(request.StoreCode ?? SheetStore, request.PeriodStart ?? LockedDay, "Synthetic walk: corrected sheet from the shop");
                };
                await fix.Run();
                await WaitIdleAsync(800);
                step.Note("Result: " + view.MessageText);
                var now = await WalkInsAsync(SheetStore, LockedDay);
                step.Expect(now == 7m, $"Re-applied: walk-ins are now {now}.", $"After re-apply walk-ins are {now}.");
                await ShotAsync(step, "after-reapply");
                await GoAsync("import-history", LockedDay, SheetStore);
                if (Window.importHistoryView is { } history) await history.ActivateAsync(new ImportHistoryScope(LockedDay, LockedDay, SheetStore));
                await WaitIdleAsync();
                await ShotAsync(step, "history");
                await GoAsync("readiness", LockedDay, SheetStore);
                await ShotAsync(step, "close-day-reapplied");
                step.Sagar("Look at the Close day picture: it should show the re-applied walk-ins (7).");
            }
        }

        private sealed class WalkClock(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        }

        private async Task MonthStartAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            await GoAsync("morning", MonthStartDay);
            var clock = new WalkClock(new DateTimeOffset(new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Local)));
            var services = new MorningScreenServices(
                () => new SqlServerMorningSummaryQuery(() => factory, () => new SqlServerNoSaleDayService(factory), clock: clock),
                (day, stores, progress, token) => DesktopCompositionRoot.FinaliseAllAsync(factory, day, stores, progress, token),
                _ => Task.FromResult(new MorningPublishResult(false, "Not run by the sign-off walk.")));
            var view = new MorningView(services, new ScreenRequest("morning", null, MonthStartDay, null, Window.CurrentShellAccess, _ => { }));
            Swap(view);
            await view.LoadAsync();
            await WaitIdleAsync();
            step.Note("The Morning page is shown as on 3 Oct 2026 (the walk gives the page a clock on that day; the card shows on days 1 to 7 only).");
            var card = view.Summary?.MonthStart;
            step.Expect(card is not null, $"The month-start card: \"{card?.Title}\" - {card?.Message}", "No month-start card.");
            await ShotAsync(step, "morning-month-start");

            await GoAsync("masters", MonthStartDay);
            TodayChecklist.SelectTab(Window.FocusedWorkspaceHost.Content as DependencyObject, TodayChecklist.MonthlyTargetsTab);
            await WaitIdleAsync();
            var masters = FindLogical<EveningMastersView>(Window.FocusedWorkspaceHost) ?? throw new InvalidOperationException("Monthly targets (EveningMastersView) not found.");
            RevealTab(masters);
            await WaitIdleAsync();
            var shown = await ShotAsync(step, "monthly-targets");
            step.Note("Header month October 2026; the screen shows: " + Short(string.Join(" / ", shown.Split(Environment.NewLine).Where(line => line.Contains("2026", StringComparison.Ordinal)).Take(4)), 300));
            string? question = null;
            masters.ConfirmOverwrite = text =>
            {
                question = text;
                ShotDialog(step, "replace-keep-cancel", new ReplaceKeepCancelDialog(null, "Replace saved targets?", text));
                return TargetOverwriteChoice.KeepExisting;
            };
            var copy = FindLogical<Button>(masters, b => ContentText(b) == EveningMastersView.CopyAllStoresText);
            if (!step.Expect(copy is not null, "\"Copy all stores from last month\" is there.", "No \"Copy all stores from last month\" button.")) return;
            Click(copy!);
            await WaitIdleAsync(1000);
            step.Expect(question is not null, $"Because WALKA already has an October target, ETP asks: \"{Short(question, 250)}\" (Replace saved targets / Keep existing / Cancel); the walk chose Keep existing.",
                "ETP did not ask Replace / Keep / Cancel although WALKA has an October target.");
            step.Expect(masters.SaveCopiedIsHighlighted, "\"Save copied targets\" is highlighted.", "\"Save copied targets\" is not highlighted.");
            await ShotAsync(step, "copied-not-saved");
            var save = FindLogical<Button>(masters, b => ContentText(b) == EveningMastersView.SaveCopiedText);
            if (save is not null)
            {
                Click(save);
                await WaitIdleAsync(1000);
                step.Expect(!masters.SaveCopiedIsHighlighted, "After pressing it, it is no longer highlighted.", "It stays highlighted after pressing it.");
                var kept = await ScalarAsync("SELECT target_sales FROM dbo.monthly_targets WHERE store_code='WALKA' AND target_month='20261001'");
                var copied = await ScalarAsync("SELECT target_sales FROM dbo.monthly_targets WHERE store_code='WALKB' AND target_month='20261001'");
                step.Expect(Convert.ToDecimal(kept, English) == 99999m, "Keep existing left WALKA's October target (99,999).", $"WALKA's October target is now {kept}.");
                step.Expect(copied is not null, $"WALKB got last month's target ({copied}).", "WALKB got no October target.");
                step.Note("Save: " + masters.StatusText);
                await ShotAsync(step, "saved");
            }
        }

        private async Task StaffTargetsAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            await GoAsync("staff-target", MonthStartDay, "WALKA");
            string? question = null;
            Window.dailyWorkflowWorkspace.ConfirmStaffTargetOverwrite = text => { question = text; return TargetOverwriteChoice.KeepExisting; };
            var copy = FindLogical<Button>(Window.dailyWorkflowWorkspace, b => ContentText(b) == DailyWorkflowWorkspaceView.CopyAllStoresStaffTargetsText);
            if (!step.Expect(copy is { IsEnabled: true }, "The Owner has \"Copy all stores from last month\" on Staff targets.", "The Owner has no usable \"Copy all stores from last month\".")) { await ShotAsync(step, "staff-targets"); return; }
            RevealTab(copy!);
            Click(copy!);
            await WaitIdleAsync(1000);
            var rows = Window.dailyWorkflowWorkspace.PendingStaffTargetCopy;
            step.Note($"Copied {rows.Count} rows: {string.Join("; ", rows.Take(6).Select(r => $"{r.StoreCode} {r.CroNumber} {r.StaffName ?? "(no name)"}"))}" + (question is null ? "" : $"; asked: {Short(question, 120)}"));
            step.Expect(rows.Count > 0 && rows.All(r => !string.IsNullOrWhiteSpace(r.StaffName)), "Staff names show beside the CRO numbers.", "Some copied rows have no staff name.");
            await ShotAsync(step, "staff-targets-copied");

            await OpenAsync(Manager);
            var offered = TaskNavigation.Find("staff-target")?.IsAllowed(Window.CurrentShellAccess) == true;
            step.Note($"Staff targets screen offered to the Store Manager: {(offered ? "yes" : "no (Owner-only screen, decision 27)")}.");
            await GoAsync("readiness", MonthStartDay, "WALKA");
            var managerCopy = FindLogical<Button>(Window.dailyWorkflowWorkspace, b => ContentText(b) == DailyWorkflowWorkspaceView.CopyAllStoresStaffTargetsText);
            step.Expect(managerCopy is null || !managerCopy.IsEnabled || !managerCopy.IsVisible || !offered,
                "The Store Manager cannot copy or save staff targets.", "The Store Manager has a working Copy button.");
            await ShotAsync(step, "manager-close-day");
            step.Vm("\"As Store Manager you can see them\": on 1.12.0 the Staff targets screen is Owner-only (decision 27); look on the VM whether the Store Manager should see the figures somewhere.");
        }

        // ================================================================== 6. Tally

        private const string TallyTestPc = "SIGNOFF-VM";

        private async Task<TallyCompaniesView> TallyCompaniesAsync()
        {
            await GoAsync("tally-companies");
            return await WaitForAsync(() => FindVisual<TallyCompaniesView>(Window.FocusedWorkspaceHost), 30, "Tally companies");
        }

        private async Task TallyProfileAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            var view = await TallyCompaniesAsync();
            await view.RefreshAsync();
            FillTallyProfile(view, TallyTestPc, "Synthetic walk: test company");
            await view.SaveAsync();
            await WaitIdleAsync();
            step.Note("Saved: " + view.StatusText.Text);
            var text = await ShotAsync(step, "tally-companies");
            step.Expect(view.ThisPcText.Text.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase), $"The profile shows this PC's name: \"{view.ThisPcText.Text}\".", $"This PC's name is not shown: \"{view.ThisPcText.Text}\".");
            step.Expect(text.Contains("EDUCATIONAL", StringComparison.Ordinal), "The build label EDUCATIONAL is shown.", "EDUCATIONAL is not on the screen.");
            step.Expect(text.Contains("TEST", StringComparison.Ordinal), "A TEST chip is shown.", "No TEST chip on the screen.");
            step.Vm("Compare with the VM's Tally (port 9000, Educational).");
        }

        /// <summary>The TEST company as TallyProfileScreenSqlTests saves it (port 9000, EDUCATIONAL, a named test PC).</summary>
        private static void FillTallyProfile(TallyCompaniesView view, string testPc, string reason)
        {
            view.CodeInput.Text = "walktest";
            view.CompanyInput.Text = Etp.Reporting.Application.Accounting.TallyTestCompany.Name;
            view.StoresInput.Text = "WALKA";
            view.DeliveryInput.SelectedItem = TallyCompaniesView.DeliveryHttpWords;
            view.BuildInput.Text = "TallyPrime 5.0 EDUCATIONAL";
            view.TestPcInput.Text = testPc;
            view.ReasonInput.Text = reason;
        }

        private async Task TallyTestPcAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            async Task<TallyTransferView?> VouchersAsync()
            {
                await GoAsync("tally-vouchers");
                var vouchers = await WaitForAsync(() => FindVisual<TallyVouchersView>(Window.FocusedWorkspaceHost), 30, "Tally vouchers");
                await WaitForAsync(() => vouchers.CompanyInput.Items.Count > 0, 15);
                await WaitIdleAsync(800);
                return vouchers.Transfer;
            }
            var transfer = await VouchersAsync();
            if (!step.Expect(transfer is not null, "The Send to Tally card is there.", "No Send to Tally card.")) return;
            step.Expect(transfer!.SendButton.Visibility != Visibility.Visible && transfer.SendCaption.Text.Contains($"Sending is set up for {TallyTestPc} only", StringComparison.Ordinal),
                $"With the test PC named {TallyTestPc}, there is no Send button and the card says: \"{transfer.SendCaption.Text}\"",
                $"Send is {transfer.SendButton.Visibility}; the card says \"{transfer.SendCaption.Text}\".");
            await ShotAsync(step, "send-elsewhere");

            var companies = await TallyCompaniesAsync();
            await companies.RefreshAsync();
            if (companies.CompanyGrid.Items.Count > 0) companies.CompanyGrid.SelectedIndex = 0;
            await WaitIdleAsync();
            FillTallyProfile(companies, Environment.MachineName, "Synthetic walk: name this PC as the test PC");
            await companies.SaveAsync();
            step.Note("Test PC changed to this PC: " + companies.StatusText.Text);
            transfer = await VouchersAsync();
            step.Note($"With this PC named, Send is {(transfer?.SendButton.Visibility == Visibility.Visible ? "offered" : "not offered")} ({transfer?.SendCaption.Text}). The walk never presses Send.");
            await ShotAsync(step, "send-this-pc");

            await OpenAsync(Manager);
            var tallyTasks = TaskNavigation.All.Where(task => task.Id.StartsWith("tally-", StringComparison.Ordinal)).ToArray();
            var offered = tallyTasks.Where(task => task.IsAllowed(Window.CurrentShellAccess)).Select(task => task.Id).ToArray();
            step.Expect(offered.Length == 0, $"As Store Manager the Tally screens are not offered ({string.Join(", ", tallyTasks.Select(t => t.Id))} are Owner-only).",
                $"The Store Manager is offered {string.Join(", ", offered)}.");
            step.Vm("Note Tally's licence answer and the allowed Educational days on the VM.");
        }

        // ================================================================== 7. Collections

        private const string TwoColumnsLayout = """
            {"value_date":"Value Dt","posting_date":"Date","narration":"Narration","reference":"Chq./Ref.No.",
             "debit":"Withdrawal Amt.","credit":"Deposit Amt.","running_balance":"Closing Balance","date_format":"dd/MM/yyyy"}
            """;

        private string BankCsv => Path.Combine(RepositoryRoot(), "tests-dotnet", "fixtures", "collections-synthetic", "bank-two-columns.csv");
        private string EvidenceRoot => Path.Combine(temp, "CollectionsEvidence");

        private sealed class WalkCollectionsDialogs(Func<string> file, Action<string> note) : ICollectionsDialogs
        {
            public IReadOnlyList<string> ChooseStatementFiles() => [file()];
            public string? ChooseSampleFile() => file();
            public string? AskReason(string title, string prompt, string? suggestion = null) { note($"ETP asked \"{title}\"; the walk typed a reason."); return "Synthetic walk: seen on the bank statement"; }
            public bool Confirm(string action, string detail) { note($"ETP asked to confirm \"{action}\": {detail}"); return true; }
        }

        private WalkCollectionsDialogs Dialogs(WalkStep step) => new(() => BankCsv, step.Note);

        private async Task CollectionsSetupAsync(WalkStep step)
        {
            await OpenAsync(Manager);
            var offered = TaskNavigation.Find(CollectionsScreens.SetupTask)?.IsAllowed(Window.CurrentShellAccess) == true;
            step.Expect(!offered, "Collections setup is not in the Store Manager's menus.", "The Store Manager is offered Collections setup.");
            var managerModel = new CollectionsSetupModel(() => new SqlServerCollectionsSetupService(factory), Window.CurrentShellAccess, Dialogs(step));
            await managerModel.LoadAsync();
            var managerView = new CollectionsSetupView(managerModel);
            await GoAsync("report-dsr");
            Swap(managerView);
            await WaitIdleAsync();
            step.Expect(managerModel.Message.Contains("Owner", StringComparison.Ordinal), $"Opened anyway, it says one Owner-only sentence: \"{managerModel.Message}\"", $"It says: \"{managerModel.Message}\"");
            await ShotAsync(step, "setup-store-manager");

            await OpenAsync(Owner);
            await GoAsync(CollectionsScreens.SetupTask);
            var model = new CollectionsSetupModel(() => new SqlServerCollectionsSetupService(factory), Window.CurrentShellAccess, Dialogs(step));
            var view = new CollectionsSetupView(model);
            Swap(view);
            await model.LoadAsync();
            Directory.CreateDirectory(EvidenceRoot);
            model.EvidenceRoot = EvidenceRoot;
            model.EvidenceReason = "Synthetic walk evidence folder";
            step.Expect(await model.SaveEvidenceRootAsync(), "Evidence folder set (a temporary folder of the walk).", "The evidence folder was not saved: " + model.Message);

            model.NewBankAccount();
            model.BankAccount.AccountKey = "SYN_WALK";
            model.BankAccount.DisplayName = "Synthetic walk account";
            model.BankAccount.BankName = "Synthetic Bank";
            model.BankAccount.Last4 = "0001";
            model.BankAccount.StoreCode = "WALKA";
            model.BankAccount.TakesCashDeposits = true;
            model.BankAccount.Reason = "Synthetic walk account";
            step.Expect(await model.SaveBankAccountAsync(), "Bank account added with the last four digits only (0001).", "The bank account was not saved: " + model.Message);

            model.NewProvider();
            model.Provider.ProviderKey = "SYN_WALK_UPI";
            model.Provider.DisplayName = "Synthetic walk UPI";
            model.Provider.Mode = "UPI";
            model.Provider.Reason = "Synthetic walk provider";
            step.Expect(await model.SaveProviderAsync(), "Provider added.", "The provider was not saved: " + model.Message);

            await model.LoadAsync();
            var account = model.Setup?.BankAccounts.FirstOrDefault(a => a.AccountKey == "SYN_WALK");
            model.NewLayout("BANK");
            model.Layout.LayoutKey = "SYN_WALK_CSV";
            model.Layout.DisplayName = "Synthetic walk CSV";
            model.Layout.BankAccountId = account?.Id;
            model.Layout.HeaderRow = 3;
            model.Layout.SkipFooterRows = 1;
            model.Layout.MappingJson = TwoColumnsLayout;
            model.Layout.Reason = "Synthetic walk layout";
            step.Expect(await model.ReadSampleAsync(BankCsv), $"The sample file's columns were read: {string.Join(", ", model.Layout.Headers)}.", "The sample file was not read: " + model.Message);
            var test = await model.TestOnFileAsync(BankCsv);
            step.Expect(test is not null, $"Test on this file: {(test is null ? "" : CollectionsSetupModel.TestSentence(test))}", "Test on this file gave nothing: " + model.Message);
            await WaitIdleAsync();
            await ShotAsync(step, "test-on-this-file");
            step.Expect(await model.SaveLayoutAsync(), "Layout saved.", "The layout was not saved: " + model.Message);
            await view.ReloadAsync();
            await WaitIdleAsync();
            await ShotAsync(step, "setup-owner");
        }

        private async Task BankFilesAsync(WalkStep step)
        {
            await OpenAsync(Manager);
            await GoAsync(CollectionsScreens.BankFilesTask);
            var model = new BankFilesModel(() => new SqlServerCollectionsImportService(factory), Window.CurrentShellAccess);
            var view = new BankFilesView(model, Dialogs(step));
            Swap(view);
            await model.AddFilesAsync([BankCsv]);
            var imported = await model.ImportReadyAsync();
            await WaitIdleAsync();
            foreach (var item in model.Items) step.Note($"{item.DisplayName}: {item.State} - {item.Message}");
            step.Expect(imported == 1 && model.Items.All(item => item.State == BankFileState.Imported), "The synthetic CSV imported, with its own result line.", "The CSV did not import.");
            var evidence = Directory.Exists(EvidenceRoot) && Directory.EnumerateFiles(EvidenceRoot, "*", SearchOption.AllDirectories).Any();
            step.Expect(evidence, "The evidence folder holds the kept source file.", "Nothing in the evidence folder.");
            await ShotAsync(step, "bank-files");
        }

        private async Task DepositsAsync(WalkStep step)
        {
            foreach (var who in new[] { Manager, Owner })
            {
                await OpenAsync(who);
                await GoAsync(CollectionsScreens.DepositsTask);
                var model = new DepositsInTransitModel(() => new SqlServerCashDepositService(factory), Window.CurrentShellAccess, "WALKA");
                var view = new DepositsInTransitView(model, Dialogs(step));
                Swap(view);
                await model.LoadAsync();
                await WaitIdleAsync();
                step.Note($"{RoleWord(who)}: {model.Rows.Count} deposits ({string.Join("; ", model.Rows.Select(r => $"{r.Date} {r.ProposalWord}"))}). {model.StatusText}");
                if (who == Manager)
                {
                    step.Expect(model.Rows.Count > 0 && model.Rows.All(r => r.Proposal is null), "As Store Manager the deposits show without bank lines.", "The Store Manager sees bank lines or no deposits.");
                    await ShotAsync(step, "deposits-store-manager");
                    continue;
                }
                var proposed = model.Rows.FirstOrDefault(r => r.Proposal is not null);
                step.Expect(proposed is not null, $"As Owner the proposal shows: {proposed?.ProposalWord}, {Short(proposed?.BankLine, 120)}.", "No proposal for the Owner.");
                await ShotAsync(step, "deposits-owner");
                if (proposed is null) continue;
                var accepted = await model.AcceptAsync(proposed, "Synthetic walk: seen on the bank statement");
                await WaitIdleAsync();
                step.Expect(accepted, $"Bank credited (with a reason): {model.NoticeText}", "Bank credited was refused: " + model.NoticeText);
                await ShotAsync(step, "bank-credited");
            }
            step.Sagar("Look at the Owner picture for the age chip beside the deposit.");
        }

        private async Task StatementAsync(WalkStep step)
        {
            foreach (var who in new[] { Owner, Viewer })
            {
                await OpenAsync(who);
                await GoAsync(CollectionsScreens.StatementTask);
                var model = new CollectionsStatementModel(() => new SqlServerCollectionsStatementQuery(factory), Window.CurrentShellAccess, "WALKA", new DateOnly(2026, 8, 5), () => new DateOnly(2026, 8, 6));
                var view = new CollectionsStatementView(model);
                Swap(view);
                await model.LoadAsync();
                await WaitIdleAsync();
                var words = model.Rows.Select(r => r.StatusWord).Distinct().ToArray();
                step.Note($"{RoleWord(who)}: {model.Rows.Count} lines; states {string.Join(", ", words)}. {model.StatusText}");
                if (who == Owner)
                {
                    step.Expect(words.Contains("Bank credited"), "Expected vs evidence says \"Bank credited\".", "No \"Bank credited\" line.");
                    if (words.Any(w => w.StartsWith("Totals only", StringComparison.Ordinal))) step.Ok("And \"Totals only, not matched yet\".");
                    else
                    {
                        step.Note("No \"Totals only, not matched yet\" line: the walk's synthetic data has no card or UPI sales for that store and week.");
                        step.Vm("Look for \"Totals only, not matched yet\" on the VM with its card or UPI tenders.");
                    }
                }
                else
                    step.Expect(model.Rows.All(r => string.IsNullOrEmpty(r.BankReference)), "As Viewer: totals without bank references.", "A Viewer sees bank references.");
                await ShotAsync(step, "statement");
            }
            step.Sagar("Look at the Viewer picture: no narrations, no bank references.");
        }

        // ================================================================== 8. software update

        private async Task UpdateScreenAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            await GoAsync(DesktopCompositionRoot.UpdateSettingsTaskId);
            var text = await ShotAsync(step, "software-update");
            step.Expect(text.Contains("not set up", StringComparison.OrdinalIgnoreCase), "Settings > Software update shows the 'not set up' state (this build trusts no update key).",
                "The update screen does not say 'not set up': " + Short(text, 300));
            step.Vm("Making the test key, the 1.12.1 test installer, the feed folder and Check now are VM steps.");
        }

        private async Task UpdateBannerAsync(WalkStep step)
        {
            foreach (var who in new[] { Owner, Manager, Viewer })
            {
                await OpenAsync(who);
                await WaitIdleAsync(1500);
                step.Expect(Window.ShellBanners.Current is null, $"No update banner for {RoleWord(who)} (no update is offered here).", $"{RoleWord(who)} sees a banner: {Window.ShellBanners.Current?.GetType().Name}.");
            }
            step.Vm("With a 1.12.1 test feed on the VM: the banner shows for the Owner on every page, and nothing for Store Manager and Viewer.");
        }

        // ================================================================== 9. Service

        private Task Db06Async(WalkStep step)
        {
            var row = healthRows.FirstOrDefault(x => x.Check == "DB-06");
            step.Expect(row?.Result.Outcome == HealthOutcome.Pass, $"DB-06 is {row?.Outcome}: {Short(row?.Finding, 300)}",
                row is null ? "DB-06 is not in step 3's result." : $"DB-06 is {row.Outcome}: {Short(row.Finding, 300)}");
            step.Note("From step 3's run on the walk database (with the synthetic Service weeks), not after a restart.");
            step.Vm("Run the health check right after a VM restart: cold about 2 to 3 s, warm well under 1 s.");
            return Task.CompletedTask;
        }

        private async Task ServiceAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            foreach (var task in new[] { "service-pending", "service-jobs", "service-job-history" })
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await GoAsync(task);
                var view = await WaitForAsync(() => Window.FocusedWorkspaceHost.Content as ServiceScreenView is { IsLoading: false } v ? v : FindVisual<ServiceScreenView>(Window.FocusedWorkspaceHost) is { IsLoading: false } nested ? nested : null, 45, task);
                var seconds = clock.Elapsed.TotalSeconds;
                var asAt = Regex.Match(view.AsAtText, @"as at (\d{2} \w{3} \d{4})");
                if (asAt.Success && DateTime.TryParseExact(asAt.Groups[1].Value, "dd MMM yyyy", English, System.Globalization.DateTimeStyles.None, out var dataDay)
                    && Window.ShellBusinessDateSelector.SelectedDate != dataDay)
                {
                    await GoAsync(task, DateOnly.FromDateTime(dataDay));
                    view = await WaitForAsync(() => FindVisual<ServiceScreenView>(Window.FocusedWorkspaceHost) is { IsLoading: false } again ? again : null, 45, task);
                }
                Window.SuccessToast.Visibility = Visibility.Collapsed;
                var title = Window.PageTitle.Text;
                step.Expect(seconds <= 3, $"{title} opened in {seconds:0.0} s ({view.Rows.Count} rows; {Short(view.StatusText, 80)}).", $"{title} took {seconds:0.0} s to open.");
                var codes = Regex.Matches(title + " " + view.CentreLabel, @"\b([RS]0\d\d|[A-Z]{2}\d{3})\b").Select(m => m.Value).Distinct().ToArray();
                step.Expect(codes.Length == 0, $"No store or report code in the title (\"{title}\", beside it \"{view.CentreLabel}\").", $"The title shows codes {string.Join(", ", codes)} (\"{title}\" / \"{view.CentreLabel}\").");
                var pickers = Visuals(view).OfType<ComboBox>().Where(box => (AutomationProperties.GetName(box) ?? "").Contains("centre", StringComparison.OrdinalIgnoreCase)).ToArray();
                step.Expect(pickers.Length == 0, "No Service Centre picker (one centre).", "A Service Centre picker is shown although there is one centre.");
                await ShotAsync(step, task);
            }
            step.Note("Times include the walk's own navigation and are on the walk database; compare on the VM.");
        }

        // ================================================================== 11. help and all roles

        private async Task HelpAsync(WalkStep step)
        {
            await OpenAsync(Owner);
            var expected = new (string Task, string Topic)[]
            {
                ("morning", "morning"), (CollectionsScreens.SetupTask, "collections"), (CollectionsScreens.BankFilesTask, "collections"),
                (CollectionsScreens.StatementTask, "collections"), (CollectionsScreens.DepositsTask, "collections"), (DesktopCompositionRoot.UpdateSettingsTaskId, "software-update")
            };
            var first = true;
            foreach (var (task, topic) in expected)
            {
                await GoAsync(task);
                Window.ShowHelpWorkspace(contextual: true);
                await WaitIdleAsync();
                var opened = (Window.FocusedWorkspaceHost.Content as HelpCentreView)?.CurrentTopicId;
                var definition = HelpCentreRegistry.Find(topic);
                step.Expect(opened == topic, $"F1 on {TaskNavigation.Find(task)?.Title ?? task} opens \"{definition?.Title ?? topic}\".", $"F1 on {task} opened \"{opened}\", not \"{topic}\".");
                if (first) { await ShotAsync(step, "f1-morning"); first = false; }
            }
            foreach (var words in new[] { "Mark as no-sale day", "Fix buttons on import problems", "Month start", "Tally screens" })
            {
                var hits = HelpCentreRegistry.Search(words);
                step.Expect(hits.Count > 0, $"The Help Centre finds \"{words}\": {string.Join(", ", hits.Take(3).Select(h => h.Title))}.", $"The Help Centre finds nothing for \"{words}\".");
                if (hits.Count > 0 && words == "Mark as no-sale day")
                {
                    Window.ShowHelpWorkspace(hits[0].Id);
                    await WaitIdleAsync();
                    await ShotAsync(step, "help-no-sale-day");
                }
            }
        }

        private Task AllRolesScanAsync(WalkStep step)
        {
            step.Note($"The walk read the text of {screenTexts.Count} pictures ({string.Join(", ", screenTexts.GroupBy(x => x.Role).Select(g => $"{RoleWord(g.Key)} {g.Count()}"))}).");
            var hits = screenTexts
                .Where(x => !x.File.Contains("help", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.Text.Split(Environment.NewLine).Where(line => line.Count(c => c == ' ') >= 4 && Regex.IsMatch(line, @"\b[RS]0\d\d\b")).Select(line => $"{x.File}: \"{Short(line, 140)}\""))
                .Distinct().ToArray();
            step.Expect(hits.Length == 0, "No report code (R0xx, S0xx) appears in a sentence on any picture.", $"Report codes in sentences ({hits.Length}): {string.Join(" | ", hits.Take(12))}");
            var viewer = screenTexts.Where(x => x.Role == Viewer).Select(x => x.File).ToArray();
            step.Pictures.AddRange(viewer);
            step.Sagar("Look at the Viewer pictures listed here: no customer name anywhere. On every picture: money right-aligned, a missing number shown as a dash. The walk's data is invented, so a customer name cannot be recognised by the walk.");
            step.Vm("Walk the three roles through the new screens once more on the VM with its own data.");
            return Task.CompletedTask;
        }

        private async Task PrintedSheetAsync(WalkStep step)
        {
            var file = "X3-blank-daily-sheet-WALKA.xlsx";
            await using (var stream = File.Create(Path.Combine(folder, file)))
                DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Retail, "WALKA", "Walk test shop A", DateOnly.FromDateTime(DateTime.Today));
            step.Files.Add(file);
            step.Sagar($"Print {file} (the blank daily sheet the shop fills in, for a test shop) and check it reads well on paper.");
        }
    }
}
