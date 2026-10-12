using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Connections;

/// <summary>
/// What an ETP connection may point at (1.12 A1, ARCHITECTURE-AUDIT finding 1). One process has one policy, set
/// once by the composition root (<see cref="EtpSqlEndpoints"/>). 1.12 ships only <see cref="LocalOnlySqlEndpointPolicy"/>
/// (decision 44 #6: the seam only, the C+ plan keeps the database on this PC).
/// </summary>
public interface ISqlEndpointPolicy
{
    /// <summary>The settings name of the policy ("LocalOnly").</summary>
    string Name { get; }

    /// <summary>
    /// Judges a parsed candidate before anything connects. It never opens a socket. Rules that every ETP connection
    /// keeps whatever the policy (no stored credentials, no Encrypt=False) are checked by the caller as well.
    /// </summary>
    SqlEndpointDecision Evaluate(SqlConnectionStringBuilder candidate);
}

/// <summary>The policy's answer. A refusal carries the sentence shown to the person.</summary>
public sealed record SqlEndpointDecision(bool Allowed, string? RefusalMessage)
{
    public static SqlEndpointDecision Allow { get; } = new(true, null);

    public static SqlEndpointDecision Refuse(string message) =>
        new(false, string.IsNullOrWhiteSpace(message) ? "This SQL Server connection is not allowed." : message);
}

/// <summary>Why a connection is opened. The purpose picks timeouts and pooling; the endpoint rules are the same.</summary>
public enum EtpConnectionPurpose
{
    /// <summary>Ordinary reads and short writes: 5 s login cap, pooled.</summary>
    Default,

    /// <summary>
    /// A transaction whose COMMIT may outlast the 5 s cap on a slow disk (imports, migrations). SqlClient times COMMIT
    /// with Connect Timeout, so this connection gets <see cref="LocalSqlConnectionPolicy.CommitBudgetSeconds"/>.
    /// </summary>
    CommitBudget,

    /// <summary>The check after a failed COMMIT: never pooled, so it cannot reuse the session whose reply was lost.</summary>
    CommitCheck,

    /// <summary>The same server's <c>master</c> database, never pooled: create, restore and drop steps.</summary>
    Maintenance
}
