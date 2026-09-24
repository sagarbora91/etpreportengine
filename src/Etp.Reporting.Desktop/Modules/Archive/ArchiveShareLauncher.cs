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
        var uri = BuildWhatsAppUri(message, phone);
        Clipboard.SetText(attachmentPath);
        Process.Start(new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = false,
            ArgumentList = { "/select,", attachmentPath }
        });
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    public static string BuildWhatsAppUri(string message, string? phone)
    {
        var number = phone?.Trim() ?? string.Empty;
        var digits = new string(number.Where(char.IsAsciiDigit).ToArray());
        if (number.Length > 0 && (digits.Length < 7 || digits.Length > 15 || number.Any(character => !char.IsAsciiDigit(character) && !"+- ()".Contains(character))))
            throw new ArgumentException("Enter the WhatsApp phone with country code (7 to 15 digits).");
        return "whatsapp://send?text=" + Uri.EscapeDataString(message) +
            (digits.Length == 0 ? string.Empty : "&phone=" + digits);
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
