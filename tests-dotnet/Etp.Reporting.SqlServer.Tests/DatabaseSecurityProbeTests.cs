using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>1.11.0 L4: the rules of DB-01..05, SEC-01..03 and the SQL build table, without a database.</summary>
public sealed class DatabaseSecurityProbeTests
{
    [Theory]
    [InlineData(DatabaseProbes.DatabaseNameSql)]
    [InlineData(DatabaseProbes.AppliedMigrationsSql)]
    [InlineData(DatabaseProbes.IntegritySql)]
    [InlineData(DatabaseProbes.SuspectPagesSql)]
    [InlineData(DatabaseProbes.OptionsSql)]
    [InlineData(DatabaseProbes.SizeSql)]
    public void Every_probe_sql_text_passes_the_read_only_guard(string sql) =>
        Assert.Empty(ReadOnlySqlGuard.Violations(sql));

    [Fact]
    public void Six_database_probes_answer_DB01_to_DB05_and_DB07_in_order()
    {
        var probes = DatabaseProbes.Create("Server=.\\SQLEXPRESS;Database=EtpReporting;Integrated Security=true;TrustServerCertificate=true", null);

        // 1.12 A3 added DB-07 (database objects); DB-06 is L9's ServiceScreenTimingProbe.
        Assert.Equal(new[] { HealthCheckId.Db01, HealthCheckId.Db02, HealthCheckId.Db03, HealthCheckId.Db04, HealthCheckId.Db05, HealthCheckId.Db07 },
            probes.SelectMany(probe => probe.CheckIds));
    }

    [Fact]
    public void DB01_failure_kinds_have_their_own_finding_codes_and_the_sql_number()
    {
        var login = DatabaseProbes.Unreachable(DatabaseFailureKind.LoginRefused, 18456, TimeSpan.FromSeconds(1));
        Assert.Equal((HealthOutcome.Fail, "LOGIN_REFUSED", "connection"), (login.Outcome, login.FindingCode, login.FixTaskId));
        Assert.Contains("18456", login.Explanation);
        Assert.Equal("DATABASE_MISSING", DatabaseProbes.Unreachable(DatabaseFailureKind.DatabaseMissing, 4060, TimeSpan.Zero).FindingCode);
        Assert.Equal("CONNECT_TIMEOUT", DatabaseProbes.Unreachable(DatabaseFailureKind.Timeout, -2, TimeSpan.Zero).FindingCode);
        Assert.Equal("SERVER_UNREACHABLE", DatabaseProbes.Unreachable(DatabaseFailureKind.Other, null, TimeSpan.Zero).FindingCode);
        Assert.Equal("EtpReporting", DatabaseProbes.ConfiguredDatabase("Server=.;Database=EtpReporting;Integrated Security=true"));
    }

    [Fact]
    public void DB02_classifies_pending_checksum_unknown_and_newer_migrations()
    {
        var discovered = new[] { Script("0001_a", "select 1"), Script("0002_b", "select 2"), Script("0003_c", "select 3") };
        (string, string) Applied(string id, string sql) => (id, MigrationChecksum.Compute(sql));

        var current = DatabaseProbes.EvaluateMigrations(discovered, [Applied("0001_a", "select 1"), Applied("0002_b", "select 2"), Applied("0003_c", "select 3")]);
        Assert.Equal((HealthOutcome.Pass, 3m), (current.Outcome, current.MetricValue));

        var pending = DatabaseProbes.EvaluateMigrations(discovered, [Applied("0001_a", "select 1")]);
        Assert.Equal((HealthOutcome.Fail, "PENDING"), (pending.Outcome, pending.FindingCode));
        Assert.Contains("0002_b", pending.Explanation);

        var changed = DatabaseProbes.EvaluateMigrations(discovered, [Applied("0001_a", "select 1 -- edited"), Applied("0002_b", "select 2"), Applied("0003_c", "select 3")]);
        Assert.Equal((HealthOutcome.Fail, "CHECKSUM_MISMATCH"), (changed.Outcome, changed.FindingCode));

        var unknown = DatabaseProbes.EvaluateMigrations(discovered, [Applied("0001_a", "select 1"), Applied("0001b_x", "x"), Applied("0002_b", "select 2"), Applied("0003_c", "select 3")]);
        Assert.Equal((HealthOutcome.Fail, "MIGRATION_UNKNOWN"), (unknown.Outcome, unknown.FindingCode));

        var newer = DatabaseProbes.EvaluateMigrations(discovered, [Applied("0001_a", "select 1"), Applied("0002_b", "select 2"), Applied("0003_c", "select 3"), Applied("0004_d", "x")]);
        Assert.Equal((HealthOutcome.Warning, "DATABASE_NEWER"), (newer.Outcome, newer.FindingCode));
    }

