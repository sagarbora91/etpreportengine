using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Morning;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Application.Service;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U1: the Morning page's pure builder over fixtures for every state (ready, blocked with reasons, locked, no-sale,
/// refused shop file, no nightly run yet), the chips, the month-start card and the footer. Synthetic store codes only.
/// </summary>
public sealed class MorningSummaryBuilderTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);
    private static readonly DateOnly Today = new(2026, 10, 10);
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 3, 30, 0, TimeSpan.Zero);
    private static readonly string[] Required = ["R025", "R022", "R013", "R003", "STOCK_LEDGER", "CLOSING_STOCK"];

    [Fact]
    public void A_store_with_every_file_and_entry_is_ready_and_named_in_the_primary_button()
    {
        var summary = Build(Store("SC901", "Shop One", Workflow("SC901")));

        var card = Assert.Single(summary.Stores);
        Assert.Equal((MorningStoreStatus.Ready, "Ready", MorningTone.Success), (card.Status, card.StatusWord, card.Tone));
        Assert.True(card.IsReady);
        Assert.Empty(card.Blockers);
        Assert.Equal(["SC901"], summary.ReadyStoreCodes);
        Assert.Equal("Finalise 1 ready store", summary.FinaliseLabel);
        Assert.Equal("Friday 09 Oct: 1 of 1 store ready to close", summary.Verdict);
        Assert.Equal(MorningTone.Success, summary.VerdictTone);
        Assert.Equal(["Files", "Entries", "Checks", "Sent back"], card.Lines.Select(line => line.Label));
        Assert.Equal("6 of 6 in", card.Lines[0].Word);
    }

    [Fact]
    public void A_blocked_store_lists_its_reasons_in_words_and_offers_the_entry_screens()
    {
        var workflow = Workflow("SC902", missingReports: ["R025", "R022"], missingInputs: ["WALK_INS", "OPENING_CASH"]);
        var summary = Build(Store("SC902", "Shop Two", workflow));

        var card = Assert.Single(summary.Stores);
        Assert.Equal((MorningStoreStatus.Blocked, "Blocked", MorningTone.Critical), (card.Status, card.StatusWord, card.Tone));
        Assert.Equal(["Sales lines not imported yet", "Invoice tenders not imported yet", "Walk-ins not entered", "Opening cash not entered"], card.Blockers);
        Assert.Contains(card.Actions, action => action.TaskId == "walk-ins");
        Assert.Contains(card.Actions, action => action.TaskId == "cash-input");
        Assert.True(card.CanMarkNoSale);
        Assert.Null(summary.FinaliseLabel);
        Assert.Equal("Friday 09 Oct: no store is ready to close", summary.Verdict);
        Assert.Equal("Shop Two is blocked: sales lines not imported yet.", summary.VerdictDetail);
        Assert.Equal(MorningTone.Critical, summary.VerdictTone);
        // No report code in a sentence (VM roles walk).
        Assert.All(card.Blockers.Append(summary.VerdictDetail), text => Assert.DoesNotMatch(@"\bR0\d\d\b", text));
    }

    [Fact]
    public void A_refused_shop_file_is_named_with_its_reason_and_gets_a_Fix_button_by_history_key()
    {
        var refused = Entry("attempt:41", "SC903", "R025", "Failed", "The sheet date and the file name differ.");
        var workflow = Workflow("SC903", missingReports: ["R025"]);
        var summary = Build(Store("SC903", "Shop Three", workflow, refused));

        var card = Assert.Single(summary.Stores);
        Assert.Equal("Sales lines was refused (The sheet date and the file name differ)", card.Blockers[0]);
        Assert.DoesNotContain("Sales lines not imported yet", card.Blockers);
        var fix = Assert.Single(card.Actions, action => action.TaskId == MorningSummaryBuilder.ImportFixTask);
        Assert.Equal(("Fix sales lines", "attempt:41"), (fix.Label, fix.Argument));
        Assert.Equal(("1 refused", MorningTone.Critical), (card.Lines[0].Word, card.Lines[0].Tone));
        var files = summary.Chips.Single(chip => chip.Label == "Shop files");
        Assert.Equal(("1 refused", MorningTone.Critical, "conflicts"), (files.Word, files.Tone, files.OpenTaskId));
    }

    [Fact]
    public void A_locked_store_is_finalised_and_not_offered_again()
    {
        var summary = Build(
            Store("SC904", "Shop Four", Workflow("SC904", status: DailyWorkflowStatus.Locked, canFinalise: false)),
            Store("SC905", "Shop Five", Workflow("SC905")));

        var locked = summary.Stores.Single(card => card.StoreCode == "SC904");
        Assert.Equal((MorningStoreStatus.Locked, "Finalised", MorningTone.Success), (locked.Status, locked.StatusWord, locked.Tone));
        Assert.False(locked.CanMarkNoSale);
        Assert.Equal(["SC905"], summary.ReadyStoreCodes);
        Assert.Equal("Friday 09 Oct: 1 of 1 store ready to close", summary.Verdict);

        var allLocked = Build(Store("SC904", "Shop Four", Workflow("SC904", status: DailyWorkflowStatus.Locked, canFinalise: false)));
        Assert.Equal("Friday 09 Oct: all 1 store finalised", allLocked.Verdict);
        Assert.Empty(allLocked.ReadyStoreCodes);
        Assert.Null(allLocked.FinaliseLabel);
    }

    [Fact]
    public void A_no_sale_day_says_who_marked_it_and_is_ready_when_close_day_allows_it()
    {
        var mark = new NoSaleDay("SC906", Day, "CLOSED_HOLIDAY", "Festival", "SYNTH\\owner", new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.Zero));
        var summary = Build(Store("SC906", "Shop Six", Workflow("SC906", missingReports: [], missingInputs: []), noSale: mark));

        var card = Assert.Single(summary.Stores);
        Assert.Equal((MorningStoreStatus.NoSaleDay, "No-sale day", MorningTone.Information), (card.Status, card.StatusWord, card.Tone));
        Assert.Same(mark, card.NoSale);
        Assert.True(card.IsReady);
        Assert.False(card.CanMarkNoSale);
        Assert.Equal(["SC906"], summary.ReadyStoreCodes);
        Assert.Equal("Not needed on a no-sale day", card.Lines.Single(line => line.Label == "Entries").Detail);

        // Marked but Close day still refuses (N1 has not waived it there): not ready, and the verdict says so in words.
        var notReady = Build(Store("SC906", "Shop Six", Workflow("SC906", missingReports: ["R025"]), noSale: mark));
        Assert.False(notReady.Stores[0].IsReady);
        Assert.StartsWith("Shop Six is a no-sale day but not ready:", notReady.VerdictDetail);
    }

    [Fact]
    public void A_store_whose_day_could_not_be_read_is_not_checked_and_says_why()
    {
        var summary = Build(new MorningStoreSource("SC907", "Shop Seven", null, [], [], LoadFailure: "SQL Server did not answer in time."));

        var card = Assert.Single(summary.Stores);
        Assert.Equal((MorningStoreStatus.NotChecked, "Not checked", MorningTone.Neutral), (card.Status, card.StatusWord, card.Tone));
        Assert.Equal(["SQL Server did not answer in time."], card.Blockers);
        Assert.False(card.IsReady);
    }

    [Fact]
    public void System_check_chip_covers_no_run_yet_unreadable_old_problems_warnings_and_ok()
    {
        Assert.Equal(("Not run yet", MorningTone.Neutral), Chip(Sources() with { Nightly = null }));
        Assert.Equal(("Not read", MorningTone.Neutral), Chip(Sources() with { NightlyUnreadable = true }));
        Assert.Equal(("Old", MorningTone.Warning), Chip(Sources() with { Nightly = Nightly(Day.AddDays(-5), HealthOutcome.Pass, 0, 0) }));
        Assert.Equal(("2 problems", MorningTone.Critical), Chip(Sources() with { Nightly = Nightly(Day, HealthOutcome.Fail, 2, 1) }));
        Assert.Equal(("1 warning", MorningTone.Warning), Chip(Sources() with { Nightly = Nightly(Day, HealthOutcome.Warning, 0, 1) }));
        Assert.Equal(("OK", MorningTone.Success), Chip(Sources() with { Nightly = Nightly(Day, HealthOutcome.Pass, 0, 0) }));

        var withFinding = MorningSummaryBuilder.SystemCheckChip(Sources() with { Nightly = Nightly(Day, HealthOutcome.Fail, 1, 0), NightlyTopFinding = "The newest backup is 3 days old." }, Today);
        Assert.Equal("Last night's check found a problem. The newest backup is 3 days old.", withFinding.Detail);
        Assert.Equal("health-check", withFinding.OpenTaskId);

        static (string, MorningTone) Chip(MorningSources sources)
        {
            var chip = MorningSummaryBuilder.SystemCheckChip(sources, Today);
            return (chip.Word, chip.Tone);
        }
    }

    [Fact]
    public void Shop_files_chip_counts_arrivals_and_says_when_results_are_waiting_or_sent()
    {
        var accepted = Entry("file:1", "SC901", "R025", "Imported");
        Assert.Equal(("None yet", MorningTone.Warning), Files(Sources() with { AllImports = [] }));
        Assert.Equal(("Not read", MorningTone.Neutral), Files(Sources() with { ImportsUnreadable = true }));
        Assert.Equal(("Results waiting", MorningTone.Warning), Files(Sources() with
        {
            AllImports = [accepted],
            ShopResults = [Result("TITAN", published: false, "Accepted", accepted.Result.FileName)]
        }));
        var sent = MorningSummaryBuilder.ShopFilesChip(Sources() with
        {
            AllImports = [accepted],
            ShopResults = [Result("TITAN", published: true, "Accepted", accepted.Result.FileName)]
        }, TimeZoneInfo.Utc);
        Assert.Equal(("All in", MorningTone.Success), (sent.Word, sent.Tone));
        Assert.Equal("1 file · results sent back at 02:10", sent.Detail);

        static (string, MorningTone) Files(MorningSources sources)
        {
            var chip = MorningSummaryBuilder.ShopFilesChip(sources, TimeZoneInfo.Utc);
            return (chip.Word, chip.Tone);
        }
    }

    [Fact]
    public void The_sent_back_line_ties_a_result_to_the_store_by_its_file_names()
    {
        var file = Entry("file:7", "SC901", "R025", "Imported");
        var store = Store("SC901", "Shop One", Workflow("SC901"), file);
        Assert.Equal("No result", MorningSummaryBuilder.SentBack(store, [Result("HELIOS", true, "Refused", "other.xlsx")], TimeZoneInfo.Utc).Word);
        Assert.Equal(("Waiting to send", MorningTone.Warning), Line(MorningSummaryBuilder.SentBack(store, [Result("TITAN", false, "Accepted", file.Result.FileName)], TimeZoneInfo.Utc)));
        Assert.Equal(("Refused", MorningTone.Critical), Line(MorningSummaryBuilder.SentBack(store, [Result("TITAN", true, "Refused", file.Result.FileName)], TimeZoneInfo.Utc)));
        Assert.Equal(("Accepted with a warning", MorningTone.Warning), Line(MorningSummaryBuilder.SentBack(store, [Result("TITAN", true, "AcceptedWithWarning", file.Result.FileName)], TimeZoneInfo.Utc)));
        Assert.Equal(("Accepted", MorningTone.Success), Line(MorningSummaryBuilder.SentBack(store, [Result("TITAN", true, "Accepted", file.Result.FileName)], TimeZoneInfo.Utc)));

        static (string, MorningTone) Line(MorningCardLine line) => (line.Word, line.Tone);
    }

    [Fact]
    public void Service_is_one_chip_decided_by_the_jobs_export()
    {
        Assert.Equal(("Not read", MorningTone.Neutral), Service(null));
        Assert.Equal(("No data", MorningTone.Neutral), Service([]));
        Assert.Equal(("Fresh", MorningTone.Success), Service([Fresh("Jobs", ServiceFreshnessColour.Fresh), Fresh("Claims", ServiceFreshnessColour.Fresh)]));
        Assert.Equal(("Out of date", MorningTone.Critical), Service([Fresh("Jobs", ServiceFreshnessColour.Red)]));
        var chip = MorningSummaryBuilder.ServiceChip([Fresh("Jobs", ServiceFreshnessColour.Fresh), Fresh("Claims", ServiceFreshnessColour.Amber), Fresh("Parts", ServiceFreshnessColour.Red)]);
        Assert.Equal(("Fresh", MorningTone.Success), (chip.Word, chip.Tone));
        Assert.EndsWith("; 2 other groups are older.", chip.Detail);
        Assert.Equal("service-today", chip.OpenTaskId);

        static (string, MorningTone) Service(IReadOnlyList<ServiceFreshnessChip>? chips)
        {
            var chip = MorningSummaryBuilder.ServiceChip(chips);
            return (chip.Word, chip.Tone);
        }
    }

    [Fact]
    public void The_month_start_card_shows_on_days_one_to_seven_when_a_target_is_missing()
    {
        var missing = new[]
        {
            new DailyReadinessItem(DailyReadinessItemKind.MissingMonthlyTarget, "SC902", "MONTHLY_TARGET", "Target missing.", DailyReadinessDestination.SettingsMonthlyTargets),
            new DailyReadinessItem(DailyReadinessItemKind.MissingStaffTargets, "SC902", "STAFF_TARGETS", "Staff missing.", DailyReadinessDestination.SettingsStaffTargets)
        };
        var sources = Sources(Store("SC902", "Shop Two", Workflow("SC902"))) with { MonthStartMissing = missing, MonthStart = new DateOnly(2026, 10, 1) };

        var card = MorningSummaryBuilder.MonthStartCard(sources, new DateOnly(2026, 10, 3));
        Assert.NotNull(card);
        Assert.Equal("October targets: not set for Shop Two", card.Title);
        Assert.Equal(["SC902"], card.StoreCodes);
        Assert.Equal("masters", card.OpenTaskId);
        Assert.Null(MorningSummaryBuilder.MonthStartCard(sources, new DateOnly(2026, 10, 8)));
        Assert.Null(MorningSummaryBuilder.MonthStartCard(sources with { MonthStartMissing = [] }, new DateOnly(2026, 10, 3)));
        Assert.Null(MorningSummaryBuilder.MonthStartCard(sources with { MonthStartMissing = null }, new DateOnly(2026, 10, 3)));
    }

    [Fact]
    public void The_footer_gives_backup_ages_by_the_36_hour_rule_and_the_as_at_line()
    {
        var footer = MorningSummaryBuilder.Footer(Sources() with { OffPcBackupConfigured = true, OffPcBackupAgeHours = 50, DiskCopyAgeHours = 5 }, Now, TimeZoneInfo.Utc);

        Assert.Equal(["Off-PC backup", "Backup disk copy", "As at"], footer.Select(line => line.Label));
        Assert.Equal(("Old", MorningTone.Warning, "2 days old (limit 36 hours)"), (footer[0].Word, footer[0].Tone, footer[0].Detail));
        Assert.Equal(("OK", MorningTone.Success, "5 hours old"), (footer[1].Word, footer[1].Tone, footer[1].Detail));
        Assert.Equal("10 Oct 2026 03:30", footer[2].Word);

        var none = MorningSummaryBuilder.Footer(Sources() with { OffPcBackupConfigured = true, OffPcBackupAgeHours = null }, Now, TimeZoneInfo.Utc);
        Assert.Equal(("No copy yet", MorningTone.Warning), (none[0].Word, none[0].Tone));
        // A PC that makes no off-PC copy (OPS-10 Skipped) shows no line for it.
        Assert.Equal(["As at"], MorningSummaryBuilder.Footer(Sources(), Now, TimeZoneInfo.Utc).Select(line => line.Label));
    }

    [Fact]
    public void Two_ready_and_one_blocked_store_give_a_warning_verdict_naming_the_blocked_one()
    {
        var summary = Build(
            Store("SC901", "Shop One", Workflow("SC901")),
            Store("SC902", "Shop Two", Workflow("SC902", missingInputs: ["WALK_INS"])),
            Store("SC903", "Shop Three", Workflow("SC903")));

        Assert.Equal(["SC901", "SC903"], summary.ReadyStoreCodes);
        Assert.Equal("Finalise 2 ready stores", summary.FinaliseLabel);
        Assert.Equal("Friday 09 Oct: 2 of 3 stores ready to close", summary.Verdict);
        Assert.Equal("Shop Two is blocked: walk-ins not entered.", summary.VerdictDetail);
        Assert.Equal(MorningTone.Warning, summary.VerdictTone);
        Assert.Equal(3, summary.Chips.Count);
    }

    // ------------------------------------------------------------------ fixtures

    private static MorningSummary Build(params MorningStoreSource[] stores) =>
        MorningSummaryBuilder.Build(Sources(stores) with { AllImports = stores.SelectMany(store => store.Imports).ToArray() }, Today, Now, TimeZoneInfo.Utc);

    private static MorningSources Sources(params MorningStoreSource[] stores) => new(Day, stores);

    private static MorningStoreSource Store(string code, string name, DailyWorkflowState workflow, params ImportHistoryEntry[] imports) =>
        new(code, name, workflow, [], imports);

    private static MorningStoreSource Store(string code, string name, DailyWorkflowState workflow, NoSaleDay noSale) =>
        new(code, name, workflow, [], [], noSale);

    private static DailyWorkflowState Workflow(string store, string[]? missingReports = null, string[]? missingInputs = null,
        DailyWorkflowStatus? status = null, bool? canFinalise = null)
    {
        var missing = missingReports ?? [];
        var inputs = missingInputs ?? [];
        var ready = missing.Length == 0 && inputs.Length == 0;
        return new(store, Day, status ?? (ready ? DailyWorkflowStatus.Reconciled : DailyWorkflowStatus.Partial),
            Required.Except(missing).ToArray(), missing, [], inputs, canFinalise ?? ready, "synthetic");
    }

    private static ImportHistoryEntry Entry(string key, string store, string report, string status, string message = "") =>
        new(key, new DateTime(2026, 10, 10, 1, 30, 0, DateTimeKind.Utc), null,
            new FolderImportFileResult($"synthetic-{store}-{report}.xlsx", report, store, Day, Day, status, Message: message));

    private static MorningShopResult Result(string role, bool published, string overall, string fileName) =>
        new(role, Day, 1, published, new DateTimeOffset(2026, 10, 10, 2, 10, 0, TimeSpan.Zero), overall, [fileName]);

    private static HealthCheckNightlySummary Nightly(DateOnly day, HealthOutcome overall, int fail, int warning) =>
        new(day, new DateTimeOffset(2026, 10, 10, 2, 31, 0, TimeSpan.Zero), overall, new HealthCheckCounts(50, warning, fail, 2, 1));

    private static ServiceFreshnessChip Fresh(string group, ServiceFreshnessColour colour) =>
        new(group, [], colour == ServiceFreshnessColour.NoData ? null : Day, null, colour, "last export 08 Oct", []);
}
