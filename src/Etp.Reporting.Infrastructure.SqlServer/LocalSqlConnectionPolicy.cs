using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>One boundary for settings, adapters and maintenance connection targets.</summary>
public static class LocalSqlConnectionPolicy
{
    /// <summary>
    /// SqlClient times COMMIT and ROLLBACK with Connect Timeout, not CommandTimeout. On a slow
    /// disk an import or migration COMMIT can outlast the 5 s connection cap and be reported as
    /// failed after SQL Server has committed it, so those transactions get this budget instead.
    /// </summary>
    public const int CommitBudgetSeconds = 120;

    public static string Validate(string connectionString) => Validate(connectionString, null, pooling: true);

    /// <summary>The same local rules as <see cref="Validate(string)"/>, with the commit budget as Connect Timeout.</summary>
    public static string ValidateWithCommitBudget(string connectionString) =>
        Validate(connectionString, CommitBudgetSeconds, pooling: true);

    /// <summary>
    /// The connection that asks, after a failed COMMIT, whether the work landed. It has the commit
    /// budget and is never pooled, so the check cannot reuse the session whose reply was lost.
    /// </summary>
    public static string ValidateForCommitCheck(string connectionString) =>
        Validate(connectionString, CommitBudgetSeconds, pooling: false);

    private static string Validate(string connectionString, int? commitBudgetSeconds, bool pooling)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Enter local SQL Server connection settings.");
        SqlConnectionStringBuilder builder;
        DbConnectionStringBuilder raw;
        try { builder = new(connectionString); raw = new() { ConnectionString = connectionString }; }
        catch (ArgumentException) { throw new ArgumentException("The SQL Server connection settings are not valid."); }
        if (!builder.IntegratedSecurity || builder.Authentication != SqlAuthenticationMethod.NotSpecified ||
            Regex.IsMatch(connectionString, @"(?:^|;)\s*(?:User\s+ID|UID|User|Password|PWD)\s*=", RegexOptions.IgnoreCase))
            throw new ArgumentException("Only Windows integrated security without stored credentials can be used.");
        if (!IsLocalServer(builder.DataSource)) throw new ArgumentException("Choose a SQL Server instance on this computer.");
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)) throw new ArgumentException("The SQL Server connection must name a database.");
        if (builder.ShouldSerialize("AttachDbFilename") || builder.UserInstance || !string.IsNullOrEmpty(builder.FailoverPartner))
            throw new ArgumentException("Attached files, user instances and failover servers cannot be used.");
        // SqlClient maps False and Optional to the same value. Inspect the supplied spelling
        // before canonicalisation so an explicit insecure legacy setting is never retained.
        if (raw.TryGetValue("Encrypt", out var encryption) &&
            !new[] { "optional", "mandatory", "strict", "true", "yes" }.Contains(encryption?.ToString()?.Trim().ToLowerInvariant()))
            throw new ArgumentException("Use Encrypt=Optional for local SQL, or Mandatory/Strict with a trusted certificate; Encrypt=False is not allowed.");
        if (!raw.ContainsKey("Encrypt")) builder.Encrypt = SqlConnectionEncryptOption.Optional;
        if (commitBudgetSeconds is { } budget) builder.ConnectTimeout = budget;
        else if (builder.ConnectTimeout == 0 || builder.ConnectTimeout > 5) builder.ConnectTimeout = 5;
        if (!pooling) builder.Pooling = false;
        // Older SqlClient versions serialize Optional as False. Keep the public
        // validated representation explicit and safe to validate again.
        if (builder.Encrypt == SqlConnectionEncryptOption.Optional)
        {
            builder.Remove("Encrypt");
            return builder.ConnectionString + ";Encrypt=Optional";
        }
        return builder.ConnectionString;
    }

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
