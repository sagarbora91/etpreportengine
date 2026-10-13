using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Morning;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Desktop.Modules.Morning;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U1 Morning page (template D) with fake services: one primary only when a store is ready, the confirm names the
/// stores, Finalise all reports per store, publish speaks in a sentence, Fix buttons carry the history key, N1's buttons
/// are placed, and a role other than the Owner reads nothing.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class MorningViewTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);
    private static readonly string[] Required = ["R025", "R022", "R013", "R003", "STOCK_LEDGER", "CLOSING_STOCK"];

    [Fact]
    public void The_primary_button_shows_only_when_a_store_is_ready_and_says_how_many()
    {
        RunSta(() =>
        {
            var services = new FakeServices(Summary(Ready("SC901", "Shop One"), Ready("SC903", "Shop Three"), Blocked("SC902", "Shop Two")));
            var view = new MorningView(services.Build(), Request(ShellAccess.Owner));

            view.LoadAsync().GetAwaiter().GetResult();

            Assert.Equal(Visibility.Visible, view.FinaliseButton.Visibility);
            Assert.Equal("Finalise 2 ready stores", view.FinaliseButton.Content);
            Assert.Contains("Friday 09 Oct: 2 of 3 stores ready to close", Texts(view));
            Assert.Contains("Shop Two", Texts(view));
            Assert.Equal([Day], services.Loads);

            view.Show(Summary(Blocked("SC902", "Shop Two")));
            Assert.Equal(Visibility.Collapsed, view.FinaliseButton.Visibility);
        });
    }

    [Fact]
    public void Finalise_all_asks_first_naming_the_stores_and_does_nothing_when_declined()
    {
        RunSta(() =>
        {
            var services = new FakeServices(Summary(Ready("SC901", "Shop One"), Ready("SC903", "Shop Three"), Blocked("SC902", "Shop Two")));
            string? asked = null;
            var view = new MorningView(services.Build(), Request(ShellAccess.Owner)) { Confirm = (_, _, detail) => { asked = detail; return false; } };
            view.LoadAsync().GetAwaiter().GetResult();

            view.FinaliseAllAsync().GetAwaiter().GetResult();

            Assert.NotNull(asked);
            Assert.Contains("Shop One (SC901), Shop Three (SC903)", asked);
            Assert.DoesNotContain("SC902", asked);
            Assert.Empty(services.Finalised);
        });
    }

    [Fact]
    public void Finalise_all_runs_for_the_ready_stores_reports_each_one_and_reloads()
    {
        RunSta(() =>
        {
            var services = new FakeServices(Summary(Ready("SC901", "Shop One"), Ready("SC903", "Shop Three"), Blocked("SC902", "Shop Two")))
            {
                Results = [new("SC901", true, "Locked."), new("SC903", false, "Not locked: the day is already locked or unavailable.")]
            };
            var view = new MorningView(services.Build(), Request(ShellAccess.Owner)) { Confirm = (_, _, _) => true };
            view.LoadAsync().GetAwaiter().GetResult();

            view.FinaliseAllAsync().GetAwaiter().GetResult();

            Assert.Equal(["SC901", "SC903"], Assert.Single(services.Finalised));
            Assert.Equal("Locked 1 of 2 stores", view.Notice.Title);
            Assert.Contains("Shop One: Locked.", view.Notice.Message);
            Assert.Contains("Shop Three: Not locked: the day is already locked or unavailable.", view.Notice.Message);
            Assert.Equal(StatusTone.Warning, view.Notice.Tone);
            Assert.Equal(2, services.Loads.Count);
            Assert.False(view.IsBusy);
        });
    }

    [Fact]
    public void Publish_now_speaks_in_a_sentence()
    {
        RunSta(() =>
        {
            var services = new FakeServices(Summary(Ready("SC901", "Shop One"))) { Publish = new(false, "Windows did not let this account start the publish task. It still runs at 07:40, or start it from Task Scheduler.") };
            var view = new MorningView(services.Build(), Request(ShellAccess.Owner));
            view.LoadAsync().GetAwaiter().GetResult();

            view.PublishAsync().GetAwaiter().GetResult();

            Assert.Equal("Shop results were not all published", view.Notice.Title);
            Assert.StartsWith("Windows did not let this account", view.Notice.Message);
            Assert.Equal(StatusTone.Warning, view.Notice.Tone);
        });
    }

    [Fact]
    public void A_fix_button_opens_the_fix_task_with_the_history_key_and_N1_buttons_are_placed()
    {
        RunSta(() =>
        {
            var refused = new ImportHistoryEntry("attempt:41", new DateTime(2026, 10, 10, 1, 0, 0, DateTimeKind.Utc), null,
                new FolderImportFileResult("synthetic.xlsx", "R025", "SC902", Day, Day, "Failed", Message: "Refused."));
            var mark = new NoSaleDay("SC904", Day, "CLOSED_OTHER", "Synthetic", "SYNTH\\manager", DateTimeOffset.UnixEpoch);
            var summary = MorningSummaryBuilder.Build(new MorningSources(Day,
            [
                new("SC902", "Shop Two", Workflow("SC902", ["R025"]), [], [refused]),
                new("SC904", "Shop Four", Workflow("SC904", []), [], [], mark)
            ]), Day.AddDays(1), DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc);
            var opened = new List<(string, string?)>();
            var marks = new List<string>();
            var request = Request(ShellAccess.Owner) with { NavigateWithArgument = (task, argument) => opened.Add((task, argument)) };
            var view = new MorningView(new FakeServices(summary).Build(), request)
            {
                NoSaleButtons = new((store, _, _) => { marks.Add("mark " + store); return new Button { Content = "Mark as no-sale day" }; },
                    (store, _, _) => { marks.Add("withdraw " + store); return new Button { Content = "Withdraw" }; })
            };

            view.Show(summary);
            var fix = Buttons(view).Single(button => (string)button.Content == "Fix sales lines");
            fix.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.Equal([(MorningSummaryBuilder.ImportFixTask, (string?)"attempt:41")], opened);
            Assert.Equal(["mark SC902", "withdraw SC904"], marks);
            Assert.Contains(Buttons(view), button => (string)button.Content == "Withdraw");
            Assert.Contains(Texts(view), text => text.StartsWith("No-sale day (marked by SYNTH\\manager", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_the_Owner_reads_the_morning_summary(bool storeManager)
    {
        RunSta(() =>
        {
            var services = new FakeServices(Summary(Ready("SC901", "Shop One")));
            var view = new MorningView(services.Build(), Request(storeManager ? ShellAccess.StoreManager : ShellAccess.Viewer));

            view.LoadAsync().GetAwaiter().GetResult();

            Assert.Empty(services.Loads);
            Assert.Null(view.Summary);
            Assert.Contains("The Morning page is for the Owner.", Texts(view));
            Assert.Equal(Visibility.Collapsed, view.FinaliseButton.Visibility);
        });
    }

    [Fact]
    public void A_failed_load_is_one_plain_notice_and_a_diagnostics_entry()
    {
        RunSta(() =>
        {
            var services = new FakeServices(Summary()) { Failure = new TimeoutException("raw driver text") };
            var view = new MorningView(services.Build(), Request(ShellAccess.Owner));

            view.LoadAsync().GetAwaiter().GetResult();

            Assert.Equal("Could not read the morning summary", view.Notice.Title);
            Assert.Equal(StatusTone.Critical, view.Notice.Tone);
            Assert.Contains(Directory.GetFiles(DiagnosticsIsolation.LogDirectory, "diagnostics-*.jsonl"),
                file => File.ReadAllText(file).Contains("MORNING_LOAD_FAILED", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Every_state_chip_carries_its_word_for_assistive_technology()
    {
        RunSta(() =>
        {
            var view = new MorningView(new FakeServices(Summary()).Build(), Request(ShellAccess.Owner));
            view.Show(Summary(Ready("SC901", "Shop One"), Blocked("SC902", "Shop Two")));

            var chips = Descendants(view).OfType<StatusChip>().ToArray();
            Assert.NotEmpty(chips);
            Assert.All(chips, chip => Assert.False(string.IsNullOrWhiteSpace(chip.Text)));
            Assert.Contains(chips, chip => chip.Text == "Blocked" && AutomationProperties.GetName(chip) == "Blocked, problem");
        });
    }

    [Fact]
    public void The_finalise_result_title_and_tone_follow_the_stores_locked()
    {
        MorningFinaliseResult[] all = [new("SC901", true, "Locked."), new("SC902", true, "Locked.")];
        MorningFinaliseResult[] none = [new("SC901", false, "Not locked: x.")];
        Assert.Equal(("2 stores locked", StatusTone.Success), (MorningView.FinaliseTitle(all), MorningView.FinaliseTone(all)));
        Assert.Equal(("Locked 0 of 1 stores", StatusTone.Critical), (MorningView.FinaliseTitle(none), MorningView.FinaliseTone(none)));
        Assert.Equal("Shop: Locked.", MorningView.FinaliseMessage([all[0]], _ => "Shop"));
    }

    // ------------------------------------------------------------------ fixtures

    private static ScreenRequest Request(ShellAccess access) => new("morning", null, Day, null, access, _ => { });

    private static MorningSummary Summary(params MorningStoreSource[] stores) =>
        MorningSummaryBuilder.Build(new MorningSources(Day, stores), Day.AddDays(1), DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc);

    private static MorningStoreSource Ready(string code, string name) => new(code, name, Workflow(code, []), [], []);

    private static MorningStoreSource Blocked(string code, string name) => new(code, name, Workflow(code, ["R025"]), [], []);

    private static DailyWorkflowState Workflow(string store, string[] missing) =>
        new(store, Day, missing.Length == 0 ? DailyWorkflowStatus.Reconciled : DailyWorkflowStatus.Partial,
            Required.Except(missing).ToArray(), missing, [], [], missing.Length == 0, "synthetic");

    private sealed class FakeServices(MorningSummary summary) : IMorningSummaryQuery
    {
        public List<DateOnly> Loads { get; } = [];
        public List<IReadOnlyList<string>> Finalised { get; } = [];
        public IReadOnlyList<MorningFinaliseResult> Results { get; init; } = [];
        public MorningPublishResult Publish { get; init; } = new(true, "Every waiting result was signed and sent.");
        public Exception? Failure { get; init; }

        public Task<MorningSummary> LoadAsync(DateOnly lastBusinessDay, CancellationToken cancellationToken = default)
        {
            Loads.Add(lastBusinessDay);
            return Failure is null ? Task.FromResult(summary) : Task.FromException<MorningSummary>(Failure);
        }

        public MorningScreenServices Build() => new(
            () => this,
            (_, stores, _, _) => { Finalised.Add(stores); return Task.FromResult(Results); },
            _ => Task.FromResult(Publish));
    }

    private static Button[] Buttons(DependencyObject root) => Descendants(root).OfType<Button>().ToArray();

    private static string[] Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(x => x.Text).ToArray();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

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
}
