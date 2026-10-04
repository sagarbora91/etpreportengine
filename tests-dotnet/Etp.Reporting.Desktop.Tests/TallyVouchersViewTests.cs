using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop.Modules.Accounting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class TallyVouchersViewTests
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    [Fact]
    public void Owner_prepares_a_day_saves_it_and_accepts_its_warning()
    {
        RunSta(() =>
        {
            var batches = new FakeBatches();
            var view = new TallyVouchersView(() => "fixture", () => true, _ => new FakeProfiles(), _ => batches);
            view.LoadCompaniesAsync().GetAwaiter().GetResult();
            Assert.Equal("TEST - ETP Golden", ((TallyProfile)view.CompanyInput.SelectedItem).CompanyName);
            Assert.Equal(new[] { "STOREA", "STOREB" }, (IEnumerable<string>)view.StoreInput.ItemsSource);
            view.DateInput.SelectedDate = Day.ToDateTime(TimeOnly.MinValue);

            Click(view.PrepareButton);
            Assert.Equal((1, "STOREA", Day), batches.Previewed!.Value);
            var rows = ((IEnumerable<TallyVouchersView.VoucherRow>)view.VoucherGrid.ItemsSource).ToArray();
            Assert.Equal(new[] { "INV-1:Ready", "INV-2:Left out" }, rows.Select(row => $"{row.Invoice}:{row.Result}"));
            Assert.StartsWith("NOT_IN_SCOPE_7A: split payment", rows[1].Reason, StringComparison.Ordinal);
            Assert.Equal("2 invoice(s): 1 ready, 1 left out for a later step, 0 need fixing. Vouchers total 1180.00.", view.SummaryText.Text);
            Assert.Equal("Warning", Assert.Single((IEnumerable<TallyVouchersView.FindingRow>)view.FindingGrid.ItemsSource).Kind);
            Assert.True(view.SaveButton.IsEnabled);
            Assert.False(view.AcceptButton.IsEnabled);

            Click(view.SaveButton);
            Assert.Equal(1, batches.Saves);
            Assert.False(view.SaveButton.IsEnabled);
            Assert.Contains("Saved as batch 42", view.StatusText.Text, StringComparison.Ordinal);
            Assert.True(view.AcceptButton.IsEnabled);

            view.FindingGrid.SelectedIndex = 0;
            view.ReasonInput.Text = "Owner confirmed the late day";
            Click(view.AcceptButton);
            Assert.Equal((7L, "Owner confirmed the late day"), Assert.Single(batches.Accepted));
            Assert.Equal("Warning accepted.", view.StatusText.Text);
            Assert.False(view.AcceptButton.IsEnabled);
        });
    }

    [Fact]
    public void A_warning_cannot_be_accepted_before_the_batch_is_saved()
    {
        RunSta(() =>
        {
            var batches = new FakeBatches();
            var view = new TallyVouchersView(() => "fixture", () => true, _ => new FakeProfiles(), _ => batches);
            view.LoadCompaniesAsync().GetAwaiter().GetResult();
            Click(view.PrepareButton);
            view.FindingGrid.SelectedIndex = 0;
            var refused = Assert.ThrowsAsync<ArgumentException>(view.AcceptAsync).GetAwaiter().GetResult();
            Assert.Contains("saved batch", refused.Message, StringComparison.Ordinal);
            Assert.Empty(batches.Accepted);
        });
    }

    [Fact]
    public void Changing_the_day_clears_the_prepared_vouchers()
    {
        RunSta(() =>
        {
            var view = new TallyVouchersView(() => "fixture", () => true, _ => new FakeProfiles(), _ => new FakeBatches());
            view.LoadCompaniesAsync().GetAwaiter().GetResult();
            Click(view.PrepareButton);
            Assert.True(view.SaveButton.IsEnabled);
            view.DateInput.SelectedDate = DateTime.Today.AddDays(-3);
            Assert.False(view.SaveButton.IsEnabled);
            Assert.Null(view.VoucherGrid.ItemsSource);
        });
    }

    [Fact]
    public void Only_test_companies_in_use_are_offered_and_only_to_the_owner()
    {
        RunSta(() =>
        {
            var batches = new FakeBatches();
            var view = new TallyVouchersView(() => "fixture", () => true, _ => new FakeProfiles(), _ => batches);
            view.LoadCompaniesAsync().GetAwaiter().GetResult();
            Assert.Single((IEnumerable<TallyProfile>)view.CompanyInput.ItemsSource);

            var viewer = new TallyVouchersView(() => "fixture", () => false, _ => new FakeProfiles(), _ => batches);
            Click(viewer.PrepareButton);
            Assert.Equal("Owner permission is required.", viewer.StatusText.Text);
            Assert.Null(batches.Previewed);
        });
    }

    [Fact]
    public void Tally_vouchers_and_ledgers_open_only_for_the_owner()
    {
        foreach (var (id, path) in new[] { ("tally-vouchers", "Settings → Accounting → Tally vouchers"), ("tally-ledgers", "Settings → Integrations → Tally ledgers") })
        {
            var task = TaskNavigation.Find(id)!;
            Assert.Equal(path, task.Path);
            Assert.True(task.IsAllowed(ShellAccess.Owner));
            Assert.False(task.IsAllowed(ShellAccess.StoreManager));
            Assert.False(task.IsAllowed(ShellAccess.Viewer));
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal sealed class FakeProfiles : ITallyProfileService
    {
        public Task<IReadOnlyList<TallyProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TallyProfile>>(
            [
                TallyProfile.NewTest("GOLDEN", "TEST - ETP Golden", new[] { "STOREA", "STOREB" }) with { Id = 1 },
                TallyProfile.NewTest("OFF", "TEST - Switched off", new[] { "STOREA" }) with { Id = 2, IsEnabled = false },
                TallyProfile.NewTest("LIVE", "Live books", new[] { "STOREB" }) with { Id = 3, Environment = "PRODUCTION" }
            ]);

        public Task<int> SaveAsync(TallyProfile profile, string reason, CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeBatches : ITallySalesBatchService
    {
        public (int Profile, string Store, DateOnly Day)? Previewed { get; private set; }
        public int Saves { get; private set; }
        public List<(long Id, string Reason)> Accepted { get; } = [];

        public Task<SalesVoucherPreview> PreviewAsync(int tallyProfileId, string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default)
        {
            Previewed = (tallyProfileId, storeCode, businessDate);
            var profile = TallyProfile.NewTest("GOLDEN", "TEST - ETP Golden", new[] { storeCode });
            InvoiceAccountingSource Sale(string document, params InvoiceSourceTender[] tenders) => new(1, storeCode, 2027, document, Day,
                [new("1", "SAREE1", "INV", 1m, 1180m, 1000m, 180m)], [new("SAREE1", 9m, 90m, 9m, 90m, 0m, 0m, 0m)], tenders);
            var plan = TallySalesVoucherComposer.Compose(new(profile, null, false,
            [
                new("TENDER_CASH", 1, 1, "Cash", "Cash"), new("SALES_REVENUE", 2, 1, "Sales", "Sales"),
                new("OUTPUT_CGST_9", 3, 1, "Output CGST 9%", "Output CGST 9%"), new("OUTPUT_SGST_9", 4, 1, "Output SGST 9%", "Output SGST 9%")
            ]), [Sale("INV-1", new("CASH", "Cash", 1180m)), Sale("INV-2", new("CASH", "Cash", 500m), new("PHONEPE", "UPI", 680m))]);
            ValidationFinding warning = new("RULE-DAT-002", 1, "WARN", 1, "Invoice INV-1", "25-Aug-2026", "within 30 days", "This voucher is more than 30 days old.", null);
            return Task.FromResult(new SalesVoucherPreview(9, profile, storeCode, businessDate, plan, [warning]));
        }

        public Task<long> SaveAsync(SalesVoucherPreview preview, CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(42L);
        }

        public Task<IReadOnlyList<SavedValidationFinding>> LoadFindingsAsync(long batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SavedValidationFinding>>(
            [
                new(7, 1, "RULE-DAT-002", "WARN", "Invoice INV-1", "This voucher is more than 30 days old.", null,
                    Accepted.Count == 0 ? null : "SHOP\\owner", Accepted.Count == 0 ? null : Accepted[0].Reason)
            ]);

        public Task AcceptWarningAsync(long findingId, string reason, CancellationToken cancellationToken = default)
        {
            Accepted.Add((findingId, reason));
            return Task.CompletedTask;
        }
    }

    internal static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
