using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// One boundary for settings, adapters and maintenance connection targets. Since 1.12 (A1) this is a facade: the
/// public methods are unchanged for the 1.11 call sites, and the endpoint rules come from <see cref="EtpSqlEndpoints.Policy"/>
/// (LocalOnly unless the composition root set another). New code takes an <see cref="IEtpConnectionFactory"/> instead.
/// </summary>
public static class LocalSqlConnectionPolicy
{
    /// <summary>
    /// SqlClient times COMMIT and ROLLBACK with Connect Timeout, not CommandTimeout. On a slow
    /// disk an import or migration COMMIT can outlast the 5 s connection cap and be reported as
    /// failed after SQL Server has committed it, so those transactions get this budget instead.
    /// </summary>
    public const int CommitBudgetSeconds = 120;

    /// <summary>
    /// Login limit of the check that follows a failed COMMIT. SQL Server is busy then, so it gets
    /// more than the 5 s cap, but a stopped server must not hold the operator for the whole budget.
    /// </summary>
    public const int CommitCheckConnectSeconds = 30;

    public static string Validate(string connectionString) =>
        EtpConnectionStrings.Validate(connectionString, EtpSqlEndpoints.Policy, EtpConnectionPurpose.Default);

    /// <summary>
    /// The same rules as <see cref="Validate(string)"/>, with the commit budget as Connect Timeout.
    /// Open it with <see cref="OpenWithCommitBudgetAsync"/>, so the budget does not also become the login wait.
    /// </summary>
    public static string ValidateWithCommitBudget(string connectionString) =>
        EtpConnectionStrings.Validate(connectionString, EtpSqlEndpoints.Policy, EtpConnectionPurpose.CommitBudget);

    /// <summary>
    /// The connection that asks, after a failed COMMIT, whether the work landed. It is never pooled,
    /// so the check cannot reuse the session whose reply was lost; its query gets the commit budget.
    /// </summary>
    public static string ValidateForCommitCheck(string connectionString) =>
        EtpConnectionStrings.Validate(connectionString, EtpSqlEndpoints.Policy, EtpConnectionPurpose.CommitCheck);

    /// <summary>
    /// Opens a connection for a transaction that needs the commit budget. Connect Timeout is also
    /// SqlClient's login wait, so a stopped or unreachable server is first found by a login under
    /// the 5 s cap; only a reachable server is given the budget connection.
    /// </summary>
    public static Task<SqlConnection> OpenWithCommitBudgetAsync(string connectionString, CancellationToken cancellationToken) =>
        new EtpConnectionFactory(connectionString).OpenAsync(EtpConnectionPurpose.CommitBudget, cancellationToken);

    /// <summary>The LocalOnly host rule (kept public for the 1.11 callers and tests).</summary>
    public static bool IsLocalServer(string? value) => LocalOnlySqlEndpointPolicy.IsLocalServer(value);
}
