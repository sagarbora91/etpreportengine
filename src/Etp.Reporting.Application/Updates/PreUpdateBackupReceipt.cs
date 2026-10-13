using System.Globalization;
using System.Text.Json;

namespace Etp.Reporting.Application.Updates;

public enum PreUpdateBackupProblem
{
    None,
    Unreadable,
    NotVerified,
    WrongPurpose,
    WrongDatabase,
    MissingField,
    TooOld,
    FromTheFuture,
}

public sealed record PreUpdateBackupCheck(PreUpdateBackupProblem Problem, string? BackupPath, DateTimeOffset? VerifiedUtc)
{
    public bool Passed => Problem == PreUpdateBackupProblem.None;
}

/// <summary>
/// 1.12 (B1, design section 4 rule 5). The update takes a fresh verified backup through the existing backup script
/// (<c>backup-etp-database.ps1 -Purpose PreUpdate -ResultPath ...</c>) and reads the receipt it writes back. The update goes
/// ahead only when that receipt says verified, purpose PRE_UPDATE, this database, and was written in the last hour. The
/// script itself has already re-read the receipt (Read-EtpVerifiedReceipt), checked the backup's hash and kept it out of
/// the daily rotation (a purpose other than SCHEDULED is never rotated away).
/// </summary>
public static class PreUpdateBackupReceipt
{
    public const string Purpose = "PRE_UPDATE";
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public static PreUpdateBackupCheck Check(ReadOnlySpan<byte> receiptUtf8, string expectedDatabase, DateTimeOffset nowUtc)
    {
        if (receiptUtf8.Length == 0 || receiptUtf8.Length > 256 * 1024) return Fail(PreUpdateBackupProblem.Unreadable);
        if (receiptUtf8.Length >= 3 && receiptUtf8[0] == 0xEF && receiptUtf8[1] == 0xBB && receiptUtf8[2] == 0xBF) receiptUtf8 = receiptUtf8[3..];
        try
        {
            using var document = JsonDocument.Parse(receiptUtf8.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail(PreUpdateBackupProblem.Unreadable);
            if (!root.TryGetProperty("verified", out var verified) || verified.ValueKind != JsonValueKind.True) return Fail(PreUpdateBackupProblem.NotVerified);
            if (Text(root, "purpose") is not { } purpose || !string.Equals(purpose, Purpose, StringComparison.Ordinal)) return Fail(PreUpdateBackupProblem.WrongPurpose);
            if (Text(root, "database") is not { } database || !string.Equals(database, expectedDatabase, StringComparison.OrdinalIgnoreCase)) return Fail(PreUpdateBackupProblem.WrongDatabase);
            if (Text(root, "backupPath") is not { Length: > 0 } path || Text(root, "sha256") is not { Length: 64 }) return Fail(PreUpdateBackupProblem.MissingField);
            if (Text(root, "verifiedAtUtc") is not { } verifiedText ||
                !DateTimeOffset.TryParse(verifiedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var verifiedAt))
                return Fail(PreUpdateBackupProblem.MissingField);
            if (verifiedAt > nowUtc + ClockSkew) return new(PreUpdateBackupProblem.FromTheFuture, path, verifiedAt);
            if (nowUtc - verifiedAt > MaximumAge) return new(PreUpdateBackupProblem.TooOld, path, verifiedAt);
            return new(PreUpdateBackupProblem.None, path, verifiedAt);
        }
        catch (JsonException)
        {
            return Fail(PreUpdateBackupProblem.Unreadable);
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static PreUpdateBackupCheck Fail(PreUpdateBackupProblem problem) => new(problem, null, null);
}
