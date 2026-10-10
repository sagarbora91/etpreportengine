using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop.Modules.Accounting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class TallyLedgersViewTests
{
    [Fact]
    public void Owner_sees_each_stores_missing_ledgers_and_saves_one_as_a_new_version()
    {
        TallyVouchersViewTests.RunSta(() =>
        {
            var mappings = new FakeMappings();
            var view = new TallyLedgersView(() => "fixture", () => true, _ => new TallyVouchersViewTests.FakeProfiles(), _ => mappings);
            view.RefreshAsync().GetAwaiter().GetResult();

            Assert.Equal(new[] { "STOREA", "STOREB" }, (IEnumerable<string>)view.StoreInput.ItemsSource);
            Assert.Equal("STOREA", view.StoreInput.SelectedItem);
            var rows = ((IEnumerable<TallyLedgersView.LedgerRow>)view.MappingGrid.ItemsSource).ToArray();
            Assert.Equal(new[] { "TENDER_CASH|STOREA|Cash|Yes", "TENDER_CASH|STOREA|Cash in hand|No", "SALES_REVENUE|All stores|Sales|Yes" },
                rows.Select(row => $"{row.Event}|{row.Store}|{row.Ledger}|{row.InUse}"));
            Assert.Equal("Still needed for STOREA: OUTPUT_CGST_9, TENDER_UPI. Invoices that need one of these are held back until it is chosen.", view.NeededText.Text);

            view.EventInput.Text = "TENDER_UPI";
            view.LedgerInput.Text = "UPI receivable - PhonePe";
            view.FromInput.SelectedDate = new DateTime(2026, 10, 1);
            view.ReasonInput.Text = "Accountant named the UPI ledger";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(("STOREA", "TENDER_UPI", "UPI receivable - PhonePe", new DateOnly(2026, 10, 1), "Accountant named the UPI ledger"), Assert.Single(mappings.Saved));
            Assert.Equal("", view.LedgerInput.Text);
            Assert.StartsWith("Ledger saved as a new version.", view.StatusText.Text, StringComparison.Ordinal);
            Assert.Equal("Still needed for STOREA: OUTPUT_CGST_9. Invoices that need one of these are held back until it is chosen.", view.NeededText.Text);
        });
    }

    [Fact]
    public void A_refused_ledger_shows_why_and_keeps_the_typing()
    {
        TallyVouchersViewTests.RunSta(() =>
        {
            var mappings = new FakeMappings { Refusal = new ArgumentException("Enter the Tally ledger name exactly as Tally shows it (at most 200 characters).") };
            var view = new TallyLedgersView(() => "fixture", () => true, _ => new TallyVouchersViewTests.FakeProfiles(), _ => mappings);
            view.RefreshAsync().GetAwaiter().GetResult();
            view.EventInput.Text = "TENDER_UPI";
            view.ReasonInput.Text = "Missing ledger";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("exactly as Tally shows it", view.StatusText.Text, StringComparison.Ordinal);
            Assert.Equal("Missing ledger", view.ReasonInput.Text);
        });
    }

    [Fact]
    public void Without_owner_permission_nothing_is_saved()
    {
        TallyVouchersViewTests.RunSta(() =>
        {
            var mappings = new FakeMappings();
            var view = new TallyLedgersView(() => "fixture", () => false, _ => new TallyVouchersViewTests.FakeProfiles(), _ => mappings);
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(mappings.Saved);
            Assert.Equal("Owner permission is required.", view.StatusText.Text);
        });
    }

    private sealed class FakeMappings : ITallyLedgerMappingService
    {
        private readonly List<TallyLedgerMappingRow> rows =
        [
            new("TENDER_CASH", "STOREA", "Cash", "Cash", new(2026, 9, 1), null, 2, true, "SHOP\\owner", new(2026, 9, 1)),
            new("TENDER_CASH", "STOREA", "Cash in hand", "Cash in hand", new(2026, 8, 1), null, 1, false, "SHOP\\owner", new(2026, 8, 1)),
            new("SALES_REVENUE", null, "Sales", "Sales", new(2026, 8, 1), null, 1, true, "SHOP\\owner", new(2026, 8, 1))
        ];

        public List<(string Store, string Event, string Ledger, DateOnly From, string Reason)> Saved { get; } = [];
        public Exception? Refusal { get; init; }

        public Task<IReadOnlyList<TallyLedgerMappingRow>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TallyLedgerMappingRow>>(rows.ToArray());

        public Task<IReadOnlyList<string>> LoadNeededEventsAsync(string storeCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["OUTPUT_CGST_9", "SALES_REVENUE", "TENDER_CASH", "TENDER_UPI"]);

        public Task SaveAsync(string storeCode, string businessEvent, string ledgerName, DateOnly effectiveFrom, string reason, CancellationToken cancellationToken = default)
        {
            if (Refusal is not null) return Task.FromException(Refusal);
            Saved.Add((storeCode, businessEvent, ledgerName, effectiveFrom, reason));
            rows.Add(new(businessEvent, storeCode, ledgerName, ledgerName, new(2026, 1, 1), null, 1, true, "SHOP\\owner", DateTime.UtcNow));
            return Task.CompletedTask;
        }
    }
}
