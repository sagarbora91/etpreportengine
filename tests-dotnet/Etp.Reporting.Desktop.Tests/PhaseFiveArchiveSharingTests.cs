using Etp.Reporting.Desktop.Modules.Archive;
using Etp.Reporting.Application.Access;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Tests;

public sealed class PhaseFiveArchiveSharingTests
{
    [Fact]
    public void Smtp_test_access_follows_loaded_role_when_archive_is_opened_or_refreshed()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var access = new AccessSession("fixture", "Not loaded", AccessRole.None, false);
                var view = new ArchiveWorkspaceView(new ArchiveDistributionPresentationSession(
                    _ => throw new InvalidOperationException("No archive fixture"),
                    _ => throw new InvalidOperationException("No contacts fixture"),
                    _ => throw new InvalidOperationException("No distribution fixture")), () => "synthetic",
                    (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, new NoSharing());
                view.AttachHost(() => access, (_, _, _) => Task.CompletedTask, exception => exception.Message);
                var button = (Button)view.FindName("TestSmtpButton");
                Assert.False(button.IsEnabled);

                access = new("fixture", "Owner", AccessRole.Owner, true);
                view.SelectTask("archive");
                Assert.True(button.IsEnabled);

                access = new("fixture", "Viewer", AccessRole.Viewer, true);
                view.RefreshAsync().GetAwaiter().GetResult();
                Assert.False(button.IsEnabled);

                access = new("fixture", "Owner", AccessRole.Owner, true);
                view.RefreshAsync().GetAwaiter().GetResult();
                Assert.True(button.IsEnabled);

                access = new("fixture", "Manager", AccessRole.StoreManager, true);
                view.SelectTask("archive");
                Assert.False(button.IsEnabled);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("SMTP role presentation failed.", failure);
    }

    private sealed class NoSharing : IArchiveShareLauncher
    {
        public void OpenWhatsApp(string attachmentPath, string message, string? phone) => throw new InvalidOperationException("No external sharing in this test.");
        public void OpenEmailDraft(string shareFolderPath, string attachmentPath, string to, string? cc, string subject, string body) => throw new InvalidOperationException("No external sharing in this test.");
    }

    [Fact]
    public void WhatsApp_handoff_targets_desktop_protocol_with_escaped_text_and_validated_number()
    {
        var uri = ArchiveShareLauncher.BuildWhatsAppUri("Attach the PDF & review", "+91 (000) 001-2345");
        Assert.StartsWith("whatsapp://send?", uri);
        Assert.Contains("text=Attach%20the%20PDF%20%26%20review", uri);
        Assert.EndsWith("&phone=910000012345", uri);
        Assert.DoesNotContain("phone=", ArchiveShareLauncher.BuildWhatsAppUri("Choose recipient", null));
        Assert.Throws<ArgumentException>(() => ArchiveShareLauncher.BuildWhatsAppUri("test", "123"));
        Assert.Throws<ArgumentException>(() => ArchiveShareLauncher.BuildWhatsAppUri("test", "1234567&text=wrong"));
    }
}
