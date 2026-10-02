using System.Text.Json;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

// Findings A and B, Workpc, 2 Oct 2026. An unelevated Owner's user save was refused by SQL
// Server with error 4613 "Grantor does not have GRANT permission.", and both the screen and
// the diagnostics log said only that something had failed.
public sealed class UserAccessFailureTests
{
    private const string Generic = "The action could not be completed. Technical details are available in the support package.";

    [Theory]
    [InlineData(4613, "Grantor does not have GRANT permission.")]
    [InlineData(15247, "User does not have permission to perform this action.")]
    [InlineData(15151, @"Cannot find the login 'WORKPC\Clerk', because it does not exist or you do not have permission.")]
    public void A_missing_server_right_tells_the_Owner_to_run_ETP_as_administrator(int number, string message)
    {
        var exception = SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, message, Procedure: "configure_application_role", Line: 61));

        var described = DesktopFriendlyError.DescribeUserAccessFailure(exception);

        Assert.Equal(DesktopFriendlyError.UserAccessNeedsElevationMessage, described);
        Assert.Contains("Run as administrator", described, StringComparison.Ordinal);
        Assert.DoesNotContain(@"WORKPC\Clerk", described, StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusal_is_found_even_behind_another_error_in_the_same_batch()
    {
        var exception = SqlExceptionFactory.Create(
            new SqlExceptionFactory.Error(3902, "The COMMIT TRANSACTION request has no corresponding BEGIN TRANSACTION."),
            new SqlExceptionFactory.Error(4613, "Grantor does not have GRANT permission."));

        Assert.Equal(DesktopFriendlyError.UserAccessNeedsElevationMessage, DesktopFriendlyError.DescribeUserAccessFailure(exception));
    }

    [Fact]
    public void An_account_Windows_cannot_find_is_named_as_such_without_echoing_it()
    {
        var exception = SqlExceptionFactory.Create(
            new SqlExceptionFactory.Error(15401, @"Windows NT user or group 'TFRROWJLTULT009\Sagar' not found. Check the name again."));

        var described = DesktopFriendlyError.DescribeUserAccessFailure(exception);

        Assert.Equal(DesktopFriendlyError.UserAccountNotFoundMessage, described);
        Assert.DoesNotContain("TFRROWJLTULT009", described, StringComparison.Ordinal);
    }

    // Migration 0043: the three refusals dbo.configure_application_role makes around the
    // Owner's ALTER ANY LOGIN WITH GRANT OPTION. Without these the screen said "The database
    // rejected this change. Review the inputs and day status."
    [Theory]
    [InlineData(51472, "You cannot take away your own Owner access. Ask another Owner to change your account.", "Ask another Owner")]
    [InlineData(51473, "SQL Server kept the right of this account to manage logins (ALTER ANY LOGIN) because another account granted it. Nothing was changed. Start ETP with Run as administrator and save again.", "Run as administrator")]
    [InlineData(51474, @"This account gave you your own right to manage logins, so taking away its Owner access would take yours too. Nothing was changed. See docs\OPERATIONS.md, Owners and SQL Server logins.", "would take yours too")]
    public void The_owner_grant_option_refusals_say_what_happened_and_that_nothing_changed(int number, string message, string advice)
    {
        var described = DesktopFriendlyError.DescribeUserAccessFailure(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, message, Procedure: "configure_application_role", Line: 140)));

        Assert.Contains(advice, described, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", described.Split(" A SQL administrator")[0], StringComparison.Ordinal);
        Assert.NotEqual("The database rejected this change. Review the inputs and day status.", described);
        // Elsewhere these numbers are not about users.
        Assert.Equal("The database rejected this change. Review the inputs and day status.",
            DesktopFriendlyError.Describe(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(number, message))));
    }

    [Fact]
    public void A_procedure_older_than_0043_refusing_a_grant_option_holder_asks_for_setup()
    {
        var exception = SqlExceptionFactory.Create(new SqlExceptionFactory.Error(4611, "To revoke or deny grantable privileges, specify the CASCADE option.", Procedure: "configure_application_role", Line: 116));

        Assert.Equal(DesktopFriendlyError.UserAccessNeedsUpdateMessage, DesktopFriendlyError.DescribeUserAccessFailure(exception));
    }

    [Fact]
    public void Other_failures_keep_the_existing_wording()
    {
        Assert.Equal(Generic, DesktopFriendlyError.DescribeUserAccessFailure(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(8152, "String or binary data would be truncated."))));
        Assert.Equal("Owner permission is required.", DesktopFriendlyError.DescribeUserAccessFailure(new UnauthorizedAccessException("x")));
        Assert.Equal("Select Owner, Store Manager or Viewer.", DesktopFriendlyError.DescribeUserAccessFailure(new ArgumentException("Select Owner, Store Manager or Viewer.")));
    }

    [Fact]
    public void Screens_other_than_users_do_not_borrow_the_elevation_advice()
    {
        // 4613 elsewhere would mean something else; only the Users save knows what it ran.
        Assert.Equal(Generic, DesktopFriendlyError.Describe(SqlExceptionFactory.Create(new SqlExceptionFactory.Error(4613, "Grantor does not have GRANT permission."))));
    }

    [Fact]
    public void Diagnostics_record_the_sql_error_number_state_procedure_and_line_but_not_its_text()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etp-diagnostics-{Guid.NewGuid():N}");
        try
        {
            var exception = SqlExceptionFactory.Create(
                new SqlExceptionFactory.Error(15401, @"Windows NT user or group 'TFRROWJLTULT009\Sagar' not found. Check the name again.", State: 1, Class: 16, Procedure: "configure_application_role", Line: 12),
                new SqlExceptionFactory.Error(4613, "Grantor does not have GRANT permission.", State: 3, Class: 16, Procedure: "", Line: 1));

            DesktopDiagnostics.Record(exception, "OperationsAdministration.Administration", "USER_ACCESS_SAVE_FAILED", logDirectory: directory);

            var line = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "diagnostics-*.jsonl")));
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            Assert.Equal("USER_ACCESS_SAVE_FAILED", root.GetProperty("EventId").GetString());
            Assert.Equal("Microsoft.Data.SqlClient.SqlException", root.GetProperty("ExceptionType").GetString());
            var errors = root.GetProperty("SqlErrors").EnumerateArray().ToArray();
            Assert.Equal(2, errors.Length);
            Assert.Equal(15401, errors[0].GetProperty("Number").GetInt32());
            Assert.Equal(1, errors[0].GetProperty("State").GetInt32());
            Assert.Equal(16, errors[0].GetProperty("Class").GetInt32());
            Assert.Equal("configure_application_role", errors[0].GetProperty("Procedure").GetString());
            Assert.Equal(12, errors[0].GetProperty("LineNumber").GetInt32());
            Assert.Equal(4613, errors[1].GetProperty("Number").GetInt32());
            Assert.Equal(3, errors[1].GetProperty("State").GetInt32());
            Assert.Equal("", errors[1].GetProperty("Procedure").GetString());

            // The log names no account and carries no SQL Server message text.
            Assert.DoesNotContain("TFRROWJLTULT009", line, StringComparison.Ordinal);
            Assert.DoesNotContain("Sagar", line, StringComparison.Ordinal);
            Assert.DoesNotContain("Grantor", line, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void A_wrapped_sql_error_is_still_recorded_and_unsafe_procedure_names_are_redacted()
    {
        var inner = SqlExceptionFactory.Create(new SqlExceptionFactory.Error(4613, "Grantor does not have GRANT permission.", Procedure: "proc with 'quoted' name", Line: 7));
        var errors = DesktopDiagnostics.SqlErrorsOf(new InvalidOperationException("wrapper", inner));

        var error = Assert.Single(errors!);
        Assert.Equal(4613, error.Number);
        Assert.Equal("redacted", error.Procedure);
        Assert.Equal(7, error.LineNumber);
    }

    [Fact]
    public void Diagnostics_without_a_sql_error_keep_their_existing_shape()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etp-diagnostics-{Guid.NewGuid():N}");
        try
        {
            DesktopDiagnostics.Record(new InvalidOperationException("x"), "Tests", "NO_SQL", logDirectory: directory);

            using var json = JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(directory))));
            Assert.False(json.RootElement.TryGetProperty("SqlErrors", out _));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void At_most_a_bounded_number_of_sql_errors_are_recorded()
    {
        var many = Enumerable.Range(1, DesktopDiagnostics.MaxSqlErrors + 5)
            .Select(index => new SqlExceptionFactory.Error(50000 + index, "x")).ToArray();

        Assert.Equal(DesktopDiagnostics.MaxSqlErrors, DesktopDiagnostics.SqlErrorsOf(SqlExceptionFactory.Create(many))!.Count);
    }
}
