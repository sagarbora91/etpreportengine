using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U2 (wave plan C-c): the Fix buttons of Problems and History, with a fake fix service: which buttons each role gets for a
/// row, what each button does, the Reopen dialog's checks, the "import-problems" filter, and review m-3 (a differing file-name
/// date is a Warning on a hand import; hand imports record where the file came from).
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ImportFixButtonsTests
{
    private static readonly DateOnly Day = new(2026, 10, 11);
    private const string Sha = "ABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB";
    private static readonly ImportWorkspaceAccess Owner = new(true, true);
    private static readonly ImportWorkspaceAccess Manager = new(true, false);
    private static readonly ImportWorkspaceAccess Viewer = new(false, false);

    private static FolderImportFileResult Refused(string code, int? sql = null, string report = "D001") =>
        new("ETP-Daily-Inputs-TST01-RETAIL-2026-10-11.xlsx", report, "TST01", Day, Day, "Failed")
        { SourceSha256 = Sha, Failure = new ImportFailure(code, FailureStage.Apply, "text", SqlNumber: sql) };

    private static ImportFixLocation Location(string path = @"C:\ETP\Failed\20261012010101000-TITAN-2026-10-11.zip", bool under = true) =>
        new(new ImportSourceLocation(7, null, null, "ETP-Daily-Inputs-TST01-RETAIL-2026-10-11.xlsx", Sha, path, ImportSourceRoute.Failed, DateTime.UtcNow), true, under);

    [Fact]
    public void The_owner_gets_reopen_tell_the_shop_and_open_folder_for_a_finalised_day()
    {
        RunSta(async () =>
        {
            var fixes = new FakeFixService(Location());
            var buttons = await ImportFixRows.ForAsync(Refused("DAILY_INPUTS_DAY_FINALISED"), Owner, Services(fixes), Handlers());

            Assert.Equal([ImportFixKind.ReopenDayAndReapply, ImportFixKind.TellShop, ImportFixKind.OpenFolder], buttons.Select(b => b.Kind));
            Assert.Equal([ImportFixAdvisor.ReopenLabel, ImportFixAdvisor.TellShopLabel, ImportFixAdvisor.OpenFolderLabel], buttons.Select(b => b.Label));
            Assert.All(buttons, button => Assert.EndsWith("ETP-Daily-Inputs-TST01-RETAIL-2026-10-11.xlsx", button.AutomationName));
            Assert.Equal(Sha, fixes.Located!.SourceSha256);
        });
    }

    [Fact]
    public void A_store_manager_gets_no_reopen_and_a_viewer_gets_nothing_and_never_looks_the_file_up()
    {
        RunSta(async () =>
        {
            var managerButtons = await ImportFixRows.ForAsync(Refused("DAILY_INPUTS_DAY_FINALISED"), Manager, Services(new FakeFixService(Location())), Handlers());
            Assert.DoesNotContain(managerButtons, b => b.Kind == ImportFixKind.ReopenDayAndReapply);

            var viewerService = new FakeFixService(Location());
            Assert.Empty(await ImportFixRows.ForAsync(Refused("DAILY_INPUTS_DAY_FINALISED"), Viewer, Services(viewerService), Handlers()));
            Assert.Null(viewerService.Located);
        });
    }

    [Fact]
    public void A_duplicate_gets_only_open_folder_a_clean_row_nothing_and_no_services_no_buttons()
    {
        RunSta(async () =>
        {
            var duplicate = new FolderImportFileResult("sales.xlsx", "R025", "TST01", Day, Day, "Duplicate") { SourceSha256 = Sha };
            Assert.Equal([ImportFixKind.OpenFolder], (await ImportFixRows.ForAsync(duplicate, Owner, Services(new FakeFixService(Location())), Handlers())).Select(b => b.Kind));
            Assert.Empty(await ImportFixRows.ForAsync(duplicate with { Status = "Imported" }, Owner, Services(new FakeFixService(Location())), Handlers()));
            Assert.Empty(await ImportFixRows.ForAsync(Refused("IMPORT_TIMEOUT"), Owner, null, Handlers()));
        });
    }

    [Fact]
    public void A_timeout_gets_retry_and_a_file_outside_the_import_folders_no_open_folder()
    {
        RunSta(async () =>
        {
            var buttons = await ImportFixRows.ForAsync(Refused("IMPORT_TIMEOUT", report: "R025"), Manager,
                Services(new FakeFixService(Location(@"D:\Somewhere\sales.xlsx", under: false))), Handlers());
            Assert.Equal([ImportFixKind.Retry], buttons.Select(b => b.Kind));
        });
    }

    [Fact]
    public void Reopen_and_reapply_asks_the_owner_then_calls_the_service_with_the_answer_and_reloads()
    {
        RunSta(async () =>
        {
            var fixes = new FakeFixService(Location());
            var reports = new List<string>();
            var reloads = 0;
            var handlers = new ImportFixHandlers(new UserControl(), reports.Add, () => { reloads++; return Task.CompletedTask; })
            {
                AskReopen = request =>
                {
                    Assert.Equal(("TST01", (DateOnly?)Day, (DateOnly?)Day), (request.StoreCode, request.PeriodStart, request.PeriodEnd));
                    return new ReopenAndReapplyAnswer("TST01", Day, "Synthetic reason");
                }
            };
            var buttons = await ImportFixRows.ForAsync(Refused("DAILY_INPUTS_DAY_FINALISED"), Owner, Services(fixes), handlers);

            await buttons.Single(b => b.Kind == ImportFixKind.ReopenDayAndReapply).Run();

            Assert.Equal(("TST01", Day, "Synthetic reason"), fixes.Reopened!.Value);
            Assert.Equal(1, reloads);
            Assert.Equal("Reapplied.", Assert.Single(reports));
        });
    }

    [Fact]
    public void Keep_working_in_the_reopen_dialog_and_no_in_the_retry_confirm_change_nothing()
    {
        RunSta(async () =>
        {
            var fixes = new FakeFixService(Location());
            var reports = new List<string>();
            var handlers = new ImportFixHandlers(new UserControl(), reports.Add, () => Task.CompletedTask)
            { AskReopen = _ => null, ConfirmRetry = _ => false };

            await (await ImportFixRows.ForAsync(Refused("DAILY_INPUTS_DAY_FINALISED"), Owner, Services(fixes), handlers))
                .Single(b => b.Kind == ImportFixKind.ReopenDayAndReapply).Run();
            await (await ImportFixRows.ForAsync(Refused("IMPORT_TIMEOUT"), Owner, Services(fixes), handlers))
                .Single(b => b.Kind == ImportFixKind.Retry).Run();

            Assert.Null(fixes.Reopened);
            Assert.Equal(0, fixes.Retries);
            Assert.Equal("Nothing changed. The day stays finalised.", Assert.Single(reports));
        });
    }

    [Fact]
    public void Tell_the_shop_copies_the_shop_sentence_and_open_folder_opens_only_the_routed_file()
    {
        RunSta(async () =>
        {
            var copied = new List<string>();
            var opened = new List<string>();
            var handlers = new ImportFixHandlers(new UserControl(), _ => { }, () => Task.CompletedTask) { CopyText = copied.Add };
            var services = Services(new FakeFixService(Location()), opened.Add);
            var buttons = await ImportFixRows.ForAsync(Refused("DAILY_INPUTS_DAY_FINALISED"), Owner, services, handlers);

            await buttons.Single(b => b.Kind == ImportFixKind.TellShop).Run();
            await buttons.Single(b => b.Kind == ImportFixKind.OpenFolder).Run();

            Assert.Equal([ImportDiagnosticCatalogue.ShopSentence("DAILY_INPUTS_DAY_FINALISED")], copied);
            Assert.Equal([Location().Location.RoutedPath], opened);
        });
    }

    [Theory]
    [InlineData("", "2026-10-11", "Reason", "store code")]
    [InlineData("TST01", null, "Reason", "business day")]
    [InlineData("TST01", "2026-10-12", "Reason", "inside the file")]
    [InlineData("TST01", "2026-10-11", " ", "reason")]
    [InlineData("TST01", "2026-10-11", "Reason", null)]
    public void The_reopen_dialog_needs_a_store_a_day_inside_the_file_and_a_reason(string store, string? day, string reason, string? problem)
    {
        var request = new ReopenAndReapplyRequest("sheet.xlsx", "TST01", Day, Day);
        var result = ReopenAndReapplyDialog.Validate(request, store, day is null ? null : DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), reason);
        if (problem is null) Assert.Null(result);
        else Assert.Contains(problem, result!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_import_problems_screen_filters_to_the_file_a_fix_link_names()
    {
        var problems = new[]
        {
            ImportProblems.Describe(Refused("DAILY_INPUTS_DAY_FINALISED")),
            ImportProblems.Describe(new FolderImportFileResult("sales.xlsx", "R025", "TST01", Day, Day, "Failed") { SourceSha256 = new string('C', 64) })
        };
        Assert.Equal(2, DesktopCompositionRoot.FilterProblems(problems, null).Count);
        Assert.Equal("sales.xlsx", Assert.Single(DesktopCompositionRoot.FilterProblems(problems, "SALES.xlsx")).File);
        Assert.Equal("sales.xlsx", Assert.Single(DesktopCompositionRoot.FilterProblems(problems, new string('c', 64))).File);
        Assert.Equal("import-problems", DesktopCompositionRoot.ImportProblemsTask);
    }

    [Fact]
    public void Problem_rows_say_which_button_to_press_and_carry_their_verdict_word_and_tone()
    {
        var finalised = ImportProblems.Describe(Refused("DAILY_INPUTS_DAY_FINALISED"));
        Assert.Contains("Reopen day and re-apply", finalised.NextStep);
        Assert.NotNull(finalised.Source);
        Assert.Contains("Press Retry", ImportProblems.NextStep(Refused("IMPORT_TIMEOUT")));
        Assert.Equal(("Refused", StatusTone.Critical), ImportProblems.VerdictOf("Failed"));
        Assert.Equal(("Already imported", StatusTone.Neutral), ImportProblems.VerdictOf("Duplicate content"));
        Assert.Equal(("Unknown layout", StatusTone.Warning), ImportProblems.VerdictOf("Unknown layout"));
        Assert.Equal("Refused", ImportHistoryView.ResultWord(Refused("HEADER_INVALID")));
        Assert.Equal("Imported", ImportHistoryView.ResultWord(new("a.xlsx", "R025", "TST01", Day, Day, "Imported")));
    }

    [Fact]
    public void A_hand_import_shows_a_differing_file_name_date_as_a_warning_and_records_where_each_file_came_from()
    {
        Assert.Equal(ImportDiagnosticSeverity.Warning, DesktopImportCoordinator.NoteSeverity("DAILY_INPUTS_NAME_DATE_DIFFERS"));
        Assert.Equal(ImportDiagnosticSeverity.Information, DesktopImportCoordinator.NoteSeverity("DAILY_INPUTS_NAME_UNUSUAL"));

        var folder = Path.Combine(Path.GetTempPath(), "etp-hand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var zip = Path.Combine(folder, "TITAN-2026-10-11.zip");
            File.WriteAllBytes(zip, [1]);
            var inZip = new FolderImportFileResult("sheet.xlsx", "D001", "TST01", Day, Day, "Imported") { SourceSha256 = Sha, SourcePath = Path.Combine(Path.GetTempPath(), "x", "sheet.xlsx") };
            var cancelled = new FolderImportFileResult("other.xlsx", null, null, null, null, "Cancelled");
            var fromZip = DesktopImportCoordinator.HandLocations(zip, [inZip, cancelled]);
            Assert.Equal((zip, ImportSourceRoute.Hand, Sha), (Assert.Single(fromZip).RoutedPath, fromZip[0].Route, fromZip[0].SourceSha256));

            var inFolder = inZip with { SourcePath = Path.Combine(folder, "sheet.xlsx") };
            Assert.Equal(Path.Combine(folder, "sheet.xlsx"), Assert.Single(DesktopImportCoordinator.HandLocations(folder, [inFolder])).RoutedPath);
        }
        finally { Directory.Delete(folder, true); }
    }

    // ----- fakes and helpers -----

    private static ImportFixServices Services(FakeFixService fixes, Action<string>? open = null) => new()
    {
        Fixes = () => fixes,
        Locations = () => throw new InvalidOperationException("The view reads locations through the fix service."),
        OpenFolder = open ?? (_ => { })
    };

    private static ImportFixHandlers Handlers() => new(new UserControl(), _ => { }, () => Task.CompletedTask);

    private sealed class FakeFixService(ImportFixLocation? known) : IImportFixService
    {
        public ImportFixTarget? Located { get; private set; }
        public (string Store, DateOnly Day, string Reason)? Reopened { get; private set; }
        public int Retries { get; private set; }

        public Task<ImportFixLocation?> LocateAsync(ImportFixTarget target, CancellationToken cancellationToken = default)
        { Located = target; return Task.FromResult(known); }

        public Task<ImportFixOutcome> RetryAsync(ImportFixLocation location, ImportFixTarget target, CancellationToken cancellationToken = default)
        { Retries++; return Task.FromResult(new ImportFixOutcome(true, "Retried.", null)); }

        public Task<ImportFixOutcome> ReopenAndReapplyAsync(ImportFixLocation location, ImportFixTarget target, string storeCode, DateOnly businessDate,
            string reason, CancellationToken cancellationToken = default)
        { Reopened = (storeCode, businessDate, reason); return Task.FromResult(new ImportFixOutcome(true, "Reapplied.", null)); }
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
