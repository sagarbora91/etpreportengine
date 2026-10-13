using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Connections;

/// <summary>
/// The Settings screen's check of a typed connection string, moved from the Desktop's
/// <c>ConnectionStringValidation</c> in 1.12 (A1) so the Desktop does not parse SQL connection strings itself. The
/// sentences are the 1.11 ones; the last step is the endpoint policy (<see cref="LocalSqlConnectionPolicy"/>).
/// </summary>
public static class SettingsConnectionCheck
{
    private static readonly Regex CredentialKeyword = new(
        @"(?:^|;)\s*(?:User\s+ID|UID|User|Password|PWD)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The validated string, or the reason it cannot be used. Never opens a connection.</summary>
    public static (string? ConnectionString, string? Error) Check(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, "A SQL Server connection string is required.");

        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(value); }
        catch (ArgumentException) { return (null, "The SQL Server connection string is not valid."); }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            return (null, "The SQL Server connection string must name a server.");
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            return (null, "The SQL Server connection string must name a database.");
        if (!builder.IntegratedSecurity)
            return (null, "Only Windows integrated security connections can be used.");
        if (CredentialKeyword.IsMatch(value))
            return (null, "User names and passwords cannot be retained in desktop settings.");

        try { return (LocalSqlConnectionPolicy.Validate(value), null); }
        catch (ArgumentException exception) { return (null, exception.Message); }
    }
}
