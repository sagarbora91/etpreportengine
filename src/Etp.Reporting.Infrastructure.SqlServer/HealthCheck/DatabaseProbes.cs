using System.Globalization;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>
/// DB-01..05 (design 5.2) and DB-07 (1.12 A3). One probe per check, so the time budget cuts as little as possible. Every SQL text goes
/// through <see cref="ReadOnlySqlGuard.Require"/> before a command is created (design 5.4); nothing here writes.
/// DB-02 reads <c>dbo.schema_migrations</c> with its own SELECT rather than
/// <see cref="SqlServerMigrationStore.GetAppliedAsync"/>, which creates the journal when it is missing.
/// </summary>
public static class DatabaseProbes
{
    /// <summary>DB-01 Warning above this connect time (an AUTO_CLOSE start-up or a busy server).</summary>
    public static readonly TimeSpan SlowConnect = TimeSpan.FromSeconds(5);

    /// <summary>DB-03 Warning when the last good CHECKDB is older than this (the monthly drill runs it, Q6).</summary>
    public const int MaximumCheckDbAgeDays = 35;

    /// <summary>DB-05 Fail at or above this share of the Express limit (the Warning share is the operations threshold, 80%).</summary>
    public const decimal DatabaseSizeCriticalPercent = 95m;

    /// <summary>DB-05 Warning from this size on a 10 GB Express limit (Architecture audit 7: warn at 6 GB, long before 10 GB).</summary>
    public const decimal DatabaseSizeWarningMegabytes = 6m * 1024m;

    /// <summary>
    /// The Database probes of this class in run order (DB-01..05, DB-07; DB-06 is L9's). <paramref name="migrationDirectory"/>
    /// is the installation's <c>database\migrations</c> folder; null or missing makes DB-02 Skipped. DB-07 reads the
    /// <c>database\objects</c> folder next to it; missing makes DB-07 Skipped.
    /// </summary>
    public static IReadOnlyList<IHealthProbe> Create(string connectionString, string? migrationDirectory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var clock = timeProvider ?? TimeProvider.System;
        return
        [
            new Probe(HealthCheckId.Db01, (context, token) => ReachableAsync(connectionString, token)),
            new Probe(HealthCheckId.Db02, (context, token) => MigrationsAsync(connectionString, migrationDirectory, token)),
            new Probe(HealthCheckId.Db03, (context, token) => IntegrityAsync(connectionString, clock, token)),
            new Probe(HealthCheckId.Db04, (context, token) => OptionsAsync(connectionString, token)),
            new Probe(HealthCheckId.Db05, (context, token) => SizeAsync(connectionString, token)),
            new Probe(HealthCheckId.Db07, (context, token) => ObjectsAsync(connectionString, migrationDirectory, token))
        ];
    }

    // ---- DB-01 ---------------------------------------------------------------------------------------------

    internal const string DatabaseNameSql = "SELECT DB_NAME();";

