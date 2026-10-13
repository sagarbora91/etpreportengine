using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

// 1.12 lane A3 (112-WAVE-PLAN A3): views, procedures and functions as repeatable objects.
//
// Tables, columns, constraints, indexes, triggers, table types, roles, column DENYs and seed data stay in the append-only,
// checksummed migrations. Views, procedures and functions live one file each in database/objects, named
// <layer>-<schema>.<name>.sql (layer 10-19 functions, 20-29 views on tables, 30-39 views or functions on other views or
// functions, 40-49 procedures; order is the file name, so layer then name). A file is the object's CREATE OR ALTER batch,
// then a line "-- >>> GRANTS", then that object's idempotent GRANT/DENY/REVOKE lines. A dropped object leaves a comment-only
// stub database/objects/retired/<schema>.<name>.sql. After the migrations, SchemaObjectRunner skips an object whose journal
// row and live definition still match, adopts one whose live definition already equals its file (the first 1.12 upgrade
// changes no object), and otherwise applies it. Every change of one run is one transaction: a failure rolls every object
// back and setup stops with "Database objects are not up to date; run setup again."

public enum SchemaObjectKind
{
    Function,
    View,
    Procedure
}

/// <summary>One object file, parsed.</summary>
public sealed record SchemaObjectFile(
    string FileName,
    int Layer,
    string Schema,
    string Name,
    SchemaObjectKind Kind,
    string FileSha256,
    IReadOnlyList<string> BodyBatches,
    int DefinitionBatchIndex,
    IReadOnlyList<string> GrantStatements)
{
    public string ObjectName => Schema + "." + Name;

    /// <summary>The batch that creates the object; SQL Server stores this whole batch as the object's definition.</summary>
    public string DefinitionBatch => BodyBatches[DefinitionBatchIndex];
}

/// <summary>A comment-only stub in <c>database/objects/retired</c>: the object is dropped if it is still there.</summary>
public sealed record RetiredSchemaObject(string FileName, string Schema, string Name, string FileSha256)
{
    public string ObjectName => Schema + "." + Name;
}

public sealed record SchemaObjectSet(IReadOnlyList<SchemaObjectFile> Objects, IReadOnlyList<RetiredSchemaObject> Retired)
{
    public static SchemaObjectSet Empty { get; } = new([], []);

    public bool IsEmpty => Objects.Count == 0 && Retired.Count == 0;
}

public interface ISchemaObjectSource
{
    Task<SchemaObjectSet> DiscoverAsync(CancellationToken cancellationToken = default);
}

/// <summary>A source with no objects: tools and tests that want migrations only.</summary>
public sealed class SchemaObjectSource : ISchemaObjectSource
{
    public static SchemaObjectSource None { get; } = new();

    private SchemaObjectSource() { }

    public Task<SchemaObjectSet> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(SchemaObjectSet.Empty);
}

/// <summary>An object file or the journal is not what the rules allow. Nothing was changed.</summary>
public sealed class SchemaObjectIntegrityException(string message) : InvalidOperationException(message);

/// <summary>Applying the changed objects failed; every object change of the run was rolled back.</summary>
public sealed class SchemaObjectsNotUpToDateException : InvalidOperationException
{
    public const string Reason = "Database objects are not up to date; run setup again.";

    public SchemaObjectsNotUpToDateException(string? objectName, Exception innerException)
        : base(objectName is null
            ? Reason + " The object changes were rolled back."
            : Reason + " Applying " + objectName + " failed and every object change was rolled back.", innerException)
    {
        ObjectName = objectName;
    }

    /// <summary>The object whose statements failed, or null when the failure came after them (refresh, journal, COMMIT).</summary>
    public string? ObjectName { get; }
}

/// <summary>Parsing, hashing and normalising object files.</summary>
public static partial class SchemaObjectFiles
{
    public const string GrantsMarker = "-- >>> GRANTS";
    public const string RetiredFolder = "retired";

    /// <summary><c>database/objects</c> next to the installation's <c>database/migrations</c>.</summary>
    public static string ObjectsDirectoryFor(string migrationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationDirectory);
        var full = Path.GetFullPath(migrationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(Path.GetDirectoryName(full) ?? full, "objects");
    }

    /// <summary>The file hash: SHA-256 of the text with LF line endings, like a migration checksum.</summary>
    public static string FileHash(string text) => MigrationChecksum.Compute(text);

