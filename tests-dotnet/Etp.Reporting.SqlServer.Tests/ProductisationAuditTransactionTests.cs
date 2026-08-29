using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class ProductisationAuditTransactionTests
{
    [Fact]
    public void Sharing_contact_mutation_and_audit_are_committed_atomically()
    {
        var sql = ProductisationRepository.SaveSharingContactSql;
        var transaction = sql.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal);
        var insert = sql.IndexOf("INSERT dbo.sharing_contacts", StringComparison.Ordinal);
        var update = sql.IndexOf("UPDATE dbo.sharing_contacts", StringComparison.Ordinal);
        var audit = sql.IndexOf("INSERT dbo.operational_audit", StringComparison.Ordinal);
        var commit = sql.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal);
        var result = sql.IndexOf("SELECT @result", StringComparison.Ordinal);

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.Ordinal);
        Assert.True(transaction >= 0);
        Assert.True(insert > transaction);
        Assert.True(update > transaction);
        Assert.True(audit > insert);
        Assert.True(audit > update);
        Assert.True(commit > audit);
        Assert.True(result > commit);
    }
}
