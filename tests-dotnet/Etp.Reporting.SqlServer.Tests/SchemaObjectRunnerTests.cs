using System.Text.Json;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 A3: object files and the runner's planning, without a database. Parsing (the frozen file format, including the
/// D2 style with header comments and DENY rows), hashing of a GO-split body, adopt normalisation, skip / adopt / apply /
/// retire / excluded, ordering, the runner's lock and journal handling, DB-07's classification and the plan verb's JSON.
/// </summary>
public sealed class SchemaObjectRunnerTests
{
    private const string ViewFile = """
        -- 1.12 lane D2 style: comments above the CREATE are part of the stored definition.
        -- A second comment line.
        CREATE OR ALTER VIEW dbo.v_example AS
        SELECT s.store_code FROM dbo.stores s;
        -- >>> GRANTS
        GRANT SELECT ON dbo.v_example TO etp_viewer,etp_store_manager,etp_owner;
        DENY INSERT,UPDATE,DELETE ON dbo.v_example TO etp_store_manager,etp_viewer;
        """;

    // ---- File format -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_in_the_frozen_format_parses_with_its_layer_kind_name_batches_and_grants()
    {
        var file = SchemaObjectFiles.Parse("20-dbo.v_example.sql", ViewFile);

        Assert.Equal((20, "dbo", "v_example", SchemaObjectKind.View, "dbo.v_example"), (file.Layer, file.Schema, file.Name, file.Kind, file.ObjectName));
        Assert.Single(file.BodyBatches);
        Assert.StartsWith("-- 1.12 lane D2 style", file.DefinitionBatch.TrimStart(), StringComparison.Ordinal);
        Assert.Equal(2, file.GrantStatements.Count);
        Assert.Equal(64, file.FileSha256.Length);
    }

    [Fact]
    public void Grants_rows_cover_grant_and_deny_per_role_and_permission()
    {
        var rows = SchemaObjectFiles.PermissionRows(SchemaObjectFiles.Parse("20-dbo.v_example.sql", ViewFile));

        Assert.Equal(9, rows.Count);
        Assert.Contains(("etp_viewer", "dbo", "v_example", "SELECT", "GRANT"), rows);
        Assert.Contains(("etp_store_manager", "dbo", "v_example", "DELETE", "DENY"), rows);
        var column = SchemaObjectFiles.PermissionRows(SchemaObjectFiles.Parse("20-dbo.v_example.sql",
            "CREATE OR ALTER VIEW dbo.v_example AS SELECT 1 AS a\n-- >>> GRANTS\nDENY SELECT ON dbo.v_example (a) TO etp_viewer;\nGRANT EXECUTE ON OBJECT::dbo.v_example TO [etp_owner] WITH GRANT OPTION;\nREVOKE SELECT ON dbo.v_example FROM etp_store_manager;\n"));
        Assert.Equal(new[] { ("etp_viewer", "dbo", "v_example(a)", "SELECT", "DENY"), ("etp_owner", "dbo", "v_example", "EXECUTE", "GRANT_WITH_GRANT_OPTION") }, column);
    }