    /// <summary>The definition hash: SHA-256 of OBJECT_DEFINITION exactly as SQL Server returns it.</summary>
    public static string DefinitionHash(string definition) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definition))).ToLowerInvariant();

    /// <summary>
    /// The text compared when adopting: LF line endings, no leading or trailing white space, and the first header written
    /// <c>CREATE &lt;kind&gt; &lt;name&gt;</c>. SQL Server stores "CREATE OR ALTER VIEW" as "CREATE   VIEW", so the file's
    /// CREATE OR ALTER and the stored CREATE compare equal; PROC compares equal to PROCEDURE. Nothing else is ignored: a
    /// changed comment, space or token inside the definition is a change.
    /// </summary>
    public static string NormaliseDefinition(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lf = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var header = Header().Match(lf);
        if (!header.Success) return lf;
        return string.Concat(lf.AsSpan(0, header.Index), "CREATE " + CanonicalKind(header.Groups["kind"].Value) + " " + header.Groups["name"].Value,
            lf.AsSpan(header.Index + header.Length));
    }

    /// <summary>Splits a script on lines holding only <c>GO</c>; batches that are only white space are dropped.</summary>
    public static IReadOnlyList<string> SplitBatches(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return GoLine().Split(text).Where(batch => !string.IsNullOrWhiteSpace(batch)).ToArray();
    }

    public static SchemaObjectFile Parse(string fileName, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = ObjectFileName().Match(fileName ?? "");
        if (!match.Success)
            throw new SchemaObjectIntegrityException($"The object file '{fileName}' is not named <layer>-<schema>.<name>.sql (layer two digits, names letters, digits and underscores).");
        var layer = int.Parse(match.Groups["layer"].Value, CultureInfo.InvariantCulture);
        var schema = match.Groups["schema"].Value;
        var name = match.Groups["name"].Value;

        var markers = GrantsLine().Matches(text);
        if (markers.Count != 1)
            throw new SchemaObjectIntegrityException($"The object file '{fileName}' must have exactly one line '{GrantsMarker}' after its CREATE OR ALTER batch.");
        var body = text[..markers[0].Index];
        var grants = text[(markers[0].Index + markers[0].Length)..];

        var batches = SplitBatches(body);
        var definitionIndex = -1;
        Match? header = null;
        for (var index = 0; index < batches.Count && definitionIndex < 0; index++)
        {
            var candidate = Header().Match(batches[index]);
            if (!candidate.Success) continue;
            definitionIndex = index;
            header = candidate;
        }
        if (header is null)
            throw new SchemaObjectIntegrityException($"The object file '{fileName}' has no CREATE OR ALTER VIEW, PROCEDURE or FUNCTION before its GRANTS line.");
        if (!WhiteSpace().Replace(header.Groups["verb"].Value, " ").Equals("CREATE OR ALTER", StringComparison.OrdinalIgnoreCase))
            throw new SchemaObjectIntegrityException($"The object file '{fileName}' must use CREATE OR ALTER (it keeps the object's permissions and works on a new database too).");
        var declared = Unquote(header.Groups["name"].Value);
        if (!string.Equals(declared, schema + "." + name, StringComparison.OrdinalIgnoreCase))
            throw new SchemaObjectIntegrityException($"The object file '{fileName}' creates {declared}, not {schema}.{name}.");
        var kind = CanonicalKind(header.Groups["kind"].Value) switch
        {
            "VIEW" => SchemaObjectKind.View,
            "PROCEDURE" => SchemaObjectKind.Procedure,
            _ => SchemaObjectKind.Function
        };
        if (!LayerAllows(layer, kind))
            throw new SchemaObjectIntegrityException($"The object file '{fileName}' is a {kind.ToString().ToLowerInvariant()} in layer {layer:00}; "
                + "layers are 10-19 functions, 20-29 views on tables, 30-39 views or functions on other objects, 40-49 procedures.");

        return new SchemaObjectFile(fileName!, layer, schema, name, kind, FileHash(text), batches, definitionIndex, ParseGrants(fileName!, schema, name, grants));
    }

    public static RetiredSchemaObject ParseRetired(string fileName, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = RetiredFileName().Match(fileName ?? "");
        if (!match.Success)
            throw new SchemaObjectIntegrityException($"The retired stub '{fileName}' is not named <schema>.<name>.sql.");
        if (StatementLines(text).Any())
            throw new SchemaObjectIntegrityException($"The retired stub '{fileName}' must hold comments only (why and when it was retired); setup drops the object itself.");
        return new RetiredSchemaObject(fileName!, match.Groups["schema"].Value, match.Groups["name"].Value, FileHash(text));
    }

    /// <summary>
    /// The GRANTS block: one statement per line, each a GRANT, DENY or REVOKE on this object only. Comment and blank lines
    /// are allowed. Anything else is refused, so the block can never change another object.
    /// </summary>
    public static IReadOnlyList<string> ParseGrants(string fileName, string schema, string name, string block)
    {
        var statements = new List<string>();
        foreach (var line in StatementLines(block))
        {
            var grant = GrantStatement().Match(line);
            if (!grant.Success || !string.Equals(Unquote(grant.Groups["object"].Value), schema + "." + name, StringComparison.OrdinalIgnoreCase))
                throw new SchemaObjectIntegrityException($"The GRANTS block of '{fileName}' may hold only one-line GRANT, DENY or REVOKE statements on {schema}.{name}; found: {Cut(line)}");
            statements.Add(line);
        }
        return statements;
    }

    /// <summary>The permission rows a GRANTS block gives (GRANT and DENY; REVOKE gives none), for the ExpectedRolePermissions guard.</summary>
    public static IReadOnlyList<(string Role, string Schema, string Object, string Permission, string State)> PermissionRows(SchemaObjectFile file)
    {
        var rows = new List<(string, string, string, string, string)>();
        foreach (var statement in file.GrantStatements)
        {
            var grant = GrantStatement().Match(statement);
            var verb = grant.Groups["verb"].Value.ToUpperInvariant();
            if (verb == "REVOKE") continue;
            var state = verb == "DENY" ? "DENY" : grant.Groups["option"].Success ? "GRANT_WITH_GRANT_OPTION" : "GRANT";
            var columns = grant.Groups["columns"].Success
                ? grant.Groups["columns"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote).ToArray()
                : [""];
            foreach (var permission in grant.Groups["permissions"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                foreach (var grantee in grant.Groups["grantees"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    foreach (var column in columns)
                        rows.Add((Unquote(grantee), file.Schema, column.Length == 0 ? file.Name : $"{file.Name}({column})",
                            WhiteSpace().Replace(permission, " ").ToUpperInvariant(), state));
        }
        return rows;
    }

    internal static bool LayerAllows(int layer, SchemaObjectKind kind) => layer switch
    {
        >= 10 and <= 19 => kind == SchemaObjectKind.Function,
        >= 20 and <= 29 => kind == SchemaObjectKind.View,
        >= 30 and <= 39 => kind is SchemaObjectKind.View or SchemaObjectKind.Function,
        >= 40 and <= 49 => kind == SchemaObjectKind.Procedure,
        _ => false
    };

    internal static bool KindMatches(SchemaObjectKind kind, string typeCode) => typeCode.Trim().ToUpperInvariant() switch
    {
        "V" => kind == SchemaObjectKind.View,
        "P" => kind == SchemaObjectKind.Procedure,
        "FN" or "IF" or "TF" => kind == SchemaObjectKind.Function,
        _ => false
    };

    internal static string DropKind(string typeCode) => typeCode.Trim().ToUpperInvariant() switch
    {
        "V" => "VIEW",
        "P" => "PROCEDURE",
        "FN" or "IF" or "TF" => "FUNCTION",
        _ => throw new SchemaObjectIntegrityException($"Object type '{typeCode}' is not a view, procedure or function.")
    };

    internal static string QuoteName(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string CanonicalKind(string kind) => kind.ToUpperInvariant() switch
    {
        "PROC" or "PROCEDURE" => "PROCEDURE",
        "VIEW" => "VIEW",
        _ => "FUNCTION"
    };

    private static string Unquote(string value) => value.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal).Trim();

    private static string Cut(string value) => value.Length <= 120 ? value : value[..120] + "...";

    private static IEnumerable<string> StatementLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

    [GeneratedRegex(@"^(?<layer>[0-9]{2})-(?<schema>[A-Za-z_][A-Za-z0-9_]*)\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectFileName();

    [GeneratedRegex(@"^(?<schema>[A-Za-z_][A-Za-z0-9_]*)\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex RetiredFileName();

    [GeneratedRegex(@"^--[ \t]*>>>[ \t]*GRANTS[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GrantsLine();

    [GeneratedRegex(@"^[ \t]*GO[ \t]*(--[^\r\n]*)?\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoLine();

    [GeneratedRegex(@"^[ \t]*(?<verb>CREATE[ \t]+OR[ \t]+ALTER|CREATE|ALTER)\s+(?<kind>VIEW|PROCEDURE|PROC|FUNCTION)\s+(?<name>(\[[^\]\r\n]+\]|[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(\[[^\]\r\n]+\]|[A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Header();

    [GeneratedRegex(@"^(?<verb>GRANT|DENY|REVOKE)\s+(?<permissions>[A-Za-z ]+(\s*,\s*[A-Za-z ]+)*?)\s+ON\s+(OBJECT\s*::\s*)?(?<object>(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*))\s*(\((?<columns>[^)]*)\))?\s+(TO|FROM)\s+(?<grantees>(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)(\s*,\s*(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*))*)(?<option>\s+WITH\s+GRANT\s+OPTION)?(\s+CASCADE)?\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GrantStatement();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhiteSpace();
}

/// <summary>Reads <c>database/objects</c>: the object files in the folder itself and the stubs in <c>retired</c>.</summary>
public sealed class DirectorySchemaObjectSource(string directory) : ISchemaObjectSource
{
    public string DirectoryPath => directory;

    public async Task<SchemaObjectSet> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return SchemaObjectSet.Empty;
        var objects = new List<SchemaObjectFile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.sql", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            objects.Add(SchemaObjectFiles.Parse(Path.GetFileName(path), await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)));
        var retired = new List<RetiredSchemaObject>();
        var retiredDirectory = Path.Combine(directory, SchemaObjectFiles.RetiredFolder);
        if (Directory.Exists(retiredDirectory))
            foreach (var path in Directory.EnumerateFiles(retiredDirectory, "*.sql", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                retired.Add(SchemaObjectFiles.ParseRetired(Path.GetFileName(path), await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)));
        return SchemaObjectPlanner.Validate(new SchemaObjectSet(objects, retired));
    }
}

/// <summary>A row of <c>dbo.schema_objects</c>.</summary>
public sealed record SchemaObjectJournalEntry(string ObjectName, string ObjectType, string FileSha256, string DefinitionSha256);

/// <summary>
/// A view, procedure or function in the database. <see cref="PinnedBy"/> says why it cannot be a repeatable object (a
/// constraint, computed column, security policy or schema-bound object uses it), or is null.
/// </summary>
public sealed record LiveSchemaObject(string ObjectName, string TypeCode, string? Definition, string? PinnedBy = null);

public enum SchemaObjectAction
{
    Skip,
    Adopt,
    Apply,
    Retire
}

public sealed record SchemaObjectStep(
    string ObjectName,
    SchemaObjectAction Action,
    SchemaObjectFile? File,
    RetiredSchemaObject? Retired,
    LiveSchemaObject? Live,
    SchemaObjectJournalEntry? Journal)
{
    /// <summary>Apply and Retire change an object; Adopt writes only the journal.</summary>
    public bool ChangesObject => Action is SchemaObjectAction.Apply or SchemaObjectAction.Retire;

    public string FileName => File?.FileName ?? Retired?.FileName ?? "";

    public string FileSha256 => File?.FileSha256 ?? Retired?.FileSha256 ?? "";
}

/// <summary>Decides, without a database, what the runner does with each object (plan A3 runner steps 2-5).</summary>
public static class SchemaObjectPlanner
{
    /// <summary>Refuses two files for one object, and an object that is both a file and a retired stub.</summary>
    public static SchemaObjectSet Validate(SchemaObjectSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var duplicate = set.Objects.GroupBy(file => file.ObjectName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new SchemaObjectIntegrityException($"{duplicate.Key} has more than one object file: {string.Join(", ", duplicate.Select(file => file.FileName))}.");
        var both = set.Retired.FirstOrDefault(stub => set.Objects.Any(file => string.Equals(file.ObjectName, stub.ObjectName, StringComparison.OrdinalIgnoreCase)));
        if (both is not null)
            throw new SchemaObjectIntegrityException($"{both.ObjectName} has an object file and a retired stub; keep one.");
        var duplicateStub = set.Retired.GroupBy(stub => stub.ObjectName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateStub is not null)
            throw new SchemaObjectIntegrityException($"{duplicateStub.Key} has more than one retired stub.");
        return set;
    }

    /// <summary>
    /// One step per object file (in file-name order, so layer then name) after one step per retired stub. Skip: the journal
    /// row has this file's hash and the live definition still has the recorded hash. Adopt: the live definition already
    /// equals the file (only CREATE vs CREATE OR ALTER, PROC vs PROCEDURE, line endings and surrounding white space ignored)
    /// and the journal has no row, or a row for this same file. Apply: anything else. Retire: a stub whose object exists or
    /// is journaled. An object a constraint, computed column, security policy or schema-bound object uses is refused: it
    /// stays in the migrations (database/objects/EXCLUDED.md).
    /// </summary>
    public static IReadOnlyList<SchemaObjectStep> Plan(
        SchemaObjectSet set,
        IReadOnlyList<SchemaObjectJournalEntry> journal,
        IReadOnlyList<LiveSchemaObject> live)
    {
        Validate(set);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(live);
        var journalByName = new Dictionary<string, SchemaObjectJournalEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in journal) journalByName.TryAdd(entry.ObjectName, entry);
        var liveByName = new Dictionary<string, LiveSchemaObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in live) liveByName.TryAdd(item.ObjectName, item);

        var steps = new List<SchemaObjectStep>();
        foreach (var stub in set.Retired.OrderBy(stub => stub.ObjectName, StringComparer.OrdinalIgnoreCase))
        {
            liveByName.TryGetValue(stub.ObjectName, out var current);
            journalByName.TryGetValue(stub.ObjectName, out var recorded);
            if (current?.PinnedBy is { } pinned)
                throw new SchemaObjectIntegrityException($"{stub.ObjectName} cannot be retired by setup: {pinned}. Drop it in a migration.");
            steps.Add(new(stub.ObjectName, current is null && recorded is null ? SchemaObjectAction.Skip : SchemaObjectAction.Retire,
                null, stub, current, recorded));
        }

        foreach (var file in set.Objects.OrderBy(file => file.FileName, StringComparer.Ordinal))
        {
            liveByName.TryGetValue(file.ObjectName, out var current);
            journalByName.TryGetValue(file.ObjectName, out var recorded);
            if (current?.PinnedBy is { } pinned)
                throw new SchemaObjectIntegrityException($"{file.ObjectName} cannot be a repeatable object: {pinned}. It stays in the migrations (database/objects/EXCLUDED.md).");
            if (current is not null && !SchemaObjectFiles.KindMatches(file.Kind, current.TypeCode))
                throw new SchemaObjectIntegrityException($"{file.ObjectName} is of type {current.TypeCode.Trim()} in the database, but its file creates a {file.Kind.ToString().ToLowerInvariant()}.");
            steps.Add(new(file.ObjectName, Decide(file, current, recorded), file, null, current, recorded));
        }
        return steps;
    }

    private static SchemaObjectAction Decide(SchemaObjectFile file, LiveSchemaObject? current, SchemaObjectJournalEntry? recorded)
    {
        if (current?.Definition is not { } definition) return SchemaObjectAction.Apply;
        var sameFile = recorded is not null && string.Equals(recorded.FileSha256.Trim(), file.FileSha256, StringComparison.OrdinalIgnoreCase);
        if (sameFile && string.Equals(recorded!.DefinitionSha256.Trim(), SchemaObjectFiles.DefinitionHash(definition), StringComparison.OrdinalIgnoreCase))
            return SchemaObjectAction.Skip;
        if ((recorded is null || sameFile)
            && string.Equals(SchemaObjectFiles.NormaliseDefinition(definition), SchemaObjectFiles.NormaliseDefinition(file.DefinitionBatch), StringComparison.Ordinal))
            return SchemaObjectAction.Adopt;
        return SchemaObjectAction.Apply;
    }
}

public sealed record SchemaObjectRunResult(IReadOnlyList<string> Adopted, IReadOnlyList<string> Applied, IReadOnlyList<string> Retired)
{
    public static SchemaObjectRunResult None { get; } = new([], [], []);
}

public interface ISchemaObjectStore
{
    Task<IAsyncDisposable> AcquireLockAsync(CancellationToken cancellationToken = default);

    /// <summary>False before migration 0062.</summary>
    Task<bool> JournalExistsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SchemaObjectJournalEntry>> LoadJournalAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveSchemaObject>> LoadLiveAsync(CancellationToken cancellationToken = default);

    /// <summary>All steps in one transaction; a failure rolls every one back and throws <see cref="SchemaObjectsNotUpToDateException"/>.</summary>
    Task ApplyAsync(IReadOnlyList<SchemaObjectStep> steps, string? releaseVersion, CancellationToken cancellationToken = default);
}

/// <summary>The objects step, run by <see cref="SqlServerDatabaseBootstrapper"/> right after the migrations.</summary>
public sealed class SchemaObjectRunner(ISchemaObjectSource source, ISchemaObjectStore store)
{
    /// <summary>Written to the journal; the product version of this assembly by default.</summary>
    public string? ReleaseVersion { get; init; } = DefaultReleaseVersion();

    public async Task<SchemaObjectRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var set = await source.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (set.IsEmpty) return SchemaObjectRunResult.None;

        var objectLock = await store.AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        SchemaObjectRunResult? result = null;
        Exception? runException = null;
        try
        {
            if (!await store.JournalExistsAsync(cancellationToken).ConfigureAwait(false))
                throw new SchemaObjectIntegrityException("The database has no object journal (dbo.schema_objects): migration 0062 has not run. Nothing was changed.");
            var journal = await store.LoadJournalAsync(cancellationToken).ConfigureAwait(false);
            var live = await store.LoadLiveAsync(cancellationToken).ConfigureAwait(false);
            var work = SchemaObjectPlanner.Plan(set, journal, live).Where(step => step.Action != SchemaObjectAction.Skip).ToArray();
            if (work.Length > 0) await store.ApplyAsync(work, ReleaseVersion, cancellationToken).ConfigureAwait(false);
            result = new(Names(work, SchemaObjectAction.Adopt), Names(work, SchemaObjectAction.Apply), Names(work, SchemaObjectAction.Retire));
        }
        catch (Exception exception)
        {
            runException = exception;
        }

        Exception? releaseException = null;
        try { await objectLock.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { releaseException = exception; }

        if (runException is not null && releaseException is not null)
            throw new AggregateException("The database objects step and the SQL migration-lock release both failed.", runException, releaseException);
        if (runException is not null) ExceptionDispatchInfo.Capture(runException).Throw();
        if (releaseException is not null) ExceptionDispatchInfo.Capture(releaseException).Throw();
        return result ?? SchemaObjectRunResult.None;
    }

    internal static string? DefaultReleaseVersion()
    {
        var version = typeof(SchemaObjectRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version)) return null;
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0) version = version[..plus];
        return version.Length <= 40 ? version : version[..40];
    }

    private static string[] Names(IEnumerable<SchemaObjectStep> steps, SchemaObjectAction action) =>
        steps.Where(step => step.Action == action).Select(step => step.ObjectName).ToArray();
}

/// <summary>The catalog and journal queries, shared by the runner, the plan verb and health check DB-07. All read only.</summary>
internal static class SchemaObjectSql
{
    internal const string JournalExistsSql =
        "SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.schema_objects',N'U') IS NULL OR OBJECT_ID(N'dbo.schema_object_history',N'U') IS NULL THEN 0 ELSE 1 END);";

    internal const string JournalSql = "SELECT object_name,object_type,file_sha256,definition_sha256 FROM dbo.schema_objects;";

    /// <summary>
    /// Every view, procedure and function of dbo and diag with its definition and, when it cannot be a repeatable object, why:
    /// a CHECK or DEFAULT constraint, a computed column (the referencing object is the table), a security policy, or a
    /// schema-bound object uses it (CREATE OR ALTER would fail or the policy would break).
    /// </summary>
    internal const string LiveObjectsSql = """
        SELECT CONCAT(s.name,N'.',o.name) AS object_name,RTRIM(o.type) AS type_code,OBJECT_DEFINITION(o.object_id) AS definition,
         CASE
          WHEN EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d JOIN sys.objects r ON r.object_id=d.referencing_id
                      WHERE d.referenced_id=o.object_id AND r.type IN ('C','D')) THEN N'a constraint uses it'
          WHEN EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d JOIN sys.objects r ON r.object_id=d.referencing_id
                      WHERE d.referenced_id=o.object_id AND r.type='U') THEN N'a computed column uses it'
          WHEN EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d JOIN sys.objects r ON r.object_id=d.referencing_id
                      WHERE d.referenced_id=o.object_id AND r.type='SP') THEN N'a security policy uses it'
          WHEN EXISTS(SELECT 1 FROM sys.security_predicates p
                      WHERE p.predicate_definition LIKE N'%' + REPLACE(o.name,N'_',N'[_]') + N'%') THEN N'a security policy uses it'
          WHEN EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d
                      WHERE d.referenced_id=o.object_id AND d.referencing_id<>o.object_id AND d.is_schema_bound_reference=1) THEN N'a schema-bound object uses it'
         END AS pinned_by
        FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
        WHERE o.type IN ('V','P','FN','IF','TF') AND s.name IN (N'dbo',N'diag') AND o.is_ms_shipped=0;
        """;

    internal static async Task<bool> JournalExistsAsync(SqlCommand command, CancellationToken cancellationToken) =>
        (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

    internal static async Task<IReadOnlyList<SchemaObjectJournalEntry>> ReadJournalAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        var result = new List<SchemaObjectJournalEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), reader.GetString(1).Trim(), reader.GetString(2).Trim(), reader.GetString(3).Trim()));
        return result;
    }

    internal static async Task<IReadOnlyList<LiveSchemaObject>> ReadLiveAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        var result = new List<LiveSchemaObject>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return result;
    }

    internal static async Task<IReadOnlyList<AppliedMigration>> ReadAppliedMigrationsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var exists = new SqlCommand("SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.schema_migrations',N'U') IS NULL THEN 0 ELSE 1 END);", connection))
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!) return [];
        await using var command = new SqlCommand("SELECT migration_id,checksum,applied_utc FROM dbo.schema_migrations ORDER BY migration_id;", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), reader.GetString(1), new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc))));
        return result;
    }
}

public sealed class SqlServerSchemaObjectStore(string connectionString) : ISchemaObjectStore
{
    /// <summary>The objects step takes the same application lock as the migrations, so two setups never interleave.</summary>
    public Task<IAsyncDisposable> AcquireLockAsync(CancellationToken cancellationToken = default) =>
        new SqlServerMigrationStore(connectionString).AcquireMigrationLockAsync(cancellationToken);

    public async Task<bool> JournalExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(SchemaObjectSql.JournalExistsSql, connection);
        return await SchemaObjectSql.JournalExistsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SchemaObjectJournalEntry>> LoadJournalAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(SchemaObjectSql.JournalSql, connection);
        return await SchemaObjectSql.ReadJournalAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LiveSchemaObject>> LoadLiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(SchemaObjectSql.LiveObjectsSql, connection) { CommandTimeout = 120 };
        return await SchemaObjectSql.ReadLiveAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyAsync(IReadOnlyList<SchemaObjectStep> steps, string? releaseVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0) return;
        // The transaction has the commit budget as Connect Timeout, which SqlClient also applies to COMMIT (as for migrations).
        await using var connection = await LocalSqlConnectionPolicy.OpenWithCommitBudgetAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string? current = null;
        try
        {
            var historyBefore = Convert.ToInt64(await ScalarAsync(connection, transaction,
                "SELECT ISNULL(MAX(schema_object_history_id),0) FROM dbo.schema_object_history;", null, cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            var changed = new List<string>();
            foreach (var step in steps)
            {
                current = step.ObjectName;
                switch (step.Action)
                {
                    case SchemaObjectAction.Retire:
                        await RetireAsync(connection, transaction, step, releaseVersion, cancellationToken).ConfigureAwait(false);
                        break;
                    case SchemaObjectAction.Adopt:
                        await RecordAsync(connection, transaction, step, "ADOPT", releaseVersion, cancellationToken).ConfigureAwait(false);
                        break;
                    case SchemaObjectAction.Apply:
                        foreach (var batch in step.File!.BodyBatches)
                            await ExecuteAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
                        foreach (var grant in step.File.GrantStatements)
                            await ExecuteAsync(connection, transaction, grant, cancellationToken).ConfigureAwait(false);
                        await RecordAsync(connection, transaction, step, "APPLY", releaseVersion, cancellationToken).ConfigureAwait(false);
                        if (step.File.Kind != SchemaObjectKind.Procedure) changed.Add(step.ObjectName);
                        break;
                    default:
                        continue;
                }
            }
            current = null;
            // A view that reads a changed view or function keeps the old column list until it is refreshed. Every
            // non-schema-bound view that depends on a changed object, directly or through another refreshed view, is
            // refreshed here, in the same transaction: a change that breaks one of them rolls the whole run back.
            await RefreshDependentViewsAsync(connection, transaction, changed, cancellationToken).ConfigureAwait(false);

            // A COMMIT whose reply is lost is judged by the history: this run's rows commit with the objects.
            await SqlTransactionGuard.CommitOrVerifyAsync(() => transaction.CommitAsync(cancellationToken),
                () => SqlTransactionGuard.ReleaseAsync(connection),
                () => SqlTransactionGuard.CheckAsync(connectionString,
                    "SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.schema_object_history WITH(READCOMMITTEDLOCK) WHERE schema_object_history_id>@before) THEN 1 ELSE 0 END)",
                    command => command.Parameters.AddWithValue("@before", historyBefore))).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            SqlTransactionGuard.MarkRolledBack(failure);
            await SqlTransactionGuard.RollBackAsync(failure, transaction).ConfigureAwait(false);
            if (SqlTransactionGuard.CommitStateOf(failure) is CommitState.Unknown) throw;
            throw new SchemaObjectsNotUpToDateException(current, failure);
        }
    }

    private static async Task RetireAsync(SqlConnection connection, SqlTransaction transaction, SchemaObjectStep step, string? releaseVersion, CancellationToken token)
    {
        var (schema, name) = Split(step.ObjectName);
        var type = Convert.ToString(await ScalarAsync(connection, transaction,
            "SELECT RTRIM(type) FROM sys.objects WHERE object_id=OBJECT_ID(@name) AND type IN ('V','P','FN','IF','TF');",
            command => command.Parameters.AddWithValue("@name", step.ObjectName), token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        string? previous = null;
        if (!string.IsNullOrEmpty(type))
        {
            var users = Convert.ToString(await ScalarAsync(connection, transaction, """
                SELECT TOP(1) CONCAT(s.name,N'.',o.name) FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON o.object_id=d.referencing_id JOIN sys.schemas s ON s.schema_id=o.schema_id
                WHERE d.referenced_id=OBJECT_ID(@name) AND d.referencing_id<>OBJECT_ID(@name) AND o.type IN ('V','FN','IF','TF')
                ORDER BY s.name,o.name;
                """, command => command.Parameters.AddWithValue("@name", step.ObjectName), token).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(users))
                throw new SchemaObjectIntegrityException($"{step.ObjectName} cannot be retired: {users} still uses it.");
            previous = await DefinitionHashAsync(connection, transaction, step.ObjectName, token).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction,
                $"DROP {SchemaObjectFiles.DropKind(type)} {SchemaObjectFiles.QuoteName(schema)}.{SchemaObjectFiles.QuoteName(name)};", token).ConfigureAwait(false);
        }
        var objectType = !string.IsNullOrEmpty(type) ? type : step.Journal?.ObjectType ?? "V";
        await using var command = new SqlCommand("""
            DELETE dbo.schema_objects WHERE object_name=@name;
            INSERT dbo.schema_object_history(object_name,object_type,action,file_name,file_sha256,previous_definition_sha256,definition_sha256,release_version)
            VALUES(@name,@type,'RETIRE',@file,@sha,@previous,NULL,@release);
            """, connection, transaction);
        command.Parameters.AddWithValue("@name", step.ObjectName);
        command.Parameters.AddWithValue("@type", objectType);
        command.Parameters.AddWithValue("@file", step.FileName);
        command.Parameters.AddWithValue("@sha", step.FileSha256);
        command.Parameters.AddWithValue("@previous", (object?)previous ?? DBNull.Value);
        command.Parameters.AddWithValue("@release", (object?)releaseVersion ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task RecordAsync(SqlConnection connection, SqlTransaction transaction, SchemaObjectStep step, string action, string? releaseVersion, CancellationToken token)
    {
        var definition = await DefinitionHashAsync(connection, transaction, step.ObjectName, token).ConfigureAwait(false)
            ?? throw new SchemaObjectIntegrityException($"{step.ObjectName} has no readable definition after {action.ToLowerInvariant()}.");
        var type = Convert.ToString(await ScalarAsync(connection, transaction, "SELECT RTRIM(type) FROM sys.objects WHERE object_id=OBJECT_ID(@name);",
            command => command.Parameters.AddWithValue("@name", step.ObjectName), token).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
        if (!SchemaObjectFiles.KindMatches(step.File!.Kind, type))
            throw new SchemaObjectIntegrityException($"{step.ObjectName} is of type {type} after {action.ToLowerInvariant()}, not a {step.File.Kind.ToString().ToLowerInvariant()}.");
        var previous = step.Live?.Definition is { } before ? SchemaObjectFiles.DefinitionHash(before) : null;
        await using var command = new SqlCommand("""
            UPDATE dbo.schema_objects SET object_type=@type,file_name=@file,file_sha256=@sha,definition_sha256=@definition,
              applied_utc=SYSUTCDATETIME(),applied_by=ORIGINAL_LOGIN(),release_version=@release
            WHERE object_name=@name;
            IF @@ROWCOUNT=0
              INSERT dbo.schema_objects(object_name,object_type,file_name,file_sha256,definition_sha256,release_version)
              VALUES(@name,@type,@file,@sha,@definition,@release);
            INSERT dbo.schema_object_history(object_name,object_type,action,file_name,file_sha256,previous_definition_sha256,definition_sha256,release_version)
            VALUES(@name,@type,@action,@file,@sha,@previous,@definition,@release);
            """, connection, transaction);
        command.Parameters.AddWithValue("@name", step.ObjectName);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@file", step.FileName);
        command.Parameters.AddWithValue("@sha", step.FileSha256);
        command.Parameters.AddWithValue("@definition", definition);
        command.Parameters.AddWithValue("@previous", (object?)previous ?? DBNull.Value);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@release", (object?)releaseVersion ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task RefreshDependentViewsAsync(SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<string> changed, CancellationToken token)
    {
        // Each view is refreshed once; an applied view that reads another applied one is refreshed too (harmless).
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(changed);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            var dependents = new List<string>();
            await using (var command = new SqlCommand("""
                SELECT DISTINCT CONCAT(s.name,N'.',o.name) FROM sys.sql_expression_dependencies d
                JOIN sys.objects o ON o.object_id=d.referencing_id JOIN sys.schemas s ON s.schema_id=o.schema_id
                WHERE d.referenced_id=OBJECT_ID(@name) AND d.referencing_id<>OBJECT_ID(@name) AND o.type='V'
                  AND OBJECTPROPERTY(o.object_id,'IsSchemaBound')=0;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@name", name);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false)) dependents.Add(reader.GetString(0));
            }
            foreach (var dependent in dependents.Order(StringComparer.Ordinal))
            {
                if (!done.Add(dependent)) continue;
                await using var refresh = new SqlCommand("EXEC sys.sp_refreshview @viewname;", connection, transaction) { CommandTimeout = 0 };
                refresh.Parameters.AddWithValue("@viewname", dependent);
                await refresh.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                queue.Enqueue(dependent);
            }
        }
    }

    private static async Task<string?> DefinitionHashAsync(SqlConnection connection, SqlTransaction transaction, string objectName, CancellationToken token)
    {
        var definition = await ScalarAsync(connection, transaction, "SELECT OBJECT_DEFINITION(OBJECT_ID(@name));",
            command => command.Parameters.AddWithValue("@name", objectName), token).ConfigureAwait(false);
        return definition is string text ? SchemaObjectFiles.DefinitionHash(text) : null;
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction transaction, string sql, Action<SqlCommand>? bind, CancellationToken token)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        bind?.Invoke(command);
        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is DBNull ? null : value;
    }

    private static (string Schema, string Name) Split(string objectName)
    {
        var dot = objectName.IndexOf('.', StringComparison.Ordinal);
        return (objectName[..dot], objectName[(dot + 1)..]);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>What setup would change: <c>{pendingMigrations:[], pendingObjects:[]}</c> (headless verb <c>--plan-database-changes</c>).</summary>
public sealed record DatabaseChangePlan(IReadOnlyList<string> PendingMigrations, IReadOnlyList<string> PendingObjects)
{
    public bool HasChanges => PendingMigrations.Count > 0 || PendingObjects.Count > 0;

    public string ToJson() => JsonSerializer.Serialize(new { pendingMigrations = PendingMigrations, pendingObjects = PendingObjects });
}

/// <summary>
/// Read-only: compares the bundled migrations and object files with the database and lists what a bootstrap would change.
/// pendingObjects holds the objects that would be applied or retired; an adopt only writes the journal and is not listed.
/// A database that does not exist yet has every migration and every object pending. Before migration 0062 there is no
/// journal, so the plan is made against the objects as they are (an equal one is adopted, not pending).
/// </summary>
public static class DatabaseChangePlanner
{
    public static async Task<DatabaseChangePlan> PlanAsync(string connectionString, string migrationDirectory, CancellationToken cancellationToken = default)
    {
        var validated = LocalSqlConnectionPolicy.Validate(connectionString);
        var databaseName = SqlServerDatabaseBootstrapper.ValidateDatabaseName(new SqlConnectionStringBuilder(validated).InitialCatalog);
        var migrations = await new DirectoryMigrationSource(migrationDirectory).DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var objects = await new DirectorySchemaObjectSource(SchemaObjectFiles.ObjectsDirectoryFor(migrationDirectory)).DiscoverAsync(cancellationToken).ConfigureAwait(false);

        var master = new SqlConnectionStringBuilder(validated) { InitialCatalog = "master" };
        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var exists = new SqlCommand("SELECT CONVERT(bit,CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END);", connection);
            exists.Parameters.AddWithValue("@name", databaseName);
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!)
                return new(migrations.Select(script => script.Id).Order(StringComparer.Ordinal).ToArray(),
                    objects.Objects.Select(file => file.ObjectName).ToArray());
        }

        IReadOnlyList<AppliedMigration> applied;
        IReadOnlyList<SchemaObjectJournalEntry> journal = [];
        IReadOnlyList<LiveSchemaObject> live;
        await using (var connection = new SqlConnection(validated))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            applied = await SchemaObjectSql.ReadAppliedMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
            await using (var exists = new SqlCommand(SchemaObjectSql.JournalExistsSql, connection))
                if (await SchemaObjectSql.JournalExistsAsync(exists, cancellationToken).ConfigureAwait(false))
                {
                    await using var read = new SqlCommand(SchemaObjectSql.JournalSql, connection);
                    journal = await SchemaObjectSql.ReadJournalAsync(read, cancellationToken).ConfigureAwait(false);
                }
            await using var liveCommand = new SqlCommand(SchemaObjectSql.LiveObjectsSql, connection) { CommandTimeout = 120 };
            live = await SchemaObjectSql.ReadLiveAsync(liveCommand, cancellationToken).ConfigureAwait(false);
        }
        var pendingMigrations = MigrationPlanner.Plan(migrations, applied).Select(script => script.Id).ToArray();
        var pendingObjects = SchemaObjectPlanner.Plan(objects, journal, live).Where(step => step.ChangesObject).Select(step => step.ObjectName).ToArray();
        return new(pendingMigrations, pendingObjects);
    }
}
