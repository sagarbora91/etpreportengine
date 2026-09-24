using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>Per-Windows-user credentials, DPAPI protected. Never stored in SQL, audit or logs.</summary>
public static class WindowsSmtpCredentials
{
    public static SmtpCredential? Load(SmtpConnection settings)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var path = CredentialPath(settings);
        if (!File.Exists(path)) return null;
        RefuseLinks(path);
        var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<SmtpCredential>(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static void Save(SmtpConnection settings, string userName, string password)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows is required for protected SMTP credentials.");
        var path = CredentialPath(settings);
        RefuseLinks(path);
        if (string.IsNullOrWhiteSpace(userName) && string.IsNullOrEmpty(password)) { if (File.Exists(path)) File.Delete(path); return; }
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password)) throw new ArgumentException("Enter both the SMTP user name and password, or leave both blank to clear them.");
        if (!settings.UseTls) throw new ArgumentException("Enable SMTP TLS before saving credentials.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new SmtpCredential(userName.Trim(), password));
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static string CredentialPath(SmtpConnection settings)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{settings.Host.ToLowerInvariant()}|{settings.Port}|{settings.FromAddress.ToLowerInvariant()}")));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "SmtpCredentials", key + ".bin");
    }

    private static void RefuseLinks(string path)
    {
        for (var candidate = path; !string.IsNullOrEmpty(candidate); candidate = Path.GetDirectoryName(candidate))
            if ((File.Exists(candidate) || Directory.Exists(candidate)) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("SMTP credential storage cannot use linked folders or files.");
    }
}
