using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class MigrationTests
{
    [Fact]
    public void Checksum_is_deterministic_and_sensitive_to_content()
    {
        Assert.Equal(MigrationChecksum.Compute("SELECT 1"), MigrationChecksum.Compute("SELECT 1"));
        Assert.NotEqual(MigrationChecksum.Compute("SELECT 1"), MigrationChecksum.Compute("SELECT 2"));
        Assert.Equal(64, MigrationChecksum.Compute("SELECT 1").Length);
    }

    // A migration's id is its whole file name, so two files with the same number both run, in
    // file-name order. 1.9.3 nearly shipped 0038_import_engine_fixes beside Phase 7's
    // 0038_tally_transfer_foundation: a database already at 0040 would have applied it out of
    // order. Every shipped migration has its own 4-digit number, and they run 0001, 0002, ...
    // with no gap.
    [Fact]
    public async Task Shipped_migrations_have_unique_contiguous_four_digit_numbers_from_0001()
    {
        var ids = (await new DirectoryMigrationSource(ShippedMigrationsDirectory()).DiscoverAsync()).Select(x => x.Id).ToArray();
        Assert.NotEmpty(ids);
        var problems = NumberingProblems(ids);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Numbering_guard_reports_a_shared_number_a_gap_and_an_unnumbered_file()
    {
        Assert.Empty(NumberingProblems(["0002_b", "0001_a"]));
        Assert.Contains("Migrations sharing a number: 0002_import and 0002_tally", NumberingProblems(["0001_a", "0002_import", "0002_tally"]));
        Assert.Contains("Migration numbers must run from 0001 without a gap; found 0001, 0003", NumberingProblems(["0001_a", "0003_c"]));
        Assert.Contains("Migration numbers must run from 0001 without a gap; found 0002", NumberingProblems(["0002_b"]));
        Assert.Contains("Migrations without a 4-digit number and '_': 38_import", NumberingProblems(["0001_a", "38_import"]));
    }

    private static List<string> NumberingProblems(IReadOnlyList<string> ids)
    {
        var problems = new List<string>();
        var unnumbered = ids.Where(id => !System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9]{4}_.+$")).ToArray();
        if (unnumbered.Length > 0) problems.Add("Migrations without a 4-digit number and '_': " + string.Join(", ", unnumbered));
        var numbered = ids.Except(unnumbered).ToArray();
        foreach (var group in numbered.GroupBy(id => id[..4], StringComparer.Ordinal).Where(group => group.Count() > 1).OrderBy(group => group.Key, StringComparer.Ordinal))
            problems.Add("Migrations sharing a number: " + string.Join(" and ", group.Order(StringComparer.Ordinal)));
        var numbers = numbered.Select(id => int.Parse(id[..4], System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray();
        if (!numbers.SequenceEqual(Enumerable.Range(1, numbers.Length)))
            problems.Add("Migration numbers must run from 0001 without a gap; found "
                + string.Join(", ", numbers.Distinct().Select(n => n.ToString("0000", System.Globalization.CultureInfo.InvariantCulture))));
        return problems;
    }

    private static string ShippedMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var migrations = Path.Combine(dir.FullName, "database", "migrations");
        Assert.True(Directory.Exists(migrations), "database/migrations not found at " + migrations);
        return migrations;
    }

    [Fact]
    public void Planner_returns_only_pending_scripts_in_id_order()
    {
        var one = Script("0001", "a");
        var two = Script("0002", "b");
        var plan = MigrationPlanner.Plan([two, one], [new(one.Id, one.Checksum, DateTimeOffset.UtcNow)]);
        Assert.Equal(["0002"], plan.Select(x => x.Id));
    }

    [Fact]
    public void Planner_rejects_changed_applied_script()
    {
        var migration = Script("0001", "new");
        var error = Assert.Throws<MigrationIntegrityException>(() => MigrationPlanner.Plan([migration], [new("0001", MigrationChecksum.Compute("old"), DateTimeOffset.UtcNow)]));
        Assert.Contains("Checksum mismatch", error.Message);
    }

    [Fact]
    public void Planner_rejects_missing_applied_script()
    {
        Assert.Throws<MigrationIntegrityException>(() => MigrationPlanner.Plan([], [new("0001", "abc", DateTimeOffset.UtcNow)]));
        // An unknown id older than the newest known one is a missing (for example renamed) script.
        var two = Script("0002_b", "b");
        var missing = Assert.Throws<MigrationIntegrityException>(() => MigrationPlanner.Plan([two], [new("0001_renamed", "abc", DateTimeOffset.UtcNow)]));
        Assert.Contains("'0001_renamed' is missing from the migration source", missing.Message, StringComparison.Ordinal);
    }

    // An older release opening a database a newer one upgraded says so, instead of "missing".
    [Fact]
    public void Planner_names_a_database_upgraded_by_a_newer_release()
    {
        var one = Script("0001_a", "a");
        var error = Assert.Throws<MigrationIntegrityException>(() => MigrationPlanner.Plan([one],
            [new(one.Id, one.Checksum, DateTimeOffset.UtcNow), new("0002_b", "abc", DateTimeOffset.UtcNow), new("0003_c", "abc", DateTimeOffset.UtcNow)]));

        Assert.Contains("upgraded by a newer release of ETP", error.Message, StringComparison.Ordinal);
        Assert.Contains("'0003_c'", error.Message, StringComparison.Ordinal);
        Assert.Contains("up to '0001_a'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", error.Message, StringComparison.Ordinal);
        Assert.Contains("restore the backup taken before that upgrade", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_applies_pending_migrations_once()
    {
        var scripts = new[] { Script("0001", "one"), Script("0002", "two") };
        var store = new MemoryStore();
        var runner = new MigrationRunner(new MemorySource(scripts), store);
        Assert.Equal(["0001", "0002"], await runner.RunAsync());
        Assert.Empty(await runner.RunAsync());
        Assert.Equal(2, store.Applied.Count);
    }

    [Fact]
    public async Task Runner_holds_exclusive_lock_across_discovery_planning_and_all_commits()
    {
        var events = new List<string>();
        var scripts = new[] { Script("0001", "one"), Script("0002", "two") };
        var store = new RecordingStore(events);
        var runner = new MigrationRunner(new RecordingSource(scripts, events), store);

        Assert.Equal(["0001", "0002"], await runner.RunAsync());

        Assert.Equal([
            "lock-acquired",
            "discover",
            "get-applied",
            "precheck-0001,0002",
            "apply-0001",
            "apply-0002",
            "lock-released",
        ], events);
    }

    // Review 1.9.3 finding 2: each script commits on its own, so 0041's pre-check refusal used to
    // come after Tally 0038-0040 had committed, leaving a database neither 1.9.2 nor 1.9.3 opens.
    [Fact]
    public async Task Runner_refuses_before_applying_anything_when_a_later_pending_precheck_refuses()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { FailOnPrecheck = true };
        var runner = new MigrationRunner(new RecordingSource([Script("0001", "one"), Script("0002", "two")], events), store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal(["lock-acquired", "discover", "get-applied", "precheck-0001,0002", "lock-released"], events);
    }

    [Fact]
    public async Task Runner_runs_no_precheck_when_nothing_is_pending()
    {
        var store = new MemoryStore();
        var runner = new MigrationRunner(new MemorySource([Script("0001", "one")]), store);
        await runner.RunAsync();
        Assert.Equal(["0001"], store.Prechecks);
        store.Prechecks.Clear();

        Assert.Empty(await runner.RunAsync());
        Assert.Empty(store.Prechecks);
    }

    [Fact]
    public void Prechecks_are_the_named_precheck_sections_in_script_order()
    {
        const string sql = "SET XACT_ABORT ON;\r\n-- >>> PRECHECK_A begin\r\nIF 1=0 THROW 50001,'a',1;\r\n-- <<< PRECHECK_A end\r\n"
            + "-- >>> B_CHANGE begin\nALTER TABLE x ADD y int;\n-- <<< B_CHANGE end\n"
            + "-- >>> PRECHECK_C begin\nIF 1=0 THROW 50002,'c',1;\n-- <<< PRECHECK_C end\n";

        Assert.Equal(["IF 1=0 THROW 50001,'a',1;", "IF 1=0 THROW 50002,'c',1;"], MigrationPrechecks.Extract(sql));
        Assert.Empty(MigrationPrechecks.Extract("SELECT 1; -- >>> PRECHECK_X begin is not a marker line"));
        var error = Assert.Throws<MigrationIntegrityException>(() => MigrationPrechecks.Extract("-- >>> PRECHECK_X begin\nSELECT 1;\n-- <<< PRECHECK_Y end\n"));
        Assert.Contains("PRECHECK_X", error.Message, StringComparison.Ordinal);
    }

    // The runner runs pre-checks against the schema from before every pending migration (a new
    // database has no tables at all), so each one only reads and guards each table it reads.
    [Fact]
    public async Task Shipped_prechecks_only_read_and_guard_every_table_they_read()
    {
        var scripts = await new DirectoryMigrationSource(ShippedMigrationsDirectory()).DiscoverAsync();
        var prechecks = scripts.SelectMany(script => MigrationPrechecks.Extract(script.Sql).Select(sql => (script.Id, Sql: sql))).ToArray();
        var import = prechecks.Where(check => check.Id.StartsWith("0041_", StringComparison.Ordinal)).Select(check => check.Sql).ToArray();
        Assert.Equal(2, import.Length);
        foreach (var number in new[] { "51700", "51701", "51702" })
            Assert.Contains(import, sql => sql.Contains("THROW " + number, StringComparison.Ordinal));
        foreach (var (id, sql) in prechecks)
        {
            var code = string.Join('\n', sql.Split('\n').Select(line => line.Split("--")[0]));
            Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE|CREATE|ALTER|DROP|GRANT|DENY|REVOKE|EXEC|EXECUTE|DISABLE|ENABLE)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase), code);
            foreach (System.Text.RegularExpressions.Match table in System.Text.RegularExpressions.Regex.Matches(code, @"\bFROM\s+(dbo\.\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                Assert.True(code.Contains($"OBJECT_ID(N'{table.Groups[1].Value}',N'U') IS NOT NULL", StringComparison.OrdinalIgnoreCase),
                    $"{id}: the pre-check reads {table.Groups[1].Value} without an OBJECT_ID guard.");
        }
    }

    [Fact]
    public async Task Runner_releases_migration_lock_when_a_commit_fails()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { FailOnApply = true };
        var runner = new MigrationRunner(new RecordingSource([Script("0001", "one")], events), store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal("lock-released", events[^1]);
        Assert.Equal(["lock-acquired", "discover", "get-applied", "precheck-0001", "apply-0001", "lock-released"], events);
    }

    [Fact]
    public async Task Runner_reports_explicit_lock_release_failure_after_successful_migrations()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { FailOnRelease = true };
        var runner = new MigrationRunner(new RecordingSource([Script("0001", "one")], events), store);

        var error = await Assert.ThrowsAsync<MigrationIntegrityException>(() => runner.RunAsync());

        Assert.Contains("Synthetic lock release failure", error.Message, StringComparison.Ordinal);
        Assert.Equal(["lock-acquired", "discover", "get-applied", "precheck-0001", "apply-0001", "lock-released"], events);
    }

    [Fact]
    public async Task Runner_aggregates_migration_and_lock_release_failures_without_masking_either()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { FailOnApply = true, FailOnRelease = true };
        var runner = new MigrationRunner(new RecordingSource([Script("0001", "one")], events), store);

        var error = await Assert.ThrowsAsync<AggregateException>(() => runner.RunAsync());

        Assert.Collection(error.InnerExceptions,
            migration => Assert.Contains("Synthetic migration failure", migration.Message, StringComparison.Ordinal),
            release => Assert.Contains("Synthetic lock release failure", release.Message, StringComparison.Ordinal));
        Assert.Equal("lock-released", events[^1]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    [InlineData(-999)]
    public void Sql_store_fails_closed_when_application_lock_is_not_acquired(int result)
    {
        var error = Assert.Throws<MigrationIntegrityException>(() => SqlServerMigrationStore.EnsureMigrationLockAcquired(result));

        Assert.Contains("migration was not started", error.Message, StringComparison.Ordinal);
        Assert.Contains(result.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Sql_store_accepts_only_successful_application_lock_results(int result)
    {
        SqlServerMigrationStore.EnsureMigrationLockAcquired(result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-999)]
    public void Sql_store_fails_closed_when_explicit_lock_release_is_not_confirmed(int result)
    {
        var error = Assert.Throws<MigrationIntegrityException>(() => SqlServerMigrationStore.EnsureMigrationLockReleased(result));

        Assert.Contains("connection pool was invalidated", error.Message, StringComparison.Ordinal);
        Assert.Contains(result.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Sql_store_accepts_nonnegative_explicit_lock_release_results(int result)
    {
        SqlServerMigrationStore.EnsureMigrationLockReleased(result);
    }

    [Fact]
    public async Task Empty_connection_string_is_invalid_without_network_access()
    {
        var result = await new SqlServerHealthCheck(" ").CheckAsync();
        Assert.Equal(DatabaseHealthStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public async Task Malformed_connection_string_does_not_expose_provider_exception_text()
    {
        var result = await new SqlServerHealthCheck("NotAKeyword=secret-value").CheckAsync();

        Assert.Equal(DatabaseHealthStatus.InvalidConfiguration, result.Status);
        Assert.Equal("The SQL Server connection settings are invalid.", result.Message);
        Assert.DoesNotContain("NotAKeyword", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_rejects_unsafe_or_missing_database_names_without_network_access()
    {
        Assert.Throws<ArgumentException>(() => SqlServerDatabaseBootstrapper.ValidateDatabaseName("reporting]; DROP DATABASE master;--"));
        Assert.Throws<ArgumentException>(() => SqlServerDatabaseBootstrapper.ValidateDatabaseName(" "));
        Assert.Equal("Etp_Reporting-01", SqlServerDatabaseBootstrapper.ValidateDatabaseName("Etp_Reporting-01"));
    }

    [Fact]
    public void Automation_paths_require_distinct_local_non_root_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpAutomationPolicy");
        var settings = AutomationPathPolicy.Validate(Path.Combine(root, "Inbound"), Path.Combine(root, "Processed"),
            Path.Combine(root, "Failed"), Path.Combine(root, "Reports"));
        Assert.EndsWith(Path.Combine("EtpAutomationPolicy", "Inbound"), settings.InboundPath, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => AutomationPathPolicy.Validate(root, Path.Combine(root, "Processed"), Path.Combine(root, "Failed"), Path.Combine(root, "Processed")));
        Assert.Throws<ArgumentException>(() => AutomationPathPolicy.Validate(root, Path.Combine(root, "inside"), Path.Combine(Path.GetTempPath(), "failed"), Path.Combine(Path.GetTempPath(), "reports")));
        Assert.Throws<ArgumentException>(() => AutomationPathPolicy.Validate(Path.GetPathRoot(root)!, Path.Combine(root, "Processed"), Path.Combine(root, "Failed"), Path.Combine(root, "Reports")));
    }

    [Fact]
    public void Import_package_keeps_movement_and_snapshot_facts_separate()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(batch.BatchId, Etp.Reporting.Import.Profiles.RetailSalesProfiles.R025.Identity, "sample.xlsx", new string('a', 64), 1);
        var package = new ImportPersistencePackage(batch, file, [], [], [], []);
        Assert.Empty(package.StockMovements);
        Assert.Empty(package.StockSnapshots);
        Assert.Equal(batch.BatchId, package.File.BatchId);
    }

    [Fact]
    public void Persistence_validation_rejects_cross_batch_files_before_database_access()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(Guid.NewGuid(), Etp.Reporting.Import.Profiles.RetailSalesProfiles.R025.Identity, "sample.xlsx", new string('a', 64), 1);
        var package = new ImportPersistencePackage(batch, file, [], [], [], []);
        Assert.Throws<ArgumentException>(() => PersistenceValidation.Validate(package));
    }

    [Fact]
    public void Persistence_validation_requires_complete_restatement_authority()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(batch.BatchId, Etp.Reporting.Import.Profiles.RetailSalesProfiles.R025.Identity, "replacement.xlsx", new string('a', 64), 1);
        var invalid = new ImportPersistencePackage(batch, file, [], [], [], [])
        {
            Restatement = new(0, "", "")
        };
        Assert.Throws<ArgumentException>(() => PersistenceValidation.Validate(invalid));

        var valid = invalid with { Restatement = new ImportRestatementRequest(12, "admin", "Corrected ETP export") };
        PersistenceValidation.Validate(valid);
    }

    private static MigrationScript Script(string id, string sql) => new(id, MigrationChecksum.Compute(sql), sql, $"{id}.sql");

    private static HashSet<string> OperationalAuditEventTypes(string sql)
    {
        const string constraintMarker = "CK_operational_audit_type CHECK";
        var constraintStart = sql.LastIndexOf(constraintMarker, StringComparison.Ordinal);
        Assert.True(constraintStart >= 0);
        var constraintEnd = sql.IndexOf("));", constraintStart, StringComparison.Ordinal);
        Assert.True(constraintEnd > constraintStart);
        var constraint = sql[constraintStart..constraintEnd];
        return System.Text.RegularExpressions.Regex.Matches(constraint, "'([^']+)'")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed class MemorySource(IReadOnlyList<MigrationScript> scripts) : IMigrationSource
    {
        public Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(scripts);
    }
    private sealed class MemoryStore : IMigrationStore
    {
        public List<AppliedMigration> Applied { get; } = [];
        public Task<IAsyncDisposable> AcquireMigrationLockAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IAsyncDisposable>(NoOpAsyncDisposable.Instance);
        public List<string> Prechecks { get; } = [];
        public Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppliedMigration>>(Applied);
        public Task PrecheckAsync(IReadOnlyList<MigrationScript> plan, CancellationToken cancellationToken = default)
        {
            Prechecks.AddRange(plan.Select(migration => migration.Id));
            return Task.CompletedTask;
        }
        public Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default)
        {
            Applied.Add(new(migration.Id, migration.Checksum, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSource(IReadOnlyList<MigrationScript> scripts, List<string> events) : IMigrationSource
    {
        public Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            events.Add("discover");
            return Task.FromResult(scripts);
        }
    }

    private sealed class RecordingStore(List<string> events) : IMigrationStore
    {
        public bool FailOnApply { get; init; }
        public bool FailOnPrecheck { get; init; }
        public bool FailOnRelease { get; init; }

        public Task PrecheckAsync(IReadOnlyList<MigrationScript> plan, CancellationToken cancellationToken = default)
        {
            events.Add("precheck-" + string.Join(',', plan.Select(migration => migration.Id)));
            return FailOnPrecheck
                ? Task.FromException(new InvalidOperationException("Synthetic pre-check refusal."))
                : Task.CompletedTask;
        }

        public Task<IAsyncDisposable> AcquireMigrationLockAsync(CancellationToken cancellationToken = default)
        {
            events.Add("lock-acquired");
            return Task.FromResult<IAsyncDisposable>(new RecordingLock(events, FailOnRelease));
        }

        public Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken cancellationToken = default)
        {
            events.Add("get-applied");
            return Task.FromResult<IReadOnlyList<AppliedMigration>>([]);
        }

        public Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default)
        {
            events.Add($"apply-{migration.Id}");
            return FailOnApply
                ? Task.FromException(new InvalidOperationException("Synthetic migration failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingLock(List<string> events, bool failOnRelease) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            events.Add("lock-released");
            return failOnRelease
                ? ValueTask.FromException(new MigrationIntegrityException("Synthetic lock release failure."))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
