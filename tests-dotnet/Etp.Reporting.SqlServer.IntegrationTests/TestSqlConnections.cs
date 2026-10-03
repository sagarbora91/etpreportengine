using System.Data.Common;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

internal static class TestSqlConnections
{
    // Add settings here, never with a SqlConnectionStringBuilder round trip: that builder writes
    // Encrypt=Optional back as Encrypt=False, which LocalSqlConnectionPolicy refuses.
    internal static string ForDatabase(string database, bool pooling = true, string? applicationName = null)
    {
        // Retain the supplied Encrypt spelling until the production policy validates it.
        var raw = new DbConnectionStringBuilder
        {
            ConnectionString = Environment.GetEnvironmentVariable("ETP_TEST_SQL_CONNECTION")
                ?? @"Server=.\SQLEXPRESS;Integrated Security=True;Encrypt=Optional;Connect Timeout=5"
        };
        raw.Remove("Initial Catalog");
        raw["Database"] = database;
        raw["Pooling"] = pooling;
        if (applicationName is not null) raw["Application Name"] = applicationName;
        return LocalSqlConnectionPolicy.Validate(raw.ConnectionString);
    }
}
