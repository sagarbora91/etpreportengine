using System.Reflection;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// IF-014: SqlClient can time out waiting for a COMMIT that SQL Server then completes. The
// import must be judged by what the database holds, not by the lost reply.
public sealed class ImportCommitTimeoutSqlTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Commit_timeout_reports_the_import_by_what_the_database_committed(bool serverCommitted)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
            var commits = 0;
            async Task LostReply(SqlTransaction transaction, CancellationToken token)
            {
                commits++;
                if (serverCommitted) await transaction.CommitAsync(token);
                throw SqlTimeout();
            }
            var reported = new List<FolderImportFailure>();
            var persistence = new SqlServerImportPersistenceUseCase(database.ConnectionString, null, LostReply);
            var file = Assert.Single((await new FolderImportService(persistence, reportFailure: reported.Add)
                .RunAsync(path, new("Synthetic Owner"))).Files);

            Assert.Equal(1, commits);
            var history = Assert.Single(await new SqlServerImportHistoryQuery(database.ConnectionString)
                .LoadAsync(new(file.PeriodStart!.Value, file.PeriodEnd!.Value, file.StoreCode)));
            if (serverCommitted)
            {
                Assert.Equal("Imported", file.Status);
                Assert.True(file.NewRows > 0);
                Assert.Equal("Imported", history.Result.Status);
                Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_batches WHERE status='Completed'")));
                Assert.Equal(file.NewRows, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines")));
                Assert.Empty(reported);
            }
            else
            {
                Assert.Equal("Failed", file.Status);
                Assert.Equal("The import timed out and can be retried.", file.Message);
                Assert.Equal("Failed", history.Result.Status);
                Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files")));
                Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_batches")));
                var failure = Assert.Single(reported);
                Assert.IsType<SqlException>(failure.Exception);
                Assert.Equal(-2, failure.SqlErrorNumber);
                Assert.NotNull(failure.BatchId);
                Assert.Equal(Path.GetFileName(path), failure.FileName);
            }
        }
        finally { await database.DisposeAsync(); }
    }

    // SqlException has no public constructor; build a client timeout through SqlClient's own factory.
    private static SqlException SqlTimeout()
    {
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        var errorConstructor = typeof(SqlError).GetConstructors(Any).OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = errorConstructor.GetParameters().Select<ParameterInfo, object?>((parameter, index) => index == 0 ? -2 : parameter.ParameterType switch
        {
            var type when type == typeof(string) => "Synthetic COMMIT timeout.",
            var type when type == typeof(byte) => (byte)0,
            var type when type == typeof(int) => 0,
            var type when type == typeof(uint) => 0u,
            _ => null
        }).ToArray();
        var errors = (SqlErrorCollection)typeof(SqlErrorCollection).GetConstructors(Any).Single(c => c.GetParameters().Length == 0).Invoke(null);
        typeof(SqlErrorCollection).GetMethod("Add", Any)!.Invoke(errors, [errorConstructor.Invoke(arguments)]);
        var create = typeof(SqlException).GetMethods(Any).First(method => method.Name == "CreateException" &&
            method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(SqlErrorCollection), typeof(string)]));
        return (SqlException)create.Invoke(null, [errors, "16.0"])!;
    }
}
