using System.Diagnostics;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 A3 cross-layer tests (112-WAVE-PLAN A3, Appendix C item 2): the C# runner applies real object files to SQL Server.
/// A database built by the migrations adopts the PowerShell-exported baseline without executing anything; a changed view
/// file is one ALTER with its permissions intact and a history row; a broken file rolls every object back and the
/// bootstrap refuses; a hand edit shows in DB-07; retire, exclusions, the plan verb and the dependency order.
/// Every synthetic object is named v_a3_* / fn_a3_* and lives only in the test's own database.
/// </summary>
public sealed class SchemaObjectRunnerSqlTests
{
    private const string Grants = "-- >>> GRANTS\n";

    [Fact]
    public async Task A_database_built_by_the_migrations_adopts_the_exported_baseline_and_executes_nothing()
    {
        // Migrations only, as the integrator's baseline database is made; then the PowerShell export, then the C# runner.
        var name = SqlDatabaseFixture.NewName();
        var connectionString = TestSqlConnections.ForDatabase(name);
        var exported = Directory.CreateTempSubdirectory("etp-a3-baseline-").FullName;
        try
        {
            await new SqlServerDatabaseBootstrapper(connectionString, new DirectoryMigrationSource(MigrationDirectory()), SchemaObjectSource.None).BootstrapAsync();
            var before = await LiveAsync(connectionString);

            var export = await RunPowerShellAsync(Path.Combine(Repository(), "scripts", "Export-EtpSchemaObjects.ps1"),
                "-ServerInstance", new SqlConnectionStringBuilder(connectionString).DataSource, "-Database", name, "-OutputDirectory", exported);
            Assert.True(export.ExitCode == 0, export.Output);
            var files = Directory.GetFiles(exported, "*.sql");
            Assert.True(files.Length > 100, $"The export wrote only {files.Length} files.{Environment.NewLine}{export.Output}");
            var excluded = File.ReadAllText(Path.Combine(exported, "EXCLUDED.md"));
            Assert.Contains("dbo.application_users_visible_row", excluded, StringComparison.Ordinal);
            Assert.Contains("dbo.is_blank_text", excluded, StringComparison.Ordinal);

            var first = await RunAsync(connectionString, exported);
            Assert.Empty(first.Applied);
            Assert.Empty(first.Retired);
            Assert.Equal(files.Length, first.Adopted.Count);
            Assert.Equal(files.Length, await ScalarIntAsync(connectionString, "SELECT COUNT(*) FROM dbo.schema_objects"));
            Assert.Equal(files.Length, await ScalarIntAsync(connectionString, "SELECT COUNT(*) FROM dbo.schema_object_history WHERE action='ADOPT'"));
            // Adopting changed no object.
            var after = await LiveAsync(connectionString);
            Assert.All(before, pair => Assert.Equal(pair.Value.Definition, after[pair.Key].Definition));

            var second = await RunAsync(connectionString, exported);
            Assert.Empty(second.Adopted);
            Assert.Empty(second.Applied);

            // The export's layers keep every view or function after what it reads.
            Assert.Empty(await OrderProblemsAsync(connectionString, exported));

            // The repository's own files (the committed baseline and the lanes' new objects) apply on top, once.
            await RunAsync(connectionString, ObjectsDirectory());
            var again = await RunAsync(connectionString, ObjectsDirectory());
            Assert.Empty(again.Applied.Concat(again.Adopted).Concat(again.Retired));
            Assert.Empty(await OrderProblemsAsync(connectionString, ObjectsDirectory()));
        }
        finally
        {
            Directory.Delete(exported, recursive: true);
            await SqlDatabaseFixture.DropIfPresentAsync(name);
        }
    }

