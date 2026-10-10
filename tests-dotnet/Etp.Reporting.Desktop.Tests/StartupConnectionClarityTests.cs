using System.Text.RegularExpressions;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.9.9 lane startup-connection: IE-CODE-05 (app side), 06, 07, 08, 09.</summary>
public sealed class StartupConnectionClarityTests
{
    // The pattern setup matches (199-STDERR-FORMAT.md). Changing it breaks the bootstrap's capture.
    private static readonly Regex HeadlessFormat = new(
        @"^ETP-STARTUP-FAILED mode=(?<mode>\S+) kind=(?<kind>\S+) sql=(?<sql>\S+): (?<reason>.+)$");

    private static Microsoft.Data.SqlClient.SqlException Sql(params int[] numbers) =>
        SqlExceptionFactory.Create(numbers.Select(number => new SqlExceptionFactory.Error(number, $"server text {number} 'Customer Secret'")).ToArray());

    [Theory]
    [InlineData(DesktopStartupMode.InitializeDatabase, "initialize-database")]
    [InlineData(DesktopStartupMode.InitializeConfiguredDatabase, "initialize-configured-database")]
    [InlineData(DesktopStartupMode.AutomationOnce, "automation-once")]
    public void Headless_modes_have_the_tokens_setup_matches(DesktopStartupMode mode, string token) =>
        Assert.Equal(token, StartupFailureText.ModeToken(mode));

