using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.2 (AUDIT-2 S-07): the Owner-only "Show customer names in shared packs" box in Settings > Sharing. Default off,
/// a change is a draft until saved with the Owner's reason, discard restores the loaded value, and an unwired setting
/// hides the box.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class SettingsSharePackNamesTests
{
    [Fact]
    public void The_box_loads_off_saves_with_the_reason_and_discards_like_the_other_sharing_settings()
    {
        RunSta(async () =>
        {
            var stored = false;
            var saves = new List<(bool Include, string Reason)>();
            var view = Create(new Administration());
            view.SharePackCustomerNames = new(
                (_, _) => Task.FromResult(stored),
                (_, include, reason, _) => { saves.Add((include, reason)); stored = include; return Task.CompletedTask; });
            view.SelectIntegrationTask("sharing");
            await view.PrepareForDisplayAsync(true);
            var box = (CheckBox)view.FindName("ShareCustomerNamesInput");
            Assert.Equal(Visibility.Visible, box.Visibility);
            Assert.False(box.IsChecked);
            Assert.False(view.HasProductDraft);

            box.IsChecked = true;
            Assert.True(view.HasProductDraft);
            view.DiscardProductDraft();
            Assert.False(box.IsChecked);
            Assert.False(view.HasProductDraft);

            box.IsChecked = true;
            ((TextBox)view.FindName("ProductSettingsReasonInput")).Text = "Owner shares packs with the accountant";
            Assert.True(await view.SaveProductConfigurationAsync());
            Assert.Equal(new[] { (true, "Owner shares packs with the accountant") }, saves.Select(save => (save.Include, save.Reason)));
            Assert.False(view.HasProductDraft);

            // Saving something else does not write the names setting again.
            ((TextBox)view.FindName("SmtpHostInput")).Text = "smtp2.example.invalid";
            ((TextBox)view.FindName("ProductSettingsReasonInput")).Text = "Owner moved SMTP";
            Assert.True(await view.SaveProductConfigurationAsync());
            Assert.Single(saves);
        });
    }

    [Fact]
    public void Without_the_setting_wired_the_box_is_hidden_and_never_a_draft()
    {
        RunSta(async () =>
        {
            var view = Create(new Administration());
            view.SelectIntegrationTask("sharing");
            await view.PrepareForDisplayAsync(true);
            var box = (CheckBox)view.FindName("ShareCustomerNamesInput");
            Assert.Equal(Visibility.Collapsed, box.Visibility);
            box.IsChecked = true;
            Assert.False(view.HasProductDraft);
        });
    }

    private static SettingsWorkspaceView Create(Administration service)
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpSyntheticSharePack_" + Guid.NewGuid().ToString("N"));
        var view = new SettingsWorkspaceView(new(new DesktopSettingsStore(root), new DesktopConnectionState(
            @"Server=.\SQLEXPRESS;Database=EtpSyntheticSharePack;Integrated Security=True;TrustServerCertificate=True")),
            _ => throw new InvalidOperationException("No database connection is used by this test."),
            _ => service, root);
        view.UpdateAccess(new(true, true));
        return view;
    }

    private sealed class Administration : IAdministrationService
    {
        public Task<AdministrationDashboard> LoadAsync(string masterType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdministrationDashboard([], [], [], [],
                new("docs", "share", "smtp.example.invalid", 587, true, "owner@example.invalid", 20, DateTime.UtcNow, "fixture")));
        public Task SaveProductConfigurationAsync(SaveProductConfiguration command, CancellationToken cancellationToken = default) =>
            string.IsNullOrWhiteSpace(command.Reason)
                ? Task.FromException(new InvalidOperationException("Owner change reason is required."))
                : Task.CompletedTask;
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
