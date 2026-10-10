using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.TestSupport;
using Microsoft.Data.SqlClient;
using App = Etp.Reporting.Application.DatabaseLifecycle;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>1.9.9 lane startup-connection: IE-CODE-07/08 classification and IE-CODE-09 pack text.</summary>
public sealed class DatabaseConnectionFailureTests
{
    private static SqlException Sql(params int[] numbers) =>
        SqlExceptionFactory.Create(numbers.Select(number => new SqlExceptionFactory.Error(number, $"server text {number} 'Customer Secret'")).ToArray());

    [Theory]
    [InlineData(18456, DatabaseFailureKind.LoginRefused)]
    [InlineData(18452, DatabaseFailureKind.LoginRefused)]
    [InlineData(4060, DatabaseFailureKind.DatabaseMissing)]
    [InlineData(911, DatabaseFailureKind.DatabaseMissing)]
    [InlineData(229, DatabaseFailureKind.PermissionDenied)]
    [InlineData(262, DatabaseFailureKind.PermissionDenied)]
    [InlineData(297, DatabaseFailureKind.PermissionDenied)]
    [InlineData(916, DatabaseFailureKind.PermissionDenied)]
    [InlineData(2, DatabaseFailureKind.ServerUnreachable)]
    [InlineData(53, DatabaseFailureKind.ServerUnreachable)]
    [InlineData(26, DatabaseFailureKind.ServerUnreachable)]
    [InlineData(10061, DatabaseFailureKind.ServerUnreachable)]
    [InlineData(-2, DatabaseFailureKind.Timeout)]
    [InlineData(208, DatabaseFailureKind.Other)]
    [InlineData(51240, DatabaseFailureKind.Other)]
    public void Sql_numbers_classify_the_connection_failure(int number, DatabaseFailureKind expected)
    {
        Assert.Equal(expected, DatabaseConnectionFailure.Classify(number));
        Assert.Equal(expected, DatabaseConnectionFailure.Classify(Sql(number)));
    }

    [Fact]
    public void Most_specific_kind_wins_and_inner_exceptions_are_searched()
    {
        Assert.Equal(DatabaseFailureKind.DatabaseMissing, DatabaseConnectionFailure.Classify(Sql(18456, 4060)));
        Assert.Equal(DatabaseFailureKind.PermissionDenied,
            DatabaseConnectionFailure.Classify(new AggregateException(new InvalidOperationException("wrap", Sql(229)))));
        Assert.Equal([18456, 4060], DatabaseConnectionFailure.SqlNumbers(new InvalidOperationException("wrap", Sql(18456, 4060))));
        Assert.Equal(4060, DatabaseConnectionFailure.DecidingNumber(Sql(18456, 4060), DatabaseFailureKind.DatabaseMissing));
    }

    [Fact]
    public void Non_sql_failures_are_other_or_invalid_configuration()
    {
        Assert.Equal(DatabaseFailureKind.InvalidConfiguration, DatabaseConnectionFailure.Classify(new ArgumentException("bad")));
        Assert.Equal(DatabaseFailureKind.Other, DatabaseConnectionFailure.Classify(new InvalidOperationException("bug")));
        Assert.Equal(DatabaseFailureKind.Other, DatabaseConnectionFailure.Classify((Exception?)null));
        Assert.False(DatabaseConnectionFailure.IsConnectionClass(DatabaseFailureKind.Other));
        Assert.False(DatabaseConnectionFailure.IsConnectionClass(DatabaseFailureKind.InvalidConfiguration));
        Assert.True(DatabaseConnectionFailure.IsConnectionClass(DatabaseFailureKind.LoginRefused));
    }

