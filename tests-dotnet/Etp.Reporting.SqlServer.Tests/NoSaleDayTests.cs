using System.Globalization;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 lane N1 "Mark as no-sale day" (decision 52; decision 55 = Q1 option a): text guards over migration 0068 and the three
/// object files (A3's format), the service's refusal translation, the plain texts and DATA-01's OK line. The SQL itself is
/// proved by IntegrationTests/NoSaleDaysSqlTests.
/// </summary>
public sealed class NoSaleDayTests
{
    private static readonly string[] ObjectFiles =
        ["20-dbo.v_no_sale_days_current.sql", "40-dbo.mark_no_sale_day.sql", "40-dbo.withdraw_no_sale_day.sql"];

    private static string Migration() =>
        File.ReadAllText(Path.Combine(SchemaSourceText.MigrationsDirectory(), "0068_no_sale_days.sql"));

    private static string ObjectText(string file) => File.ReadAllText(Path.Combine(SchemaSourceText.ObjectsDirectory(), file));

    [Fact]
    public void Migration_0068_follows_the_runner_rules_creates_no_view_or_procedure_and_throws_only_in_the_lane_block()
    {
        var sql = Migration();

        Assert.StartsWith("--", sql, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), sql);
        Assert.DoesNotMatch(new Regex(@"BEGIN\s+TRAN", RegexOptions.IgnoreCase), sql);
        Assert.DoesNotMatch(new Regex(@"CREATE\s+(OR\s+ALTER\s+)?(VIEW|PROC|PROCEDURE|FUNCTION)\b", RegexOptions.IgnoreCase), sql);
        Assert.DoesNotMatch(new Regex(@"\bEXEC(UTE)?\s+dbo\.", RegexOptions.IgnoreCase), sql);
        var numbers = Regex.Matches(sql, @"THROW\s+(\d+)", RegexOptions.IgnoreCase).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        Assert.NotEmpty(numbers);
        Assert.All(numbers, n => Assert.InRange(n, 51980, 51989));
        Assert.Single(MigrationPrechecks.Extract(sql));
    }

    [Fact]
    public void Migration_0068_holds_every_0062_audit_type_plus_the_two_no_sale_types()
    {
        static HashSet<string> Types(string sql)
        {
            var start = sql.LastIndexOf("ADD CONSTRAINT CK_operational_audit_type", StringComparison.Ordinal);
            Assert.True(start >= 0);
            var end = sql.IndexOf("));", start, StringComparison.Ordinal);
            return Regex.Matches(sql[start..end], "'([A-Za-z]+)'").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        }

        var before = Types(File.ReadAllText(Path.Combine(SchemaSourceText.MigrationsDirectory(), "0062_schema_objects_journal.sql")));
        var after = Types(Migration());

        Assert.Equal(before.Count + 2, after.Count);
        Assert.Subset(after, before);
        Assert.Contains("NoSaleDayMarked", after);
        Assert.Contains("NoSaleDayWithdrawn", after);
        // The pre-check refuses on the same list, and the constraint is rebuilt WITH CHECK.
        Assert.Contains("ALTER TABLE dbo.operational_audit WITH CHECK ADD CONSTRAINT CK_operational_audit_type", Migration(), StringComparison.Ordinal);
        var precheck = Assert.Single(MigrationPrechecks.Extract(Migration()));
        Assert.Contains("'NoSaleDayWithdrawn'", precheck, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_0068_keeps_the_reason_from_the_viewer_and_writes_from_nobody_but_the_owner()
    {
        var sql = Migration();

        Assert.Contains("CONSTRAINT PK_no_sale_days PRIMARY KEY(store_code,business_date)", sql, StringComparison.Ordinal);
        Assert.Contains("reason_code IN('CLOSED_HOLIDAY','CLOSED_OTHER','OPEN_NO_SALE')", sql, StringComparison.Ordinal);
        Assert.Contains("row_version rowversion NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("DENY INSERT,UPDATE,DELETE ON dbo.no_sale_days TO etp_store_manager,etp_viewer;", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT ON dbo.no_sale_days(reason,withdraw_reason) TO etp_viewer;", sql, StringComparison.Ordinal);
        Assert.Contains("DENY INSERT,UPDATE,DELETE ON dbo.no_sale_day_history TO etp_store_manager,etp_viewer;", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT ON dbo.no_sale_day_history(reason) TO etp_viewer;", sql, StringComparison.Ordinal);
        // The lock protects the mark (51983) and nothing is deleted (51989); history is append-only.
        Assert.Contains("d.status=''LOCKED''", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER dbo.trg_no_sale_day_history_immutable ON dbo.no_sale_day_history AFTER UPDATE,DELETE", sql, StringComparison.Ordinal);
        // Synthetic only: no customer name, phone or real person in the file.
        Assert.DoesNotMatch(new Regex(@"\b\d{10}\b"), sql);
    }

    [Fact]
    public void No_sale_object_files_are_in_the_objects_format_with_their_grants()
    {
        foreach (var file in ObjectFiles)
        {
            var text = ObjectText(file);
            var match = Regex.Match(file, @"^(?<layer>\d{2})-dbo\.(?<name>[a-z_]+)\.sql$");
            Assert.True(match.Success, file);
            var name = match.Groups["name"].Value;
            var kind = match.Groups["layer"].Value == "40" ? "PROCEDURE" : "VIEW";
            Assert.Matches(new Regex($@"^CREATE OR ALTER {kind} dbo\.{name}\b", RegexOptions.Multiline), text);
            Assert.Single(Regex.Matches(text, @"^CREATE OR ALTER ", RegexOptions.Multiline));
            var grants = text.IndexOf("-- >>> GRANTS", StringComparison.Ordinal);
            Assert.True(grants > 0, file);
            Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), text);
            Assert.All(text[grants..].Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)),
                line => Assert.Matches(new Regex(@"^(GRANT|DENY) [A-Z ,]+ ON dbo\." + name + @"(\([a-z_,]+\))? TO [a-z_,]+;$"), line));
            foreach (Match number in Regex.Matches(text, @"THROW\s+(\d+)", RegexOptions.IgnoreCase))
                Assert.InRange(int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture), 51980, 51989);
        }

        Assert.Contains("DENY SELECT ON dbo.v_no_sale_days_current(reason) TO etp_viewer;", ObjectText(ObjectFiles[0]), StringComparison.Ordinal);
        Assert.Contains("WHERE n.withdrawn_utc IS NULL", ObjectText(ObjectFiles[0]), StringComparison.Ordinal);
        foreach (var procedure in ObjectFiles.Skip(1))
        {
            var text = ObjectText(procedure);
            Assert.Contains("DENY EXECUTE ON dbo." + procedure[3..^4].Replace("dbo.", "", StringComparison.Ordinal) + " TO etp_viewer;", text, StringComparison.Ordinal);
            Assert.Contains("TO etp_owner,etp_store_manager;", text, StringComparison.Ordinal);
            Assert.Contains("THROW 51980,", text, StringComparison.Ordinal);
            Assert.Contains("THROW 51983,", text, StringComparison.Ordinal);
            Assert.Contains("BEGIN TRANSACTION;", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Mark_refuses_a_future_date_a_locked_day_sales_and_a_non_zero_entry_and_audits_the_mark()
    {
        var mark = ObjectText("40-dbo.mark_no_sale_day.sql");

        Assert.Contains("IF @date>CAST(SYSDATETIME() AS date)", mark, StringComparison.Ordinal);
        Assert.Contains("status='LOCKED'", mark, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.sales_invoices WHERE store_code=@store AND transaction_date=@date", mark, StringComparison.Ordinal);
        Assert.Contains("COALESCE(numeric_value,0)<>0", mark, StringComparison.Ordinal);
        Assert.Contains("EXEC dbo.record_operational_audit 'NoSaleDayMarked'", mark, StringComparison.Ordinal);
        // The day row exists afterwards, so the day can be finalised.
        Assert.Contains("INSERT dbo.daily_reporting_days(store_code,business_date,status) VALUES(@store,@date,'OPEN');", mark, StringComparison.Ordinal);
        Assert.Contains("EXEC dbo.record_operational_audit 'NoSaleDayWithdrawn'", ObjectText("40-dbo.withdraw_no_sale_day.sql"), StringComparison.Ordinal);
        // Audit details hold no digits or paths (record_operational_audit's rule).
        foreach (Match detail in Regex.Matches(mark + ObjectText("40-dbo.withdraw_no_sale_day.sql"), @"'Succeeded',N'([^']*)'"))
            Assert.DoesNotMatch(new Regex(@"[0-9:/\\]"), detail.Groups[1].Value);
    }

    [Theory]
    [InlineData(51980)]
    [InlineData(51984)]
    [InlineData(51989)]
    public void Lane_refusals_reach_the_screen_as_their_own_plain_sentence(int number)
    {
        var refusal = SqlServerNoSaleDayService.Translate(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, "A plain sentence.")));

        var typed = Assert.IsType<NoSaleDayRefusedException>(refusal);
        Assert.Equal("A plain sentence.", typed.Message);
    }

    [Theory]
    [InlineData(229)]
    [InlineData(230)]
    public void A_permission_error_becomes_the_role_sentence(int number)
    {
        var refusal = SqlServerNoSaleDayService.Translate(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, "The EXECUTE permission was denied.")));

        Assert.Equal(SqlServerNoSaleDayService.PermissionMessage, Assert.IsType<UnauthorizedAccessException>(refusal).Message);
    }

    [Theory]
    [InlineData(51979)]
    [InlineData(51990)]
    [InlineData(1205)]
    public void Other_errors_are_not_translated(int number) =>
        Assert.Null(SqlServerNoSaleDayService.Translate(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, "x"))));

    [Fact]
    public async Task The_service_checks_the_input_before_it_opens_a_connection()
    {
        var service = new SqlServerNoSaleDayService(new RefusingConnections());

        Assert.Equal("Choose why there were no sales.",
            (await Assert.ThrowsAsync<NoSaleDayRefusedException>(() => service.MarkAsync("SC999", new(2026, 8, 21), "SOMETHING", "Closed"))).Message);
        Assert.Equal("Write a short reason.",
            (await Assert.ThrowsAsync<NoSaleDayRefusedException>(() => service.MarkAsync("SC999", new(2026, 8, 21), NoSaleDayText.ClosedHoliday, "  "))).Message);
        await Assert.ThrowsAsync<NoSaleDayRefusedException>(() => service.MarkAsync(" ", new(2026, 8, 21), NoSaleDayText.ClosedHoliday, "Holiday"));
        await Assert.ThrowsAsync<NoSaleDayRefusedException>(() => service.WithdrawAsync("SC999", new(2026, 8, 21), ""));
        await Assert.ThrowsAsync<NoSaleDayRefusedException>(() => service.MarkAsync("SC999", new(2026, 8, 21), NoSaleDayText.ClosedHoliday, new string('x', 501)));
    }

    [Fact]
    public void The_service_reads_the_reason_only_where_the_caller_may_and_is_guarded_for_an_older_database()
    {
        var sql = SqlServerNoSaleDayService.LoadSql;

        Assert.Contains("IF OBJECT_ID(N'dbo.v_no_sale_days_current',N'V') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("HAS_PERMS_BY_NAME(N'dbo.v_no_sale_days_current',N'OBJECT',N'SELECT',N'reason',N'COLUMN')", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(NULL AS nvarchar(500)) reason", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.v_no_sale_days_current',N'V') IS NOT NULL", SqlServerNoSaleDayService.DsrMarksSql, StringComparison.Ordinal);
        Assert.DoesNotContain("reason", SqlServerNoSaleDayService.DsrMarksSql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.v_no_sale_days_current',N'V') IS NOT NULL", DailyReportingWorkflowRepository.NoSaleDaySql, StringComparison.Ordinal);
        foreach (var text in new[] { sql, SqlServerNoSaleDayService.DsrMarksSql, DailyReportingWorkflowRepository.NoSaleDaySql })
            Assert.DoesNotMatch(new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE)\b", RegexOptions.IgnoreCase), text);
    }

    [Fact]
    public void Texts_name_the_marker_without_the_domain_and_the_date_in_the_screen_format()
    {
        var at = new DateTimeOffset(2026, 8, 21, 6, 30, 0, TimeSpan.Zero);
        var day = NoSaleDayText.MarkedOn(at);
        var shown = Etp.Reporting.Application.Formatting.DisplayText.Date(day);

        Assert.Equal($"No sales (marked by manager on {shown})", NoSaleDayText.NoSalesLine(@"SHOPPC\manager", at));
        Assert.Equal($"SC999: no-sale day (marked by owner on {shown})", NoSaleDayText.ChecklistLine("SC999", "owner", at));
        Assert.Equal("someone", NoSaleDayText.Who(" "));
        Assert.Equal(3, NoSaleDayText.Reasons.Count);
        Assert.All(NoSaleDayText.Reasons, reason => Assert.DoesNotMatch(new Regex(@"[0-9_]"), reason.Label));
        Assert.Null(NoSaleDayText.ValidateMark(NoSaleDayText.OpenNoSale, "Power cut all day"));
        Assert.Null(NoSaleDayText.ValidateWithdraw("Sales file arrived"));
    }

    [Fact]
    public void Data01_ok_line_counts_the_store_days_marked_no_sale()
    {
        var day = new DateOnly(2026, 8, 21);
        var none = DataProbes.EvaluateCompleteness(day, new DailyReadinessResult(day, ["ZZ001"], []), TimeSpan.Zero);
        var one = DataProbes.EvaluateCompleteness(day, new DailyReadinessResult(day, ["ZZ001"], []) { NoSaleStoreCodes = ["ZZ001"] }, TimeSpan.Zero);
        var two = DataProbes.EvaluateCompleteness(day, new DailyReadinessResult(day, ["ZZ001", "ZZ002"], []) { NoSaleStoreCodes = ["ZZ001", "ZZ002"] }, TimeSpan.Zero);

        Assert.All(new[] { none, one, two }, result => Assert.Equal(HealthOutcome.Pass, result.Outcome));
        Assert.DoesNotContain("no-sale", none.Explanation, StringComparison.Ordinal);
        Assert.EndsWith("(1 store-day marked no-sale)", one.Explanation, StringComparison.Ordinal);
        Assert.EndsWith("(2 store-days marked no-sale)", two.Explanation, StringComparison.Ordinal);
    }

    private sealed class RefusingConnections : Etp.Reporting.Infrastructure.SqlServer.Connections.IEtpConnectionFactory
    {
        public string DatabaseName => "EtpTest";
        public Microsoft.Data.SqlClient.SqlConnection Create(Etp.Reporting.Infrastructure.SqlServer.Connections.EtpConnectionPurpose purpose = Etp.Reporting.Infrastructure.SqlServer.Connections.EtpConnectionPurpose.Default) =>
            throw new InvalidOperationException("No connection expected.");
        public Task<Microsoft.Data.SqlClient.SqlConnection> OpenAsync(Etp.Reporting.Infrastructure.SqlServer.Connections.EtpConnectionPurpose purpose = Etp.Reporting.Infrastructure.SqlServer.Connections.EtpConnectionPurpose.Default, CancellationToken ct = default) =>
            throw new InvalidOperationException("No connection expected.");
    }
}
