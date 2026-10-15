using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Updates;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.13 (U1, wired at integration): Windows' Authenticode check against files on this PC; no certificate is created or trusted.</summary>
public sealed class WindowsAuthenticodeVerifierTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("etp-authenticode-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // The .NET runtime's own assemblies carry an embedded Microsoft Authenticode signature.
    private static string SignedRuntimeAssembly => typeof(object).Assembly.Location;

    [Fact]
    public void An_unsigned_file_is_NotSigned()
    {
        var path = Path.Combine(_folder, "unsigned.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0, 1, 2, 3, 4]);
        var inspection = new WindowsAuthenticodeVerifier().Inspect(path);
        Assert.Equal(AuthenticodeSignatureState.NotSigned, inspection.State);
        Assert.Null(inspection.SignerThumbprint);
    }

    [Fact]
    public void A_signed_runtime_assembly_is_Valid_with_its_signer_thumbprint()
    {
        var copy = Path.Combine(_folder, "signed.dll");
        File.Copy(SignedRuntimeAssembly, copy);
        var inspection = new WindowsAuthenticodeVerifier().Inspect(copy);
        Assert.Equal(AuthenticodeSignatureState.Valid, inspection.State);
        Assert.Matches("^[0-9A-F]{40}$", inspection.SignerThumbprint);
    }

    [Fact]
    public void A_signed_file_changed_afterwards_is_Tampered()
    {
        var copy = Path.Combine(_folder, "tampered.dll");
        File.Copy(SignedRuntimeAssembly, copy);
        var bytes = File.ReadAllBytes(copy);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(copy, bytes);
        Assert.Equal(AuthenticodeSignatureState.Tampered, new WindowsAuthenticodeVerifier().Inspect(copy).State);
    }

    [Fact]
    public void A_runtime_assembly_signed_by_Microsoft_is_WrongPublisher_for_the_ETP_list()
    {
        var copy = Path.Combine(_folder, "other.dll");
        File.Copy(SignedRuntimeAssembly, copy);
        var result = InstallerAuthenticodeCheck.Evaluate(copy, new WindowsAuthenticodeVerifier(), ["0123456789ABCDEF0123456789ABCDEF01234567"]);
        Assert.Equal(InstallerAuthenticodeOutcome.WrongPublisher, result.Outcome);
        Assert.False(result.MayLaunch);
    }

    [Fact]
    public void The_publisher_list_ships_empty_until_Sagar_trusts_the_certificate()
    {
        // Decision D10 / U1 note: pinning before the certificate is trusted on Workpc would refuse every update (NotTrusted).
        Assert.Empty(ReleasePublisherCertificates.TrustedThumbprints);
        Assert.Equal(InstallerAuthenticodeOutcome.NotConfigured, InstallerAuthenticodeCheck.Evaluate("any.exe", new WindowsAuthenticodeVerifier()).Outcome);
    }
}
