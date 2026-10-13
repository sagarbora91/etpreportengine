using System.Globalization;
using System.Text.Json;

namespace Etp.Reporting.Application.Updates;

/// <summary>
/// 1.12 (B1, plan section B). The signed <c>release.json</c> that <c>Publish-Release.ps1 -SigningKeyPath</c> writes into
/// <c>OneDrive\ETP-Updates\&lt;version&gt;\</c>. Schema 2 = the 1.11.2 L3 fields plus <c>schema</c>, <c>keyId</c>,
/// <c>hasSchemaChange</c>, <c>minimumFromVersion</c> and <c>notes</c>; the in-file <c>signature: null</c> of schema 1 is gone,
/// because the signature is detached (<c>release.json.sig</c>, over the exact bytes of this file).
/// </summary>
public sealed record ReleaseManifest(
    int Schema,
    string Product,
    ReleaseVersion Version,
    string InstallerFileName,
    long InstallerBytes,
    string InstallerSha256,
    string Commit,
    DateTimeOffset PublishedUtc,
    int SchemaCompatLevel,
    bool HasSchemaChange,
    ReleaseVersion MinimumFromVersion,
    string Notes,
    bool SqlPayloadIncluded,
    string KeyId)
{
    public const int CurrentSchema = 2;
    public const string ProductName = "ETP Reporting Engine";
    /// <summary>release.json is a few hundred bytes; anything larger is not ours.</summary>
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumNotesLength = 4000;
    /// <summary>The largest installer ETP will accept (the SQL media makes it about 900 MB).</summary>
    public const long MaximumInstallerBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>The only installer file name a manifest may name: no folder, no other product, the manifest's own version.</summary>
    public static string ExpectedInstallerFileName(ReleaseVersion version) =>
        string.Create(CultureInfo.InvariantCulture, $"EtpReportingEngine-Setup-{version}-x64.exe");
}

public enum ReleaseManifestProblem
{
    None,
    TooLarge,
    NotJson,
    DuplicateField,
    MissingField,
    WrongType,
    UnsupportedSchema,
    WrongProduct,
    BadVersion,
    BadInstallerName,
    BadInstallerSize,
    BadHash,
    BadCommit,
    BadDate,
    BadMinimumVersion,
    NotesTooLong,
    BadKeyId,
}

public sealed record ReleaseManifestParseResult(ReleaseManifest? Manifest, ReleaseManifestProblem Problem, string? Field)
{
    public bool Succeeded => Manifest is not null;
}

/// <summary>
/// Strict reader for <see cref="ReleaseManifest"/>. Call it only on bytes whose signature has already verified: the parser
/// is careful, but the signature is what makes the content trustworthy. Every refusal names the field, never a value.
/// </summary>
public static class ReleaseManifestParser
{
    private static readonly string[] Required =
    [
        "schema", "product", "version", "installer", "installerBytes", "sha256", "commit", "publishedUtc",
        "schemaCompatLevel", "hasSchemaChange", "minimumFromVersion", "notes", "sqlPayloadIncluded", "keyId",
    ];