    [Fact]
    public void DB03_suspect_pages_and_page_verify_fail_and_an_old_checkdb_warns()
    {
        var today = new DateOnly(2026, 10, 11);
        Assert.Equal(HealthOutcome.Pass, DatabaseProbes.EvaluateIntegrity(new DateTime(2026, 10, 9, 19, 36, 0), "CHECKSUM", 0, today).Outcome);
        Assert.Equal("SUSPECT_PAGES", DatabaseProbes.EvaluateIntegrity(new DateTime(2026, 10, 9), "CHECKSUM", 2, today).FindingCode);
        Assert.Equal("PAGE_VERIFY_NOT_CHECKSUM", DatabaseProbes.EvaluateIntegrity(new DateTime(2026, 10, 9), "TORN_PAGE_DETECTION", 0, today).FindingCode);
        var stale = DatabaseProbes.EvaluateIntegrity(new DateTime(2026, 9, 1), "CHECKSUM", 0, today);
        Assert.Equal((HealthOutcome.Warning, "CHECKDB_STALE", "recovery"), (stale.Outcome, stale.FindingCode, stale.FixTaskId));
        Assert.Equal("CHECKDB_STALE", DatabaseProbes.EvaluateIntegrity(new DateTime(1900, 1, 1), "CHECKSUM", 0, today).FindingCode);
        // msdb not readable: no Fail, the sub-result says Skipped.
        var unread = DatabaseProbes.EvaluateIntegrity(new DateTime(2026, 10, 9), "CHECKSUM", null, today);
        Assert.Equal(HealthOutcome.Pass, unread.Outcome);
        Assert.Contains(unread.SubResults!, sub => sub.Key == "SUSPECT_PAGES" && sub.Outcome == HealthOutcome.Skipped);
    }

    [Fact]
    public void DB04_auto_close_warns_and_offline_read_only_or_single_user_fail()
    {
        DatabaseProbes.DatabaseOptions Options(bool autoClose = false, string state = "ONLINE", bool readOnly = false, string access = "MULTI_USER") =>
            new(autoClose, state, readOnly, access, "SIMPLE");

        Assert.Equal(HealthOutcome.Pass, DatabaseProbes.EvaluateOptions(Options()).Outcome);
        var autoClose = DatabaseProbes.EvaluateOptions(Options(autoClose: true));
        Assert.Equal((HealthOutcome.Warning, "AUTO_CLOSE_ON"), (autoClose.Outcome, autoClose.FindingCode));
        Assert.Equal("DB_NOT_ONLINE", DatabaseProbes.EvaluateOptions(Options(autoClose: true, state: "RECOVERY_PENDING")).FindingCode);
        Assert.Equal("DB_READ_ONLY", DatabaseProbes.EvaluateOptions(Options(readOnly: true)).FindingCode);
        Assert.Equal("DB_USER_ACCESS", DatabaseProbes.EvaluateOptions(Options(access: "SINGLE_USER")).FindingCode);
    }

    [Fact]
    public void DB05_uses_the_express_limit_of_the_version()
    {
        Assert.Equal(51200m, DatabaseProbes.ExpressLimitMb(4, 17));
        Assert.Equal(10240m, DatabaseProbes.ExpressLimitMb(4, 16));
        Assert.Null(DatabaseProbes.ExpressLimitMb(3, 17));
        Assert.Equal(HealthOutcome.Pass, DatabaseProbes.EvaluateSize(464m, 51200m, 80m).Outcome);
        Assert.Equal("DB_SIZE_HIGH", DatabaseProbes.EvaluateSize(41000m, 51200m, 80m).FindingCode);
        Assert.Equal("DB_SIZE_CRITICAL", DatabaseProbes.EvaluateSize(49000m, 51200m, 80m).FindingCode);
        Assert.Equal(HealthOutcome.Pass, DatabaseProbes.EvaluateSize(900000m, null, 80m).Outcome);
    }

