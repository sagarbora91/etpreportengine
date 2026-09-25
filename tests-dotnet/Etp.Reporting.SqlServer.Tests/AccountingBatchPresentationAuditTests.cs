using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class AccountingBatchPresentationAuditTests
{
    [Fact]
    public void Accounting_batch_grid_contract_does_not_claim_a_Tally_voucher_identity()
    {
        // The WPF batch grid auto-generates columns from this public contract.
        Assert.DoesNotContain(typeof(AccountingBatchSummary).GetProperties(), property => property.Name == "TallyReference");
        Assert.DoesNotContain(typeof(AccountingBatchRow).GetProperties(), property => property.Name == "TallyReference");
        Assert.Contains(typeof(AccountingBatchSummary).GetProperties(), property => property.Name == "ExportedUtc");
        Assert.Contains(typeof(AccountingBatchSummary).GetProperties(), property => property.Name == "BlockingReason");
    }
}