    [Theory]
    [InlineData(18456, DatabaseFailureKind.LoginRefused, DatabaseConnectionFailure.LoginRefusedMessage)]
    [InlineData(4060, DatabaseFailureKind.DatabaseMissing, DatabaseConnectionFailure.DatabaseMissingMessage)]
    [InlineData(229, DatabaseFailureKind.PermissionDenied, DatabaseConnectionFailure.PermissionDeniedMessage)]
    [InlineData(53, DatabaseFailureKind.ServerUnreachable, DatabaseConnectionFailure.ServerUnreachableMessage)]
    [InlineData(-2, DatabaseFailureKind.Timeout, DatabaseConnectionFailure.TimeoutMessage)]
    public void Connection_test_reports_login_database_and_permission_distinctly_with_the_number(
        int number, DatabaseFailureKind kind, string message)
    {
        var health = SqlServerHealthCheck.Failed(Sql(number), TimeSpan.FromMilliseconds(5));

        Assert.Equal(DatabaseHealthStatus.Unreachable, health.Status);
        Assert.Equal(kind, health.FailureKind);
        Assert.Equal(number, health.SqlErrorNumber);
        Assert.Equal($"{message} (SQL error {number})", health.Message);
        Assert.DoesNotContain("Secret", health.Message);
    }

    [Fact]
    public void Unclassified_connection_test_failure_still_reads_as_unreachable_without_exception_text()
    {
        var health = SqlServerHealthCheck.Failed(new InvalidOperationException("Customer Secret"), null);

        Assert.Equal(DatabaseFailureKind.ServerUnreachable, health.FailureKind);
        Assert.Null(health.SqlErrorNumber);
        Assert.Equal(DatabaseConnectionFailure.ServerUnreachableMessage, health.Message);
    }

    [Fact]
    public void Login_failure_message_no_longer_says_unreachable()
    {
        Assert.DoesNotContain("could not be reached", DatabaseConnectionFailure.LoginRefusedMessage);
        Assert.DoesNotContain("could not be reached", DatabaseConnectionFailure.DatabaseMissingMessage);
        Assert.DoesNotContain("could not be reached", DatabaseConnectionFailure.PermissionDeniedMessage);
        Assert.DoesNotContain("could not be reached", DatabaseConnectionFailure.TimeoutMessage);
    }

    [Theory]
    [InlineData(DatabaseHealthStatus.Unreachable, 18456)]
    [InlineData(DatabaseHealthStatus.Unreachable, null)]
    public async Task Lifecycle_service_passes_the_sql_number_to_the_application_contract(DatabaseHealthStatus status, int? number)
    {
        var service = new SqlServerDatabaseLifecycleService(new HealthOnlyGateway(new(status, "detail", SqlErrorNumber: number)));

        var health = await service.CheckHealthAsync();

        Assert.Equal(App.DatabaseConnectionStatus.Unreachable, health.Status);
        Assert.Equal(number, health.SqlErrorNumber);
    }

    [Fact]
    public void Failed_pack_history_text_names_the_connection_problem_and_number()
    {
        var text = AutomatedOperationsService.PackFailureMessage(new InvalidOperationException("Customer Secret", Sql(18456)));

        Assert.Equal($"Report generation failed: {DatabaseConnectionFailure.LoginRefusedMessage} (SQL error 18456) Details are in the application diagnostics log.", text);
    }

    [Fact]
    public void Failed_pack_history_text_for_other_failures_has_no_exception_text()
    {
        Assert.Equal("Report generation failed: The database refused the report query. (SQL error 8134) Details are in the application diagnostics log.",
            AutomatedOperationsService.PackFailureMessage(Sql(8134)));
        Assert.Equal("Report generation failed: An unexpected NullReferenceException occurred. Details are in the application diagnostics log.",
            AutomatedOperationsService.PackFailureMessage(new NullReferenceException("Customer Secret")));
        Assert.StartsWith("Report generation failed: The report file could not be written.",
            AutomatedOperationsService.PackFailureMessage(new IOException("C:\\Customer Secret.xlsx")));
        Assert.True(AutomatedOperationsService.PackFailureMessage(new IOException(new string('x', 2000))).Length <= 500);
    }

    private sealed class HealthOnlyGateway(DatabaseHealth health) : IDatabaseLifecycleSqlGateway
    {
        public Task<DatabaseHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(health);
        public Task<DatabaseBootstrapResult> BootstrapAsync(string migrationDirectory, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task RecordAuditAsync(string eventType, string outcome, string? safeDetail, string? actorName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