    [Theory]
    [InlineData("17.0.1000.7", null, HealthOutcome.Warning, "SQL_RTM")]
    [InlineData("17.0.1000.7", "RTM", HealthOutcome.Warning, "SQL_RTM")]
    [InlineData("17.0.4025.3", "CU5", HealthOutcome.Warning, "SQL_BELOW_RECOMMENDED")]
    [InlineData("17.0.5005.3", "CU9", HealthOutcome.Pass, "OK")]
    [InlineData("17.0.5100.1", "CU10", HealthOutcome.Pass, "OK")]
    [InlineData("16.0.1000.6", null, HealthOutcome.Warning, "SQL_RTM")]
    [InlineData("16.0.4003.1", "CU1", HealthOutcome.Pass, "OK")]
    [InlineData("15.0.2000.5", "RTM", HealthOutcome.Fail, "SQL_UNSUPPORTED")]
    [InlineData("18.0.100.1", null, HealthOutcome.Warning, "SQL_UNTESTED")]
    [InlineData("not a version", null, HealthOutcome.Warning, "SQL_BUILD_UNKNOWN")]
    public void OPS06_sql_build_rule(string version, string? level, HealthOutcome outcome, string code)
    {
        var assessment = KnownSqlBuilds.Assess(version, level);
        Assert.Equal((outcome, code), (assessment.Outcome, assessment.FindingCode));
    }

    // ---- SEC-01 --------------------------------------------------------------------------------------------

    private static readonly ExpectedRolePermissionMatrix SmallMatrix = new(
    [
        new("etp_store_manager", "SCHEMA", "dbo", null, "SELECT", "GRANT"),
        new("etp_store_manager", "OBJECT_OR_COLUMN", "dbo", "staff_sales_targets", "INSERT", "DENY", "D22"),
        new("etp_viewer", "OBJECT_OR_COLUMN", "dbo", "record_operational_audit", "EXECUTE", "GRANT"),
        new("etp_store_manager", "OBJECT_OR_COLUMN", "dbo", "staff_sales_targets", "INSERT", "GRANT", "D22", "absent")
    ]);

    private static RolePermission P(string role, string cls, string? schema, string? obj, string permission, string state) =>
        new(role, cls, schema, obj, permission, state);

    private static List<RolePermission> Exact() =>
    [
        P("etp_store_manager", "SCHEMA", "dbo", null, "SELECT", "GRANT"),
        P("etp_store_manager", "OBJECT_OR_COLUMN", "dbo", "staff_sales_targets", "INSERT", "DENY"),
        P("etp_viewer", "OBJECT_OR_COLUMN", "dbo", "record_operational_audit", "EXECUTE", "GRANT")
    ];

    [Fact]
    public void SEC01_identical_is_pass_and_case_and_column_duplicates_do_not_matter()
    {
        var actual = Exact();
        actual.Add(P("ETP_VIEWER", "OBJECT_OR_COLUMN", "dbo", "Record_Operational_Audit", "EXECUTE", "GRANT"));

        Assert.Equal(HealthOutcome.Pass, SecurityProbes.EvaluatePermissions(actual, SmallMatrix).Outcome);
    }

    [Fact]
    public void SEC01_the_staff_targets_grant_on_live_is_a_decision_fail()
    {
        var actual = Exact();
        actual.RemoveAll(permission => permission.State == "DENY");
        actual.Add(P("etp_store_manager", "OBJECT_OR_COLUMN", "dbo", "staff_sales_targets", "INSERT", "GRANT"));

        var line = SecurityProbes.EvaluatePermissions(actual, SmallMatrix);

        Assert.Equal((HealthOutcome.Fail, "DECISION_VIOLATED"), (line.Outcome, line.FindingCode));
        Assert.Contains("D22", line.Explanation);
        Assert.Contains("staff_sales_targets", line.Explanation);
        Assert.Contains(line.SubResults!, sub => sub.FindingCode == "MISSING_DENY");
        Assert.DoesNotContain(line.SubResults!, sub => sub.FindingCode == "EXTRA_GRANT");
    }