    [Fact]
    public async Task A_changed_view_file_is_one_alter_with_permissions_intact_a_history_row_and_its_readers_refreshed()
    {
        var database = new SqlDatabaseFixture();
        var root = Directory.CreateTempSubdirectory("etp-a3-change-").FullName;
        try
        {
            await database.InitializeAsync();
            var objects = Directory.CreateDirectory(Path.Combine(root, "objects")).FullName;
            Write(objects, "20-dbo.v_a3_probe.sql", "CREATE OR ALTER VIEW dbo.v_a3_probe AS SELECT CONVERT(int,1) AS a\n" + Grants
                + "GRANT SELECT ON dbo.v_a3_probe TO etp_viewer;\n");
            Write(objects, "30-dbo.v_a3_reader.sql", "CREATE OR ALTER VIEW dbo.v_a3_reader AS SELECT * FROM dbo.v_a3_probe\n" + Grants);

            var first = await RunAsync(database.ConnectionString, objects);
            Assert.Equal(new[] { "dbo.v_a3_probe", "dbo.v_a3_reader" }, first.Applied);
            // A permission the file does not give survives CREATE OR ALTER.
            await database.ExecuteAsync("GRANT VIEW DEFINITION ON dbo.v_a3_probe TO etp_owner;");
            var firstDefinition = await DefinitionHashAsync(database.ConnectionString, "dbo.v_a3_probe");

            Write(objects, "20-dbo.v_a3_probe.sql", "CREATE OR ALTER VIEW dbo.v_a3_probe AS SELECT CONVERT(int,2) AS a,CONVERT(int,3) AS b\n" + Grants
                + "GRANT SELECT ON dbo.v_a3_probe TO etp_viewer;\n");
            var second = await RunAsync(database.ConnectionString, objects);

            Assert.Equal(new[] { "dbo.v_a3_probe" }, second.Applied);
            Assert.Empty(second.Adopted);
            Assert.Equal(3, await ScalarIntAsync(database.ConnectionString, "SELECT b FROM dbo.v_a3_reader"));
            Assert.Equal(2, await ScalarIntAsync(database.ConnectionString, """
                SELECT COUNT(*) FROM sys.database_permissions p
                WHERE p.major_id=OBJECT_ID(N'dbo.v_a3_probe') AND p.state='G'
                  AND ((p.permission_name='SELECT' AND p.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'etp_viewer'))
                    OR (p.permission_name='VIEW DEFINITION' AND p.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'etp_owner')))
                """));
            Assert.Equal(2, await ScalarIntAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.schema_object_history WHERE object_name=N'dbo.v_a3_probe' AND action='APPLY'"));
            Assert.Equal(firstDefinition, await ScalarTextAsync(database.ConnectionString,
                "SELECT TOP(1) previous_definition_sha256 FROM dbo.schema_object_history WHERE object_name=N'dbo.v_a3_probe' ORDER BY schema_object_history_id DESC"));
            Assert.Equal(await DefinitionHashAsync(database.ConnectionString, "dbo.v_a3_probe"),
                await ScalarTextAsync(database.ConnectionString, "SELECT definition_sha256 FROM dbo.schema_objects WHERE object_name=N'dbo.v_a3_probe'"));

            var third = await RunAsync(database.ConnectionString, objects);
            Assert.Empty(third.Applied.Concat(third.Adopted).Concat(third.Retired));

            // The history is append-only (0062 trigger, THROW 51961).
            var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("DELETE dbo.schema_object_history WHERE object_name=N'dbo.v_a3_probe';"));
            Assert.Equal(51961, error.Number);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_broken_file_rolls_every_object_back_and_the_bootstrap_refuses()
    {
        var database = new SqlDatabaseFixture();
        var root = Directory.CreateTempSubdirectory("etp-a3-broken-").FullName;
        try
        {
            await database.InitializeAsync();
            var objects = Directory.CreateDirectory(Path.Combine(root, "objects")).FullName;
            Write(objects, "20-dbo.v_a3_good.sql", "CREATE OR ALTER VIEW dbo.v_a3_good AS SELECT CONVERT(int,1) AS a\n" + Grants);
            await RunAsync(database.ConnectionString, objects);
            var journal = await ScalarTextAsync(database.ConnectionString, "SELECT file_sha256 FROM dbo.schema_objects WHERE object_name=N'dbo.v_a3_good'");
            var history = await ScalarIntAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.schema_object_history");

            Write(objects, "20-dbo.v_a3_good.sql", "CREATE OR ALTER VIEW dbo.v_a3_good AS SELECT CONVERT(int,2) AS a\n" + Grants);
            Write(objects, "30-dbo.v_a3_bad.sql", "CREATE OR ALTER VIEW dbo.v_a3_bad AS SELECT x FROM dbo.no_such_table_a3\n" + Grants);

            var failure = await Assert.ThrowsAsync<SchemaObjectsNotUpToDateException>(() => RunAsync(database.ConnectionString, objects));
            Assert.Equal("dbo.v_a3_bad", failure.ObjectName);
            Assert.StartsWith(SchemaObjectsNotUpToDateException.Reason, failure.Message, StringComparison.Ordinal);
            Assert.Equal(1, await ScalarIntAsync(database.ConnectionString, "SELECT a FROM dbo.v_a3_good"));
            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString, "SELECT CASE WHEN OBJECT_ID(N'dbo.v_a3_bad') IS NULL THEN 0 ELSE 1 END"));
            Assert.Equal(journal, await ScalarTextAsync(database.ConnectionString, "SELECT file_sha256 FROM dbo.schema_objects WHERE object_name=N'dbo.v_a3_good'"));
            Assert.Equal(history, await ScalarIntAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.schema_object_history"));

            // Setup's bootstrap (no migration pending) refuses with the same reason, so the app is not started on it.
            await Assert.ThrowsAsync<SchemaObjectsNotUpToDateException>(() =>
                new SqlServerDatabaseBootstrapper(database.ConnectionString, new DirectoryMigrationSource(database.MigrationDirectory), new DirectorySchemaObjectSource(objects)).BootstrapAsync());

            // DB-07 reads the same folder (next to <root>\migrations) and says what setup still has to do.
            var probe = await DatabaseProbes.ObjectsAsync(database.ConnectionString, Path.Combine(root, "migrations"), CancellationToken.None);
            Assert.Equal(HealthOutcome.Warning, probe.Outcome);
            Assert.Contains(probe.SubResults!, sub => sub.Key == "dbo.v_a3_bad" && sub.FindingCode == "MISSING");
            Assert.Contains(probe.SubResults!, sub => sub.Key == "dbo.v_a3_good" && sub.FindingCode == "PENDING");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_hand_edit_shows_in_DB07_and_the_next_run_puts_the_release_version_back()
    {
        var database = new SqlDatabaseFixture();
        var root = Directory.CreateTempSubdirectory("etp-a3-hand-").FullName;
        try
        {
            await database.InitializeAsync();
            var objects = Directory.CreateDirectory(Path.Combine(root, "objects")).FullName;
            Write(objects, "20-dbo.v_a3_hand.sql", "CREATE OR ALTER VIEW dbo.v_a3_hand AS SELECT CONVERT(int,1) AS a\n" + Grants);
            await RunAsync(database.ConnectionString, objects);

            await database.ExecuteAsync("EXEC(N'ALTER VIEW dbo.v_a3_hand AS SELECT CONVERT(int,99) AS a');");
            var edited = await DatabaseProbes.ObjectsAsync(database.ConnectionString, Path.Combine(root, "migrations"), CancellationToken.None);
            Assert.Equal((HealthCheckId.Db07, HealthOutcome.Warning, "CHANGED_BY_HAND"), (edited.CheckId, edited.Outcome, edited.FindingCode));
            Assert.Contains(edited.SubResults!, sub => sub.Key == "dbo.v_a3_hand" && sub.FindingCode == "CHANGED_BY_HAND");

            var repaired = await RunAsync(database.ConnectionString, objects);
            Assert.Equal(new[] { "dbo.v_a3_hand" }, repaired.Applied);
            Assert.Equal(1, await ScalarIntAsync(database.ConnectionString, "SELECT a FROM dbo.v_a3_hand"));
            var clean = await DatabaseProbes.ObjectsAsync(database.ConnectionString, Path.Combine(root, "migrations"), CancellationToken.None);
            Assert.DoesNotContain(clean.SubResults ?? [], sub => sub.Key == "dbo.v_a3_hand");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_retired_stub_drops_its_object_but_never_one_another_view_still_reads()
    {
        var database = new SqlDatabaseFixture();
        var root = Directory.CreateTempSubdirectory("etp-a3-retire-").FullName;
        try
        {
            await database.InitializeAsync();
            var objects = Directory.CreateDirectory(Path.Combine(root, "objects")).FullName;
            Write(objects, "20-dbo.v_a3_old.sql", "CREATE OR ALTER VIEW dbo.v_a3_old AS SELECT CONVERT(int,1) AS a\n" + Grants);
            Write(objects, "20-dbo.v_a3_base.sql", "CREATE OR ALTER VIEW dbo.v_a3_base AS SELECT CONVERT(int,1) AS a\n" + Grants);
            Write(objects, "30-dbo.v_a3_user.sql", "CREATE OR ALTER VIEW dbo.v_a3_user AS SELECT a FROM dbo.v_a3_base\n" + Grants);
            await RunAsync(database.ConnectionString, objects);

            File.Delete(Path.Combine(objects, "20-dbo.v_a3_old.sql"));
            var retired = Directory.CreateDirectory(Path.Combine(objects, SchemaObjectFiles.RetiredFolder)).FullName;
            Write(retired, "dbo.v_a3_old.sql", "-- Retired by the A3 integration test.\n");
            var result = await RunAsync(database.ConnectionString, objects);

            Assert.Equal(new[] { "dbo.v_a3_old" }, result.Retired);
            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString, "SELECT CASE WHEN OBJECT_ID(N'dbo.v_a3_old') IS NULL THEN 0 ELSE 1 END"));
            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.schema_objects WHERE object_name=N'dbo.v_a3_old'"));
            Assert.Equal(1, await ScalarIntAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM dbo.schema_object_history WHERE object_name=N'dbo.v_a3_old' AND action='RETIRE' AND definition_sha256 IS NULL AND previous_definition_sha256 IS NOT NULL"));

            File.Delete(Path.Combine(objects, "20-dbo.v_a3_base.sql"));
            Write(retired, "dbo.v_a3_base.sql", "-- Retired too early: v_a3_user still reads it.\n");
            var refused = await Assert.ThrowsAsync<SchemaObjectsNotUpToDateException>(() => RunAsync(database.ConnectionString, objects));
            Assert.Equal("dbo.v_a3_base", refused.ObjectName);
            Assert.Contains("v_a3_user", refused.InnerException!.Message, StringComparison.Ordinal);
            Assert.Equal(1, await ScalarIntAsync(database.ConnectionString, "SELECT a FROM dbo.v_a3_user"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Objects_used_by_a_constraint_or_the_security_policy_are_excluded_and_a_file_for_one_is_refused()
    {
        var database = new SqlDatabaseFixture();
        var root = Directory.CreateTempSubdirectory("etp-a3-excluded-").FullName;
        try
        {
            await database.InitializeAsync();
            var live = await LiveAsync(database.ConnectionString);
            Assert.NotNull(live["dbo.application_users_visible_row"].PinnedBy);
            Assert.Equal("a constraint uses it", live["dbo.is_blank_text"].PinnedBy);

            await database.ExecuteAsync("EXEC(N'CREATE FUNCTION dbo.fn_a3_pinned(@v int) RETURNS bit AS BEGIN RETURN CASE WHEN @v>0 THEN 1 ELSE 0 END END');");
            await database.ExecuteAsync("CREATE TABLE dbo.a3_pinned_table(v int NOT NULL CONSTRAINT CK_a3_pinned CHECK(dbo.fn_a3_pinned(v)=1));");
            var objects = Directory.CreateDirectory(Path.Combine(root, "objects")).FullName;
            Write(objects, "10-dbo.fn_a3_pinned.sql",
                "CREATE OR ALTER FUNCTION dbo.fn_a3_pinned(@v int) RETURNS bit AS BEGIN RETURN CASE WHEN @v>=0 THEN 1 ELSE 0 END END\n" + Grants);

            var error = await Assert.ThrowsAsync<SchemaObjectIntegrityException>(() => RunAsync(database.ConnectionString, objects));
            Assert.Contains("a constraint uses it", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, await ScalarIntAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.schema_objects WHERE object_name=N'dbo.fn_a3_pinned'"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_plan_verb_lists_pending_changes_and_setup_reads_its_json()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var current = await DatabaseChangePlanner.PlanAsync(database.ConnectionString, database.MigrationDirectory);
            Assert.Empty(current.PendingMigrations);

            var missing = await DatabaseChangePlanner.PlanAsync(TestSqlConnections.ForDatabase(SqlDatabaseFixture.NewName()), database.MigrationDirectory);
            Assert.True(missing.HasChanges);
            Assert.Contains("0062_schema_objects_journal", missing.PendingMigrations);

            // The application prints the plan; setup's PowerShell parser reads it (bootstrap-etp-prerequisites.ps1).
            var application = DesktopApplication();
            var info = new ProcessStartInfo(application) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("--plan-database-changes");
            info.ArgumentList.Add("--connection-string");
            info.ArgumentList.Add(database.ConnectionString);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token);
            Assert.True(process.ExitCode == 0, await stderr);

            var planFile = Path.Combine(Path.GetTempPath(), "etp-a3-plan-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                await File.WriteAllTextAsync(planFile, await stdout);
                var script = Path.Combine(Repository(), "scripts", "bootstrap-etp-prerequisites.ps1").Replace("'", "''", StringComparison.Ordinal);
                var parsed = await RunPowerShellCommandAsync(
                    $". '{script}' -ApplicationDirectory 'C:\\UnusedBootstrapTest'; $plan = ConvertFrom-EtpDatabaseChangePlan -Text ([IO.File]::ReadAllText('{planFile.Replace("'", "''", StringComparison.Ordinal)}')); " +
                    "Write-Output \"migrations=$($plan.PendingMigrations.Count) changes=$($plan.HasChanges)\"");
                Assert.True(parsed.ExitCode == 0, parsed.Output);
                Assert.Contains("migrations=0", parsed.Output, StringComparison.Ordinal);
            }
            finally { File.Delete(planFile); }
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Migration_0062_allows_every_1_12_audit_type()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var definition = await ScalarTextAsync(database.ConnectionString, "SELECT definition FROM sys.check_constraints WHERE name=N'CK_operational_audit_type'");
            foreach (var type in new[] { "CollectionsSetup", "CollectionsImport", "CashDepositConfirmed", "SoftwareUpdate", "ShopResultPublished", "TallyReadback", "ImportFixAction", "VisualRender" })
                Assert.Contains($"'{type}'", definition, StringComparison.Ordinal);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>
    /// The integrator's "objects baseline" step, not a check: with ETP_EXPORT_OBJECTS_BASELINE set to the repository's
    /// database\objects folder, builds a scratch database with the migrations only (objects step off), runs
    /// Export-EtpSchemaObjects.ps1 into that folder (existing files are kept) and drops the database. Without the variable
    /// it does nothing. Commit the result as one commit "objects baseline".
    /// </summary>
    [Fact]
    public async Task Objects_baseline_export_runs_only_when_the_integrator_asks_for_it()
    {
        var target = Environment.GetEnvironmentVariable("ETP_EXPORT_OBJECTS_BASELINE");
        if (string.IsNullOrWhiteSpace(target)) return;
        Assert.True(Directory.Exists(target), "ETP_EXPORT_OBJECTS_BASELINE must name the existing database\\objects folder: " + target);
        var name = SqlDatabaseFixture.NewName();
        var connectionString = TestSqlConnections.ForDatabase(name);
        try
        {
            await new SqlServerDatabaseBootstrapper(connectionString, new DirectoryMigrationSource(MigrationDirectory()), SchemaObjectSource.None).BootstrapAsync();
            var export = await RunPowerShellAsync(Path.Combine(Repository(), "scripts", "Export-EtpSchemaObjects.ps1"),
                "-ServerInstance", new SqlConnectionStringBuilder(connectionString).DataSource, "-Database", name, "-OutputDirectory", target);
            Assert.True(export.ExitCode == 0, export.Output);
        }
        finally
        {
            await SqlDatabaseFixture.DropIfPresentAsync(name);
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------

    private static Task<SchemaObjectRunResult> RunAsync(string connectionString, string objects) =>
        new SchemaObjectRunner(new DirectorySchemaObjectSource(objects), new SqlServerSchemaObjectStore(connectionString)).RunAsync();

    private static void Write(string directory, string fileName, string text) =>
        File.WriteAllText(Path.Combine(directory, fileName), text.Replace("\n", "\r\n", StringComparison.Ordinal));

    private static async Task<Dictionary<string, LiveSchemaObject>> LiveAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(SchemaObjectSql.LiveObjectsSql, connection) { CommandTimeout = 120 };
        return (await SchemaObjectSql.ReadLiveAsync(command, CancellationToken.None)).ToDictionary(item => item.ObjectName, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string?> DefinitionHashAsync(string connectionString, string objectName)
    {
        var definition = await ScalarTextAsync(connectionString, $"SELECT OBJECT_DEFINITION(OBJECT_ID(N'{objectName}'))");
        return definition is null ? null : SchemaObjectFiles.DefinitionHash(definition);
    }

    /// <summary>Every view or function file that reads another object with a file must come after it (layer, then name).</summary>
    private static async Task<IReadOnlyList<string>> OrderProblemsAsync(string connectionString, string objects)
    {
        var set = await new DirectorySchemaObjectSource(objects).DiscoverAsync();
        var order = set.Objects.ToDictionary(file => file.ObjectName, file => file.FileName, StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT DISTINCT CONCAT(sa.name,N'.',a.name),CONCAT(sb.name,N'.',b.name)
            FROM sys.sql_expression_dependencies d
            JOIN sys.objects a ON a.object_id=d.referencing_id JOIN sys.schemas sa ON sa.schema_id=a.schema_id
            JOIN sys.objects b ON b.object_id=d.referenced_id JOIN sys.schemas sb ON sb.schema_id=b.schema_id
            WHERE d.referencing_id<>d.referenced_id AND a.type IN ('V','FN','IF','TF') AND b.type IN ('V','FN','IF','TF');
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var (user, used) = (reader.GetString(0), reader.GetString(1));
            if (order.TryGetValue(user, out var userFile) && order.TryGetValue(used, out var usedFile) && string.CompareOrdinal(usedFile, userFile) >= 0)
                problems.Add($"{userFile} reads {used}, whose file {usedFile} applies later.");
        }
        return problems;
    }

    private static async Task<int> ScalarIntAsync(string connectionString, string sql) =>
        Convert.ToInt32(await ScalarAsync(connectionString, sql), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<string?> ScalarTextAsync(string connectionString, string sql) => await ScalarAsync(connectionString, sql) as string;

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string MigrationDirectory() => Path.Combine(AppContext.BaseDirectory, "database", "migrations");

    private static string ObjectsDirectory() => Path.Combine(Repository(), "database", "objects");

    private static string DesktopApplication()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var application = Path.Combine(Repository(), "src", "Etp.Reporting.Desktop", "bin", configuration, "net10.0-windows", "Etp.Reporting.Desktop.exe");
        Assert.True(File.Exists(application), $"Build the desktop application before running this test: {application}");
        return application;
    }

    private static Task<(int ExitCode, string Output)> RunPowerShellAsync(string script, params string[] arguments) =>
        RunPowerShellProcessAsync(["-File", script, .. arguments]);

    private static Task<(int ExitCode, string Output)> RunPowerShellCommandAsync(string command) =>
        RunPowerShellProcessAsync(["-Command", command]);

    private static async Task<(int ExitCode, string Output)> RunPowerShellProcessAsync(IReadOnlyList<string> arguments)
    {
        var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(powerShell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);
        return (process.ExitCode, await output + Environment.NewLine + await error);
    }
}
