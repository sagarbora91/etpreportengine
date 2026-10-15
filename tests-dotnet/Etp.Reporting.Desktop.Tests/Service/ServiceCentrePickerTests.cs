using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Application.Stores;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// 1.12.1 lane P1: the Service screens filter per Service Centre. With two or more centres a "Service Centre" picker beside
/// the title offers All Service Centres and each centre; a choice scopes the screen's query (ServiceCentreServices.Scope),
/// names the centre beside the title and in the exports, says under the title what the choice does not cover, and is
/// shared by the next Service screen. With one centre, or a query that cannot be scoped, nothing changes. Synthetic only.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServiceCentrePickerTests
{
    private static readonly ServiceCentre First = new("SCSYN1", "Synthetic Service Centre One", "JOSCSYN1");
    private static readonly ServiceCentre Second = new("SC999", "Synthetic Service Centre", "JOSC999");

    [Fact]
    public void One_centre_shows_no_picker_and_names_the_centre()
    {
        WithServices([First], () =>
        {
            var calls = new List<string?>();
            var view = new ServicePendingBoardView(() => new ScopedFake(null, calls), NoExport);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.False(view.CentrePickerVisible);
            Assert.Empty(view.CentreChoices);
            Assert.Equal(First.Name, view.CentreLabel);
            Assert.Null(view.ChosenCentreCode);
            Assert.Equal("", view.CentreScopeText);
            Assert.All(calls, centre => Assert.Null(centre));
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text == First.Name + " · read only");
        });
    }

    [Fact]
    public void Two_centres_offer_the_picker_and_a_choice_scopes_the_query_and_carries_to_the_next_screen()
    {
        WithServices([First, Second], () =>
        {
            var calls = new List<string?>();
            var board = new ServicePendingBoardView(() => new ScopedFake(null, calls), NoExport);

            board.ActivateAsync().GetAwaiter().GetResult();

            Assert.True(board.CentrePickerVisible);
            // 1.12.1 P8: "Not matched to any centre" is the last choice with two or more centres.
            Assert.Equal([ServiceCentreServices.AllCentresLabel, First.Name, Second.Name, "Not matched to any centre"], board.CentreChoices.Select(choice => choice.Label));
            Assert.Equal([null, First.StoreCode, Second.StoreCode, "~NOT_MATCHED"], board.CentreChoices.Select(choice => choice.Code));
            Assert.Equal(ServiceCentreServices.AllCentresLabel, board.CentreLabel);
            Assert.Null(board.ChosenCentreCode);
            Assert.Equal("", board.CentreScopeText);
            Assert.Contains(null, calls);
            Assert.DoesNotContain(Second.StoreCode, calls);

            calls.Clear();
            board.ChooseCentreAsync(Second.StoreCode).GetAwaiter().GetResult();

            Assert.Equal(Second.StoreCode, board.ChosenCentreCode);
            Assert.Equal(Second.Name, board.CentreLabel);
            Assert.Equal(ServiceCentreServices.ScopeNote(Second.Name), board.CentreScopeText);
            Assert.NotEmpty(calls);
            Assert.All(calls, centre => Assert.Equal(Second.StoreCode, centre));
            // The picker names the centre, so the caption beside it says only "read only".
            Assert.Contains(Descendants<TextBlock>(board), block => block.Text == "read only");
            Assert.Equal(Second.StoreCode, ((ServiceListChoice)Descendants<ComboBox>(board)
                .Single(combo => System.Windows.Automation.AutomationProperties.GetName(combo) == "Service Centre").SelectedItem).Code);

            // The next Service screen opens on the same centre.
            calls.Clear();
            var today = new ServiceTodayView(() => new ScopedFake(null, calls), NoExport);
            today.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(Second.StoreCode, today.ChosenCentreCode);
            Assert.All(calls, centre => Assert.Equal(Second.StoreCode, centre));

            // Back to all centres.
            calls.Clear();
            today.ChooseCentreAsync(null).GetAwaiter().GetResult();
            Assert.Null(today.ChosenCentreCode);
            Assert.Equal(ServiceCentreServices.AllCentresLabel, today.CentreLabel);
            Assert.All(calls, centre => Assert.Null(centre));
        });
    }

    [Fact]
    public void A_chosen_centre_that_is_gone_or_a_query_that_cannot_be_scoped_shows_every_centre()
    {
        WithServices([First, Second], () =>
        {
            ServiceCentreServices.SelectedStoreCode = "SYNGONE";
            var calls = new List<string?>();
            var board = new ServicePendingBoardView(() => new ScopedFake(null, calls), NoExport);
            board.ActivateAsync().GetAwaiter().GetResult();
            Assert.Null(board.ChosenCentreCode);
            Assert.Equal(ServiceCentreServices.AllCentresLabel, board.CentreLabel);
            Assert.All(calls, centre => Assert.Null(centre));

            // A query that cannot be scoped (the default scope only knows the SQL Server query): no picker, every centre.
            ServiceCentreServices.Scope = ServiceCentreServices.DefaultScope;
            ServiceCentreServices.SelectedStoreCode = Second.StoreCode;
            var plain = new ServicePendingBoardView(() => new ScopedFake(null, calls), NoExport);
            plain.ActivateAsync().GetAwaiter().GetResult();
            Assert.False(plain.CentrePickerVisible);
            Assert.Null(plain.ChosenCentreCode);
            Assert.Equal(ServiceCentreServices.AllCentresLabel, plain.CentreLabel);
            Assert.Equal("", plain.CentreScopeText);
        });
    }

    [Fact]
    public void The_money_check_and_job_history_say_the_choice_does_not_change_them_and_exports_name_the_centre()
    {
        WithServices([First, Second], () =>
        {
            ServiceCentreServices.SelectedStoreCode = First.StoreCode;
            var calls = new List<string?>();
            ExcelReportMetadata? written = null;
            var money = new ServiceMoneyView(() => new ScopedFake(null, calls), (_, metadata, _) => { written = metadata; return Task.CompletedTask; });
            money.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(First.StoreCode, money.ChosenCentreCode);
            Assert.Equal(ServiceMoneyView.MoneyScopeNote, money.CentreScopeText);
            Assert.DoesNotMatch(@"\bS0\d\d\b", money.CentreScopeText);

            var history = new ServiceJobHistoryView(() => new ScopedFake(null, calls), NoExport);
            history.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(ServiceJobHistoryView.JobHistoryScopeNote, history.CentreScopeText);

            // The export's applied scope is the chosen centre's name (the delegate captures it; nothing is written).
            money.ExportToPathAsync(Path.Combine(Path.GetTempPath(), "p1-synthetic-not-written.xlsx")).GetAwaiter().GetResult();
            Assert.Equal(First.Name, written!.AppliedScope);
        });
    }

    [Fact]
    public void The_choices_and_the_chosen_centre_follow_the_catalogue()
    {
        Assert.Equal([ServiceCentreServices.AllCentresLabel, First.Name], ServiceCentreServices.Choices([First]).Select(choice => choice.Label));
        Assert.Null(ServiceCentreServices.Chosen([First], First.StoreCode));
        Assert.Equal(Second, ServiceCentreServices.Chosen([First, Second], " sc999 "));
        Assert.Null(ServiceCentreServices.Chosen([First, Second], null));
        Assert.Null(ServiceCentreServices.Chosen([First, Second], "SYNGONE"));
        Assert.DoesNotMatch(@"\bS0\d\d\b", ServiceCentreServices.ScopeNote(Second.Name));
    }

    // ------------------------------------------------------------------ helpers

    private static void WithServices(IReadOnlyList<ServiceCentre> centres, Action action)
    {
        var catalog = ServiceCentreServices.Catalog;
        var scope = ServiceCentreServices.Scope;
        var selected = ServiceCentreServices.SelectedStoreCode;
        try
        {
            ServiceCentreServices.Catalog = () => new FixedServiceCentreCatalog(centres);
            ServiceCentreServices.Scope = (query, code) => query is ScopedFake fake ? new ScopedFake(code, fake.Calls) : null;
            ServiceCentreServices.SelectedStoreCode = null;
            RunSta(action);
        }
        finally
        {
            ServiceCentreServices.Catalog = catalog;
            ServiceCentreServices.Scope = scope;
            ServiceCentreServices.SelectedStoreCode = selected;
        }
    }

    private static Task NoExport(string path, ExcelReportMetadata metadata, ExcelReportData data) =>
        throw new InvalidOperationException("This test does not export.");

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

    /// <summary>A Service query that records, for every read, the Service Centre it was scoped to (null = all).</summary>
    private sealed class ScopedFake(string? centre, List<string?> calls) : IServiceReportQuery
    {
        public List<string?> Calls => calls;

        private T Record<T>(T value) { calls.Add(centre); return value; }

        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Record<IReadOnlyList<ServiceRefresh>>([new("S009", new(2026, 10, 5), 1, 1, new DateTime(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc))]));

        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record<IReadOnlyList<ServiceJobRow>>([]));

        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record<IReadOnlyList<ServicePendingRow>>([]));

        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record<IReadOnlyList<ServiceJobEvent>>([]));

        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record<IReadOnlyList<ServiceMoneyDay>>([]));

        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Record<IReadOnlyList<ServiceMoneyChange>>([]));

        public Task<ServicePendingBoard> LoadPendingBoardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Record(new ServicePendingBoard([], 0, 0, 0, 0, 0, null)));

        public Task<ServiceJobDetail?> LoadJobAsync(string jobOrderNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record<ServiceJobDetail?>(null));
    }
}
