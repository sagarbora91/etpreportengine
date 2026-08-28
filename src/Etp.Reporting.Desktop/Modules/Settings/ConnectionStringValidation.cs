using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed record ConnectionStringValidation(
    bool IsValid,
    string? ConnectionString,
    string? Error)
{
    private static readonly Regex CredentialKeyword = new(
        @"(?:^|;)\s*(?:User\s+ID|UID|User|Password|PWD)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ConnectionStringValidation Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Invalid("A SQL Server connection string is required.");

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(value);
        }
        catch (ArgumentException)
        {
            return Invalid("The SQL Server connection string is not valid.");
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            return Invalid("The SQL Server connection string must name a server.");
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            return Invalid("The SQL Server connection string must name a database.");
        if (!builder.IntegratedSecurity)
            return Invalid("Only Windows integrated security connections can be used.");
        if (ContainsCredentialKeyword(value))
            return Invalid("User names and passwords cannot be retained in desktop settings.");

        return new(true, builder.ConnectionString, null);
    }

    private static ConnectionStringValidation Invalid(string error) =>
        new(false, null, error);

    private static bool ContainsCredentialKeyword(string connectionString)
        => CredentialKeyword.IsMatch(connectionString);
}