    [Theory]
    [InlineData(18456, "LoginRefused", "refused the Windows login")]
    [InlineData(4060, "DatabaseMissing", "database could not be opened")]
    [InlineData(229, "PermissionDenied", "permission denied")]
    [InlineData(53, "ServerUnreachable", "could not be reached")]
    [InlineData(-2, "Timeout", "took too long")]
    public void Headless_failure_is_one_line_with_kind_sql_number_and_fixed_reason(int number, string kind, string reason)
    {
        var line = StartupFailureText.HeadlessLine("initialize-configured-database", Sql(number));

        var match = HeadlessFormat.Match(line);
        Assert.True(match.Success, line);
        Assert.Equal("initialize-configured-database", match.Groups["mode"].Value);
        Assert.Equal(kind, match.Groups["kind"].Value);
        Assert.Equal(number.ToString(System.Globalization.CultureInfo.InvariantCulture), match.Groups["sql"].Value);
        Assert.Contains(reason, match.Groups["reason"].Value);
        Assert.DoesNotContain("Secret", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void Headless_migration_refusal_keeps_etp_own_sql_text_and_lists_every_number()
    {
        var refusal = SqlExceptionFactory.Create(
            new SqlExceptionFactory.Error(51240, "Migration 0049 precheck: 3 rows need review.\r\nRun the repair first."),
            new SqlExceptionFactory.Error(3621, "The statement has been terminated."));

        var line = StartupFailureText.HeadlessLine("initialize-configured-database", new InvalidOperationException("wrapper", refusal));

        var match = HeadlessFormat.Match(line);
        Assert.True(match.Success, line);
        Assert.Equal("Other", match.Groups["kind"].Value);
        Assert.Equal("51240,3621", match.Groups["sql"].Value);
        Assert.Equal("Migration 0049 precheck: 3 rows need review.  Run the repair first. (SQL error 51240)", match.Groups["reason"].Value);
    }

    [Fact]
    public void Headless_configuration_rejection_is_invalid_configuration_without_parameter_noise()
    {
        var line = StartupFailureText.HeadlessLine(StartupFailureText.ConfigurationModeToken,
            new ArgumentException("Provide a connection string after --connection-string.", "arguments"));

        Assert.Equal("ETP-STARTUP-FAILED mode=configuration kind=InvalidConfiguration sql=none: Provide a connection string after --connection-string.", line);
    }

    [Fact]
    public void Headless_checksum_or_other_failure_without_sql_uses_the_friendly_text_capped_to_one_line()
    {
        var line = StartupFailureText.HeadlessLine("initialize-database",
            new InvalidOperationException("Migration 0012 checksum mismatch.\n" + new string('x', 900)));

        var match = HeadlessFormat.Match(line);
        Assert.True(match.Success);
        Assert.Equal("Other", match.Groups["kind"].Value);
        Assert.Equal("none", match.Groups["sql"].Value);
        Assert.StartsWith("Migration 0012 checksum mismatch. ", match.Groups["reason"].Value);
        Assert.Equal(StartupFailureText.MaxReasonLength, match.Groups["reason"].Value.Length);
    }

    [Fact]
    public void Missing_database_wins_over_the_login_failure_sql_server_raises_with_it()
    {
        var line = StartupFailureText.HeadlessLine("automation-once", Sql(4060, 18456));
        Assert.Contains("kind=DatabaseMissing sql=4060,18456:", line);
    }

    [Theory]
    [InlineData(53, "Cannot reach SQL Server")]
    [InlineData(-2, "SQL Server is not answering")]
    [InlineData(18456, "Login refused")]
    [InlineData(4060, "Database not available")]
    [InlineData(262, "Permission denied")]
    [InlineData(297, "Permission denied")]
    public void Welcome_overlay_names_the_connection_problem_with_its_sql_number(int number, string title)
    {
        var presentation = StartupFailureText.Welcome(Sql(number), "checking your access");

        Assert.Equal(title, presentation.Title);
        Assert.EndsWith($"(SQL error {number})", presentation.Message);
        Assert.DoesNotContain("Secret", presentation.Message);
    }

    [Fact]
    public void Welcome_overlay_does_not_blame_sql_server_for_a_non_connection_failure()
    {
        var presentation = StartupFailureText.Welcome(new InvalidOperationException("The store catalogue has two active stores with code HEMW."), "loading the stores");

        Assert.Equal("ETP could not open", presentation.Title);
        Assert.Equal("ETP could not open while loading the stores: The store catalogue has two active stores with code HEMW.", presentation.Message);
        Assert.DoesNotContain("Cannot reach", presentation.Message);
    }

    [Fact]
    public void Welcome_overlay_for_a_missing_object_after_a_partial_upgrade_is_not_a_connection_failure()
    {
        var presentation = StartupFailureText.Welcome(Sql(208), "loading the dashboard");

        Assert.Equal(DatabaseFailureKind.Other, presentation.Kind);
        Assert.StartsWith("ETP could not open while loading the dashboard: ", presentation.Message);
        Assert.DoesNotContain("Secret", presentation.Message);
    }

    [Theory]
    [InlineData(18456)]
    [InlineData(4060)]
    [InlineData(229)]
    [InlineData(10061)]
    public void Access_reload_connection_failure_removes_access_and_says_why(int number)
    {
        var failure = StartupFailureText.AccessRefresh(Sql(number));

        Assert.True(failure.RemoveAccess);
        Assert.StartsWith("Access could not be refreshed: ", failure.Status);
        Assert.EndsWith($"(SQL error {number})", failure.Status);
    }

    [Fact]
    public void Access_reload_code_failure_keeps_current_access_and_says_so()
    {
        var failure = StartupFailureText.AccessRefresh(new InvalidOperationException("Role code 'X' is not recognised."));

        Assert.False(failure.RemoveAccess);
        Assert.Equal("Access could not be refreshed: Role code 'X' is not recognised. Your current access is kept; reopen ETP if it looks wrong.", failure.Status);
    }

    [Theory]
    [InlineData(18456, "DATABASE_HEALTH_CHECK_FAILED.SQL18456")]
    [InlineData(4060, "DATABASE_HEALTH_CHECK_FAILED.SQL4060")]
    [InlineData(-2, "DATABASE_HEALTH_CHECK_FAILED.SQL-2")]
    [InlineData(null, "DATABASE_HEALTH_CHECK_FAILED")]
    public void Connection_test_diagnostics_event_carries_the_sql_number(int? number, string expected) =>
        Assert.Equal(expected, StartupFailureText.HealthCheckEventId(number));

    [Fact]
    public void Connection_test_event_id_survives_the_diagnostics_token_filter()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpHealthLog_" + Guid.NewGuid().ToString("N"));
        try
        {
            DesktopDiagnostics.Record(null, "Settings.Workspace", StartupFailureText.HealthCheckEventId(18456),
                DesktopDiagnosticSeverity.Warning, logDirectory: directory);
            var line = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "diagnostics-*.jsonl")));
            Assert.Contains("\"EventId\":\"DATABASE_HEALTH_CHECK_FAILED.SQL18456\"", line);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Failed_report_pack_is_logged_with_run_type_sql_number_and_date_but_no_message()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpPackFailureLog_" + Guid.NewGuid().ToString("N"));
        try
        {
            ReportPackFailureDiagnostics.Record(new("SCHEDULED_REPORT_PACK", new(2026, 10, 9), 1205,
                new InvalidOperationException("Customer Secret message", Sql(1205))), directory);

            var line = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "diagnostics-*.jsonl")));
            Assert.Contains("\"EventId\":\"SCHEDULED_REPORT_PACK_FAILED.SQL1205\"", line);
            Assert.Contains("\"Source\":\"Automation.ReportPack\"", line);
            Assert.Contains("20261009", line);
            Assert.Contains("System.InvalidOperationException", line);
            Assert.DoesNotContain("Secret", line);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Report_pack_event_id_without_sql_number_is_plain() =>
        Assert.Equal("AUTO_REPORT_PACK_FAILED", ReportPackFailureDiagnostics.EventId(
            new("AUTO_REPORT_PACK", new(2026, 10, 9), null, new IOException())));
}
