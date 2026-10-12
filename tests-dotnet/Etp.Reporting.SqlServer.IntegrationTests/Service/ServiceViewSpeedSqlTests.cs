using System.Diagnostics;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// 1.11.4: migration 0061 restates six Service views for speed (VM acceptance of 1.11.3: v_service_money_changes 58.9 s,
/// v_service_s004_daily 5.0 s, v_service_job 2.45 s). The old texts (the latest definition before 0061, from the shipped
/// migrations) are created beside the new views as dbo.old1114_* and must return exactly the same rows, on the synthetic
/// Service fixtures (week1, week2 and the raw pack) and again after a simulated Restate of S003 and S004 (the superseded
/// reading path of v_service_money_changes), for the database owner and for a Viewer (customer name NULL). A second test
/// is the gate's timing guard: every Service view reads in full under one second on the fixture database, and the filtered
/// reads the screens use still compile (the hash-join hints must never leave a join without an equality, error 8622).
/// </summary>
public sealed partial class ServiceViewSpeedSqlTests
{
    // Restated by 0061, in dependency order; references between them are redirected to the old copies.
    private static readonly string[] Restated =
        ["v_service_datelog_readings", "v_service_s004_daily", "v_service_money_changes", "v_service_parts", "v_service_claims", "v_service_job"];

    private const string Prefix = "old1114_";

    [Fact]
    public async Task The_restated_views_return_the_rows_of_their_previous_text_also_after_a_restate_and_for_a_viewer()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAllAsync(database);
            await CreateOldCopiesAsync(database);
            await database.ExecuteAsync("CREATE USER speed_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER speed_viewer;");
            foreach (var view in Restated) await database.ExecuteAsync($"GRANT SELECT ON dbo.{Prefix}{view} TO etp_viewer;");

            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_money_changes") > 0, "The fixtures restate S004 days.");
            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job WHERE customer_name IS NOT NULL") > 0, "The fixtures give jobs names.");
            await AssertSameAsync(database, "fixtures", null);
            await AssertSameAsync(database, "fixtures, viewer", "speed_viewer");
            Assert.Equal(0, await IntAsync(database, As("speed_viewer", "SELECT COUNT(*) FROM dbo.v_service_job WHERE customer_name IS NOT NULL")));

            // A Restate: the week1 S003 and S004 readings superseded by week2's (the import keeps their landing rows), so the
            // money view compares week2 with the reading it restated instead of the rank-2 covering reading.
            await database.ExecuteAsync("""
                UPDATE o SET o.is_superseded=1,o.superseded_by_import_file_id=n.import_file_id,
                  o.superseded_utc=SYSUTCDATETIME(),o.superseded_by=N'speed-test',o.restatement_reason=N'Simulated restate (1.11.4 speed test)'
                FROM dbo.import_files o
                JOIN dbo.import_files n ON n.report_code=o.report_code AND n.period_end='2026-10-05' AND n.is_superseded=0
                WHERE o.report_code IN('S003','S004') AND o.period_end='2026-09-28' AND o.is_superseded=0;
                """);
            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.import_files WHERE report_code IN('S003','S004') AND is_superseded=1") >= 1);
            await AssertSameAsync(database, "restated", null);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Every_Service_view_reads_in_full_under_one_second_and_the_screens_filtered_reads_compile()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAllAsync(database);
            var views = await NamesAsync(database, "SELECT name FROM sys.views WHERE name LIKE N'v[_]service[_]%' ORDER BY name");
            Assert.Contains("v_service_money_changes", views);
            var slow = new List<string>();
            foreach (var view in views)
            {
                await ReadAllAsync(database, $"SELECT * FROM dbo.{view}"); // compile and warm the cache
                var timer = Stopwatch.StartNew();
                var rows = await ReadAllAsync(database, $"SELECT * FROM dbo.{view}");
                timer.Stop();
                if (timer.Elapsed > TimeSpan.FromSeconds(1)) slow.Add($"{view} {timer.ElapsedMilliseconds} ms ({rows} rows)");
            }
            Assert.True(slow.Count == 0, "Service views slower than 1 s on the fixtures: " + string.Join("; ", slow));

