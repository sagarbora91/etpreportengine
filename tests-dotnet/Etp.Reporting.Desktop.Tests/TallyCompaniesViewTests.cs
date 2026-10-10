using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop.Modules.Accounting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class TallyCompaniesViewTests
{
    [Fact]
    public void Owner_edits_a_company_and_saves_it_with_a_reason_through_the_service()
    {
        RunSta(() =>
        {
            var service = new FakeProfiles();
            var view = new TallyCompaniesView(() => "fixture", () => true, _ => service);
            view.RefreshAsync().GetAwaiter().GetResult();

            var rows = ((IEnumerable<TallyCompaniesView.TallyCompanyRow>)view.CompanyGrid.ItemsSource).ToArray();
            Assert.Equal(new[] { "Test", "Live (not enabled)" }, rows.Select(row => row.Books));
            view.CompanyGrid.SelectedIndex = 0;
            Assert.Equal("GOLDEN", view.CodeInput.Text);
            Assert.Equal("WLMHW", view.StoresInput.Text);

            view.CompanyInput.Text = "TEST - Renamed";
            view.StoresInput.Text = "WLMHW, HEMW";
            view.CostCentresInput.Text = "WLMHW=Titan World; HEMW=Helios";
            view.ReasonInput.Text = "Accountant named the test company";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var (saved, reason) = Assert.Single(service.Saved);
            Assert.Equal(1, saved.Id);
            Assert.Equal("TEST - Renamed", saved.CompanyName);
            Assert.Equal(new[] { "WLMHW", "HEMW" }, saved.StoreCodes);
            Assert.Equal("Titan World", saved.CostCentreFor("WLMHW"));
            Assert.Equal("Helios", saved.CostCentreFor("HEMW"));
            Assert.Equal("FILE", saved.DefaultDeliveryMode);
            // Fields the screen does not show are kept, not reset (posting dates feed RULE-DAT-001).
            Assert.Equal("JSON", saved.PayloadFormat);
            Assert.Equal(new DateOnly(2026, 10, 1), saved.PostingFromDate);
            Assert.Equal(new DateOnly(2027, 3, 31), saved.PostingToDate);
            Assert.Equal("Accountant named the test company", reason);
            Assert.Equal("", view.ReasonInput.Text);
            Assert.Equal("Tally company saved. Nothing has been sent to Tally.", view.StatusText.Text);
        });
    }

    [Theory]
    [InlineData("WLMHW", "Write each cost centre as STORE=Name")]
    [InlineData("WLMHW=Titan World; wlmhw=Other", "more than one cost centre")]
    public void Cost_centres_are_read_as_store_equals_name(string text, string error)
    {
        Assert.Equal("Titan World", TallyCompaniesView.ParseCostCentres(" WLMHW = Titan World ;")["wlmhw"]);
        Assert.Empty(TallyCompaniesView.ParseCostCentres(""));
        Assert.Contains(error, Assert.Throws<ArgumentException>(() => TallyCompaniesView.ParseCostCentres(text)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_company_starts_from_the_test_book_assumptions()
    {
        RunSta(() =>
        {
            var service = new FakeProfiles();
            var view = new TallyCompaniesView(() => "fixture", () => true, _ => service);
            Assert.Equal("TEST", view.BooksInput.SelectedItem);
            Assert.Equal("PER_INVOICE", view.GranularityInput.SelectedItem);
            Assert.Equal("SINGLE_LEDGER", view.PartyInput.SelectedItem);
            Assert.Equal("Cash Sales", view.LedgerInput.Text);
            view.CodeInput.Text = "NEWTEST"; view.CompanyInput.Text = "TEST - New"; view.ReasonInput.Text = "First test company";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(Assert.Single(service.Saved).Profile.Id);
        });
    }

    [Fact]
    public void Without_owner_permission_nothing_is_loaded_or_saved()
    {
        RunSta(() =>
        {
            var service = new FakeProfiles();
            var view = new TallyCompaniesView(() => "fixture", () => false, _ => service);
            view.ReasonInput.Text = "Attempt";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(service.Saved);
            Assert.Equal("Owner permission is required.", view.StatusText.Text);
        });
    }

    [Fact]
    public void A_refused_save_shows_its_reason_and_keeps_the_typing()
    {
        RunSta(() =>
        {
            var service = new FakeProfiles { Refusal = new ArgumentException(TallyProfileRules.OnlyThisPc) };
            var view = new TallyCompaniesView(() => "fixture", () => true, _ => service);
            view.EndpointInput.Text = "http://192.168.1.20:9000/";
            view.ReasonInput.Text = "Remote attempt";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("Only this PC's Tally", view.StatusText.Text, StringComparison.Ordinal);
            Assert.Equal("Remote attempt", view.ReasonInput.Text);
            Assert.True(view.IsEnabled);
        });
    }

    [Fact]
    public void Tally_companies_open_only_for_the_owner_under_integrations()
    {
        var task = TaskNavigation.Find("tally-companies")!;
        Assert.Equal("Settings → Integrations → Tally companies", task.Path);
        Assert.True(task.IsAllowed(ShellAccess.Owner));
        Assert.False(task.IsAllowed(ShellAccess.StoreManager));
        Assert.False(task.IsAllowed(ShellAccess.Viewer));
        Assert.False(new ShellNavigationService().Navigate(task.Route, ShellAccess.StoreManager).IsAllowed);
        Assert.Equal("tally-companies", TaskNavigation.Search("Tally companies", ShellAccess.Owner).First().Id);
    }

    private sealed class FakeProfiles : ITallyProfileService
    {
        public List<(TallyProfile Profile, string Reason)> Saved { get; } = [];
        public Exception? Refusal { get; init; }

        public Task<IReadOnlyList<TallyProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TallyProfile>>(
            [
                TallyProfile.NewTest("GOLDEN", "TEST - ETP Golden", new[] { "WLMHW" }) with
                {
                    Id = 1, ModifiedBy = "SHOP\\owner", PayloadFormat = "JSON",
                    PostingFromDate = new DateOnly(2026, 10, 1), PostingToDate = new DateOnly(2027, 3, 31)
                },
                TallyProfile.NewTest("LIVE", "Saagar Books", Array.Empty<string>()) with { Id = 2, Environment = "PRODUCTION" }
            ]);

        public Task<int> SaveAsync(TallyProfile profile, string reason, CancellationToken cancellationToken = default)
        {
            if (Refusal is not null) return Task.FromException<int>(Refusal);
            Saved.Add((profile, reason));
            return Task.FromResult(profile.Id ?? 3);
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
