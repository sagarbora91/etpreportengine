using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Accounting;

namespace Etp.Reporting.Desktop.Tests;

// 1.12 U3 (decisions 49, 53, 57): the profile fields that decide whether this PC may send to Tally.
public sealed class TallyCompaniesProfileFieldsTests
{
    [Fact]
    public void The_test_address_is_fixed_read_only_and_follows_the_delivery_choice()
    {
        TallyStaRunner.Run(() =>
        {
            var service = new FakeProfiles();
            var view = new TallyCompaniesView(() => true, () => service, () => "SYNTH-PC");
            Assert.True(view.EndpointInput.IsReadOnly);
            Assert.Equal("", view.EndpointInput.Text);
            Assert.Equal(TallyCompaniesView.DeliveryFileWords, view.DeliveryInput.SelectedItem);

            view.DeliveryInput.SelectedItem = TallyCompaniesView.DeliveryHttpWords;
            Assert.Equal("http://127.0.0.1:9000/", view.EndpointInput.Text);
            Assert.True(TallyConnectedMode.IsAllowedEndpoint(view.EndpointInput.Text, out _));

            view.CodeInput.Text = "SYNTH"; view.CompanyInput.Text = TallyTestCompany.Name; view.StoresInput.Text = "SC999";
            view.ReasonInput.Text = "Set up the test company";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saved = Assert.Single(service.Saved);
            Assert.Equal("HTTP", saved.DefaultDeliveryMode);
            Assert.Equal("http://127.0.0.1:9000/", saved.EndpointUrl);
        });
    }

    [Fact]
    public void The_test_pc_is_named_beside_this_pc_and_saved_with_the_profile()
    {
        TallyStaRunner.Run(() =>
        {
            var service = new FakeProfiles();
            var view = new TallyCompaniesView(() => true, () => service, () => "SYNTH-PC");
            Assert.Equal("This PC is SYNTH-PC.", view.ThisPcText.Text);
            Assert.Equal("", view.TestPcInput.Text);

            view.UseThisPcButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("SYNTH-PC", view.TestPcInput.Text);
            view.CodeInput.Text = "SYNTH"; view.CompanyInput.Text = TallyTestCompany.Name; view.ReasonInput.Text = "Name the test PC";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("SYNTH-PC", Assert.Single(service.Saved).TestMachineName);

            // Selecting a saved company shows its test PC and whether this PC is it.
            view.RefreshAsync().GetAwaiter().GetResult();
            view.CompanyGrid.SelectedIndex = 0;
            Assert.Equal("OTHER-PC", view.TestPcInput.Text);
            Assert.Equal("This PC is SYNTH-PC.", view.ThisPcText.Text);

            var same = new TallyCompaniesView(() => true, () => service, () => "other-pc");
            same.RefreshAsync().GetAwaiter().GetResult();
            same.CompanyGrid.SelectedIndex = 0;
            Assert.Equal("This PC is other-pc. Sending is allowed here.", same.ThisPcText.Text);
        });
    }

    [Fact]
    public void Books_are_a_chip_with_the_word_and_live_books_cannot_name_a_test_pc()
    {
        TallyStaRunner.Run(() =>
        {
            var view = new TallyCompaniesView(() => true, () => new FakeProfiles());
            Assert.Equal("TEST books", view.BooksChip.Text);
            Assert.Equal(StatusTone.Information, view.BooksChip.Tone);
            Assert.True(view.TestPcInput.IsEnabled);
            view.BooksInput.SelectedItem = "PRODUCTION";
            Assert.Equal("LIVE books", view.BooksChip.Text);
            Assert.Equal(StatusTone.Warning, view.BooksChip.Tone);
            Assert.False(view.TestPcInput.IsEnabled);
            Assert.False(view.UseThisPcButton.IsEnabled);
        });
    }

    [Fact]
    public void The_build_field_explains_the_word_EDUCATIONAL()
    {
        TallyStaRunner.Run(() =>
        {
            var view = new TallyCompaniesView(() => true, () => new FakeProfiles());
            Assert.Contains("EDUCATIONAL", view.BuildHintText.Text, StringComparison.Ordinal);
            Assert.Contains("only when Tally itself says it is Educational", view.BuildHintText.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Without_owner_permission_the_profile_cannot_be_saved()
    {
        TallyStaRunner.Run(() =>
        {
            var service = new FakeProfiles();
            var view = new TallyCompaniesView(() => false, () => service);
            view.TestPcInput.Text = "SYNTH-PC"; view.ReasonInput.Text = "Attempt";
            view.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(service.Saved);
            Assert.Equal("Owner permission is required.", view.StatusText.Text);
        });
    }

    private sealed class FakeProfiles : ITallyProfileService
    {
        public List<TallyProfile> Saved { get; } = [];

        public Task<IReadOnlyList<TallyProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TallyProfile>>(
            [
                TallyProfile.NewTest("SYNTH", TallyTestCompany.Name, new[] { "SC999" }) with
                { Id = 1, DefaultDeliveryMode = "HTTP", EndpointUrl = "http://127.0.0.1:9000/", TestMachineName = "OTHER-PC" }
            ]);

        public Task<int> SaveAsync(TallyProfile profile, string reason, CancellationToken cancellationToken = default)
        {
            Saved.Add(profile);
            return Task.FromResult(profile.Id ?? 1);
        }
    }
}