    internal static async Task<HealthCheckResult> ReachableAsync(string connectionString, CancellationToken cancellationToken)
    {
        var health = await new SqlServerHealthCheck(connectionString).CheckAsync(cancellationToken).ConfigureAwait(false);
        var elapsed = health.Elapsed ?? TimeSpan.Zero;
        if (health.Status == DatabaseHealthStatus.InvalidConfiguration)
            return Line(HealthCheckId.Db01, HealthOutcome.Fail, "CONFIG_INVALID", HealthCheckTexts.DbConfigInvalid, "connection", elapsed);
        if (health.Status == DatabaseHealthStatus.Unreachable)
            return Unreachable(health.FailureKind ?? DatabaseFailureKind.ServerUnreachable, health.SqlErrorNumber, elapsed);

        var configured = ConfiguredDatabase(connectionString);
        string actual;
        try
        {
            await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
            await using var command = DatabaseProbeSql.Command(connection, DatabaseNameSql);
            actual = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
        }
        catch (SqlException exception)
        {
            var kind = DatabaseConnectionFailure.Classify(exception);
            if (!DatabaseConnectionFailure.IsConnectionClass(kind)) throw;
            return Unreachable(kind, DatabaseConnectionFailure.DecidingNumber(exception, kind), elapsed);
        }

        if (configured.Length > 0 && !string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase))
            return Line(HealthCheckId.Db01, HealthOutcome.Fail, "WRONG_DATABASE", HealthCheckTexts.DbWrongDatabase(configured, actual), "connection", elapsed);
        var metric = (decimal)Math.Round(elapsed.TotalMilliseconds);
        return elapsed > SlowConnect
            ? Line(HealthCheckId.Db01, HealthOutcome.Warning, "SLOW_CONNECT", HealthCheckTexts.DbConnectedSlow(actual, elapsed), "connection", elapsed, metric)
            : Line(HealthCheckId.Db01, HealthOutcome.Pass, HealthCheckTexts.Ok, HealthCheckTexts.DbConnected(actual, elapsed), null, elapsed, metric);
    }

    internal static HealthCheckResult Unreachable(DatabaseFailureKind kind, int? sqlNumber, TimeSpan elapsed)
    {
        var (code, text) = kind switch
        {
            DatabaseFailureKind.Timeout => ("CONNECT_TIMEOUT", HealthCheckTexts.DbTimeoutText),
            DatabaseFailureKind.LoginRefused => ("LOGIN_REFUSED", HealthCheckTexts.DbLoginRefusedText),
            DatabaseFailureKind.DatabaseMissing => ("DATABASE_MISSING", HealthCheckTexts.DbDatabaseMissingText),
            DatabaseFailureKind.PermissionDenied => ("PERMISSION_DENIED", HealthCheckTexts.DbPermissionDeniedText),
            DatabaseFailureKind.InvalidConfiguration => ("CONFIG_INVALID", HealthCheckTexts.DbConfigInvalid.TrimEnd('.')),
            _ => ("SERVER_UNREACHABLE", HealthCheckTexts.DbServerUnreachableText)
        };
        return Line(HealthCheckId.Db01, HealthOutcome.Fail, code, HealthCheckTexts.DbConnectFailed(text, sqlNumber), "connection", elapsed, sqlNumber);
    }

    internal static string ConfiguredDatabase(string connectionString)
    {
        try { return new SqlConnectionStringBuilder(connectionString).InitialCatalog ?? ""; }
        catch (ArgumentException) { return ""; }
    }

    // ---- DB-02 ---------------------------------------------------------------------------------------------

    internal const string AppliedMigrationsSql = "SELECT migration_id, checksum FROM dbo.schema_migrations ORDER BY migration_id;";

    internal static async Task<HealthCheckResult> MigrationsAsync(string connectionString, string? migrationDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationDirectory) || !Directory.Exists(migrationDirectory))
            return Line(HealthCheckId.Db02, HealthOutcome.Skipped, "MIGRATIONS_NOT_FOUND", HealthCheckTexts.DbMigrationsNotFound, null, TimeSpan.Zero);
        var discovered = await new DirectoryMigrationSource(migrationDirectory).DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (discovered.Count == 0)
            return Line(HealthCheckId.Db02, HealthOutcome.Skipped, "MIGRATIONS_NOT_FOUND", HealthCheckTexts.DbMigrationsNotFound, null, TimeSpan.Zero);

        var applied = new List<(string Id, string Checksum)>();
        try
        {
            await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
            await using var command = DatabaseProbeSql.Command(connection, AppliedMigrationsSql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                applied.Add((reader.GetString(0), reader.GetString(1).Trim()));
        }
        catch (SqlException exception) when (DatabaseConnectionFailure.SqlNumbers(exception).Contains(208))
        {
            return Line(HealthCheckId.Db02, HealthOutcome.Fail, "NOT_INITIALISED", HealthCheckTexts.DbNotInitialised, null, TimeSpan.Zero);
        }
        int? objectFiles = null;
        try
        {
            var objects = await new DirectorySchemaObjectSource(SchemaObjectFiles.ObjectsDirectoryFor(migrationDirectory)).DiscoverAsync(cancellationToken).ConfigureAwait(false);
            if (objects.Objects.Count > 0) objectFiles = objects.Objects.Count;
        }
        catch (SchemaObjectIntegrityException)
        {
            // DB-07 reports unreadable object files; DB-02 stays about the migrations.
        }
        return EvaluateMigrations(discovered, applied, objectFiles);
    }

    /// <summary>
    /// The same rules as <see cref="MigrationPlanner.Plan"/>, classified instead of thrown: a checksum mismatch or an
    /// unknown older migration is Fail, a pending migration is Fail ("run ETP setup"), a database upgraded by a newer
    /// ETP is Warning ("update ETP on this PC").
    /// </summary>
    internal static HealthCheckResult EvaluateMigrations(IReadOnlyList<MigrationScript> discovered, IReadOnlyList<(string Id, string Checksum)> applied,
        int? objectFiles = null)
    {
        var known = new Dictionary<string, MigrationScript>(StringComparer.OrdinalIgnoreCase);
        foreach (var script in discovered) known.TryAdd(script.Id, script);
        var newestKnown = discovered.Select(script => script.Id).Max(StringComparer.Ordinal) ?? "";
        var appliedIds = applied.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mismatched = applied.Where(item => known.TryGetValue(item.Id, out var script)
                && !string.Equals(script.Checksum, item.Checksum, StringComparison.OrdinalIgnoreCase)
                && !MigrationChecksum.Matches(script.Sql, item.Checksum))
            .Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        var unknown = applied.Where(item => !known.ContainsKey(item.Id)).Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        var newer = unknown.Where(id => string.CompareOrdinal(id, newestKnown) > 0).ToArray();
        var unknownOlder = unknown.Except(newer, StringComparer.Ordinal).ToArray();
        var pending = discovered.Where(script => !appliedIds.Contains(script.Id)).Select(script => script.Id).Order(StringComparer.Ordinal).ToArray();

        var subs = new List<HealthCheckSubResult>();
        subs.AddRange(mismatched.Select(id => new HealthCheckSubResult(id, HealthOutcome.Fail, "CHECKSUM_MISMATCH")));
        subs.AddRange(unknownOlder.Select(id => new HealthCheckSubResult(id, HealthOutcome.Fail, "MIGRATION_UNKNOWN")));
        subs.AddRange(pending.Select(id => new HealthCheckSubResult(id, HealthOutcome.Fail, "PENDING")));
        subs.AddRange(newer.Select(id => new HealthCheckSubResult(id, HealthOutcome.Warning, "DATABASE_NEWER")));
        var detail = subs.Count > 0 ? subs : null;
        decimal metric = applied.Count;

        if (mismatched.Length > 0)
            return Line(HealthCheckId.Db02, HealthOutcome.Fail, "CHECKSUM_MISMATCH", HealthCheckTexts.DbMigrationChecksum(mismatched.Length, mismatched[0]), null, TimeSpan.Zero, metric, detail);
        if (unknownOlder.Length > 0)
            return Line(HealthCheckId.Db02, HealthOutcome.Fail, "MIGRATION_UNKNOWN", HealthCheckTexts.DbMigrationUnknown(unknownOlder.Length, unknownOlder[0]), null, TimeSpan.Zero, metric, detail);
        if (newer.Length > 0)
            return Line(HealthCheckId.Db02, HealthOutcome.Warning, "DATABASE_NEWER", HealthCheckTexts.DbDatabaseNewer(newer[^1]), null, TimeSpan.Zero, metric, detail);
        if (pending.Length > 0)
            return Line(HealthCheckId.Db02, HealthOutcome.Fail, "PENDING", HealthCheckTexts.DbMigrationsPending(pending.Length, pending[0]), null, TimeSpan.Zero, metric, detail);
        var latest = applied.Select(item => item.Id).Max(StringComparer.Ordinal) ?? "";
        var current = objectFiles is { } count
            ? HealthCheckTexts.DbMigrationsCurrent(applied.Count, latest, count)
            : HealthCheckTexts.DbMigrationsCurrent(applied.Count, latest);
        return Line(HealthCheckId.Db02, HealthOutcome.Pass, HealthCheckTexts.Ok, current, null, TimeSpan.Zero, metric);
    }

    // ---- DB-07 (1.12 A3) -----------------------------------------------------------------------------------

    /// <summary>
    /// DB-07 "Database objects match this release" (Warning at most). Reads the object files, <c>dbo.schema_objects</c> and
    /// the live definitions, and classifies with <see cref="SchemaObjectPlanner"/> what setup would do: a journaled object
    /// whose definition no longer has the recorded hash was changed by hand; one setup would apply is pending (or missing).
    /// </summary>
    internal static async Task<HealthCheckResult> ObjectsAsync(string connectionString, string? migrationDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationDirectory))
            return Line(HealthCheckId.Db07, HealthOutcome.Skipped, "OBJECTS_NOT_FOUND", HealthCheckTexts.DbObjectsNotFound, null, TimeSpan.Zero);
        SchemaObjectSet files;
        try
        {
            files = await new DirectorySchemaObjectSource(SchemaObjectFiles.ObjectsDirectoryFor(migrationDirectory)).DiscoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SchemaObjectIntegrityException)
        {
            return Line(HealthCheckId.Db07, HealthOutcome.Warning, "OBJECT_FILES_INVALID", HealthCheckTexts.DbObjectFilesInvalid, null, TimeSpan.Zero);
        }
        if (files.IsEmpty)
            return Line(HealthCheckId.Db07, HealthOutcome.Skipped, "OBJECTS_NOT_FOUND", HealthCheckTexts.DbObjectsNotFound, null, TimeSpan.Zero);

        await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using (var exists = DatabaseProbeSql.Command(connection, SchemaObjectSql.JournalExistsSql))
            if (!await SchemaObjectSql.JournalExistsAsync(exists, cancellationToken).ConfigureAwait(false))
                return Line(HealthCheckId.Db07, HealthOutcome.Warning, "OBJECTS_NOT_RECORDED", HealthCheckTexts.DbObjectsNotRecorded, null, TimeSpan.Zero);
        IReadOnlyList<SchemaObjectJournalEntry> journal;
        await using (var command = DatabaseProbeSql.Command(connection, SchemaObjectSql.JournalSql))
            journal = await SchemaObjectSql.ReadJournalAsync(command, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LiveSchemaObject> live;
        await using (var command = DatabaseProbeSql.Command(connection, SchemaObjectSql.LiveObjectsSql))
            live = await SchemaObjectSql.ReadLiveAsync(command, cancellationToken).ConfigureAwait(false);
        return EvaluateObjects(files, journal, live);
    }

    internal static HealthCheckResult EvaluateObjects(SchemaObjectSet files, IReadOnlyList<SchemaObjectJournalEntry> journal, IReadOnlyList<LiveSchemaObject> live)
    {
        IReadOnlyList<SchemaObjectStep> steps;
        try { steps = SchemaObjectPlanner.Plan(files, journal, live); }
        catch (SchemaObjectIntegrityException)
        {
            return Line(HealthCheckId.Db07, HealthOutcome.Warning, "OBJECT_FILES_INVALID", HealthCheckTexts.DbObjectFilesInvalid, null, TimeSpan.Zero);
        }

        var byHand = new List<string>();
        var missing = new List<string>();
        var pending = new List<string>();
        foreach (var step in steps)
        {
            if (step.Action == SchemaObjectAction.Retire) pending.Add(step.ObjectName);
            else if (step.Action == SchemaObjectAction.Apply && step.Live is null) missing.Add(step.ObjectName);
            else if (step.Action == SchemaObjectAction.Apply && step.Journal is not null
                && string.Equals(step.Journal.FileSha256, step.FileSha256, StringComparison.OrdinalIgnoreCase)) byHand.Add(step.ObjectName);
            else if (step.Action == SchemaObjectAction.Apply || (step.Action == SchemaObjectAction.Adopt && step.Journal is null)) pending.Add(step.ObjectName);
        }
        var known = new HashSet<string>(files.Objects.Select(file => file.ObjectName).Concat(files.Retired.Select(stub => stub.ObjectName)), StringComparer.OrdinalIgnoreCase);
        var unknown = journal.Select(entry => entry.ObjectName).Where(name => !known.Contains(name)).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        var subs = new List<HealthCheckSubResult>();
        subs.AddRange(byHand.Select(name => new HealthCheckSubResult(HealthCheckTexts.DetailToken(name), HealthOutcome.Warning, "CHANGED_BY_HAND")));
        subs.AddRange(missing.Select(name => new HealthCheckSubResult(HealthCheckTexts.DetailToken(name), HealthOutcome.Warning, "MISSING")));
        subs.AddRange(pending.Select(name => new HealthCheckSubResult(HealthCheckTexts.DetailToken(name), HealthOutcome.Warning, "PENDING")));
        subs.AddRange(unknown.Select(name => new HealthCheckSubResult(HealthCheckTexts.DetailToken(name), HealthOutcome.Warning, "NO_FILE")));
        var detail = subs.Count > 0 ? subs : null;
        decimal metric = files.Objects.Count;

        if (byHand.Count > 0)
            return Line(HealthCheckId.Db07, HealthOutcome.Warning, "CHANGED_BY_HAND", HealthCheckTexts.DbObjectsChangedByHand(byHand.Count, byHand[0]), null, TimeSpan.Zero, metric, detail);
        if (missing.Count > 0)
            return Line(HealthCheckId.Db07, HealthOutcome.Warning, "MISSING", HealthCheckTexts.DbObjectsMissing(missing.Count, missing[0]), null, TimeSpan.Zero, metric, detail);
        if (pending.Count > 0)
            return Line(HealthCheckId.Db07, HealthOutcome.Warning, "PENDING", HealthCheckTexts.DbObjectsPending(pending.Count, pending[0]), null, TimeSpan.Zero, metric, detail);
        if (unknown.Length > 0)
            return Line(HealthCheckId.Db07, HealthOutcome.Warning, "NO_FILE", HealthCheckTexts.DbObjectsUnknown(unknown.Length, unknown[0]), null, TimeSpan.Zero, metric, detail);
        return Line(HealthCheckId.Db07, HealthOutcome.Pass, HealthCheckTexts.Ok, HealthCheckTexts.DbObjectsCurrent(files.Objects.Count), null, TimeSpan.Zero, metric);
    }

    // ---- DB-03 ---------------------------------------------------------------------------------------------

    internal const string IntegritySql = """
        SELECT CONVERT(datetime2(0), DATABASEPROPERTYEX(DB_NAME(), 'LastGoodCheckDbTime')) AS last_good_checkdb,
               d.page_verify_option_desc
        FROM sys.databases AS d
        WHERE d.database_id = DB_ID();
        """;

    internal const string SuspectPagesSql = "SELECT COUNT(*) FROM msdb.dbo.suspect_pages WHERE database_id = DB_ID();";

    internal static async Task<HealthCheckResult> IntegrityAsync(string connectionString, TimeProvider clock, CancellationToken cancellationToken)
    {
        DateTime? lastGood = null;
        var pageVerify = "";
        int? suspect = null;
        await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using (var command = DatabaseProbeSql.Command(connection, IntegritySql))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lastGood = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
                pageVerify = reader.IsDBNull(1) ? "" : reader.GetString(1);
            }
        }
        try
        {
            await using var command = DatabaseProbeSql.Command(connection, SuspectPagesSql);
            suspect = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        catch (SqlException exception) when (DatabaseProbeSql.IsNotPermitted(exception))
        {
            // msdb is readable through guest on a default install; a hardened server may refuse. Not a Fail.
        }
        return EvaluateIntegrity(lastGood, pageVerify, suspect, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
    }

    internal static HealthCheckResult EvaluateIntegrity(DateTime? lastGoodCheckDb, string pageVerify, int? suspectPages, DateOnly today)
    {
        // SQL Server reports 1900-01-01 when no CHECKDB has completed since the database was created or restored.
        DateOnly? lastGood = lastGoodCheckDb is { Year: > 1900 } value ? DateOnly.FromDateTime(value) : null;
        var subs = new List<HealthCheckSubResult>
        {
            new("SUSPECT_PAGES", suspectPages is null ? HealthOutcome.Skipped : suspectPages > 0 ? HealthOutcome.Fail : HealthOutcome.Pass,
                suspectPages > 0 ? "SUSPECT_PAGES" : null, suspectPages),
            new("PAGE_VERIFY", string.Equals(pageVerify, "CHECKSUM", StringComparison.OrdinalIgnoreCase) ? HealthOutcome.Pass : HealthOutcome.Fail,
                string.Equals(pageVerify, "CHECKSUM", StringComparison.OrdinalIgnoreCase) ? null : "PAGE_VERIFY_NOT_CHECKSUM",
                Fields: new Dictionary<string, string> { ["option"] = HealthCheckTexts.SafeToken(pageVerify) }),
        };
        var stale = lastGood is null || lastGood.Value.AddDays(MaximumCheckDbAgeDays) < today;
        subs.Add(new("LAST_GOOD_CHECKDB", stale ? HealthOutcome.Warning : HealthOutcome.Pass, stale ? "CHECKDB_STALE" : null,
            lastGood is null ? null : today.DayNumber - lastGood.Value.DayNumber,
            lastGood is null ? null : new Dictionary<string, string> { ["date"] = lastGood.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));

        if (suspectPages > 0)
            return Line(HealthCheckId.Db03, HealthOutcome.Fail, "SUSPECT_PAGES", HealthCheckTexts.DbSuspectPages(suspectPages.Value), "backups", TimeSpan.Zero, suspectPages, subs);
        if (!string.Equals(pageVerify, "CHECKSUM", StringComparison.OrdinalIgnoreCase))
            return Line(HealthCheckId.Db03, HealthOutcome.Fail, "PAGE_VERIFY_NOT_CHECKSUM", HealthCheckTexts.DbPageVerify(pageVerify.Length == 0 ? "unknown" : pageVerify), null, TimeSpan.Zero, null, subs);
        if (stale)
            return Line(HealthCheckId.Db03, HealthOutcome.Warning, "CHECKDB_STALE", HealthCheckTexts.DbCheckDbStale(lastGood, MaximumCheckDbAgeDays), "recovery", TimeSpan.Zero, null, subs);
        return Line(HealthCheckId.Db03, HealthOutcome.Pass, HealthCheckTexts.Ok, HealthCheckTexts.DbIntegrityOk(lastGood!.Value), null, TimeSpan.Zero, null, subs);
    }

    // ---- DB-04 ---------------------------------------------------------------------------------------------

    internal const string OptionsSql = """
        SELECT d.is_auto_close_on, d.state_desc, d.is_read_only, d.user_access_desc, d.recovery_model_desc
        FROM sys.databases AS d
        WHERE d.database_id = DB_ID();
        """;

    internal sealed record DatabaseOptions(bool AutoClose, string State, bool ReadOnly, string UserAccess, string RecoveryModel);

    internal static async Task<HealthCheckResult> OptionsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DatabaseProbeSql.Command(connection, OptionsSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("sys.databases returned no row for the current database.");
        return EvaluateOptions(new DatabaseOptions(
            reader.GetBoolean(0), reader.GetString(1), reader.GetBoolean(2), reader.GetString(3), reader.GetString(4)));
    }

    internal static HealthCheckResult EvaluateOptions(DatabaseOptions options)
    {
        var fields = new Dictionary<string, string>
        {
            ["state"] = HealthCheckTexts.SafeToken(options.State),
            ["userAccess"] = HealthCheckTexts.SafeToken(options.UserAccess),
            ["recoveryModel"] = HealthCheckTexts.SafeToken(options.RecoveryModel)
        };
        var online = string.Equals(options.State, "ONLINE", StringComparison.OrdinalIgnoreCase);
        var multiUser = string.Equals(options.UserAccess, "MULTI_USER", StringComparison.OrdinalIgnoreCase);
        var subs = new List<HealthCheckSubResult>
        {
            new("STATE", online ? HealthOutcome.Pass : HealthOutcome.Fail, online ? null : "DB_NOT_ONLINE"),
            new("READ_ONLY", options.ReadOnly ? HealthOutcome.Fail : HealthOutcome.Pass, options.ReadOnly ? "DB_READ_ONLY" : null),
            new("USER_ACCESS", multiUser ? HealthOutcome.Pass : HealthOutcome.Fail, multiUser ? null : "DB_USER_ACCESS"),
            new("AUTO_CLOSE", options.AutoClose ? HealthOutcome.Warning : HealthOutcome.Pass, options.AutoClose ? "AUTO_CLOSE_ON" : null, null, fields)
        };
        if (!online)
            return Line(HealthCheckId.Db04, HealthOutcome.Fail, "DB_NOT_ONLINE", HealthCheckTexts.DbNotOnline(options.State), null, TimeSpan.Zero, null, subs);
        if (options.ReadOnly)
            return Line(HealthCheckId.Db04, HealthOutcome.Fail, "DB_READ_ONLY", HealthCheckTexts.DbReadOnly, null, TimeSpan.Zero, null, subs);
        if (!multiUser)
            return Line(HealthCheckId.Db04, HealthOutcome.Fail, "DB_USER_ACCESS", HealthCheckTexts.DbUserAccess(options.UserAccess), null, TimeSpan.Zero, null, subs);
        if (options.AutoClose)
            return Line(HealthCheckId.Db04, HealthOutcome.Warning, "AUTO_CLOSE_ON", HealthCheckTexts.DbAutoCloseOn, null, TimeSpan.Zero, null, subs);
        return Line(HealthCheckId.Db04, HealthOutcome.Pass, HealthCheckTexts.Ok, HealthCheckTexts.DbOptionsOk, null, TimeSpan.Zero, null, subs);
    }

    // ---- DB-05 ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Data files only (the Express limit counts data, not the log; <c>dbo.load_database_operational_health</c> adds
    /// the log, so OPS reads a little more than this).
    /// </summary>
    internal const string SizeSql = """
        SELECT CAST(SUM(CAST(f.size AS bigint)) * 8 / 1024.0 AS decimal(18,2)) AS data_mb,
               CONVERT(int, SERVERPROPERTY('EngineEdition')) AS engine_edition,
               CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) AS major_version
        FROM sys.database_files AS f
        WHERE f.type = 0;
        """;

    internal static async Task<HealthCheckResult> SizeAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DatabaseProbeSql.Command(connection, SizeSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("sys.database_files returned no row.");
        var size = reader.IsDBNull(0) ? 0m : reader.GetDecimal(0);
        var edition = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        var major = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
        return EvaluateSize(size, ExpressLimitMb(edition, major), DatabaseOperationalHealthThresholds.Default.DatabaseSizeWarningPercent);
    }

    /// <summary>Express (engine edition 4): 10 GB per database up to SQL Server 2022, 50 GB from SQL Server 2025 (17).</summary>
    internal static decimal? ExpressLimitMb(int engineEdition, int majorVersion) =>
        engineEdition == 4 ? (majorVersion >= 17 ? 50m * 1024m : 10m * 1024m) : null;

    internal static HealthCheckResult EvaluateSize(decimal sizeMb, decimal? limitMb, decimal warningPercent)
    {
        if (limitMb is not { } limit || limit <= 0)
            return Line(HealthCheckId.Db05, HealthOutcome.Pass, HealthCheckTexts.Ok, HealthCheckTexts.DbSizeOk(sizeMb, null), null, TimeSpan.Zero, sizeMb);
        var percent = sizeMb / limit * 100m;
        if (percent >= DatabaseSizeCriticalPercent)
            return Line(HealthCheckId.Db05, HealthOutcome.Fail, "DB_SIZE_CRITICAL", HealthCheckTexts.DbSizeCritical(sizeMb, limit), "health", TimeSpan.Zero, sizeMb);
        if (percent >= warningPercent || (limit <= 10m * 1024m && sizeMb >= DatabaseSizeWarningMegabytes))
            return Line(HealthCheckId.Db05, HealthOutcome.Warning, "DB_SIZE_HIGH", HealthCheckTexts.DbSizeHigh(sizeMb, limit), "health", TimeSpan.Zero, sizeMb);
        return Line(HealthCheckId.Db05, HealthOutcome.Pass, HealthCheckTexts.Ok, HealthCheckTexts.DbSizeOk(sizeMb, limit), null, TimeSpan.Zero, sizeMb);
    }

    // ---- Shared --------------------------------------------------------------------------------------------

    internal static HealthCheckResult Line(
        string checkId, HealthOutcome outcome, string findingCode, string explanation, string? fixTaskId, TimeSpan duration,
        decimal? metric = null, IReadOnlyList<HealthCheckSubResult>? subResults = null) =>
        new(checkId, HealthCheckId.AreaOf(checkId), outcome, findingCode, explanation, fixTaskId, duration, metric, subResults);

    /// <summary>
    /// One check. A permission refusal becomes Skipped (design 13: never Fail for "could not read"); any other
    /// exception reaches the runner, which records it and writes Fail <c>PROBE_FAILED</c>.
    /// </summary>
    private sealed class Probe : IHealthProbe
    {
        private readonly string checkId;
        private readonly Func<HealthCheckContext, CancellationToken, Task<HealthCheckResult>> run;

        public Probe(string checkId, Func<HealthCheckContext, CancellationToken, Task<HealthCheckResult>> run)
        {
            this.checkId = checkId;
            this.run = run;
            CheckIds = [checkId];
        }

        public IReadOnlyList<string> CheckIds { get; }

        public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            if (checkId != HealthCheckId.Db01 && context.Items.TryGetValue(HealthCheckRunner.DatabaseUnreachableItem, out var unreachable) && unreachable is true)
                return [Line(checkId, HealthOutcome.Skipped, HealthCheckTexts.DatabaseUnreachable, HealthCheckTexts.SkippedDatabaseUnreachable, null, TimeSpan.Zero)];
            try
            {
                var result = await run(context, cancellationToken).ConfigureAwait(false);
                return [result with { Duration = started.Elapsed }];
            }
            catch (SqlException exception) when (DatabaseProbeSql.IsNotPermitted(exception))
            {
                return [Line(checkId, HealthOutcome.Skipped, HealthCheckTexts.NotPermitted, HealthCheckTexts.SkippedNotPermitted, null, started.Elapsed)];
            }
        }
    }
}

/// <summary>Opening a connection and creating a guarded read-only command for the Database and Security probes.</summary>
internal static class DatabaseProbeSql
{
    /// <summary>Seconds per probe query; the runner's budget cancels earlier when the run is short of time.</summary>
    internal const int CommandTimeoutSeconds = 15;

    internal static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
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

    /// <summary>Design 5.4.1: the text is refused before a command exists when it is not read-only.</summary>
    internal static SqlCommand Command(SqlConnection connection, string sql)
    {
        ReadOnlySqlGuard.Require(sql);
        return new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
    }

    /// <summary>229/230 permission denied, 262/297/300 missing right, 916 database not accessible in this context.</summary>
    internal static bool IsNotPermitted(SqlException exception) =>
        DatabaseConnectionFailure.SqlNumbers(exception).Any(number => DatabaseConnectionFailure.Classify(number) == DatabaseFailureKind.PermissionDenied);
}
