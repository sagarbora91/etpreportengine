using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>1.9.9 import reminder: the readiness facts read against a migrated database.</summary>
public sealed class DailyReadinessSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Reads_received_exports_entered_inputs_and_targets_for_the_business_date()
    {
        await database.ExecuteAsync("""
            DECLARE @done uniqueidentifier=NEWID(),@open uniqueidentifier=NEWID();
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc) VALUES(@done,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@open,'Processing',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version) VALUES
             (@done,'r025-range.xlsx',REPLICATE('1',64),1,'R025','HEMW','20261008','20261001','20261008',1),
             (@done,'r022-before.xlsx',REPLICATE('2',64),1,'R022','HEMW','20261007','20261001','20261007',1),
             (@done,'r011-snapshot.xlsx',REPLICATE('3',64),1,'CLOSING_STOCK','HEMW','20261008',NULL,NULL,1),
             (@open,'r030-processing.xlsx',REPLICATE('4',64),1,'STOCK_LEDGER','HEMW','20261008','20261008','20261008',1),
             (@done,'s004.csv',REPLICATE('5',64),1,'S004','AW330','20261008','20261008','20261008',1),
             (@done,'r024-other.xlsx',REPLICATE('6',64),1,'R024','HEMW','20261008','20261008','20261008',1);
            INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason) VALUES
             ('HEMW','20261008','WALK_INS',0,'test','test','Explicit zero counts as entered'),
             ('HEMW','20261008','EXPENSES',120,'test','test','Entered'),
             ('HEMW','20261007','OPENING_CASH',500,'test','test','Another day');
            INSERT dbo.monthly_targets(store_code,target_month,target_sales,modified_by) VALUES('HEMW','20261001',100000,'test'),('WLMHW','20260901',1,'test');
            INSERT dbo.staff_sales_targets(store_code,cro_number,period_start,period_end,target_sales,entered_by,modified_by,change_reason)
             VALUES('HEMW','C1','20261001','20261031',5000,'test','test','Test target');
            """);

        var result = await new SqlServerDailyReadinessQuery(database.ConnectionString)
            .LoadAsync(new(new DateOnly(2026, 10, 8), ["HEMW"]));

        Assert.Equal(["HEMW"], result.StoreCodes);
        Assert.Equal(["R022", "R030", "OPENING_CASH", "CASH_DEPOSIT"], result.Missing.Select(x => x.Code));
        Assert.All(result.Missing, x => Assert.Equal("HEMW", x.StoreCode));

        var all = await new SqlServerDailyReadinessQuery(database.ConnectionString)
            .LoadAsync(new(new DateOnly(2026, 10, 8), []));
        Assert.Contains("WLMHW", all.StoreCodes);
        Assert.DoesNotContain("AW330", all.StoreCodes);
        Assert.Contains(all.Missing, x => x.StoreCode == "WLMHW" && x.Code == "MONTHLY_TARGET");
        Assert.DoesNotContain(all.Missing, x => x.Code == "SERVICE_RAW");

        var nextDay = await new SqlServerDailyReadinessQuery(database.ConnectionString)
            .LoadAsync(new(new DateOnly(2026, 10, 9), ["HEMW"]));
        Assert.Contains(nextDay.Missing, x => x.Code == "R025");
        Assert.Contains(nextDay.Missing, x => x.Code == "SERVICE_RAW");
    }
}
