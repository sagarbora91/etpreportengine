using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>One boundary for settings, adapters and maintenance connection targets.</summary>
public static class LocalSqlConnectionPolicy
{
    public static string Validate(string connectionString)
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
        if (builder.ConnectTimeout == 0 || builder.ConnectTimeout > 5) builder.ConnectTimeout = 5;
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
