using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class AggregateAuditSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Aggregate_counts_pass_client_and_SQL_but_paths_and_identifiers_remain_blocked()
    {
        var repository = new OperationalAuditRepository(database.ConnectionString);
        await repository.RecordAsync("ImportBatch", "Succeeded", "3 files imported");
        Assert.Contains(await repository.LoadRecentAsync(), x => x.SafeDetail == "3 files imported");
        await database.ExecuteAsync("CREATE USER audit_count_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER audit_count_manager;");
        await database.ExecuteAsync("EXECUTE AS USER='audit_count_manager'; EXEC dbo.record_operational_audit 'ImportBatch','Succeeded',N'42 rows skipped.'; REVERT;");
        await database.ExecuteAsync("CREATE USER audit_count_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER audit_count_viewer;");
        await database.ExecuteAsync("EXECUTE AS USER='audit_count_viewer'; EXEC dbo.record_operational_audit 'ImportBatch','Succeeded',N'0 reports generated'; REVERT;");
        foreach (var detail in new[] { "invoice 12345", "Invoice１２３", "３ files imported", "1234567890 files imported", @"3 files imported C:\private", "3 files imported 9876543210", "folder/file" })
        {
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("EXECUTE AS USER='audit_count_viewer'; EXEC dbo.record_operational_audit 'ImportBatch','Succeeded',@detail;", connection);
            command.Parameters.AddWithValue("@detail", detail);
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(51310, error.Number);
        }
        Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE safe_detail IN(N'3 files imported',N'42 rows skipped.')"));
    }
}
