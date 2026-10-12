using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Connections;

/// <summary>
/// The one way new code gets a SQL connection (1.12 A1). Every connection it hands out was validated by the
/// endpoint policy for its purpose; <see cref="Create"/> never opens it, so a refused endpoint fails before any socket.
/// </summary>
public interface IEtpConnectionFactory
{
    /// <summary>The ETP database the connections point at (never <c>master</c>).</summary>
    string DatabaseName { get; }

    /// <summary>A validated, unopened connection for the purpose.</summary>
    SqlConnection Create(EtpConnectionPurpose purpose = EtpConnectionPurpose.Default);

    /// <summary>
    /// An open connection for the purpose. For <see cref="EtpConnectionPurpose.CommitBudget"/> a stopped or
    /// unreachable server is first found by a login under the 5 s cap; only a reachable server gets the budget connection.
    /// </summary>
    Task<SqlConnection> OpenAsync(EtpConnectionPurpose purpose = EtpConnectionPurpose.Default, CancellationToken ct = default);
}

/// <summary>
/// The factory for one database target. The policy is read once, at construction (by default the process-wide
/// <see cref="EtpSqlEndpoints.Policy"/>); construction throws <see cref="ArgumentException"/> for a refused target.
/// </summary>
public sealed class EtpConnectionFactory : IEtpConnectionFactory
{
    private readonly string defaultConnectionString;
    private readonly string commitBudgetConnectionString;
    private readonly string commitCheckConnectionString;
    private readonly string maintenanceConnectionString;

    public EtpConnectionFactory(string connectionString, ISqlEndpointPolicy? policy = null)
    {
        Policy = policy ?? EtpSqlEndpoints.Policy;
        defaultConnectionString = EtpConnectionStrings.Validate(connectionString, Policy, EtpConnectionPurpose.Default);
        commitBudgetConnectionString = EtpConnectionStrings.Validate(connectionString, Policy, EtpConnectionPurpose.CommitBudget);
        commitCheckConnectionString = EtpConnectionStrings.Validate(connectionString, Policy, EtpConnectionPurpose.CommitCheck);
        maintenanceConnectionString = EtpConnectionStrings.Validate(connectionString, Policy, EtpConnectionPurpose.Maintenance);
        DatabaseName = new SqlConnectionStringBuilder(defaultConnectionString).InitialCatalog;
    }

    public ISqlEndpointPolicy Policy { get; }

    public string DatabaseName { get; }

    /// <summary>
    /// The validated Default string, for the 1.11 adapters that still take a string (they validate it again). Only a
    /// composition root reads it; screens never see it (ArchitectureGuardTests).
    /// </summary>
    public string ConnectionString => defaultConnectionString;

    /// <summary>The validated string for a purpose (same rules as <see cref="Create"/>).</summary>
    public string ConnectionStringFor(EtpConnectionPurpose purpose) => purpose switch
    {
        EtpConnectionPurpose.CommitBudget => commitBudgetConnectionString,
        EtpConnectionPurpose.CommitCheck => commitCheckConnectionString,
        EtpConnectionPurpose.Maintenance => maintenanceConnectionString,
        EtpConnectionPurpose.Default => defaultConnectionString,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };

    public SqlConnection Create(EtpConnectionPurpose purpose = EtpConnectionPurpose.Default) => new(ConnectionStringFor(purpose));

    public async Task<SqlConnection> OpenAsync(EtpConnectionPurpose purpose = EtpConnectionPurpose.Default, CancellationToken ct = default)
    {
        if (purpose == EtpConnectionPurpose.CommitBudget)
        {
            await using var probe = Create(EtpConnectionPurpose.Default);
            await probe.OpenAsync(ct).ConfigureAwait(false);
        }
        var connection = Create(purpose);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
        return connection;
    }
}
