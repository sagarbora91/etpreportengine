using System.Security.Cryptography;

namespace Etp.Reporting.Application.Updates;

/// <summary>
/// 1.12 (B1, decisions 39 and 44 #1). The public halves of the release signing keys this build trusts.
///
/// The key pair is made once, offline, by Sagar with <c>scripts\release\New-EtpReleaseSigningKey.ps1</c>. The private key
/// (passphrase-encrypted) lives only on two USB sticks; it is never in this repository, a test, a build or a log. The
/// script prints the line to paste below. More than one entry is allowed so a key can be rotated: a release signed with the
/// new key, carrying both public keys, is installed once by hand; the old entry is removed a release later.
///
/// While this list is empty ETP says "Updates are not set up on this PC" and offers nothing (only the key is missing).
/// </summary>
public static class ReleaseSigningKeys
{
    public static IReadOnlyList<ReleaseSigningKey> Trusted { get; } =
    [
        // Paste the line New-EtpReleaseSigningKey.ps1 prints here, for example:
        // new("0123456789abcdef", "BASE64-OF-THE-65-BYTE-PUBLIC-POINT"),
    ];

    public static bool AreConfigured => Trusted.Count > 0;
}

/// <summary>
/// One trusted ECDSA P-256 public key. <paramref name="PublicKey"/> is the base64 of the uncompressed point
/// (0x04, then X and Y, 32 bytes each = 65 bytes). <paramref name="KeyId"/> is the first 8 bytes of SHA-256 of those 65
/// bytes, as 16 lower-case hex characters; it is recomputed before use, so a mistyped paste is refused, not trusted.
/// </summary>
public sealed record ReleaseSigningKey(string KeyId, string PublicKey)
{
    public const int PointLength = 65;

    public static bool IsKeyIdText(string? text) =>
        text is { Length: 16 } && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>The key id of a 65-byte uncompressed point (the same rule as Get-EtpReleaseKeyId in EtpReleaseSigning.psm1).</summary>
    public static string ComputeKeyId(ReadOnlySpan<byte> uncompressedPoint) =>
        Convert.ToHexStringLower(SHA256.HashData(uncompressedPoint)[..8]);

    /// <summary>Full SHA-256 fingerprint, upper-case hex in groups of four, as the key ceremony prints it on paper.</summary>
    public static string ComputeFingerprint(ReadOnlySpan<byte> uncompressedPoint)
    {
        var hex = Convert.ToHexString(SHA256.HashData(uncompressedPoint));
        return string.Join(' ', Enumerable.Range(0, hex.Length / 4).Select(i => hex.Substring(i * 4, 4)));
    }

    /// <summary>The point, when the text is well formed and the id matches it; otherwise null.</summary>
    internal byte[]? TryGetPoint()
    {
        byte[] point;
        try { point = Convert.FromBase64String(PublicKey); }
        catch (FormatException) { return null; }
        if (point.Length != PointLength || point[0] != 0x04) return null;
        return string.Equals(ComputeKeyId(point), KeyId, StringComparison.Ordinal) ? point : null;
    }
}
