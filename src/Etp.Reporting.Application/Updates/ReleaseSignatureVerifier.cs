using System.Security.Cryptography;
using System.Text.Json;

namespace Etp.Reporting.Application.Updates;

public enum ReleaseSignatureOutcome
{
    Verified,
    /// <summary>This build trusts no key yet: "Updates are not set up on this PC".</summary>
    NotConfigured,
    SignatureFileMalformed,
    UnsupportedAlgorithm,
    /// <summary>Signed with a key this build does not trust (or the key id was edited).</summary>
    UnknownKey,
    /// <summary>A compiled-in key is not a valid P-256 point or its id does not match it.</summary>
    TrustedKeyInvalid,
    /// <summary>The signature does not match release.json: changed after signing, or signed by someone else.</summary>
    BadSignature,
}

public sealed record ReleaseSignatureResult(ReleaseSignatureOutcome Outcome, string? KeyId)
{
    public bool IsVerified => Outcome == ReleaseSignatureOutcome.Verified;
}

/// <summary>
/// 1.12 (B1, AUDIT-2 S-03). Checks <c>release.json.sig</c> against the exact bytes of <c>release.json</c> and the keys
/// compiled into this build (<see cref="ReleaseSigningKeys"/>). ECDSA P-256 with SHA-256, the signature as IEEE P1363
/// (r then s, 32 bytes each) - what .NET's <c>ECDsa.SignData</c> gives in both Windows PowerShell 5.1 and .NET 10, so the
/// PowerShell signer (scripts\release\EtpReleaseSigning.psm1) and this verifier share one format.
///
/// <c>release.json.sig</c> is a small JSON object:
/// <code>{ "schema": 1, "algorithm": "ECDSA-P256-SHA256", "keyId": "0123456789abcdef", "signature": "base64" }</code>
/// Nothing in release.json is canonicalised or re-serialised before verifying: a byte changed anywhere fails.
/// </summary>
public static class ReleaseSignatureVerifier
{
    public const string Algorithm = "ECDSA-P256-SHA256";
    public const int SignatureFileSchema = 1;
    public const int MaximumSignatureFileBytes = 4 * 1024;
    private const int SignatureLength = 64;

    public static ReleaseSignatureResult Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureFileBytes) =>
        Verify(manifestBytes, signatureFileBytes, ReleaseSigningKeys.Trusted);

    public static ReleaseSignatureResult Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureFileBytes, IReadOnlyList<ReleaseSigningKey> trusted)
    {
        if (trusted.Count == 0) return new(ReleaseSignatureOutcome.NotConfigured, null);
        if (!TryReadSignatureFile(signatureFileBytes, out var algorithm, out var keyId, out var signature))
            return new(ReleaseSignatureOutcome.SignatureFileMalformed, null);
        if (!string.Equals(algorithm, Algorithm, StringComparison.Ordinal)) return new(ReleaseSignatureOutcome.UnsupportedAlgorithm, keyId);

        var key = trusted.FirstOrDefault(k => string.Equals(k.KeyId, keyId, StringComparison.Ordinal));
        if (key is null) return new(ReleaseSignatureOutcome.UnknownKey, keyId);
        var point = key.TryGetPoint();
        if (point is null) return new(ReleaseSignatureOutcome.TrustedKeyInvalid, keyId);

        try
        {
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33], Y = point[33..65] },
            });
            return ecdsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                ? new(ReleaseSignatureOutcome.Verified, keyId)
                : new(ReleaseSignatureOutcome.BadSignature, keyId);
        }
        catch (CryptographicException)
        {
            // A point that is not on the curve.
            return new(ReleaseSignatureOutcome.TrustedKeyInvalid, keyId);
        }
    }

    private static bool TryReadSignatureFile(ReadOnlySpan<byte> bytes, out string algorithm, out string keyId, out byte[] signature)
    {
        algorithm = ""; keyId = ""; signature = [];
        if (bytes.Length == 0 || bytes.Length > MaximumSignatureFileBytes) return false;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!seen.Add(property.Name)) return false;
            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var schemaValue) || schemaValue != SignatureFileSchema) return false;
            if (!root.TryGetProperty("algorithm", out var a) || a.ValueKind != JsonValueKind.String) return false;
            if (!root.TryGetProperty("keyId", out var k) || k.ValueKind != JsonValueKind.String) return false;
            if (!root.TryGetProperty("signature", out var s) || s.ValueKind != JsonValueKind.String) return false;
            algorithm = a.GetString() ?? "";
            keyId = k.GetString() ?? "";
            if (!ReleaseSigningKey.IsKeyIdText(keyId)) return false;
            signature = Convert.FromBase64String(s.GetString() ?? "");
            return signature.Length == SignatureLength;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return false;
        }
    }
}