    [Fact]
    public void SEC01_extra_write_or_execute_for_a_non_owner_fails_and_other_differences_warn()
    {
        var extraExecute = Exact();
        extraExecute.Add(P("etp_automation", "OBJECT_OR_COLUMN", "dbo", "configure_application_role", "EXECUTE", "GRANT"));
        Assert.Equal((HealthOutcome.Fail, "EXTRA_GRANT"), Outcome(SecurityProbes.EvaluatePermissions(extraExecute, SmallMatrix)));

        var withGrantOption = Exact();
        withGrantOption.Add(P("etp_viewer", "OBJECT_OR_COLUMN", "dbo", "record_operational_audit", "EXECUTE", "GRANT_WITH_GRANT_OPTION"));
        Assert.Equal((HealthOutcome.Fail, "EXTRA_GRANT"), Outcome(SecurityProbes.EvaluatePermissions(withGrantOption, SmallMatrix)));

        var missingDeny = Exact();
        missingDeny.RemoveAll(permission => permission.State == "DENY");
        Assert.Equal((HealthOutcome.Fail, "MISSING_DENY"), Outcome(SecurityProbes.EvaluatePermissions(missingDeny, SmallMatrix)));

        var missingGrant = Exact();
        missingGrant.RemoveAll(permission => permission.Permission == "EXECUTE");
        Assert.Equal((HealthOutcome.Warning, "MISSING_GRANT"), Outcome(SecurityProbes.EvaluatePermissions(missingGrant, SmallMatrix)));

        var ownerExtra = Exact();
        ownerExtra.Add(P("etp_owner", "OBJECT_OR_COLUMN", "dbo", "anything", "EXECUTE", "GRANT"));
        Assert.Equal((HealthOutcome.Warning, "EXTRA_PERMISSION"), Outcome(SecurityProbes.EvaluatePermissions(ownerExtra, SmallMatrix)));

        var extraSelect = Exact();
        extraSelect.Add(P("etp_viewer", "OBJECT_OR_COLUMN", "diag", "error_register", "SELECT", "GRANT"));
        Assert.Equal((HealthOutcome.Warning, "EXTRA_PERMISSION"), Outcome(SecurityProbes.EvaluatePermissions(extraSelect, SmallMatrix)));

        var extraDeny = Exact();
        extraDeny.Add(P("etp_viewer", "OBJECT_OR_COLUMN", "dbo", "stores", "SELECT", "DENY"));
        Assert.Equal((HealthOutcome.Warning, "EXTRA_DENY"), Outcome(SecurityProbes.EvaluatePermissions(extraDeny, SmallMatrix)));

        Assert.Equal((HealthOutcome.Fail, "MATRIX_MISSING"), Outcome(SecurityProbes.EvaluatePermissions(Exact(), null)));
    }

    // ---- SEC-02 / SEC-03 -----------------------------------------------------------------------------------

    private static RoleMembership M(string role, string? name, bool active, string? code, int count = 1) => new(role, name, active, code, count);

    [Fact]
    public void SEC02_owner_view_with_names()
    {
        var healthy = new[]
        {
            M("etp_owner", "WORKPC\\Owner", true, "OWNER"),
            M("etp_store_manager", "WORKPC\\EtpAutomation", true, "STORE_MANAGER"),
            M("etp_automation", "WORKPC\\EtpAutomation", true, "STORE_MANAGER")
        };
        var pass = SecurityProbes.EvaluateMemberships(healthy, "WORKPC\\EtpAutomation");
        Assert.Equal(HealthOutcome.Pass, pass.Outcome);
        Assert.DoesNotContain("EtpAutomation", pass.Explanation);
        Assert.All(pass.SubResults!, sub =>
        {
            Assert.DoesNotContain("WORKPC", sub.Key);
            Assert.All(sub.Fields?.Values ?? [], value => Assert.DoesNotContain("WORKPC", value));
        });

        Assert.Equal(HealthOutcome.Pass, SecurityProbes.EvaluateMemberships(healthy, "EtpAutomation").Outcome);
        Assert.Equal("AUTOMATION_NOT_IN_ROLE", SecurityProbes.EvaluateMemberships(healthy, "WORKPC\\Other").FindingCode);

        var leftBehind = healthy.Append(M("etp_store_manager", "WORKPC\\Former", false, null)).ToArray();
        Assert.Equal((HealthOutcome.Fail, "MEMBER_NOT_APP_USER"), Outcome(SecurityProbes.EvaluateMemberships(leftBehind, null)));

        var escalated = healthy.Append(M("etp_owner", "WORKPC\\Clerk", true, "STORE_MANAGER")).ToArray();
        Assert.Equal((HealthOutcome.Fail, "MEMBER_NOT_APP_USER"), Outcome(SecurityProbes.EvaluateMemberships(escalated, null)));

        var staleViewer = healthy.Append(M("etp_viewer", "WORKPC\\Old", false, null)).ToArray();
        Assert.Equal((HealthOutcome.Warning, "MEMBER_NOT_APP_USER"), Outcome(SecurityProbes.EvaluateMemberships(staleViewer, null)));

        Assert.Equal((HealthOutcome.Fail, "NO_ACTIVE_OWNER"), Outcome(SecurityProbes.EvaluateMemberships(healthy.Skip(1).ToArray(), null)));
    }