    [Theory]
    [InlineData("2-dbo.v_example.sql", "is not named")]
    [InlineData("20-v_example.sql", "is not named")]
    [InlineData("50-dbo.v_example.sql", "layer 50")]
    [InlineData("40-dbo.v_example.sql", "layer 40")]
    [InlineData("20-dbo.v_other.sql", "creates dbo.v_example, not dbo.v_other")]
    public void A_misnamed_or_mislayered_file_is_refused(string fileName, string message)
    {
        var error = Assert.Throws<SchemaObjectIntegrityException>(() => SchemaObjectFiles.Parse(fileName, ViewFile));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CREATE VIEW dbo.v_example AS SELECT 1 AS a\n-- >>> GRANTS\n", "CREATE OR ALTER")]
    [InlineData("ALTER VIEW dbo.v_example AS SELECT 1 AS a\n-- >>> GRANTS\n", "CREATE OR ALTER")]
    [InlineData("CREATE OR ALTER VIEW dbo.v_example AS SELECT 1 AS a\n", "exactly one line")]
    [InlineData("CREATE OR ALTER VIEW dbo.v_example AS SELECT 1 AS a\n-- >>> GRANTS\n-- >>> GRANTS\n", "exactly one line")]
    [InlineData("SELECT 1;\n-- >>> GRANTS\n", "has no CREATE OR ALTER")]
    [InlineData("CREATE OR ALTER VIEW dbo.v_example AS SELECT 1 AS a\n-- >>> GRANTS\nGRANT SELECT ON dbo.stores TO etp_viewer;\n", "only one-line GRANT")]
    [InlineData("CREATE OR ALTER VIEW dbo.v_example AS SELECT 1 AS a\n-- >>> GRANTS\nDROP TABLE dbo.stores;\n", "only one-line GRANT")]
    public void A_file_outside_the_format_is_refused(string text, string message)
    {
        var error = Assert.Throws<SchemaObjectIntegrityException>(() => SchemaObjectFiles.Parse("20-dbo.v_example.sql", text));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Layers_allow_functions_views_and_procedures_where_the_plan_puts_them()
    {
        Assert.Equal(SchemaObjectKind.Function, SchemaObjectFiles.Parse("10-dbo.f.sql", "CREATE OR ALTER FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END\n-- >>> GRANTS\n").Kind);
        Assert.Equal(SchemaObjectKind.Function, SchemaObjectFiles.Parse("31-diag.f.sql", "CREATE OR ALTER FUNCTION diag.f() RETURNS TABLE AS RETURN SELECT 1 AS a\n-- >>> GRANTS\n").Kind);
        Assert.Equal(SchemaObjectKind.View, SchemaObjectFiles.Parse("30-dbo.v.sql", "CREATE OR ALTER VIEW dbo.v AS SELECT 1 AS a\n-- >>> GRANTS\n").Kind);
        Assert.Equal(SchemaObjectKind.Procedure, SchemaObjectFiles.Parse("40-dbo.p.sql", "CREATE OR ALTER PROC [dbo].[p] AS SELECT 1\n-- >>> GRANTS\nGRANT EXECUTE ON [dbo].[p] TO etp_owner\n").Kind);
    }

    [Fact]
    public void A_GO_split_body_keeps_its_batches_and_its_hash_ignores_line_endings_only()
    {
        const string lf = "-- set-up\nSET NOCOUNT ON;\nGO\nCREATE OR ALTER PROCEDURE dbo.p AS\nSELECT 1;\n  go  \n-- >>> GRANTS\nGRANT EXECUTE ON dbo.p TO etp_owner;\n";
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        var file = SchemaObjectFiles.Parse("40-dbo.p.sql", lf);
        Assert.Equal(2, file.BodyBatches.Count);
        Assert.Equal(1, file.DefinitionBatchIndex);
        Assert.StartsWith("CREATE OR ALTER PROCEDURE dbo.p", file.DefinitionBatch.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("GO", file.DefinitionBatch, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(file.FileSha256, SchemaObjectFiles.Parse("40-dbo.p.sql", crlf).FileSha256);
        Assert.NotEqual(file.FileSha256, SchemaObjectFiles.Parse("40-dbo.p.sql", lf.Replace("SELECT 1;", "SELECT 1 ;", StringComparison.Ordinal)).FileSha256);
        Assert.Equal(MigrationChecksum.Compute(lf), file.FileSha256);
    }

    [Fact]
    public void Adopt_normalisation_ignores_only_the_create_form_proc_spelling_line_endings_and_outer_white_space()
    {
        var file = "\r\n-- note\r\nCREATE OR ALTER VIEW dbo.v AS\r\nSELECT 1 AS a\r\n";
        // SQL Server stores CREATE OR ALTER as "CREATE" followed by the spaces of "OR ALTER".
        Assert.Equal(SchemaObjectFiles.NormaliseDefinition(file), SchemaObjectFiles.NormaliseDefinition("-- note\nCREATE   VIEW dbo.v AS\nSELECT 1 AS a"));
        Assert.Equal(SchemaObjectFiles.NormaliseDefinition("CREATE PROC dbo.p AS SELECT 1"), SchemaObjectFiles.NormaliseDefinition("CREATE OR ALTER PROCEDURE dbo.p AS SELECT 1"));
        Assert.NotEqual(SchemaObjectFiles.NormaliseDefinition(file), SchemaObjectFiles.NormaliseDefinition("-- note changed\nCREATE VIEW dbo.v AS\nSELECT 1 AS a"));
        Assert.NotEqual(SchemaObjectFiles.NormaliseDefinition(file), SchemaObjectFiles.NormaliseDefinition("-- note\nCREATE VIEW dbo.v AS\nSELECT 1  AS a"));
    }

    [Fact]
    public void A_retired_stub_holds_comments_only()
    {
        Assert.Equal("dbo.v_old", SchemaObjectFiles.ParseRetired("dbo.v_old.sql", "-- Retired in 1.12: replaced by dbo.v_new.\n").ObjectName);
        Assert.Throws<SchemaObjectIntegrityException>(() => SchemaObjectFiles.ParseRetired("dbo.v_old.sql", "DROP VIEW dbo.v_old;"));
        Assert.Throws<SchemaObjectIntegrityException>(() => SchemaObjectFiles.ParseRetired("20-dbo.v_old.sql", "-- x"));
    }

    [Fact]
    public void The_objects_folder_is_next_to_the_migrations_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "etp-a3", "database");
        Assert.Equal(Path.Combine(root, "objects"), SchemaObjectFiles.ObjectsDirectoryFor(Path.Combine(root, "migrations")));
        Assert.Equal(Path.Combine(root, "objects"), SchemaObjectFiles.ObjectsDirectoryFor(Path.Combine(root, "migrations") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void The_bootstrapper_takes_objects_from_beside_a_directory_migration_source_only()
    {
        var migrations = Path.Combine(Path.GetTempPath(), "etp-a3", "database", "migrations");
        var derived = Assert.IsType<DirectorySchemaObjectSource>(SqlServerDatabaseBootstrapper.ObjectSourceFor(new DirectoryMigrationSource(migrations), null));
        Assert.Equal(SchemaObjectFiles.ObjectsDirectoryFor(migrations), derived.DirectoryPath);
        Assert.Same(SchemaObjectSource.None, SqlServerDatabaseBootstrapper.ObjectSourceFor(new DirectoryMigrationSource(migrations), SchemaObjectSource.None));
        Assert.Same(SchemaObjectSource.None, SqlServerDatabaseBootstrapper.ObjectSourceFor(new ListMigrationSource(), null));
    }

    [Fact]
    public async Task A_missing_objects_folder_is_an_empty_set()
    {
        var set = await new DirectorySchemaObjectSource(Path.Combine(Path.GetTempPath(), "etp-a3-" + Guid.NewGuid().ToString("N"))).DiscoverAsync();
        Assert.True(set.IsEmpty);
    }

    // ---- Planning ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_journaled_object_with_its_recorded_definition_is_skipped()
    {
        var file = View("v_a", "SELECT 1 AS a");
        var live = Live(file, "CREATE   VIEW dbo.v_a AS SELECT 1 AS a");
        var step = Single(Set(file), [Journal(file, live)], [live]);
        Assert.Equal(SchemaObjectAction.Skip, step.Action);
    }

    [Fact]
    public void An_unjournaled_object_equal_to_its_file_is_adopted_and_one_that_differs_is_applied()
    {
        var file = View("v_a", "SELECT 1 AS a");
        Assert.Equal(SchemaObjectAction.Adopt, Single(Set(file), [], [Live(file, "CREATE   VIEW dbo.v_a AS SELECT 1 AS a\r\n")]).Action);
        Assert.Equal(SchemaObjectAction.Apply, Single(Set(file), [], [Live(file, "CREATE VIEW dbo.v_a AS SELECT 2 AS a")]).Action);
        Assert.Equal(SchemaObjectAction.Apply, Single(Set(file), [], []).Action);
    }

    [Fact]
    public void A_changed_file_is_applied_even_when_only_its_grants_changed()
    {
        var old = View("v_a", "SELECT 1 AS a");
        var live = Live(old, "CREATE VIEW dbo.v_a AS SELECT 1 AS a");
        var regranted = View("v_a", "SELECT 1 AS a", "GRANT SELECT ON dbo.v_a TO etp_owner;");
        Assert.Equal(SchemaObjectAction.Apply, Single(Set(regranted), [Journal(old, live)], [live]).Action);
    }

    [Fact]
    public void A_hand_edited_object_is_applied_again_and_a_cosmetic_recreate_is_re_adopted()
    {
        var file = View("v_a", "SELECT 1 AS a");
        var recorded = Journal(file, Live(file, "CREATE   VIEW dbo.v_a AS SELECT 1 AS a"));
        Assert.Equal(SchemaObjectAction.Apply, Single(Set(file), [recorded], [Live(file, "CREATE VIEW dbo.v_a AS SELECT 99 AS a")]).Action);
        Assert.Equal(SchemaObjectAction.Adopt, Single(Set(file), [recorded], [Live(file, "CREATE VIEW dbo.v_a AS SELECT 1 AS a")]).Action);
    }

    [Fact]
    public void A_retired_stub_retires_an_object_that_exists_or_is_journaled_and_skips_one_that_is_gone()
    {
        var stub = SchemaObjectFiles.ParseRetired("dbo.v_old.sql", "-- retired");
        var set = new SchemaObjectSet([], [stub]);
        Assert.Equal(SchemaObjectAction.Retire, SchemaObjectPlanner.Plan(set, [], [new LiveSchemaObject("dbo.v_old", "V", "CREATE VIEW dbo.v_old AS SELECT 1 AS a")]).Single().Action);
        Assert.Equal(SchemaObjectAction.Retire, SchemaObjectPlanner.Plan(set, [new SchemaObjectJournalEntry("dbo.v_old", "V", new string('a', 64), new string('b', 64))], []).Single().Action);
        Assert.Equal(SchemaObjectAction.Skip, SchemaObjectPlanner.Plan(set, [], []).Single().Action);
    }

    [Theory]
    [InlineData("a constraint uses it")]
    [InlineData("a security policy uses it")]
    [InlineData("a schema-bound object uses it")]
    public void An_excluded_object_is_refused_with_its_reason(string pinnedBy)
    {
        var file = View("v_a", "SELECT 1 AS a");
        var error = Assert.Throws<SchemaObjectIntegrityException>(() =>
            SchemaObjectPlanner.Plan(Set(file), [], [new LiveSchemaObject("dbo.v_a", "V", "CREATE VIEW dbo.v_a AS SELECT 1 AS a", pinnedBy)]));
        Assert.Contains(pinnedBy, error.Message, StringComparison.Ordinal);
        Assert.Contains("EXCLUDED.md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_files_for_one_object_a_file_beside_a_stub_and_a_type_change_are_refused()
    {
        var view = View("v_a", "SELECT 1 AS a");
        var again = SchemaObjectFiles.Parse("30-dbo.v_a.sql", "CREATE OR ALTER VIEW dbo.v_a AS SELECT 2 AS a\n-- >>> GRANTS\n");
        Assert.Throws<SchemaObjectIntegrityException>(() => SchemaObjectPlanner.Plan(Set(view, again), [], []));
        Assert.Throws<SchemaObjectIntegrityException>(() => SchemaObjectPlanner.Plan(new SchemaObjectSet([view], [SchemaObjectFiles.ParseRetired("dbo.v_a.sql", "-- x")]), [], []));
        Assert.Throws<SchemaObjectIntegrityException>(() => SchemaObjectPlanner.Plan(Set(view), [], [new LiveSchemaObject("dbo.v_a", "P", "CREATE PROCEDURE dbo.v_a AS SELECT 1")]));
    }

    [Fact]
    public void Retired_stubs_come_first_then_files_in_layer_then_name_order()
    {
        var procedure = SchemaObjectFiles.Parse("40-dbo.a_proc.sql", "CREATE OR ALTER PROCEDURE dbo.a_proc AS SELECT 1\n-- >>> GRANTS\n");
        var onViews = SchemaObjectFiles.Parse("30-dbo.a_view.sql", "CREATE OR ALTER VIEW dbo.a_view AS SELECT * FROM dbo.z_view\n-- >>> GRANTS\n");
        var onTables = SchemaObjectFiles.Parse("20-dbo.z_view.sql", "CREATE OR ALTER VIEW dbo.z_view AS SELECT 1 AS a\n-- >>> GRANTS\n");
        var function = SchemaObjectFiles.Parse("10-dbo.z_fn.sql", "CREATE OR ALTER FUNCTION dbo.z_fn() RETURNS int AS BEGIN RETURN 1 END\n-- >>> GRANTS\n");
        var stub = SchemaObjectFiles.ParseRetired("dbo.old.sql", "-- x");

        var steps = SchemaObjectPlanner.Plan(new SchemaObjectSet([procedure, onViews, onTables, function], [stub]), [], [new LiveSchemaObject("dbo.old", "V", "CREATE VIEW dbo.old AS SELECT 1 AS a")]);

        Assert.Equal(new[] { "dbo.old", "dbo.z_fn", "dbo.z_view", "dbo.a_view", "dbo.a_proc" }, steps.Select(step => step.ObjectName));
    }

    // ---- Runner (fake store) -----------------------------------------------------------------------------------------

    [Fact]
    public async Task No_object_files_means_no_lock_and_no_database_work()
    {
        var store = new FakeStore();
        var result = await new SchemaObjectRunner(SchemaObjectSource.None, store).RunAsync();
        Assert.Same(SchemaObjectRunResult.None, result);
        Assert.Equal(0, store.LocksTaken);
    }

    [Fact]
    public async Task The_runner_sends_only_the_steps_that_change_something_and_releases_the_lock()
    {
        var same = View("v_same", "SELECT 1 AS a");
        var adopt = View("v_adopt", "SELECT 2 AS a");
        var change = View("v_change", "SELECT 3 AS a");
        var sameLive = Live(same, "CREATE   VIEW dbo.v_same AS SELECT 1 AS a");
        var store = new FakeStore
        {
            Journal = [Journal(same, sameLive)],
            LiveObjects = [sameLive, Live(adopt, "CREATE   VIEW dbo.v_adopt AS SELECT 2 AS a"), Live(change, "CREATE VIEW dbo.v_change AS SELECT 0 AS a")]
        };

        var result = await new SchemaObjectRunner(new FixedSource(Set(same, adopt, change)), store) { ReleaseVersion = "1.12.0" }.RunAsync();

        Assert.Equal(["dbo.v_adopt"], result.Adopted);
        Assert.Equal(["dbo.v_change"], result.Applied);
        Assert.Empty(result.Retired);
        Assert.Equal(new[] { "dbo.v_adopt", "dbo.v_change" }, store.Applied!.Select(step => step.ObjectName).Order(StringComparer.Ordinal));
        Assert.Equal("1.12.0", store.ReleaseVersion);
        Assert.Equal((1, 1), (store.LocksTaken, store.LocksReleased));
    }

    [Fact]
    public async Task A_database_without_the_journal_is_refused_and_the_lock_is_still_released()
    {
        var store = new FakeStore { JournalExists = false };
        var error = await Assert.ThrowsAsync<SchemaObjectIntegrityException>(() => new SchemaObjectRunner(new FixedSource(Set(View("v_a", "SELECT 1 AS a"))), store).RunAsync());
        Assert.Contains("0062", error.Message, StringComparison.Ordinal);
        Assert.Null(store.Applied);
        Assert.Equal((1, 1), (store.LocksTaken, store.LocksReleased));
    }

    [Fact]
    public void The_failure_text_is_the_one_setup_shows()
    {
        var error = new SchemaObjectsNotUpToDateException("dbo.v_a", new InvalidOperationException("x"));
        Assert.StartsWith("Database objects are not up to date; run setup again.", error.Message, StringComparison.Ordinal);
        Assert.Contains("dbo.v_a", error.Message, StringComparison.Ordinal);
    }

    // ---- DB-02, DB-07 and the plan verb ------------------------------------------------------------------------------

    [Fact]
    public void DB07_passes_when_every_object_is_recorded_with_its_definition()
    {
        var file = View("v_a", "SELECT 1 AS a");
        var live = Live(file, "CREATE   VIEW dbo.v_a AS SELECT 1 AS a");
        var result = DatabaseProbes.EvaluateObjects(Set(file), [Journal(file, live)], [live]);
        Assert.Equal((HealthCheckId.Db07, HealthOutcome.Pass, 1m), (result.CheckId, result.Outcome, result.MetricValue));
        Assert.Equal(HealthCheckArea.Database, result.Area);
    }

    [Fact]
    public void DB07_names_hand_edits_first_then_missing_pending_and_unknown_objects()
    {
        var edited = View("v_edited", "SELECT 1 AS a");
        var missing = View("v_missing", "SELECT 1 AS a");
        var pending = View("v_pending", "SELECT 1 AS a");
        var recorded = Journal(edited, Live(edited, "CREATE   VIEW dbo.v_edited AS SELECT 1 AS a"));
        var orphan = new SchemaObjectJournalEntry("dbo.v_orphan", "V", new string('a', 64), new string('b', 64));

        var result = DatabaseProbes.EvaluateObjects(Set(edited, missing, pending), [recorded, orphan],
            [Live(edited, "CREATE VIEW dbo.v_edited AS SELECT 2 AS a"), Live(pending, "CREATE VIEW dbo.v_pending AS SELECT 0 AS a")]);

        Assert.Equal((HealthOutcome.Warning, "CHANGED_BY_HAND"), (result.Outcome, result.FindingCode));
        Assert.Contains("dbo.v_edited", result.Explanation, StringComparison.Ordinal);
        Assert.Equal(new[] { ("dbo.v_edited", "CHANGED_BY_HAND"), ("dbo.v_missing", "MISSING"), ("dbo.v_pending", "PENDING"), ("dbo.v_orphan", "NO_FILE") },
            result.SubResults!.Select(sub => (sub.Key, sub.FindingCode!)));

        var notYet = DatabaseProbes.EvaluateObjects(Set(pending), [], [Live(pending, "CREATE VIEW dbo.v_pending AS SELECT 1 AS a")]);
        Assert.Equal((HealthOutcome.Warning, "PENDING"), (notYet.Outcome, notYet.FindingCode));
    }

    [Fact]
    public void DB07_reports_unreadable_files_as_a_warning_not_a_failure()
    {
        var file = View("v_a", "SELECT 1 AS a");
        var result = DatabaseProbes.EvaluateObjects(Set(file), [], [new LiveSchemaObject("dbo.v_a", "V", "CREATE VIEW dbo.v_a AS SELECT 1 AS a", "a constraint uses it")]);
        Assert.Equal((HealthOutcome.Warning, "OBJECT_FILES_INVALID"), (result.Outcome, result.FindingCode));
    }

    [Fact]
    public void Every_DB07_sql_text_passes_the_read_only_guard()
    {
        Assert.Empty(Etp.Reporting.Infrastructure.SqlServer.Audit.ReadOnlySqlGuard.Violations(SchemaObjectSql.JournalExistsSql));
        Assert.Empty(Etp.Reporting.Infrastructure.SqlServer.Audit.ReadOnlySqlGuard.Violations(SchemaObjectSql.JournalSql));
        Assert.Empty(Etp.Reporting.Infrastructure.SqlServer.Audit.ReadOnlySqlGuard.Violations(SchemaObjectSql.LiveObjectsSql));
    }

    [Fact]
    public void DB02_counts_the_object_files_when_there_are_some()
    {
        var discovered = new[] { new MigrationScript("0001_a", MigrationChecksum.Compute("select 1"), "select 1", "0001_a.sql") };
        var applied = new[] { ("0001_a", MigrationChecksum.Compute("select 1")) };
        Assert.Equal(HealthCheckTexts.DbMigrationsCurrent(1, "0001_a"), DatabaseProbes.EvaluateMigrations(discovered, applied).Explanation);
        var counted = DatabaseProbes.EvaluateMigrations(discovered, applied, 7);
        Assert.Equal(HealthOutcome.Pass, counted.Outcome);
        Assert.Contains("7 database object files", counted.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_verb_prints_both_lists_in_the_contract_shape()
    {
        var json = new DatabaseChangePlan(["0062_schema_objects_journal"], ["dbo.v_a"]).ToJson();
        using var document = JsonDocument.Parse(json);
        Assert.Equal("0062_schema_objects_journal", document.RootElement.GetProperty("pendingMigrations")[0].GetString());
        Assert.Equal("dbo.v_a", document.RootElement.GetProperty("pendingObjects")[0].GetString());
        Assert.False(new DatabaseChangePlan([], []).HasChanges);
        Assert.Equal("""{"pendingMigrations":[],"pendingObjects":[]}""", new DatabaseChangePlan([], []).ToJson());
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------

    private static SchemaObjectFile View(string name, string select, string grants = "") =>
        SchemaObjectFiles.Parse($"20-dbo.{name}.sql", $"CREATE OR ALTER VIEW dbo.{name} AS {select}\n-- >>> GRANTS\n{grants}\n");

    private static LiveSchemaObject Live(SchemaObjectFile file, string definition) => new(file.ObjectName, "V", definition);

    private static SchemaObjectJournalEntry Journal(SchemaObjectFile file, LiveSchemaObject live) =>
        new(file.ObjectName, "V", file.FileSha256, SchemaObjectFiles.DefinitionHash(live.Definition!));

    private static SchemaObjectSet Set(params SchemaObjectFile[] files) => new(files, []);

    private static SchemaObjectStep Single(SchemaObjectSet set, IReadOnlyList<SchemaObjectJournalEntry> journal, IReadOnlyList<LiveSchemaObject> live) =>
        Assert.Single(SchemaObjectPlanner.Plan(set, journal, live));

    private sealed class FixedSource(SchemaObjectSet set) : ISchemaObjectSource
    {
        public Task<SchemaObjectSet> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(set);
    }

    private sealed class ListMigrationSource : IMigrationSource
    {
        public Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MigrationScript>>([]);
    }

    private sealed class FakeStore : ISchemaObjectStore
    {
        public bool JournalExists { get; init; } = true;
        public IReadOnlyList<SchemaObjectJournalEntry> Journal { get; init; } = [];
        public IReadOnlyList<LiveSchemaObject> LiveObjects { get; init; } = [];
        public IReadOnlyList<SchemaObjectStep>? Applied { get; private set; }
        public string? ReleaseVersion { get; private set; }
        public int LocksTaken { get; private set; }
        public int LocksReleased { get; private set; }

        public Task<IAsyncDisposable> AcquireLockAsync(CancellationToken cancellationToken = default)
        {
            LocksTaken++;
            return Task.FromResult<IAsyncDisposable>(new Release(() => LocksReleased++));
        }

        public Task<bool> JournalExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(JournalExists);
        public Task<IReadOnlyList<SchemaObjectJournalEntry>> LoadJournalAsync(CancellationToken cancellationToken = default) => Task.FromResult(Journal);
        public Task<IReadOnlyList<LiveSchemaObject>> LoadLiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(LiveObjects);

        public Task ApplyAsync(IReadOnlyList<SchemaObjectStep> steps, string? releaseVersion, CancellationToken cancellationToken = default)
        {
            Applied = steps;
            ReleaseVersion = releaseVersion;
            return Task.CompletedTask;
        }

        private sealed class Release(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
