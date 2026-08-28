using System.Diagnostics;
using System.Windows;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Archive;

public interface IArchiveShareLauncher
{
    void OpenWhatsApp(string attachmentPath, string message, string? phone);
    void OpenEmailDraft(string shareFolderPath, string attachmentPath, string to, string? cc, string subject, string body);
}

public sealed class ArchiveShareLauncher : IArchiveShareLauncher
{
    public void OpenWhatsApp(string attachmentPath, string message, string? phone)
    {
        Clipboard.SetText(attachmentPath);
        Process.Start(new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = false,
            ArgumentList = { "/select,", attachmentPath }
        });
        SafeShareLauncher.OpenWhatsApp(message, phone);
    }

    public void OpenEmailDraft(
        string shareFolderPath,
        string attachmentPath,
        string to,
        string? cc,
        string subject,
        string body)
    {
        var draft = SafeShareLauncher.CreateEmailDraft(shareFolderPath, attachmentPath, to, cc, subject, body);
        Process.Start(new ProcessStartInfo(draft) { UseShellExecute = true });
    }
}
