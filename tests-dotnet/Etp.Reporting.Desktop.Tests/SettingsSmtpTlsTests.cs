using System.Reflection;
using System.Windows.Controls;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

public sealed class SettingsSmtpTlsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tls_only_edits_are_retained_discarded_and_saved_without_overwriting_the_loaded_value(bool initialTls)
    {
        RunSta(async () =>
        {
            var service = new Administration(initialTls);
            var view = Create(service);
            await view.PrepareForDisplayAsync(true);
            var tls = (CheckBox)view.FindName("SmtpUseTlsInput");
            Assert.Equal(initialTls, tls.IsChecked);
            Assert.False(view.HasProductDraft);

            tls.IsChecked = !initialTls;
            Assert.True(view.HasProductDraft);
            await view.PrepareForDisplayAsync(true);
            Assert.Equal(1, service.Loads);
            Assert.Equal(!initialTls, tls.IsChecked);
            view.DiscardProductDraft();
            Assert.Equal(initialTls, tls.IsChecked);
            Assert.False(view.HasProductDraft);

            ((TextBox)view.FindName("ProductSettingsReasonInput")).Text = "Confirm the existing SMTP configuration";
            Assert.True(await view.SaveProductConfigurationAsync());
            Assert.Equal(initialTls, service.Saved!.SmtpUseTls);

            tls.IsChecked = !initialTls;
            ((TextBox)view.FindName("ProductSettingsReasonInput")).Text = "Owner approved SMTP TLS change";
            service.FailSave = true;
            Assert.False(await view.SaveProductConfigurationAsync());
            Assert.True(view.HasProductDraft);
            Assert.Equal(!initialTls, tls.IsChecked);
            service.FailSave = false;
            Assert.True(await view.SaveProductConfigurationAsync());
            Assert.Equal(!initialTls, service.Saved!.SmtpUseTls);
            Assert.False(view.HasProductDraft);
            tls.IsChecked = initialTls;
            view.DiscardProductDraft();
            Assert.Equal(!initialTls, tls.IsChecked);
            Assert.False(view.HasProductDraft);
        });
    }

    [Fact]
    public void Navigation_save_does_not_drop_a_TLS_only_change_after_saving_the_Tally_draft()
    {
        RunSta(async () =>
        {
            var accounting = DispatchProxy.Create<IAccountingService, AccountingWorkspaceControlsTests.AccountingProxy>();
            var service = new Administration(true);
            var view = Create(service, accounting);
            await view.PrepareForDisplayAsync(true);
            var tally = ((StackPanel)view.FindName("ProductSettingsPanel")).Children.OfType<TallyDestinationSettingsView>().Single();
            var fields = tally.Children.OfType<TextBox>().ToArray();
            fields[0].Text = "TEST New destination";
            fields[2].Text = "Owner approved Tally destination";
            ((CheckBox)view.FindName("SmtpUseTlsInput")).IsChecked = false;

            Assert.False(await view.SaveProductConfigurationAsync());
            Assert.NotNull(((AccountingWorkspaceControlsTests.AccountingProxy)(object)accounting).Saved);
            Assert.Equal(1, service.SaveAttempts);
            Assert.Null(service.Saved);
            Assert.True(view.HasProductDraft);

            ((TextBox)view.FindName("ProductSettingsReasonInput")).Text = "Owner approved SMTP TLS change";
            Assert.True(await view.SaveProductConfigurationAsync());
            Assert.False(service.Saved!.SmtpUseTls);
            Assert.False(view.HasProductDraft);
        });
    }

    private static SettingsWorkspaceView Create(Administration service, IAccountingService? accounting = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpSyntheticTls_" + Guid.NewGuid().ToString("N"));
        var view = new SettingsWorkspaceView(new(new DesktopSettingsStore(root), new DesktopConnectionState(
            @"Server=.\SQLEXPRESS;Database=EtpSyntheticTls;Integrated Security=True;TrustServerCertificate=True")),
            _ => throw new InvalidOperationException("No database connection is used by this test."),
            _ => service, root, accounting is null ? null : _ => accounting);
        view.UpdateAccess(new(true, true));
        return view;
    }

    private sealed class Administration(bool initialTls) : IAdministrationService
    {
        public int Loads { get; private set; }
        public int SaveAttempts { get; private set; }
        public SaveProductConfiguration? Saved { get; private set; }
        public bool FailSave { get; set; }
        public Task<AdministrationDashboard> LoadAsync(string masterType, CancellationToken cancellationToken = default)
        {
            Loads++;
            return Task.FromResult(new AdministrationDashboard([], [], [], [],
                new("docs", "share", "smtp.example.invalid", 587, initialTls, "owner@example.invalid", 20, DateTime.UtcNow, "fixture")));
        }
        public Task SaveProductConfigurationAsync(SaveProductConfiguration command, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (FailSave || string.IsNullOrWhiteSpace(command.Reason))
                return Task.FromException(new InvalidOperationException("Owner change reason is required."));
            Saved = command;
            return Task.CompletedTask;
        }
        public Task SaveMasterAsync(SaveControlledMaster command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveUserAsync(SaveApplicationUser command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
