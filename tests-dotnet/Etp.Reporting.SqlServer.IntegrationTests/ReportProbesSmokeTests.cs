using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Modules.HealthCheck;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.0 self-diagnostics, lane L5: RPT-01..22 against a migrated database with one synthetic R025 invoice. Every report
/// path must complete (a report may say Blocked for missing data; that is a Pass), the run must be read-only, and no
/// ReportRun audit row may be written (the probe never calls the Reports view).
/// </summary>
public sealed class ReportProbesSmokeTests(SqlDatabaseFixture db) : IClassFixture<SqlDatabaseFixture>
{
    private static readonly DateOnly Day = new(2033, 4, 4);

    [SqlFact]
    public async Task All_22_report_probes_complete_on_a_seeded_database_without_writing_anything()
    {
        await db.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(); DECLARE @file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date)
            VALUES(@batch,'probe-smoke-r025.xlsx',REPLICATE('d',64),1,'WLMHW','R025','20330404');
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sales',1,'sale');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date)
            VALUES('WLMHW','PROBE-SMOKE-1',2033,'20330404');
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,
                                   source_quantity,source_gross_amount,source_net_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'1','ITEM','INV',1,1180,1000,'INR',s.source_lineage_id
            FROM dbo.sales_invoices i
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales' AND s.source_row_number=1
            WHERE i.store_code='WLMHW' AND i.document_number='PROBE-SMOKE-1';
            """);
        var auditBefore = await db.ExecuteAsync("SELECT COUNT_BIG(*) FROM dbo.operational_audit");
        var runsBefore = await db.ExecuteAsync("SELECT COUNT_BIG(*) FROM dbo.automation_runs");
        var linesBefore = await db.ExecuteAsync("SELECT COUNT_BIG(*) FROM dbo.sales_lines");

        var probe = Etp.Reporting.Desktop.Composition.DesktopCompositionRoot.CreateReportProbes(db.ConnectionString);
        var context = new HealthCheckContext(Day, HealthCheckTrigger.Manual, DateTimeOffset.UtcNow, ["WLMHW"], null,
            HealthCheckBudget.Default, new Dictionary<string, object>());
        var results = await probe.RunAsync(context, CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 22).Select(x => $"RPT-{x:00}").ToArray(), results.Select(x => x.CheckId).ToArray());
        Assert.Equal(probe.CheckIds.ToArray(), results.Select(x => x.CheckId).ToArray());
        // A Warning would be a slow report on an empty database; a Fail or Skipped is a broken report path.
        Assert.True(results.All(x => x.Outcome == HealthOutcome.Pass),
            string.Join(Environment.NewLine, results.Where(x => x.Outcome != HealthOutcome.Pass).Select(x => $"{x.CheckId} {x.Outcome} {x.FindingCode}: {x.Explanation}")));
        Assert.DoesNotContain("PROBE-SMOKE-1", string.Join('\n', results.Select(x => x.Explanation)));

        Assert.Equal(auditBefore, await db.ExecuteAsync("SELECT COUNT_BIG(*) FROM dbo.operational_audit"));
        Assert.Equal(runsBefore, await db.ExecuteAsync("SELECT COUNT_BIG(*) FROM dbo.automation_runs"));
        Assert.Equal(linesBefore, await db.ExecuteAsync("SELECT COUNT_BIG(*) FROM dbo.sales_lines"));
    }
}