            // The filters the screens and tests put on the hinted views: each must still have a plan.
            foreach (var sql in new[]
            {
                "SELECT * FROM dbo.v_service_s004_daily WHERE business_date='2026-10-05'",
                "SELECT * FROM dbo.v_service_money_changes WHERE report_code='S004' AND business_date='2026-10-05'",
                "SELECT * FROM dbo.v_service_claims WHERE business_date>='2026-09-01' AND business_date<='2026-10-09'",
                "SELECT * FROM dbo.v_service_claims WHERE job_order_number=(SELECT TOP (1) job_order_number FROM dbo.v_service_job ORDER BY job_order_number)",
                "SELECT * FROM dbo.v_service_claims WHERE import_file_id=1 AND business_date='2026-10-05'",
                "SELECT * FROM dbo.v_service_job WHERE job_order_number=(SELECT MIN(job_order_number) FROM dbo.v_service_job_status_current)",
                "SELECT * FROM dbo.v_service_parts WHERE invoice_number IS NOT NULL",
                "SELECT * FROM dbo.v_service_parts WHERE invoice_number=N'X' AND item_id=N'Y'",
                "SELECT * FROM dbo.v_service_gprc_claims WHERE report_code='S023'",
            })
                _ = await ReadAllAsync(database, sql);
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task AssertSameAsync(SqlDatabaseFixture database, string when, string? user)
    {
        foreach (var view in Restated)
        {
            string Sql(string a, string b) => $"SELECT COUNT(*) FROM (SELECT * FROM dbo.{a} EXCEPT SELECT * FROM dbo.{b}) d";
            string Wrap(string sql) => user is null ? sql : As(user, sql);
            var newRows = await IntAsync(database, Wrap($"SELECT COUNT(*) FROM dbo.{view}"));
            var oldRows = await IntAsync(database, Wrap($"SELECT COUNT(*) FROM dbo.{Prefix}{view}"));
            Assert.True(newRows == oldRows, $"{when}: {view} has {newRows} rows, its previous text {oldRows}.");
            Assert.True(await IntAsync(database, Wrap(Sql(view, Prefix + view))) == 0, $"{when}: {view} has rows its previous text does not.");
            Assert.True(await IntAsync(database, Wrap(Sql(Prefix + view, view))) == 0, $"{when}: {view} misses rows of its previous text.");
        }
    }

    // The latest definition of each restated view before 0061, created as dbo.old1114_<name>.
    private static async Task CreateOldCopiesAsync(SqlDatabaseFixture database)
    {
        var latest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(database.MigrationDirectory, "*.sql").Order(StringComparer.Ordinal))
        {
            if (string.CompareOrdinal(Path.GetFileName(path), "0061") >= 0) continue;
            foreach (Match block in ViewBlock().Matches(File.ReadAllText(path).Replace("\r\n", "\n")))
                latest[block.Groups["name"].Value] = block.Groups["body"].Value;
        }
        foreach (var view in Restated)
        {
            var body = latest[view];
            foreach (var other in Restated) body = Regex.Replace(body, $@"\bdbo\.{other}\b", $"dbo.{Prefix}{other}");
            await database.ExecuteAsync($"EXEC(N'CREATE VIEW dbo.{Prefix}{view} AS\n{body}');");
        }
    }

    private static async Task ImportAllAsync(SqlDatabaseFixture database)
    {
        foreach (var folder in new[] { FixtureFolder("week1"), FixtureFolder("week2"), FixtureFolder("raw") })
        {
            var summary = await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString)).RunAsync(folder, new("Synthetic Owner"));
            Assert.Equal(0, summary.Failed);
        }
    }

    private static string FixtureFolder(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "tests-dotnet", "fixtures", "service-interim", name);
    }

    private static string As(string user, string sql) => $"EXECUTE AS USER='{user}'; {sql}; REVERT;";

    private static async Task<int> IntAsync(SqlDatabaseFixture database, string sql) => Convert.ToInt32(await database.ExecuteAsync(sql, 300));

    private static async Task<List<string>> NamesAsync(SqlDatabaseFixture database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task<int> ReadAllAsync(SqlDatabaseFixture database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        var rows = 0;
        while (await reader.ReadAsync()) rows++;
        return rows;
    }

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();
}