    public static ReleaseManifestParseResult Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > ReleaseManifest.MaximumBytes) return Fail(ReleaseManifestProblem.TooLarge);
        // PowerShell 5.1's Set-Content -Encoding utf8 writes a byte order mark; the signature covers it like any other byte.
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF) utf8 = utf8[3..];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
        }
        catch (JsonException)
        {
            return Fail(ReleaseManifestProblem.NotJson);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail(ReleaseManifestProblem.NotJson);
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!fields.TryAdd(property.Name, property.Value)) return Fail(ReleaseManifestProblem.DuplicateField, property.Name);
            foreach (var name in Required)
                if (!fields.ContainsKey(name)) return Fail(ReleaseManifestProblem.MissingField, name);

            if (!TryInt(fields["schema"], out var schema)) return Fail(ReleaseManifestProblem.WrongType, "schema");
            if (schema != ReleaseManifest.CurrentSchema) return Fail(ReleaseManifestProblem.UnsupportedSchema, "schema");

            if (!TryString(fields["product"], out var product)) return Fail(ReleaseManifestProblem.WrongType, "product");
            if (!string.Equals(product, ReleaseManifest.ProductName, StringComparison.Ordinal)) return Fail(ReleaseManifestProblem.WrongProduct, "product");

            if (!TryString(fields["version"], out var versionText) || !ReleaseVersion.TryParse(versionText, out var version))
                return Fail(ReleaseManifestProblem.BadVersion, "version");

            if (!TryString(fields["installer"], out var installer)) return Fail(ReleaseManifestProblem.WrongType, "installer");
            if (!string.Equals(installer, ReleaseManifest.ExpectedInstallerFileName(version), StringComparison.Ordinal))
                return Fail(ReleaseManifestProblem.BadInstallerName, "installer");

            if (!TryLong(fields["installerBytes"], out var bytes)) return Fail(ReleaseManifestProblem.WrongType, "installerBytes");
            if (bytes <= 0 || bytes > ReleaseManifest.MaximumInstallerBytes) return Fail(ReleaseManifestProblem.BadInstallerSize, "installerBytes");

            if (!TryString(fields["sha256"], out var sha256) || !IsHex(sha256, 64)) return Fail(ReleaseManifestProblem.BadHash, "sha256");

            if (!TryString(fields["commit"], out var commit) || commit.Length is < 7 or > 40 || !IsHex(commit, commit.Length))
                return Fail(ReleaseManifestProblem.BadCommit, "commit");

            if (!TryString(fields["publishedUtc"], out var publishedText) ||
                !DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var published))
                return Fail(ReleaseManifestProblem.BadDate, "publishedUtc");

            if (!TryInt(fields["schemaCompatLevel"], out var compat) || compat < 0) return Fail(ReleaseManifestProblem.WrongType, "schemaCompatLevel");
            if (!TryBool(fields["hasSchemaChange"], out var hasSchemaChange)) return Fail(ReleaseManifestProblem.WrongType, "hasSchemaChange");

            if (!TryString(fields["minimumFromVersion"], out var minimumText) || !ReleaseVersion.TryParse(minimumText, out var minimum) || minimum >= version)
                return Fail(ReleaseManifestProblem.BadMinimumVersion, "minimumFromVersion");

            if (!TryString(fields["notes"], out var notes)) return Fail(ReleaseManifestProblem.WrongType, "notes");
            if (notes.Length > ReleaseManifest.MaximumNotesLength) return Fail(ReleaseManifestProblem.NotesTooLong, "notes");

            if (!TryBool(fields["sqlPayloadIncluded"], out var sqlPayload)) return Fail(ReleaseManifestProblem.WrongType, "sqlPayloadIncluded");

            if (!TryString(fields["keyId"], out var keyId) || !ReleaseSigningKey.IsKeyIdText(keyId)) return Fail(ReleaseManifestProblem.BadKeyId, "keyId");

            return new(new ReleaseManifest(schema, product, version, installer, bytes, sha256.ToUpperInvariant(), commit.ToLowerInvariant(),
                published, compat, hasSchemaChange, minimum, notes, sqlPayload, keyId), ReleaseManifestProblem.None, null);
        }
    }

    private static ReleaseManifestParseResult Fail(ReleaseManifestProblem problem, string? field = null) => new(null, problem, field);

    private static bool TryString(JsonElement element, out string value)
    {
        value = element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
        return element.ValueKind == JsonValueKind.String;
    }

    private static bool TryInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static bool TryLong(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    private static bool TryBool(JsonElement element, out bool value)
    {
        value = element.ValueKind == JsonValueKind.True;
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    internal static bool IsHex(string text, int length)
    {
        if (text.Length != length) return false;
        foreach (var c in text)
            if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }
}
