using System.Diagnostics;
using System.Data.Common;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class ConcurrentImportAttemptTests
{
    [Theory]
    [InlineData("R025")]
    [InlineData("R022")]
    [InlineData("R020")]
    [InlineData("R013")]
    public async Task Concurrent_identical_sources_record_one_import_and_one_duplicate_without_extra_facts(string report)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), report + "_*.xlsx").Single();
            var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(await new OpenXmlWorkbookReader().ReadAsync(path));
            var connectionBuilder = new DbConnectionStringBuilder { ConnectionString = database.ConnectionString };
            var applicationName = "ConcurrentImport_" + Guid.NewGuid().ToString("N");
            connectionBuilder["Application Name"] = applicationName;
            connectionBuilder["Pooling"] = false;
            var connectionString = connectionBuilder.ConnectionString;
            await using var gate = new SqlConnection(database.ConnectionString);
            await gate.OpenAsync();
            await using var transaction = (SqlTransaction)await gate.BeginTransactionAsync();
            await using var hold = new SqlCommand("""
                DECLARE @lock int;
                EXEC @lock=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
                IF @lock<0 THROW 51997,'Could not arrange concurrent import fixture.',1;
                SELECT @@SPID;
                """, gate, transaction);
            hold.Parameters.AddWithValue("@resource", $"ETP_IMPORT:{accepted.Scope.StoreCode}:{accepted.ProfileIdentity.ReportCode}");
            await hold.ExecuteScalarAsync();
            var first = new FolderImportService(new SqlServerImportPersistenceUseCase(connectionString))
                .RunFilesAsync([path], new("Concurrent fixture one"));
            var second = new FolderImportService(new SqlServerImportPersistenceUseCase(connectionString))
                .RunFilesAsync([path], new("Concurrent fixture two"));
            try
            {
                var elapsed = Stopwatch.StartNew();
                while (Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id WHERE s.program_name='{applicationName}' AND r.wait_type LIKE 'LCK_M_%'")) < 2)
                {
                    Assert.False(first.IsCompleted || second.IsCompleted, "Both attempts must reach the transaction-owned import lock before it is released.");
                    Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15), "Concurrent imports did not both reach the existing SQL import lock.");
                    await Task.Delay(25);
                }
            }
            finally
            {
                await transaction.CommitAsync();
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
            }
            var summaries = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
            var results = summaries.Select(summary => Assert.Single(summary.Files)).ToArray();
            Assert.Equal(new[] { "Duplicate", "Imported" }, results.Select(result => result.Status).Order());
            var duplicate = Assert.Single(results, result => result.Status == "Duplicate");
            Assert.Equal(0, duplicate.NewRows);
            Assert.Equal(accepted.Staging.Rows.Count, duplicate.AlreadyPresentRows);
            Assert.Equal(accepted.Staging.Rows.Count, results.Sum(result => result.NewRows));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_batches"));
            Assert.Equal(accepted.Staging.Rows.Count, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.[{EtpReportFamilyRegistry.Resolve(report).TableName}]"));
            if (report == "R025") Assert.Equal(accepted.Staging.Rows.Count, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_attempts"));
            var history = await new SqlServerImportHistoryQuery(database.ConnectionString).LoadAsync(
                new(accepted.Scope.PeriodStart!.Value, accepted.Scope.PeriodEnd!.Value, accepted.Scope.StoreCode));
            Assert.Equal(new[] { "Duplicate", "Imported" }, history.Select(entry => entry.Result.Status).Order());
        }
        finally { await database.DisposeAsync(); }
    }
}
