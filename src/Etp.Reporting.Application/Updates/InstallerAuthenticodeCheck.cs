namespace Etp.Reporting.Application.Updates;

/// <summary>
/// Optional lane U1. SHA-1 thumbprints (40 upper-case hex characters) of the ETP publisher (code signing) certificates a
/// held installer may be signed with. While the list is empty the Authenticode check is "not set up" and the update
/// behaves exactly as before: only the release.json ECDSA check (<see cref="ReleaseSigningKeys"/>) applies.
/// This check is ADDED after the ECDSA check; it never replaces or relaxes it.
/// </summary>
public static class ReleasePublisherCertificates
{
    public static IReadOnlyList<string> TrustedThumbprints { get; } =
    [
        // Paste the thumbprint New-EtpCodeSigningCertificate.ps1 prints here, for example:
        // "0123456789ABCDEF0123456789ABCDEF01234567",
    ];

    public static bool AreConfigured => TrustedThumbprints.Count > 0;

    public static string Normalize(string? thumbprint) =>
        new((thumbprint ?? "").Where(c => !char.IsWhiteSpace(c) && c != '‎' && c != '‏').ToArray()).ToUpperInvariant();
}

public enum AuthenticodeSignatureState
{
    /// <summary>Windows says the file is signed, the signature is intact and the chain is trusted on this PC.</summary>
    Valid,
    NotSigned,
    /// <summary>The file was changed after it was signed.</summary>
    Tampered,
    /// <summary>Signed and intact, but the certificate is not trusted on this PC (not installed by hand yet).</summary>
    NotTrusted,
    /// <summary>Windows could not check (API failure, file unreadable).</summary>
    Unknown,
}

public sealed record AuthenticodeInspection(AuthenticodeSignatureState State, string? SignerThumbprint);

/// <summary>Seam over WinVerifyTrust / Get-AuthenticodeSignature, implemented in Desktop; tests use a fake.</summary>
public interface IAuthenticodeVerifier
{
    AuthenticodeInspection Inspect(string filePath);
}

public enum InstallerAuthenticodeOutcome
{
    /// <summary>No publisher certificate is compiled in: nothing to check, behave as before.</summary>
    NotConfigured,
    Verified,
    NotSigned,
    Tampered,
    NotTrusted,
    WrongPublisher,
    CouldNotCheck,
}

public sealed record InstallerAuthenticodeResult(InstallerAuthenticodeOutcome Outcome)
{
    /// <summary>True when setup may be launched as far as this check goes.</summary>
    public bool MayLaunch => Outcome is InstallerAuthenticodeOutcome.NotConfigured or InstallerAuthenticodeOutcome.Verified;

    /// <summary>Plain words for the Owner; null when the check does not stop the update.</summary>
    public string? RefusalMessage => Outcome switch
    {
        InstallerAuthenticodeOutcome.NotSigned => "The downloaded setup file is not signed by Saagar Traders, so ETP did not start it. Nothing was installed. Install this version by hand from the setup file you trust.",
        InstallerAuthenticodeOutcome.Tampered => "The downloaded setup file was changed after it was signed, so ETP did not start it. Nothing was installed. Download it again, or install by hand.",
        InstallerAuthenticodeOutcome.NotTrusted => "This PC does not trust the Saagar Traders publisher certificate yet, so ETP did not start the setup file. Nothing was installed. Run Install-EtpPublisherCertificate.ps1 as administrator, then try again.",
        InstallerAuthenticodeOutcome.WrongPublisher => "The downloaded setup file is signed by someone other than Saagar Traders, so ETP did not start it. Nothing was installed.",
        InstallerAuthenticodeOutcome.CouldNotCheck => "ETP could not check who signed the setup file, so it did not start it. Nothing was installed. Try again, or install by hand.",
        _ => null,
    };
}

/// <summary>
/// Optional lane U1. Call after the ECDSA manifest check has passed and the installer is held, immediately before launch.
/// </summary>
public static class InstallerAuthenticodeCheck
{
    public static InstallerAuthenticodeResult Evaluate(string installerPath, IAuthenticodeVerifier verifier, IReadOnlyList<string>? trustedThumbprints = null)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        var trusted = trustedThumbprints ?? ReleasePublisherCertificates.TrustedThumbprints;
        var wanted = trusted.Select(ReleasePublisherCertificates.Normalize).Where(t => t.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0) return new(InstallerAuthenticodeOutcome.NotConfigured);

        AuthenticodeInspection inspection;
        try { inspection = verifier.Inspect(installerPath); }
        catch (Exception) { return new(InstallerAuthenticodeOutcome.CouldNotCheck); }
        if (inspection is null) return new(InstallerAuthenticodeOutcome.CouldNotCheck);

        switch (inspection.State)
        {
            case AuthenticodeSignatureState.NotSigned: return new(InstallerAuthenticodeOutcome.NotSigned);
            case AuthenticodeSignatureState.Tampered: return new(InstallerAuthenticodeOutcome.Tampered);
            case AuthenticodeSignatureState.NotTrusted: return new(InstallerAuthenticodeOutcome.NotTrusted);
            case AuthenticodeSignatureState.Valid: break;
            default: return new(InstallerAuthenticodeOutcome.CouldNotCheck);
        }
        var signer = ReleasePublisherCertificates.Normalize(inspection.SignerThumbprint);
        return wanted.Contains(signer)
            ? new(InstallerAuthenticodeOutcome.Verified)
            : new(InstallerAuthenticodeOutcome.WrongPublisher);
    }
}