    [Fact]
    public void SEC02_automation_view_has_counts_only()
    {
        var counts = new[]
        {
            M("etp_owner", null, true, "OWNER"),
            M("etp_store_manager", null, true, "STORE_MANAGER", 2),
            M("etp_automation", null, true, "STORE_MANAGER")
        };
        Assert.Equal(HealthOutcome.Pass, SecurityProbes.EvaluateMemberships(counts, "WORKPC\\EtpAutomation").Outcome);
        Assert.Equal("AUTOMATION_NOT_IN_ROLE",
            SecurityProbes.EvaluateMemberships(counts.Where(row => row.RoleName != "etp_automation").ToArray(), "WORKPC\\EtpAutomation").FindingCode);
    }

    [Fact]
    public void SEC03_owners_without_the_grant_option_warn()
    {
        Assert.Equal(HealthOutcome.Pass, SecurityProbes.EvaluateGrantOption(0).Outcome);
        // -1: the caller (the automation account) cannot see server permissions (0055) - Skipped, never Fail.
        Assert.Equal((HealthOutcome.Skipped, HealthCheckTexts.NotPermitted), Outcome(SecurityProbes.EvaluateGrantOption(-1)));
        var line = SecurityProbes.EvaluateGrantOption(2);
        Assert.Equal((HealthOutcome.Warning, "OWNER_NO_GRANT_OPTION", "users"), (line.Outcome, line.FindingCode, line.FixTaskId));
    }

    [Fact]
    public void SEC01_sub_results_are_tokens_the_run_procedure_accepts()
    {
        var actual = Exact();
        actual.Add(P("etp_viewer", "OBJECT_OR_COLUMN", "dbo", "stores", "TAKE OWNERSHIP", "GRANT"));

        var line = SecurityProbes.EvaluatePermissions(actual, SmallMatrix);

        var sub = Assert.Single(line.SubResults!);
        Assert.Equal("etp_viewer:dbo.stores:TAKE_OWNERSHIP", sub.Key);
        Assert.All(sub.Fields!.Values, value => Assert.Equal(HealthCheckTexts.SafeToken(value), value));
    }

    // ---- The embedded matrix -------------------------------------------------------------------------------

    [Fact]
    public void The_embedded_matrix_loads_and_holds_the_staff_targets_decision()
    {
        var matrix = ExpectedRolePermissionMatrix.LoadEmbedded();

        Assert.NotNull(matrix);
        Assert.Contains(matrix!.Absent, entry => entry.Role == "etp_store_manager" && entry.Object == "staff_sales_targets" && entry.Permission == "INSERT" && entry.State == "GRANT");
        Assert.Contains(matrix.Absent, entry => entry.Role == "etp_store_manager" && entry.Object == "staff_sales_targets" && entry.Permission == "UPDATE" && entry.State == "GRANT");
        Assert.Contains(new RolePermissionKey("etp_store_manager", "OBJECT_OR_COLUMN", "dbo", "staff_sales_targets", "INSERT", "DENY"), matrix.Expected);
        Assert.DoesNotContain(new RolePermissionKey("etp_store_manager", "OBJECT_OR_COLUMN", "dbo", "staff_sales_targets", "INSERT", "GRANT"), matrix.Expected);
        Assert.All(matrix.Entries, entry => Assert.Contains(entry.Role, ExpectedRolePermissionMatrix.Roles));
        Assert.Contains(new RolePermissionKey("etp_automation", "OBJECT_OR_COLUMN", "diag", "load_role_permissions", "EXECUTE", "GRANT"), matrix.Expected);
    }

    [Fact]
    public void Generating_the_embedded_matrix_from_its_own_permissions_reproduces_the_file()
    {
        using var stream = typeof(ExpectedRolePermissionMatrix).Assembly.GetManifestResourceStream(ExpectedRolePermissionMatrix.ResourceName);
        Assert.NotNull(stream);
        var text = new StreamReader(stream!).ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        var matrix = ExpectedRolePermissionMatrix.Parse(text);
        var permissions = matrix.Entries.Where(entry => !entry.MustBeAbsent)
            .Select(entry => new RolePermission(entry.Role, entry.ClassDesc, entry.Schema, entry.Object, entry.Permission, entry.State));

        Assert.Equal(text, ExpectedRolePermissionMatrix.Generate(permissions, matrix));
    }

    private static (HealthOutcome, string) Outcome(HealthCheckResult line) => (line.Outcome, line.FindingCode);

    private static MigrationScript Script(string id, string sql) => new(id, MigrationChecksum.Compute(sql), sql, id + ".sql");
}
