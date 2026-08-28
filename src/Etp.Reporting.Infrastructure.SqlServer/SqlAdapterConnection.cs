using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

internal static class SqlAdapterConnection
{
    public static string RequireWindowsIntegrated(string connectionString, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", parameterName);

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("A valid SQL Server connection string is required.", parameterName, exception);
        }

        if (!builder.IntegratedSecurity ||
            !string.IsNullOrWhiteSpace(builder.UserID) ||
            !string.IsNullOrWhiteSpace(builder.Password))
            throw new ArgumentException(
                "The SQL adapter requires Windows-integrated SQL Server security without SQL credentials.",
                parameterName);

        return builder.ConnectionString;
    }
}
