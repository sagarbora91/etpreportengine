using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record CertificateRecoveryCopy(string CertificatePath, string CertificateSha256, string PrivateKeyPath, string PrivateKeySha256);
public sealed record CertificateCustodyReceipt(string CertificateName, string CertificateThumbprint, DateTime ExportedAtUtc, IReadOnlyList<CertificateRecoveryCopy> Copies)
{
    public int SchemaVersion { get; init; } = 2;
    public string ExportId { get; init; } = Guid.NewGuid().ToString("N");
}
public sealed record CertificateCustodyPointer(int SchemaVersion, string CertificateThumbprint, string ImmutableReceiptPath);

/// <summary>Owner-triggered export. SQL permissions and directory access are provisioned during deployment.</summary>
public sealed class BackupCertificateService(string connectionString)
{
    public static void ValidateExport(string password, string firstDirectory, string secondDirectory)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 16 || password.Length > 128) throw new ArgumentException("Use a recovery password between sixteen and one hundred twenty-eight characters.");
        foreach (var directory in new[] { firstDirectory, secondDirectory })
        {
            if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose two absolute recovery folder locations.");
            if (OperatingSystem.IsWindows() && (directory.StartsWith(@"\\?\", StringComparison.Ordinal) || directory.StartsWith(@"\\.\", StringComparison.Ordinal) ||
                directory.Split(['\\', '/']).Any(part => part.EndsWith('.') || part.EndsWith(' '))))
                throw new ArgumentException("Choose standard recovery folder locations without device aliases.");
            RejectLinks(directory);
            if (!Directory.Exists(directory)) throw new ArgumentException("Connect and select both recovery folders first.");
        }
        var first = CanonicalDirectory(firstDirectory);
        var second = CanonicalDirectory(secondDirectory);
        if (first.Equals(second, StringComparison.OrdinalIgnoreCase) || IsWithin(first, second) || IsWithin(second, first))
            throw new ArgumentException("Choose two separate recovery locations.");
    }

    private static bool IsWithin(string path, string parent) =>
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string CanonicalDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows())
        {
            // A DOS short name can otherwise make the same directory look like a second copy.
            var longPath = new StringBuilder(32768);
            var length = GetLongPathName(fullPath, longPath, longPath.Capacity);
            if (length == 0 || length >= longPath.Capacity) throw new ArgumentException("The recovery folder could not be resolved.");
            fullPath = longPath.ToString();
        }
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, int length);

    public async Task ExportAsync(string password, string firstDirectory, string secondDirectory, string receiptPath, CancellationToken token = default)
    {
        ValidateExport(password, firstDirectory, secondDirectory);
        RejectLinks(receiptPath);
        var validated = LocalSqlConnectionPolicy.Validate(connectionString);
        await using (var application = new SqlConnection(validated))
        {
            await application.OpenAsync(token);
            await using var role = new SqlCommand("SELECT CASE WHEN IS_ROLEMEMBER('etp_owner')=1 OR IS_SRVROLEMEMBER('sysadmin')=1 THEN 1 ELSE 0 END", application);
            if (Convert.ToInt32(await role.ExecuteScalarAsync(token)) != 1) throw new UnauthorizedAccessException("Owner permission is required to export recovery keys.");
        }
        var master = new SqlConnectionStringBuilder(validated) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync(token);
        const string create = """
            IF CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Express%' OR CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Web%'
              THROW 51320,'Native encrypted backups require a supported SQL Server edition.',1;
            IF NOT EXISTS(SELECT 1 FROM sys.symmetric_keys WHERE name='##MS_DatabaseMasterKey##')
            BEGIN
              DECLARE @create nvarchar(max)=N'CREATE MASTER KEY ENCRYPTION BY PASSWORD=N'''+REPLACE(@password,'''','''''')+N'''';
              EXEC sys.sp_executesql @create;
            END;
            IF NOT EXISTS(SELECT 1 FROM sys.certificates WHERE name='EtpBackupCert')
              CREATE CERTIFICATE EtpBackupCert WITH SUBJECT='ETP encrypted database backups';
            SELECT CONVERT(varchar(128),thumbprint,2) FROM sys.certificates WHERE name='EtpBackupCert';
            """;
        await using var createCommand = new SqlCommand(create, connection);
        createCommand.Parameters.AddWithValue("@password", password);
        var thumbprint = (string)(await createCommand.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("Backup certificate creation was not confirmed."));
        var copies = new List<CertificateRecoveryCopy>();
        var exportName = "EtpBackupCert-" + Guid.NewGuid().ToString("N");
        foreach (var directory in new[] { firstDirectory, secondDirectory })
        {
            var certificatePath = Path.Combine(Path.GetFullPath(directory), exportName + ".cer");
            var keyPath = Path.Combine(Path.GetFullPath(directory), exportName + ".pvk");
            // File names are unique; never overwrite an earlier recovery export.
            const string export = """
                DECLARE @export nvarchar(max)=N'BACKUP CERTIFICATE EtpBackupCert TO FILE=N'''+REPLACE(@certificate,'''','''''')+
                 N''' WITH PRIVATE KEY(FILE=N'''+REPLACE(@key,'''','''''')+N''',ENCRYPTION BY PASSWORD=N'''+REPLACE(@password,'''','''''')+N''')';
                EXEC sys.sp_executesql @export;
                """;
            await using var command = new SqlCommand(export, connection);
            command.Parameters.AddWithValue("@certificate", certificatePath);
            command.Parameters.AddWithValue("@key", keyPath);
            command.Parameters.AddWithValue("@password", password);
            await command.ExecuteNonQueryAsync(token);
            ValidateExportedCertificate(certificatePath, thumbprint);
            copies.Add(new(certificatePath, await HashAsync(certificatePath, token), keyPath, await HashAsync(keyPath, token)));
        }
        var receipt = new CertificateCustodyReceipt("EtpBackupCert", thumbprint, DateTime.UtcNow, copies);
        await WriteCustodyReceiptAsync(receipt, receiptPath, token);
    }

    internal static void ValidateExportedCertificate(string certificatePath, string expectedThumbprint)
    {
        RejectLinks(certificatePath);
        using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
        // SQL sys.certificates.thumbprint identifies the DER certificate by its SHA-1 fingerprint.
        if (!certificate.Thumbprint.Equals(expectedThumbprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The exported certificate does not match the backup encryption key. Export both recovery copies again.");
    }

    internal static async Task<string> WriteCustodyReceiptAsync(CertificateCustodyReceipt receipt, string latestReceiptPath, CancellationToken token = default)
    {
        if (receipt.SchemaVersion != 2 || receipt.CertificateName != "EtpBackupCert" || receipt.CertificateThumbprint.Length is < 40 or > 128 ||
            receipt.CertificateThumbprint.Length % 2 != 0 || !receipt.CertificateThumbprint.All(char.IsAsciiHexDigit) ||
            receipt.ExportId.Length != 32 || !receipt.ExportId.All(char.IsAsciiHexDigit) || receipt.Copies.Count != 2)
            throw new ArgumentException("Certificate custody metadata is invalid.");
        if (!Path.IsPathFullyQualified(latestReceiptPath)) throw new ArgumentException("Choose an absolute certificate receipt location.");
        RejectLinks(latestReceiptPath);
        receipt = receipt with { CertificateThumbprint = receipt.CertificateThumbprint.ToUpperInvariant(), ExportId = receipt.ExportId.ToLowerInvariant(), Copies = receipt.Copies.ToArray() };
        var directory = Path.GetDirectoryName(Path.GetFullPath(latestReceiptPath))!;
        var immutablePath = Path.Combine(directory, $"certificate-custody-{receipt.CertificateThumbprint}-{receipt.ExportId}.json");
        RejectLinks(immutablePath);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var serialized = JsonSerializer.Serialize(receipt, options);
        await WriteReceiptAtomicallyAsync(immutablePath, serialized, overwrite: false, token);
        if (!string.Equals(await File.ReadAllTextAsync(immutablePath, token), serialized, StringComparison.Ordinal))
            throw new IOException("Certificate custody receipt verification failed.");
        var pointer = JsonSerializer.Serialize(new CertificateCustodyPointer(2, receipt.CertificateThumbprint, immutablePath), options);
        await WriteReceiptAtomicallyAsync(latestReceiptPath, pointer, overwrite: true, token);
        if (!string.Equals(await File.ReadAllTextAsync(latestReceiptPath, token), pointer, StringComparison.Ordinal))
            throw new IOException("The latest certificate custody pointer could not be verified.");
        return immutablePath;
    }

    private static async Task WriteReceiptAtomicallyAsync(string path, string contents, bool overwrite, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, contents, token);
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        RejectLinks(path);
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token));
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Choose recovery folders without links or junctions.");
    }
}
