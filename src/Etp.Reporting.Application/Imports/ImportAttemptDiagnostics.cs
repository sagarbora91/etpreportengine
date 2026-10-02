using System.Text;

namespace Etp.Reporting.Application.Imports;

/// <summary>Where in the import an attempt failed (<c>import_attempts.failure_stage</c>, spec 5.1 A).</summary>
public enum FailureStage { Read, Match, Source, Scope, Plan, Apply, Commit, Evidence, Record }

/// <summary>What became of the import transaction (<c>import_attempts.commit_state</c>, IF-014).</summary>
public enum CommitState { RolledBack, Committed, Unknown }

/// <summary>
/// Whether the source bytes are held inside the database (<c>import_attempts.evidence_state</c>, IF-023). Every
/// attempt records one: <see cref="NotAttempted"/> when nothing committed (a read, match or scope failure, a
/// rollback, a file a cancel never reached), and <see cref="Unknown"/> when the import committed but what it
/// kept could not be read back.
/// </summary>
public enum EvidenceState { Retained, AlreadyHeld, NotRetained, NotAttempted, Unknown }

/// <summary>
/// Why one attempt failed (spec 11.1). Messages are written by the code and never contain a cell value;
/// SQL errors 50000-59999 (our own THROWs) keep their text, any other keeps only number, procedure and line.
/// </summary>
public sealed record ImportFailure(
    string Code,
    FailureStage Stage,
    string SafeMessage,
    string? ExceptionType = null,
    int? SqlNumber = null)
{
    /// <summary>Counted issues behind the failure, e.g. conflict samples or stager skips.</summary>
    public IReadOnlyList<ImportIssue> Issues { get; init; } = [];
}

/// <summary>
/// The text the database stores for the import engine's enum values: the member name in upper snake case
/// (<c>RolledBack</c> is <c>ROLLED_BACK</c>, <c>ConsolidatedLegacy</c> is <c>CONSOLIDATED_LEGACY</c>).
/// </summary>
public static class ImportDatabaseCodes
{
    public static string ToDatabaseCode<TEnum>(this TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value), "The value has no database code.");
        var text = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]))) text.Append('_');
            text.Append(char.ToUpperInvariant(name[i]));
        }
        return text.ToString();
    }

    public static bool TryParseDatabaseCode<TEnum>(string? code, out TEnum value) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
            if (string.Equals(candidate.ToDatabaseCode(), code, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        value = default;
        return false;
    }

    public static TEnum ParseDatabaseCode<TEnum>(string code) where TEnum : struct, Enum =>
        TryParseDatabaseCode<TEnum>(code, out var value) ? value
            : throw new FormatException($"'{code}' is not a {typeof(TEnum).Name} database code.");
}
