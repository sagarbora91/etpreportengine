using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.0, migration 0055 (lane L1): health check runs, results, Known warnings and the role matrix procedure.
/// Grants per role with EXECUTE AS USER, validation of the run JSON (51820/51821), Known rules (51822/51823),
/// pruning (400-day runs, expired Known rows), and diag.load_role_permissions: fixed shape, only the four etp roles,
/// member names only for an Owner, nothing for a Store Manager or Viewer. All data is synthetic.
/// </summary>
public sealed class HealthCheckSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Owner = "diag_hc_owner";
    private const string Manager = "diag_hc_manager";
    private const string Viewer = "diag_hc_viewer";
    private const string Automation = "diag_hc_automation";

    [Fact]
    public async Task Owner_and_automation_record_and_read_runs_and_every_role_reads_the_nightly_summary()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var started = DateTime.UtcNow.AddMinutes(-10);
        var id = await RecordAsync(Automation, Run("Nightly", started, Line("DB-04", "Warning", "AUTO_CLOSE_ON"), Line("SEC-01", "Fail", "EXTRA_GRANT"),
            Line("RPT-01", "Pass", "OK", detail: [Sub("AX123", "Warning", "EXPORTS_MISSING", 2, new() { ["report"] = "R022", ["days"] = 3, ["note"] = "" })]),
            Line("DB-01", "Pass", "OK"), Line("ERR-02", "Known", "NEW_ERRORS"), Line("OPS-06", "Skipped", "NOT_READABLE")));
        Assert.True(id > 0);
        var manual = await RecordAsync(Owner, Run("Manual", started.AddMinutes(1), Line("DB-01", "Pass", "OK")));

        foreach (var user in new[] { Owner, Automation })
        {
            var runs = (await DiagSql.QueryAsync(database, user, "EXEC diag.load_health_check_runs @top=@t,@trigger=@k", ("@t", 10), ("@k", "Nightly")))[0];
            Assert.Equal(id, runs[0]["health_check_run_id"]);
            Assert.All(runs, run => Assert.Equal("Nightly", run["trigger_kind"]));
            Assert.Equal(new[]
            {
                "health_check_run_id", "trigger_kind", "business_date", "started_utc", "completed_utc", "duration_ms", "app_version", "machine_name",
                "run_by_role", "overall", "pass_count", "warning_count", "fail_count", "known_count", "skipped_count", "export_file_name",
            }, runs[0].Keys);
            Assert.Equal((short)2, runs[0]["pass_count"]);

            var results = (await DiagSql.QueryAsync(database, user, "EXEC diag.load_health_check_results @id", ("@id", id)))[0];
            // Catalogue order: DB, SEC, RPT, ..., OPS, ERR - not the order sent.
            Assert.Equal(["DB-01", "DB-04", "SEC-01", "RPT-01", "OPS-06", "ERR-02"], results.Select(row => (string)row["check_id"]!));
            Assert.Equal(new[] { "check_id", "area", "outcome", "finding_code", "explanation", "fix_task_id", "duration_ms", "metric_value", "detail_json" }, results[0].Keys);
            var detail = JsonDocument.Parse((string)results.Single(row => (string)row["check_id"]! == "RPT-01")["detail_json"]!).RootElement;
            Assert.Equal("AX123", detail[0].GetProperty("key").GetString());
        }
        var all = (await DiagSql.QueryAsync(database, Owner, "EXEC diag.load_health_check_runs"))[0];
        Assert.Contains(all, run => (long)run["health_check_run_id"]! == manual);

        foreach (var user in new[] { Owner, Manager, Viewer, Automation })
        {
            var summary = (await DiagSql.QueryAsync(database, user, "EXEC diag.load_health_check_summary"))[0];
            var row = Assert.Single(summary);
            Assert.Equal(new[] { "business_date", "completed_utc", "overall", "pass_count", "warning_count", "fail_count", "known_count", "skipped_count" }, row.Keys);
            Assert.Equal("Fail", row["overall"]);
        }

        foreach (var user in new[] { Manager, Viewer })
        {
            foreach (var sql in new[]
                     {
                         "EXEC diag.load_health_check_runs",
                         "EXEC diag.load_health_check_results 1",
                         "EXEC diag.load_health_check_known",
                         "EXEC diag.record_health_check_run N'{}'",
                         "EXEC diag.load_role_permissions",
                         "SELECT COUNT(*) FROM diag.health_check_runs",
                         "SELECT COUNT(*) FROM diag.health_check_known",
                     })
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, user, sql));
                Assert.Equal(229, denied.Number);
            }
        }
        foreach (var sql in new[] { "EXEC diag.set_health_check_known 'DB-04','AUTO_CLOSE_ON','20991231',N'x'", "EXEC diag.clear_health_check_known 'DB-04','AUTO_CLOSE_ON'", "SELECT COUNT(*) FROM diag.health_check_results" })
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, Automation, sql));
            Assert.Equal(229, denied.Number);
        }

        var missing = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, Owner, "EXEC diag.load_health_check_results @id", ("@id", long.MaxValue)));
        Assert.Equal(51824, missing.Number);
    }

    [Fact]
    public async Task A_run_that_is_not_valid_is_refused_whole()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var started = DateTime.UtcNow.AddMinutes(-5);
        var before = await CountRunsAsync();

        var tooMany = Run("Manual", started, Enumerable.Range(1, 81).Select(i => Line("RPT-" + (i % 100).ToString("00", CultureInfo.InvariantCulture), "Pass", "OK")).ToArray());
        Assert.Equal(51821, (await Assert.ThrowsAsync<SqlException>(() => RecordAsync(Owner, tooMany))).Number);

        var refused = new List<string>
        {
            "not json",
            "[]",
            "{\"trigger\":\"Nightly\"}",
            Run("Daily", started, Line("DB-01", "Pass", "OK")),
            Run("Manual", started, Line("DB-1", "Pass", "OK")),
            Run("Manual", started, Line("db-01", "Pass", "OK")),
            Run("Manual", started, Line("DB-01", "Pass", "ok")),
            Run("Manual", started, Line("DB-01", "Fine", "OK")),
            Run("Manual", started, Line("DB-01", "Pass", "OK"), Line("DB-01", "Pass", "OK")),
            Run("Manual", started, Line("DB-01", "Pass", "OK", explanation: "Line one\r\nLine two")),
            Run("Manual", started, Line("DB-01", "Pass", "OK", explanation: " ")),
            Run("Manual", started, Line("DB-01", "Warning", "X", detail: [Sub("Customer Name", "Warning", null, null, null)])),
            Run("Manual", started, Line("DB-01", "Warning", "X", detail: [Sub("AX123", "Warning", null, null, new() { ["phone"] = "+91 98765 43210" })])),
            Run("Manual", started, Line("DB-01", "Warning", "X", detail: [Sub("AX123", "Warning", null, null, new() { ["path"] = "C:\\Users" })])),
            Run("Manual", started, Line("DB-01", "Warning", "X", detail: [new Dictionary<string, object?> { ["key"] = "AX123", ["outcome"] = "Pass", ["message"] = "free text" }])),
            Run("Manual", started, Line("DB-01", "Pass", "OK"), machineName: "Shop PC"),
            Run("Manual", started, Line("DB-01", "Pass", "OK"), appVersion: "latest"),
            Run("Manual", started, Line("DB-01", "Pass", "OK"), runByRole: "Administrator"),
            Run("Manual", started, Line("DB-01", "Pass", "OK"), countsOverride: new() { ["pass"] = 2, ["warning"] = 0, ["fail"] = 0, ["known"] = 0, ["skipped"] = 0 }),
            Run("Manual", started, Line("DB-01", "Pass", "OK"), completed: started.AddMinutes(-1)),
        };
        foreach (var run in refused)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => RecordAsync(Owner, run));
            Assert.True(error.Number == 51820, run + " -> " + error.Number.ToString(CultureInfo.InvariantCulture));
        }
        Assert.Equal(before, await CountRunsAsync());
    }

    [Fact]
    public async Task A_long_detail_keeps_the_whole_sub_results_that_fit()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var subs = Enumerable.Range(0, 60).Select(i => Sub("STORE" + i.ToString("00", CultureInfo.InvariantCulture), "Warning", "EXPORTS_MISSING", i, new() { ["report"] = "R0" + i.ToString("00", CultureInfo.InvariantCulture) })).ToArray();
        var id = await RecordAsync(Automation, Run("Manual", DateTime.UtcNow.AddMinutes(-2), Line("RPT-05", "Warning", "EXPORTS_MISSING", detail: subs)));
        var detail = (string)(await DiagSql.QueryAsync(database, null, "SELECT detail_json FROM diag.health_check_results WHERE health_check_run_id=@id", ("@id", id)))[0][0]["detail_json"]!;
        Assert.InRange(detail.Length, 1500, 2000);
        var parsed = JsonDocument.Parse(detail).RootElement;
        Assert.InRange(parsed.GetArrayLength(), 10, 59);
        Assert.Equal("STORE00", parsed[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task Runs_older_than_400_days_and_expired_known_rows_are_pruned_by_the_next_record()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var old = await RecordAsync(Owner, Run("Nightly", DateTime.UtcNow.AddDays(-401), Line("DB-01", "Pass", "OK")));
        var kept = await RecordAsync(Owner, Run("Nightly", DateTime.UtcNow.AddDays(-399), Line("DB-01", "Pass", "OK")));
        // An expired Known row (written directly: the procedure refuses a past date).
        await database.ExecuteAsync("INSERT diag.health_check_known(check_id,finding_code,until_date,reason,set_by,set_utc) VALUES('DATA-04','INPUTS_MISSING',DATEADD(day,-1,CONVERT(date,SYSDATETIME())),N'Expired',N'test',DATEADD(day,-30,SYSUTCDATETIME()))");

        // Manual, so the latest Nightly asserted by the other tests is not this one (test order is not fixed).
        await RecordAsync(Automation, Run("Manual", DateTime.UtcNow.AddMinutes(-1), Line("DB-01", "Pass", "OK")));

        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM diag.health_check_runs WHERE health_check_run_id=@id", ("@id", old)));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM diag.health_check_results WHERE health_check_run_id=@id", ("@id", old)));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM diag.health_check_runs WHERE health_check_run_id=@id", ("@id", kept)));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM diag.health_check_known WHERE check_id='DATA-04' AND finding_code='INPUTS_MISSING'"));
    }

    [Fact]
    public async Task Only_the_owner_marks_a_warning_known_for_at_most_90_days()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var today = DateTime.Today;

        async Task<int> SetAsync(string user, string check, string finding, DateTime? until, string? reason)
        {
            var error = await Record.ExceptionAsync(() => DiagSql.QueryAsync(database, user, "EXEC diag.set_health_check_known @c,@f,@u,@r",
                ("@c", check), ("@f", finding), ("@u", until), ("@r", reason)));
            return error is SqlException sql ? sql.Number : error is null ? 0 : throw error;
        }

        Assert.Equal(0, await SetAsync(Owner, "DATA-04", "MONTHLY_TARGETS_MISSING", today.AddDays(30), "Targets start next month"));
        Assert.Equal(0, await SetAsync(Owner, "DATA-04", "MONTHLY_TARGETS_MISSING", today.AddDays(90), "Moved to the end of the quarter"));   // upsert
        Assert.Equal(51823, await SetAsync(Owner, "DATA-04", "STAFF_TARGETS_MISSING", today.AddDays(91), "Too far"));
        Assert.Equal(51823, await SetAsync(Owner, "DATA-04", "STAFF_TARGETS_MISSING", today.AddDays(-1), "In the past"));
        Assert.Equal(51823, await SetAsync(Owner, "DATA-04", "STAFF_TARGETS_MISSING", today.AddDays(10), null));
        Assert.Equal(51823, await SetAsync(Owner, "DATA-04", "STAFF_TARGETS_MISSING", today.AddDays(10), "Two\nlines"));
        Assert.Equal(51823, await SetAsync(Owner, "data-04", "STAFF_TARGETS_MISSING", today.AddDays(10), "Reason"));
        Assert.Equal(51823, await SetAsync(Owner, "DATA-04", "staff targets", today.AddDays(10), "Reason"));

        foreach (var user in new[] { Owner, Automation })
        {
            var known = Assert.Single((await DiagSql.QueryAsync(database, user, "EXEC diag.load_health_check_known"))[0], row => (string)row["check_id"]! == "DATA-04");
            Assert.Equal(new[] { "check_id", "finding_code", "until_date", "reason", "set_by", "set_utc" }, known.Keys);
            Assert.Equal(today.AddDays(90), known["until_date"]);
            Assert.Equal("Moved to the end of the quarter", known["reason"]);
        }

        // Even an explicit EXECUTE grant does not get past the Owner check.
        await database.ExecuteAsync($"GRANT EXECUTE ON diag.set_health_check_known TO {Manager}; GRANT EXECUTE ON diag.clear_health_check_known TO {Manager};");
        try
        {
            Assert.Equal(51822, await SetAsync(Manager, "DATA-04", "R011_MISSING", today.AddDays(5), "Reason"));
            var refused = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, Manager, "EXEC diag.clear_health_check_known 'DATA-04','MONTHLY_TARGETS_MISSING'"));
            Assert.Equal(51822, refused.Number);
        }
        finally
        {
            await database.ExecuteAsync($"REVOKE EXECUTE ON diag.set_health_check_known FROM {Manager}; REVOKE EXECUTE ON diag.clear_health_check_known FROM {Manager};");
        }

        await DiagSql.QueryAsync(database, Owner, "EXEC diag.clear_health_check_known 'DATA-04','MONTHLY_TARGETS_MISSING'");
        await DiagSql.QueryAsync(database, Owner, "EXEC diag.clear_health_check_known 'DATA-04','MONTHLY_TARGETS_MISSING'");   // idempotent
        Assert.DoesNotContain((await DiagSql.QueryAsync(database, Owner, "EXEC diag.load_health_check_known"))[0], row => (string)row["finding_code"]! == "MONTHLY_TARGETS_MISSING");
    }

    [Fact]
    public async Task Role_permissions_have_a_fixed_shape_and_never_leak_names_to_a_non_owner()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        // Something the procedure must never show: a grant to a single user and a grant to public.
        await database.ExecuteAsync($"GRANT SELECT ON dbo.stores TO {Viewer}; GRANT VIEW DEFINITION ON dbo.stores TO public;");

        var owner = await DiagSql.QueryAsync(database, Owner, "EXEC diag.load_role_permissions");
        var automation = await DiagSql.QueryAsync(database, Automation, "EXEC diag.load_role_permissions");
        foreach (var sets in new[] { owner, automation })
        {
            Assert.Equal(3, sets.Count);
            Assert.Equal(new[] { "role_name", "class_desc", "schema_name", "object_name", "permission_name", "state_desc" }, sets[0][0].Keys);
            Assert.All(sets[0], row => Assert.Contains((string)row["role_name"]!, new[] { "etp_owner", "etp_store_manager", "etp_viewer", "etp_automation" }));
            Assert.All(sets[0], row => Assert.Contains((string)row["state_desc"]!, new[] { "GRANT", "DENY", "GRANT_WITH_GRANT_OPTION" }));
            // 0051: Store Managers are denied writes to staff targets; 0055: the summary is granted to all four roles.
            Assert.Contains(sets[0], row => (string)row["role_name"]! == "etp_store_manager" && (string?)row["object_name"] == "staff_sales_targets"
                && (string)row["permission_name"]! == "INSERT" && (string)row["state_desc"]! == "DENY");
            Assert.Contains(sets[0], row => (string)row["role_name"]! == "etp_viewer" && (string?)row["schema_name"] == "diag"
                && (string?)row["object_name"] == "load_health_check_summary" && (string)row["permission_name"]! == "EXECUTE");
            Assert.Contains(sets[0], row => (string)row["role_name"]! == "etp_viewer" && (string)row["class_desc"]! == "SCHEMA" && (string?)row["schema_name"] == "dbo"
                && (string)row["permission_name"]! == "SELECT");
            Assert.DoesNotContain(sets[0], row => (string?)row["object_name"] == "stores");
            Assert.DoesNotContain(sets[0], row => (string?)row["schema_name"] == "diag" && (string?)row["object_name"] is "error_register" or "health_check_runs");

            Assert.Equal(new[] { "role_name", "member_name", "is_active_application_user", "application_role_code", "member_count" }, sets[1][0].Keys);
            Assert.Equal(new[] { "member_name", "owners_without_grant_option" }, sets[2][0].Keys);
        }

        // The Owner sees each member by name; the automation account sees counts only.
        Assert.Contains(owner[1], row => (string?)row["member_name"] == Automation && (string)row["role_name"]! == "etp_automation" && (int)row["member_count"]! == 1);
        Assert.All(automation[1], row => Assert.Null(row["member_name"]));
        Assert.Contains(automation[1], row => (string)row["role_name"]! == "etp_automation" && (int)row["member_count"]! >= 1);
        Assert.All(automation[2], row => Assert.Null(row["member_name"]));
        Assert.Single(automation[2]);
        // The test users have no login, so neither caller can see server permissions from inside the database: -1 (SEC-03 Skipped).
        Assert.All(owner[2].Concat(automation[2]), row => Assert.Equal(-1, row["owners_without_grant_option"]));

        // The reader the procedure runs as cannot sign in and holds only VIEW DEFINITION.
        Assert.Equal("NONE", await ScalarAsync("SELECT authentication_type_desc FROM sys.database_principals WHERE name='diag_catalog_reader'"));
        Assert.Equal("VIEW DEFINITION", await ScalarAsync(
            "SELECT STRING_AGG(permission_name,',') FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID('diag_catalog_reader') AND permission_name<>'CONNECT'"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM sys.database_role_members WHERE member_principal_id=DATABASE_PRINCIPAL_ID('diag_catalog_reader')"));
        var impersonate = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, Automation, "EXECUTE AS USER='diag_catalog_reader'; SELECT 1"));
        Assert.Equal(15517, impersonate.Number);

        await database.ExecuteAsync($"REVOKE SELECT ON dbo.stores FROM {Viewer}; REVOKE VIEW DEFINITION ON dbo.stores FROM public;");
    }

    // ---- helpers ----

    private static Dictionary<string, object?> Line(string checkId, string outcome, string findingCode, string? explanation = null,
        IReadOnlyList<Dictionary<string, object?>>? detail = null) => new()
    {
        ["checkId"] = checkId,
        ["area"] = AreaOf(checkId),
        ["outcome"] = outcome,
        ["findingCode"] = findingCode,
        ["explanation"] = explanation ?? "Synthetic explanation for " + checkId + ".",
        ["fixTaskId"] = outcome == "Pass" ? null : "settings.users",
        ["durationMs"] = 12,
        ["metricValue"] = outcome == "Pass" ? null : 1.5m,
        ["detail"] = detail,
    };

    private static string AreaOf(string checkId) => checkId.Split('-')[0].ToUpperInvariant() switch
    {
        "DB" => "Database", "SEC" => "Security", "RPT" => "Reports", "IMP" => "Imports", "DATA" => "Data", "OPS" => "Operations", _ => "Errors",
    };

    private static Dictionary<string, object?> Sub(string key, string outcome, string? findingCode, decimal? metric, Dictionary<string, object?>? fields) => new()
    {
        ["key"] = key, ["outcome"] = outcome, ["findingCode"] = findingCode, ["metricValue"] = metric, ["fields"] = fields,
    };

    private static string Run(string trigger, DateTime startedUtc, params Dictionary<string, object?>[] results) =>
        Run(trigger, startedUtc, results, null);

    private static string Run(string trigger, DateTime startedUtc, Dictionary<string, object?> result, string? machineName = null, string? appVersion = null,
        string? runByRole = null, Dictionary<string, object?>? countsOverride = null, DateTime? completed = null) =>
        Run(trigger, startedUtc, [result], countsOverride, machineName, appVersion, runByRole, completed);

    private static string Run(string trigger, DateTime startedUtc, Dictionary<string, object?>[] results, Dictionary<string, object?>? countsOverride,
        string? machineName = null, string? appVersion = null, string? runByRole = null, DateTime? completed = null)
    {
        int Count(string outcome) => results.Count(line => (string)line["outcome"]! == outcome);
        var completedUtc = completed ?? startedUtc.AddSeconds(41.25);
        var overall = Count("Fail") > 0 ? "Fail" : Count("Warning") > 0 ? "Warning" : "Pass";
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["trigger"] = trigger,
            ["businessDate"] = startedUtc.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["startedUtc"] = startedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["completedUtc"] = completedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["durationMs"] = 41250,
            ["appVersion"] = appVersion ?? "1.11.0.0",
            ["machineName"] = machineName ?? "TESTPC",
            ["runByRole"] = runByRole ?? "Automation",
            ["overall"] = overall,
            ["counts"] = countsOverride ?? new Dictionary<string, object?>
            {
                ["pass"] = Count("Pass"), ["warning"] = Count("Warning"), ["fail"] = Count("Fail"), ["known"] = Count("Known"), ["skipped"] = Count("Skipped"),
            },
            ["exportFileName"] = "health-20261010-210004-" + trigger + ".json",
            ["results"] = results,
        });
    }

    private async Task<long> RecordAsync(string user, string run) =>
        Convert.ToInt64((await DiagSql.QueryAsync(database, user, "EXEC diag.record_health_check_run @run", ("@run", run)))[0][0]["health_check_run_id"], CultureInfo.InvariantCulture);

    private async Task<int> CountRunsAsync() => Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM diag.health_check_runs"), CultureInfo.InvariantCulture);

    private async Task<object?> ScalarAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        var rows = (await DiagSql.QueryAsync(database, null, sql, parameters))[0];
        return rows.Count == 0 ? null : rows[0].Values.First();
    }
}
