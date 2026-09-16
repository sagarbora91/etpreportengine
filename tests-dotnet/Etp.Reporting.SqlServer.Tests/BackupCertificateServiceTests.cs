using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class BackupCertificateServiceTests : IDisposable
{
    private const string Password = "Two distinct recovery copies!";
    private readonly string root = Path.Combine(Path.GetTempPath(), "EtpCertificateBoundary", Guid.NewGuid().ToString("N"));
    private readonly string first;
    private readonly string second;

    public BackupCertificateServiceTests()
    {
        first = Directory.CreateDirectory(Path.Combine(root, "First recovery copy")).FullName;
        second = Directory.CreateDirectory(Path.Combine(root, "Second recovery copy")).FullName;
    }

    [Fact]
    public async Task Reexport_of_same_certificate_keeps_original_copies_and_publishes_a_new_immutable_receipt()
    {
        var pointerPath = Path.Combine(root, "certificate-custody.json");
        var receipt = Custody(new string('A', 40), "first-export");
        var firstPath = await BackupCertificateService.WriteCustodyReceiptAsync(receipt, pointerPath);
        var firstContent = await File.ReadAllTextAsync(firstPath);
        var secondPath = await BackupCertificateService.WriteCustodyReceiptAsync(Custody(receipt.CertificateThumbprint, "second-export"), pointerPath);
        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(firstContent, await File.ReadAllTextAsync(firstPath));
        Assert.Contains("first-export", firstContent);
        Assert.Contains("second-export", await File.ReadAllTextAsync(secondPath));
        using var pointer = JsonDocument.Parse(await File.ReadAllTextAsync(pointerPath));
        Assert.Equal(2, pointer.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(secondPath, pointer.RootElement.GetProperty("immutableReceiptPath").GetString());
        Assert.False(pointer.RootElement.TryGetProperty("copies", out _));
    }

    [Fact]
    public async Task Certificate_rotation_never_overwrites_an_older_custody_receipt()
    {
        var pointerPath = Path.Combine(root, "certificate-custody.json");
        var original = Custody(new string('A', 40), "original-key");
        var originalPath = await BackupCertificateService.WriteCustodyReceiptAsync(original, pointerPath);
        var originalContent = await File.ReadAllTextAsync(originalPath);
        var rotatedPath = await BackupCertificateService.WriteCustodyReceiptAsync(Custody(new string('B', 40), "rotated-key"), pointerPath);
        var pointerContent = await File.ReadAllTextAsync(pointerPath);
        Assert.NotEqual(originalPath, rotatedPath);
        using var pointer = JsonDocument.Parse(pointerContent);
        Assert.Equal(new string('B', 40), pointer.RootElement.GetProperty("certificateThumbprint").GetString());
        Assert.Equal(rotatedPath, pointer.RootElement.GetProperty("immutableReceiptPath").GetString());
        await Assert.ThrowsAsync<IOException>(() => BackupCertificateService.WriteCustodyReceiptAsync(original with { Copies = Custody(new string('A', 40), "replacement-key").Copies }, pointerPath));
        Assert.Equal(originalContent, await File.ReadAllTextAsync(originalPath));
        Assert.Equal(pointerContent, await File.ReadAllTextAsync(pointerPath));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
    }

    [Fact]
    public void Exported_certificate_must_match_the_SQL_certificate_fingerprint()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Disposable custody validation", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        var path = Path.Combine(first, "disposable.cer");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Cert));
        BackupCertificateService.ValidateExportedCertificate(path, certificate.Thumbprint.ToLowerInvariant());
        Assert.Throws<InvalidOperationException>(() => BackupCertificateService.ValidateExportedCertificate(path, new string('0', 40)));
        File.WriteAllText(path, "changed certificate data");
        Assert.Throws<CryptographicException>(() => BackupCertificateService.ValidateExportedCertificate(path, certificate.Thumbprint));
    }

    [Fact]
    public async Task Invalid_custody_identifier_cannot_create_or_replace_a_receipt()
    {
        var pointerPath = Path.Combine(root, "certificate-custody.json");
        await Assert.ThrowsAsync<ArgumentException>(() => BackupCertificateService.WriteCustodyReceiptAsync(Custody(new string('A', 40), "test") with { ExportId = "../outside" }, pointerPath));
        await Assert.ThrowsAsync<ArgumentException>(() => BackupCertificateService.WriteCustodyReceiptAsync(Custody(new string('G', 40), "test"), pointerPath));
        Assert.False(File.Exists(pointerPath));
        Assert.Empty(Directory.EnumerateFiles(root));
    }

    private CertificateCustodyReceipt Custody(string thumbprint, string export) => new("EtpBackupCert", thumbprint, DateTime.UtcNow,
    [
        new(Path.Combine(first, export + ".cer"), new string('A', 64), Path.Combine(first, export + ".pvk"), new string('B', 64)),
        new(Path.Combine(second, export + ".cer"), new string('A', 64), Path.Combine(second, export + ".pvk"), new string('B', 64))
    ]);

    [Theory]
    [InlineData("")]
    [InlineData("short password")]
    [InlineData("                ")]
    public void Weak_password_is_rejected_before_any_export(string password) =>
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(password, first, second));

    [Fact]
    public void Overlong_password_is_rejected_before_any_export() =>
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(new string('a', 129), first, second));

    [Fact]
    public void Two_distinct_existing_folders_and_boundary_length_passwords_are_accepted()
    {
        BackupCertificateService.ValidateExport(Password, first, second);
        BackupCertificateService.ValidateExport("Sixteen letters!", first, second);
        BackupCertificateService.ValidateExport(new string('a', 127) + "!", first, second);
    }

    [Theory]
    [InlineData("relative-recovery-folder")]
    [InlineData("")]
    public void Relative_or_empty_folder_is_rejected(string directory) =>
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, first, directory));

    [Fact]
    public void Missing_folder_is_rejected_without_creating_it()
    {
        var missing = Path.Combine(root, "Not connected");
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, first, missing));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Identical_case_dot_and_trailing_separator_aliases_are_rejected()
    {
        foreach (var alias in new[] { first, first + Path.DirectorySeparatorChar, Path.Combine(first, "."), Path.Combine(second, "..", Path.GetFileName(first)) })
            Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, first, alias));
        if (OperatingSystem.IsWindows())
            Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, first, first.ToUpperInvariant()));
    }

    [Fact]
    public void Nested_folder_is_rejected_in_either_order()
    {
        var nested = Directory.CreateDirectory(Path.Combine(first, "Nested copy")).FullName;
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, first, nested));
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, nested, first));
    }

    [Fact]
    public void Drive_root_cannot_be_the_other_recovery_location()
    {
        var drive = Path.GetPathRoot(first)!;
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, drive, first));
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, first, drive));
    }

    [Fact]
    public void Windows_device_namespace_alias_is_rejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, first, @"\\?\" + first));
    }

    [Fact]
    public void Windows_short_name_alias_is_rejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var shortName = new System.Text.StringBuilder(32768);
        var length = GetShortPathName(first, shortName, shortName.Capacity);
        Assert.True(length > 0, "The existing temporary folder could not be resolved.");
        Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, first, shortName.ToString()));
    }

    [Fact]
    public void Junction_and_a_folder_beneath_it_are_rejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var junction = Path.Combine(root, "Recovery junction");
        Directory.CreateDirectory(Path.Combine(first, "Child"));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path '{junction.Replace("'", "''")}' -Target '{first.Replace("'", "''")}' | Out-Null");
        using (var process = Process.Start(start)!)
        {
            Assert.True(process.WaitForExit(15000), "Temporary junction creation timed out.");
            Assert.Equal(0, process.ExitCode);
        }
        try
        {
            Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, junction, second));
            Assert.Throws<ArgumentException>(() => BackupCertificateService.ValidateExport(Password, second, Path.Combine(junction, "Child")));
        }
        finally
        {
            // Remove only the temporary link, before the owned test tree is removed.
            Directory.Delete(junction);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string path, System.Text.StringBuilder shortPath, int length);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
