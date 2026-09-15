using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class PhaseFourBoundaryTests
{
    [Theory]
    [InlineData(@".\SQLEXPRESS")]
    [InlineData("(local)")]
    [InlineData("localhost")]
    [InlineData(@"lpc:.\SQLEXPRESS")]
    [InlineData(@"np:.\SQLEXPRESS")]
    [InlineData(@"np:\\localhost\pipe\MSSQL$SQLEXPRESS\sql\query")]
    [InlineData(@"(localdb)\MSSQLLocalDB")]
    public void Local_integrated_connections_remain_usable(string server)
    {
        var result = new SqlConnectionStringBuilder(LocalSqlConnectionPolicy.Validate($"Server={server};Database=Test;Integrated Security=True"));
        Assert.Equal(SqlConnectionEncryptOption.Optional, result.Encrypt);
        Assert.False(result.TrustServerCertificate);
        Assert.Equal(5, result.ConnectTimeout);
        Assert.Equal(LocalSqlConnectionPolicy.Validate($"Server={server};Database=Test;Integrated Security=True"), LocalSqlConnectionPolicy.Validate(LocalSqlConnectionPolicy.Validate($"Server={server};Database=Test;Integrated Security=True")));
    }

    [Theory]
    [InlineData("Server=remotehost;Database=Test;Integrated Security=True")]
    [InlineData(@"Server=np:\\remotehost\pipe\sql\query;Database=Test;Integrated Security=True")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;Encrypt=False")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;Encrypt=no")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;AttachDbFilename=C:\\secret.mdf")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;Failover Partner=remotehost")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;UID=secret")]
    [InlineData("Server=tcp:localhost,1433;Database=Test;Integrated Security=True")]
    public async Task Unsafe_connection_is_rejected_before_network_access(string value)
    {
        Assert.Throws<ArgumentException>(() => LocalSqlConnectionPolicy.Validate(value));
        var health = await new SqlServerHealthCheck(value).CheckAsync();
        Assert.Equal(DatabaseHealthStatus.InvalidConfiguration, health.Status);
        Assert.DoesNotContain("secret", health.Message);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Legacy_checksum_accepts_only_line_ending_changes(string newline)
    {
        const string sql = "SELECT 1;\nSELECT 2;\n";
        var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql.Replace("\n", newline))));
        var current = new MigrationScript("test", MigrationChecksum.Compute(sql), sql, "test.sql");
        Assert.Empty(MigrationPlanner.Plan([current], [new("test", legacyHash, DateTimeOffset.UtcNow)]));
        Assert.Throws<MigrationIntegrityException>(() => MigrationPlanner.Plan([current with { Sql = "SELECT 9;", Checksum = MigrationChecksum.Compute("SELECT 9;") }], [new("test", legacyHash, DateTimeOffset.UtcNow)]));
        Assert.Equal(MigrationChecksum.Compute(sql), MigrationChecksum.Compute(sql.Replace("\n", "\r\n")));
    }
}
