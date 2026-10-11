using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.0 L4, design 5.4.3 (write detector): a full health check, including recording the run, changes no
/// <c>dbo</c> table. Every <c>dbo</c> table's <c>COUNT_BIG(*)</c> and <c>CHECKSUM_AGG(BINARY_CHECKSUM(*))</c> is taken
/// before and after; <c>diag.*</c> is excluded (the run writes its own rows there). <c>dbo.operational_audit</c> and
/// <c>dbo.automation_runs</c> must gain no row (no ReportRun audit, no automation run).
/// </summary>
public sealed class HealthCheckReadOnlyTests
{
    private const string TablesSql = """
        SELECT t.name FROM sys.tables AS t
        WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.is_ms_shipped = 0
        ORDER BY t.name;
        """;

    [Fact]
    public async Task A_full_health_check_changes_no_dbo_table()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedAsync(database);
            var before = await SnapshotAsync(database.ConnectionString);

            var store = new SqlServerHealthCheckStore(database.ConnectionString);
            var runner = new HealthCheckRunner(AllProbes(database.ConnectionString, database.MigrationDirectory, store), new HealthCheckRunnerOptions
            {
                Store = store,
                AppVersion = "1.11.0.0",
                MachineName = "TESTPC",
                RunByRole = "Owner",
                LoadRetailStoreCodes = _ => Task.FromResult<IReadOnlyList<string>>(["ZZSYN"])
            });
            var run = await runner.RunAsync(HealthCheckTrigger.Manual);
            await store.RecordRunAsync(run);

            var after = await SnapshotAsync(database.ConnectionString);

            Assert.Equal(HealthCheckId.All.Count, run.Results.Count);
            Assert.DoesNotContain(run.Results, line => line.FindingCode == HealthCheckTexts.ProbeFailed);
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            var changed = before.Where(pair => !Equals(pair.Value, after[pair.Key])).Select(pair => pair.Key).ToArray();
            Assert.True(changed.Length == 0, "The health check changed dbo tables: " + string.Join(", ", changed));
            Assert.Equal(before["operational_audit"].Rows, after["operational_audit"].Rows);
            Assert.Equal(before["automation_runs"].Rows, after["automation_runs"].Rows);
        }
        finally { await database.DisposeAsync(); }
    }

    /// <summary>
    /// Every probe of the health check. Integrator: when the lanes are merged, add the Errors (L2), Operations (L7),
    /// Imports and Data (L6) and Reports (L5) probes here exactly as the composition root (L9) wires them, so this test
    /// covers the whole check (design 5.4.3).
    /// </summary>
    private static IReadOnlyList<IHealthProbe> AllProbes(string connectionString, string migrationDirectory, IHealthCheckStore store) =>
    [
        .. DatabaseProbes.Create(connectionString, migrationDirectory),
        new SecurityProbes(store, automationPrincipal: null)
    ];

    /// <summary>
    /// Synthetic sales for yesterday (store ZZSYN, made-up amounts, the pattern of EveningReportsSqlTests), so the
    /// probes read real rows and an UPDATE of an existing row would show in the checksum.
    /// </summary>
    private static async Task SeedAsync(SqlDatabaseFixture database)
    {
        await database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@day date=CONVERT(date,DATEADD(day,-1,SYSDATETIME()));
            DECLARE @year int=CASE WHEN MONTH(@day)>=4 THEN YEAR(@day)+1 ELSE YEAR(@day) END;
            INSERT dbo.stores(store_code,store_name,is_active) VALUES('ZZSYN',N'Synthetic health store',1);
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'syn-r025.xlsx',REPLICATE('5',64),1,'ZZSYN','R025',@day,@day,@day,1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sales',1,'sale');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('ZZSYN','SYN-1',@year,@day);
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,source_brand_code,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'1','SYN-0001','INV',1,1.00,1.00,0,'SYN','INR',s.source_lineage_id
            FROM dbo.sales_invoices i JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.source_row_number=1
            WHERE i.store_code='ZZSYN' AND i.document_number='SYN-1';
            """);
    }

    private sealed record TableState(long Rows, int? Checksum);

    private static async Task<IReadOnlyDictionary<string, TableState>> SnapshotAsync(string connectionString)
    {
        var states = new Dictionary<string, TableState>(StringComparer.Ordinal);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = new SqlCommand(TablesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

        foreach (var table in tables)
        {
            var name = "dbo." + "[" + table.Replace("]", "]]", StringComparison.Ordinal) + "]";
            await using var command = new SqlCommand($"SELECT COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM {name} WITH (NOLOCK);", connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            states[table] = new TableState(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt32(1));
        }
        return states;
    }
}
