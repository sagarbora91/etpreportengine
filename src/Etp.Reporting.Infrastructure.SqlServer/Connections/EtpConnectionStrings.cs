using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Connections;

/// <summary>
/// Turns a supplied connection string into the validated one for a purpose. The rules every ETP connection keeps
/// whatever the endpoint policy are here; the endpoint rules are the policy's. Nothing here opens a socket.
/// </summary>
internal static class EtpConnectionStrings
{
    internal const int DefaultConnectSeconds = 5;
    internal const string Required = "Enter local SQL Server connection settings.";
    internal const string NotValid = "The SQL Server connection settings are not valid.";
    internal const string EncryptRefused = "Use Encrypt=Optional for local SQL, or Mandatory/Strict with a trusted certificate; Encrypt=False is not allowed.";

    private static readonly Regex CredentialKeyword = new(@"(?:^|;)\s*(?:User\s+ID|UID|User|Password|PWD)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] AcceptedEncrypt = ["optional", "mandatory", "strict", "true", "yes"];

    /// <summary>Throws <see cref="ArgumentException"/> with the person-facing reason when the string may not be used.</summary>
    public static string Validate(string connectionString, ISqlEndpointPolicy policy, EtpConnectionPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException(Required);
        SqlConnectionStringBuilder builder;
        DbConnectionStringBuilder raw;
        try { builder = new(connectionString); raw = new() { ConnectionString = connectionString }; }
        catch (ArgumentException) { throw new ArgumentException(NotValid); }
        // Stored credentials are refused for every policy (no SQL passwords on disk, audit finding 15).
        if (CredentialKeyword.IsMatch(connectionString)) throw new ArgumentException(LocalOnlySqlEndpointPolicy.CredentialsRefused);
        var decision = policy.Evaluate(builder) ?? throw new InvalidOperationException("The SQL endpoint policy gave no decision.");
        if (!decision.Allowed) throw new ArgumentException(decision.RefusalMessage ?? "This SQL Server connection is not allowed.");
        // SqlClient maps False and Optional to the same value. Inspect the supplied spelling
        // before canonicalisation so an explicit insecure legacy setting is never retained.
        if (raw.TryGetValue("Encrypt", out var encryption) && !AcceptedEncrypt.Contains(encryption?.ToString()?.Trim().ToLowerInvariant()))
            throw new ArgumentException(EncryptRefused);
        if (!raw.ContainsKey("Encrypt")) builder.Encrypt = SqlConnectionEncryptOption.Optional;
        switch (purpose)
        {
            case EtpConnectionPurpose.CommitBudget:
                builder.ConnectTimeout = LocalSqlConnectionPolicy.CommitBudgetSeconds;
                break;
            case EtpConnectionPurpose.CommitCheck:
                builder.ConnectTimeout = LocalSqlConnectionPolicy.CommitCheckConnectSeconds;
                builder.Pooling = false;
                break;
            case EtpConnectionPurpose.Maintenance:
                CapConnectTimeout(builder);
                builder.InitialCatalog = "master";
                builder.Pooling = false;
                break;
            default:
                CapConnectTimeout(builder);
                break;
        }
        // Older SqlClient versions serialize Optional as False. Keep the public
        // validated representation explicit and safe to validate again.
        if (builder.Encrypt == SqlConnectionEncryptOption.Optional)
        {
            builder.Remove("Encrypt");
            return builder.ConnectionString + ";Encrypt=Optional";
        }
        return builder.ConnectionString;
    }

    private static void CapConnectTimeout(SqlConnectionStringBuilder builder)
    {
        if (builder.ConnectTimeout == 0 || builder.ConnectTimeout > DefaultConnectSeconds) builder.ConnectTimeout = DefaultConnectSeconds;
    }
}
