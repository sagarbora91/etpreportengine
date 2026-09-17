using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class AccountingMappingAtomicityTests
{
    [Fact]
    public async Task Failed_mapping_save_rolls_back_request_decision_audit_and_previous_mapping_deactivation()
    {
        var database=new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var service=new SqlServerAccountingService(database.ConnectionString);
            var input=new ApproveAccountingMapping(new("PHASE5",new(2026,8,25)),"ADJUSTMENT","Expense","Clearing","{reference}","Owner checked mapping");
            await service.ApproveMappingAsync(input);
            var requests=await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.approval_requests");
            var audit=await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit");
            await database.ExecuteAsync("""
                CREATE TRIGGER dbo.test_refuse_mapping ON dbo.accounting_mappings AFTER INSERT AS
                BEGIN THROW 51990,'Synthetic mapping persistence failure',1; END;
                """);
            var failure=await Assert.ThrowsAsync<SqlException>(()=>service.ApproveMappingAsync(input with { DebitLedger="Replacement" }));
            Assert.Equal(51990,failure.Number);
            Assert.Equal(requests,await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.approval_requests"));
            Assert.Equal(audit,await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit"));
            var retained=Assert.Single(await new ProductisationRepository(database.ConnectionString).LoadApprovedAccountingMappingsAsync("PHASE5",new(2026,8,25)));
            Assert.Equal("Expense",retained.DebitLedger);
            Assert.Equal(1,await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_mappings WHERE store_code='PHASE5'"));
            await database.ExecuteAsync("DROP TRIGGER dbo.test_refuse_mapping;");
            await service.ApproveMappingAsync(input with { DebitLedger="Replacement" });
            Assert.Equal("Replacement",Assert.Single(await new ProductisationRepository(database.ConnectionString).LoadApprovedAccountingMappingsAsync("PHASE5",new(2026,8,25))).DebitLedger);
            Assert.Equal(1,await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_mappings WHERE store_code='PHASE5' AND is_active=1"));
            Assert.Equal(2,await database.ExecuteAsync("SELECT MAX(version) FROM dbo.accounting_mappings WHERE store_code='PHASE5'"));
        }
        finally { await database.DisposeAsync(); }
    }
}
