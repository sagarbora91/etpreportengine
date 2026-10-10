using System.Threading;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// Service Today (1.10.0 Service UI wave, design 3.2) against a fake IServiceReportQuery: the seven cards, the default
/// business date (the latest snapshot, Q15), drill-down targets, empty states and the export of the card values.
/// All values are synthetic.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServiceTodayViewTests
{
    private static readonly string[] ForbiddenHeaderWords = ["phone", "mobile", "landline", "e-mail", "email", "address"];
    private static readonly DateTime ImportedUtc = new(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc);

    private static ServiceToday Summary(DateOnly date) => new(date, new DateOnly(2026, 10, 5),
        BookedToday: 6, BookedTodayBooking: 4, BookedTodayQuickBilling: 2, BookedThisMonth: 61, BookedThisMonthBooking: 40, BookedThisMonthQuickBilling: 21,
        DeliveredToday: 4, DeliveredThisMonth: 58, RwrToday: 1,
        OnBench: 46, IndentRaised: 25, EddPassed: 11, InTransit: 97, ReadyAtCentre: 12,
        CollectionToday: 12_500m, CollectionCash: 8_000m, CollectionCard: 2_500m, CollectionUpi: 2_000m, ManualEntered: false, ManualAmount: null,
        JobsOver15Days: 9, ClaimsRaisedThisMonth: 7, ClaimsValueThisMonth: 41_000m);

    [Fact]
    public void The_business_date_defaults_to_the_latest_Service_snapshot_not_today()
    {
        RunSta(() =>
        {
            var query = new FakeTodayQuery();
            var view = new ServiceTodayView(() => query, NoExport);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(new DateOnly(2026, 10, 5), query.LastDate);
            Assert.Equal(new DateOnly(2026, 10, 5), view.BusinessDate);
            Assert.Equal("Counts for 05 Oct 2026. This is the latest Service export.", view.StatusText);
        });
    }

    [Fact]
    public void The_cards_show_the_counts_with_the_Booking_Quick_Billing_split_and_RWR()
    {
        RunSta(() =>
        {
            var query = new FakeTodayQuery();
            var view = new ServiceTodayView(() => query, NoExport);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal([ServiceTodayView.BookedLabel, ServiceTodayView.DeliveredLabel, ServiceTodayView.OnBenchLabel, ServiceTodayView.ReadyLabel,
                ServiceTodayView.CollectionLabel, ServiceTodayView.Over15Label, ServiceTodayView.ClaimsLabel], view.Cards.Select(card => card.Label));
            var booked = view.Cards[0];
            Assert.Equal("6", booked.Value);
            Assert.Equal("Booking 4 · Quick Billing 2 · this month 61 (40 / 21)", booked.Detail);
            var delivered = view.Cards[1];
            Assert.Equal("4", delivered.Value);
            Assert.Equal("RWR 1 · this month 58 delivered", delivered.Detail);
            var bench = view.Cards[2];
            Assert.Equal("46", bench.Value);
            Assert.Equal("indent raised 25 · EDD passed 11", bench.Detail);
            Assert.Equal("Warning", bench.Accent);
            Assert.Equal("12", view.Cards[3].Value);
            Assert.Equal("in transit back 97", view.Cards[3].Detail);
            var money = view.Cards[4];
            Assert.Equal("12,500", money.Value);
            Assert.EndsWith(ServiceTodayView.ManualMissingText, money.Detail, StringComparison.Ordinal);
            Assert.Equal("Critical", money.Accent);
            Assert.Equal("9", view.Cards[5].Value);
            Assert.Equal("7", view.Cards[6].Value);
            Assert.StartsWith("41,000 net incl. tax", view.Cards[6].Detail, StringComparison.Ordinal);
            Assert.Equal(7, view.CardButtons.Count);
            Assert.Equal(7, Descendants<KpiCard>(view).Count());
            Assert.All(view.CardButtons, button => Assert.EndsWith("Open the list.", AutomationProperties.GetName(button), StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Every_card_drills_to_the_matching_Pending_group_or_Jobs_filter()
    {
        RunSta(() =>
        {
            var opened = new List<ServiceDrillDown>();
            var view = new ServiceTodayView(() => new FakeTodayQuery(), NoExport, opened.Add);
            view.ActivateAsync().GetAwaiter().GetResult();

            foreach (var button in view.CardButtons) button.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(
            [
                new(ServiceScreens.JobsTask, ServiceStages.Booked),
                new(ServiceScreens.JobsTask, ServiceStages.Delivered),
                new(ServiceScreens.PendingTask, ServiceStages.OnBench),
                new(ServiceScreens.PendingTask, ServiceStages.ReadyForDelivery),
                new(ServiceScreens.MoneyTask, "2026-10-05"),
                new ServiceDrillDown(ServiceScreens.PendingTask),
                new ServiceDrillDown(ServiceScreens.ClaimsTask)
            ], opened);
            Assert.All(opened, target => Assert.Contains(target.TaskId, ServiceScreens.Tasks));
        });
    }

    [Fact]
    public void A_drill_down_argument_is_applied_to_the_interim_screens_before_they_load()
    {
        RunSta(() =>
        {
            var query = new FakeTodayQuery();
            var pending = (ServicePendingView)ServiceScreens.Create(ServiceScreens.PendingTask, () => query, NoExport, ServiceStages.ReadyForDelivery);
            Assert.Equal(ServicePendingLists.PendingDelivery, pending.SelectedList.Code);
            var srn = (ServicePendingView)ServiceScreens.Create(ServiceScreens.PendingTask, () => query, NoExport, ServiceStages.SrnOut);
            Assert.Equal(ServicePendingLists.SrnStatus, srn.SelectedList.Code);
            var jobs = (ServiceJobsView)ServiceScreens.Create(ServiceScreens.JobsTask, () => query, NoExport, ServiceStages.Delivered);
            Assert.Equal("S018", jobs.SelectedStatus.Code);
            var all = (ServiceJobsView)ServiceScreens.Create(ServiceScreens.JobsTask, () => query, NoExport, ServiceStages.Booked);
            Assert.Null(all.SelectedStatus.Code);
            var money = (ServiceMoneyView)ServiceScreens.Create(ServiceScreens.MoneyTask, () => query, NoExport, "2026-10-05");
            Assert.Equal((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)), (money.From, money.To));
            var history = (ServiceJobHistoryView)ServiceScreens.Create(ServiceScreens.JobHistoryTask, () => query, NoExport, "JOAW330SYN0007");
            Assert.Equal("JOAW330SYN0007", history.JobNumber);
            var today = (ServiceTodayView)ServiceScreens.Create(ServiceScreens.TodayTask, () => query, NoExport, "2026-10-01");
            Assert.Equal(new DateOnly(2026, 10, 1), today.BusinessDate);
            foreach (var view in new ServiceScreenView[] { pending, srn, jobs, all, money, history, today }) SpinUntil(() => !view.IsLoading);
            Assert.Equal(new DateOnly(2026, 10, 1), query.LastDate);
        });
    }

    [Fact]
    public void A_chosen_date_after_the_latest_export_says_no_file_covers_it_yet()
    {
        RunSta(() =>
        {
            var query = new FakeTodayQuery { Summaries = date => Summary(date) with { BookedToday = 0, DeliveredToday = 0, CollectionToday = null } };
            var view = new ServiceTodayView(() => query, NoExport) { BusinessDate = new(2026, 10, 7) };

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(new DateOnly(2026, 10, 7), query.LastDate);
            Assert.Equal("Nothing booked or delivered on 07 Oct 2026. The latest Service export is 05 Oct 2026; no file covers this date yet.", view.StatusText);
            Assert.Equal("—", view.Cards[4].Value);
            Assert.StartsWith("no S004 collection for the date", view.Cards[4].Detail, StringComparison.Ordinal);
            Assert.Equal("PrimaryText", view.Cards[4].Accent);
        });
    }

    [Fact]
    public void Money_entered_shows_the_manual_amount_in_green()
    {
        var card = ServiceTodayView.BuildCards(Summary(new(2026, 10, 5)) with { ManualEntered = true, ManualAmount = 12_500m })[4];
        Assert.Equal("S004 cash, card and UPI · " + ServiceTodayView.ManualEnteredText + " (12,500)", card.Detail);
        Assert.Equal("Success", card.Accent);
        Assert.Equal(new ServiceDrillDown(ServiceScreens.MoneyTask, "2026-10-05"), card.Target);
    }

    [Fact]
    public void Without_Service_data_the_screen_says_so_and_shows_no_cards()
    {
        RunSta(() =>
        {
            var query = new FakeTodayQuery { Refreshes = [] };
            var view = new ServiceTodayView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(ServiceScreenView.NoDataText, view.AsAtText);
            Assert.StartsWith(ServiceScreenView.NoDataText, view.StatusText, StringComparison.Ordinal);
            Assert.Null(query.LastDate);
            Assert.Empty(view.Cards);
            Assert.Empty(view.CardButtons);
            Assert.Empty(view.Freshness);
        });
    }

    [Fact]
    public void Export_writes_one_row_per_card_with_the_card_columns_and_the_business_date_as_the_period()
    {
        RunSta(() =>
        {
            (string Path, ExcelReportMetadata Metadata, ExcelReportData Data)? captured = null;
            var view = new ServiceTodayView(() => new FakeTodayQuery(), (path, metadata, data) => { captured = (path, metadata, data); return Task.CompletedTask; });
            view.ActivateAsync().GetAwaiter().GetResult();

            view.ExportToPathAsync("synthetic.xlsx").GetAwaiter().GetResult();

            var export = Assert.NotNull(captured);
            Assert.Equal(["Card", "Value", "Detail"], export.Data.Columns.Select(column => column.Header));
            Assert.Equal(view.Table.Columns.Select(column => (string)column.Header), export.Data.Columns.Select(column => column.Header));
            Assert.Equal(view.Cards.Count, export.Data.Rows.Count);
            Assert.Equal(view.Cards.Select(card => card.Label), export.Data.Rows.Select(row => (string)row[0]!));
            Assert.Equal(view.Cards.Select(card => card.Value), export.Data.Rows.Select(row => (string)row[1]!));
            Assert.DoesNotContain(export.Data.Columns, column => ForbiddenHeaderWords.Any(word => column.Header.Contains(word, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)), (export.Metadata.PeriodFrom, export.Metadata.PeriodTo));
            Assert.Equal(ServiceScreenView.ServiceCentreLabel, export.Metadata.AppliedScope);
        });
    }

    [Fact]
    public void The_freshness_strip_colours_amber_after_7_days_and_red_after_14()
    {
        RunSta(() =>
        {
            var refreshes = new List<ServiceRefresh>();
            foreach (var code in new[] { "S002", "S036", "S037" }) refreshes.Add(new(code, new(2026, 10, 5), 10, 1, ImportedUtc));
            foreach (var code in new[] { "S009", "S010" }) refreshes.Add(new(code, new(2026, 10, 1), 10, 2, ImportedUtc));
            foreach (var code in new[] { "S011", "S012", "S013" }) refreshes.Add(new(code, new(2026, 9, 24), 10, 3, ImportedUtc));
            var query = new FakeTodayQuery { Refreshes = refreshes, SourceKinds = new Dictionary<string, string> { ["S002"] = "RAW", ["S009"] = "RAW", ["S011"] = "CONSOLIDATED" } };
            var view = new ServiceTodayView(() => query, NoExport) { FreshnessToday = () => new DateOnly(2026, 10, 9) };

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(ServiceFreshnessColour.Fresh, view.Freshness.Single(chip => chip.Group == "Jobs").Colour);
            Assert.Equal(ServiceFreshnessColour.Amber, view.Freshness.Single(chip => chip.Group == "Pending lists").Colour);
            Assert.Equal(ServiceFreshnessColour.Red, view.Freshness.Single(chip => chip.Group == "SRN").Colour);
            Assert.Equal("SRN: last export 24 Sep 2026 (consolidated)", ServiceFreshnessStrip.TextFor(view.Freshness.Single(chip => chip.Group == "SRN")));
            var strip = Descendants<System.Windows.Controls.WrapPanel>(view).Single(panel => AutomationProperties.GetName(panel) == "Service data freshness");
            Assert.Equal(9, strip.Children.Count);
        });
    }

    private static Task NoExport(string path, ExcelReportMetadata metadata, ExcelReportData data) =>
        throw new InvalidOperationException("This test does not export.");

    private static void SpinUntil(Func<bool> condition)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            frame.Continue = true;
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        Assert.True(condition(), "The screen did not finish loading.");
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
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

    /// <summary>A query written before 1.10.0: only the interim methods, so the contract defaults answer the rest.</summary>
    private class RefreshesOnlyQuery : IServiceReportQuery
    {
        public IReadOnlyList<ServiceRefresh> Refreshes { get; init; } =
        [
            new("S009", new(2026, 9, 28), 6, 101, ImportedUtc.AddDays(-7)),
            new("S009", new(2026, 10, 5), 7, 140, ImportedUtc),
            new("S004", new(2026, 10, 5), 9, 141, ImportedUtc.AddMinutes(-2))
        ];

        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Refreshes);

        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceJobRow>>([]);

        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServicePendingRow>>([]);

        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceJobEvent>>([]);

        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceMoneyDay>>([]);

        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceMoneyChange>>([]);
    }

    private sealed class FakeTodayQuery : RefreshesOnlyQuery
    {
        public Func<DateOnly, ServiceToday> Summaries { get; init; } = Summary;
        public IReadOnlyDictionary<string, string> SourceKinds { get; init; } = new Dictionary<string, string> { ["S009"] = "RAW", ["S004"] = "RAW" };
        public DateOnly? LastDate { get; private set; }

        public Task<IReadOnlyList<ServiceFreshnessChip>> LoadFreshnessAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceFreshness.Build(Refreshes, asOf ?? new DateOnly(2026, 10, 9), SourceKinds));

        public Task<ServiceToday> LoadTodayAsync(DateOnly? businessDate = null, CancellationToken cancellationToken = default)
        {
            LastDate = businessDate;
            return Task.FromResult(Summaries(businessDate ?? Refreshes.Max(refresh => refresh.SnapshotDate)));
        }
    }
}
