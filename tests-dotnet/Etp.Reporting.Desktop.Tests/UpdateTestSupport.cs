using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 (B1). Throwaway signing keys and manifests for the update tests. Every key is made in the test process and dropped
/// with it; no key, passphrase or key file is ever stored in the repository.
/// </summary>
internal static class UpdateTestSupport
{
    public const string Sha = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    public static (ECDsa Key, ReleaseSigningKey Trusted) NewKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        byte[] point = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];
        return (key, new ReleaseSigningKey(ReleaseSigningKey.ComputeKeyId(point), Convert.ToBase64String(point)));
    }

    public static Dictionary<string, object?> Fields(string version, string keyId, string minimum = "1.11.0") => new()
    {
        ["schema"] = 2,
        ["product"] = "ETP Reporting Engine",
        ["version"] = version,
        ["installer"] = $"EtpReportingEngine-Setup-{version}-x64.exe",
        ["installerBytes"] = 1234L,
        ["sha256"] = Sha,
        ["commit"] = "9d9ae21",
        ["publishedUtc"] = "2026-10-12T18:30:00Z",
        ["schemaCompatLevel"] = 65,
        ["hasSchemaChange"] = true,
        ["hasMigration"] = true,
        ["minimumFromVersion"] = minimum,
        ["notes"] = "Synthetic release notes.",
        ["sqlPayloadIncluded"] = false,
        ["keyId"] = keyId,
    };

    public static byte[] Manifest(string version, string keyId, string minimum = "1.11.0", Action<Dictionary<string, object?>>? change = null)
    {
        var fields = Fields(version, keyId, minimum);
        change?.Invoke(fields);
        return JsonSerializer.SerializeToUtf8Bytes(fields);
    }

    public static byte[] Sign(ECDsa key, string keyId, byte[] manifest) =>
        SignatureFile(keyId, key.SignData(manifest, HashAlgorithmName.SHA256));

    public static byte[] SignatureFile(string keyId, byte[] signature, string algorithm = "ECDSA-P256-SHA256") =>
        Encoding.ASCII.GetBytes($$"""{"schema":1,"algorithm":"{{algorithm}}","keyId":"{{keyId}}","signature":"{{Convert.ToBase64String(signature)}}"}""");

    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    public static string NewTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpUpdateTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void DeleteDirectory(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
