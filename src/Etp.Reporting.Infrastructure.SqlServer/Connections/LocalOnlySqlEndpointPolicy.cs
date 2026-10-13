using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Connections;

/// <summary>
/// Today's rules, moved from <see cref="LocalSqlConnectionPolicy"/> without change: Windows integrated security, a SQL
/// Server instance on this computer (shared memory or a local named pipe; never <c>tcp:</c>), a named database, and no
/// attached files, user instances or failover partners. The scripts keep the same rule (<c>Assert-EtpLocalSqlTarget</c>).
/// </summary>
public sealed class LocalOnlySqlEndpointPolicy : ISqlEndpointPolicy
{
    public const string PolicyName = "LocalOnly";

    internal const string CredentialsRefused = "Only Windows integrated security without stored credentials can be used.";
    internal const string RemoteRefused = "Choose a SQL Server instance on this computer.";
    internal const string DatabaseRequired = "The SQL Server connection must name a database.";
    internal const string AttachRefused = "Attached files, user instances and failover servers cannot be used.";

    public static LocalOnlySqlEndpointPolicy Instance { get; } = new();

    public string Name => PolicyName;

    public SqlEndpointDecision Evaluate(SqlConnectionStringBuilder candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IntegratedSecurity || candidate.Authentication != SqlAuthenticationMethod.NotSpecified ||
            !string.IsNullOrEmpty(candidate.UserID) || !string.IsNullOrEmpty(candidate.Password))
            return SqlEndpointDecision.Refuse(CredentialsRefused);
        if (!IsLocalServer(candidate.DataSource)) return SqlEndpointDecision.Refuse(RemoteRefused);
        if (string.IsNullOrWhiteSpace(candidate.InitialCatalog)) return SqlEndpointDecision.Refuse(DatabaseRequired);
        if (candidate.ShouldSerialize("AttachDbFilename") || candidate.UserInstance || !string.IsNullOrEmpty(candidate.FailoverPartner))
            return SqlEndpointDecision.Refuse(AttachRefused);
        return SqlEndpointDecision.Allow;
    }

    /// <summary>True for <c>.</c>, <c>(local)</c>, <c>localhost</c>, this computer's name, <c>(localdb)\x</c>, with an optional instance, over shared memory or a local pipe.</summary>
    public static bool IsLocalServer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var server = value.Trim();
        if (server.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase)) server = server[4..];
        else if (server.StartsWith("np:", StringComparison.OrdinalIgnoreCase))
        {
            server = server[3..];
            if (server.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var pipe = Regex.Match(server, @"^\\\\([^\\]+)\\pipe\\[A-Za-z0-9_$\\.-]+$", RegexOptions.IgnoreCase);
                return pipe.Success && IsLocalHost(pipe.Groups[1].Value);
            }
        }
        var match = Regex.Match(server, @"^(\.|\(local\)|localhost|\(localdb\)|[A-Za-z0-9_-]+)(?:\\([A-Za-z0-9_$-]+))?$", RegexOptions.IgnoreCase);
        return match.Success && (IsLocalHost(match.Groups[1].Value) ||
            (match.Groups[1].Value.Equals("(localdb)", StringComparison.OrdinalIgnoreCase) && match.Groups[2].Success));
    }

    private static bool IsLocalHost(string host) => host.Equals(".", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("(local)", StringComparison.OrdinalIgnoreCase) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}
