using Etp.Reporting.Desktop;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFiveFriendlyErrorAuditTests(SqlDatabaseFixture db) : IClassFixture<SqlDatabaseFixture>
{
    [Theory]
    [InlineData(51451, "Accounting is busy. Try again.")]
    [InlineData(51457, "A balanced, unblocked batch and an approval reason are required.")]
    [InlineData(51431, "Enter a rejection reason of at most 1000 characters.")]
    [InlineData(51220, "Finalise the report generation before preparing accounting.")]
    [InlineData(51461, "The database rejected this change. Review the inputs and day status.")]
    public async Task Business_error_text_is_curated_instead_of_returning_raw_SQL(int number, string expected)
    {
        var error = await ThrowAsync(number, "private database detail, source document or Windows path");
        Assert.Equal(expected, DesktopFriendlyError.Describe(error));
    }

    [Theory]
    [InlineData("Invoice PRIVATE-DOC is already in batch 42. Reject that unexported batch before preparing another.", false)]
    [InlineData("Invoice PRIVATE-DOC is already in exported batch 42. An exported batch is final; it cannot be replaced.", true)]
    [InlineData("Invoice PRIVATE batch 999. An exported batch is final; it cannot be replaced. is already in batch 42. Reject that unexported batch before preparing another.", false)]
    [InlineData("Invoice PRIVATE is already in exported batch 999. An exported batch is final; it cannot be replaced. is already in batch 42. Reject that unexported batch before preparing another.", false)]
    [InlineData("Invoice PRIVATE is already in batch 999. Reject that unexported batch before preparing another. is already in exported batch 42. An exported batch is final; it cannot be replaced.", true)]
    public async Task Duplicate_batch_error_keeps_only_the_true_batch_and_finality_from_reviewed_suffix(string raw, bool exported)
    {
        var error = await ThrowAsync(51452, raw);
        Assert.Equal(exported
            ? "This day is already in exported batch 42. An exported batch is final; it cannot be replaced."
            : "This day is already in batch 42. Reject that unexported batch before preparing another.", DesktopFriendlyError.Describe(error));
    }

    [Fact]
    public async Task Unrecognised_duplicate_batch_message_cannot_supply_raw_detail_or_invent_an_id()
    {
        var error = await ThrowAsync(51452, "PRIVATE invoice batch 999; exported elsewhere");
        var message = DesktopFriendlyError.Describe(error);
        Assert.DoesNotContain("PRIVATE", message);
        Assert.DoesNotContain("999", message);
        Assert.DoesNotContain("exported elsewhere", message);
        Assert.Contains("batch", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Trigger_error_uses_the_reviewed_business_error_without_concatenated_SQL_details()
    {
        await db.ExecuteAsync("""
            CREATE TABLE dbo.f18_trigger_probe(id int);
            EXEC(N'CREATE TRIGGER dbo.f18_trigger_failure ON dbo.f18_trigger_probe AFTER INSERT AS
              THROW 51452,''Invoice PRIVATE-DOC is already in batch 42. Reject that unexported batch before preparing another.'',1;');
            """);
        var error = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("INSERT dbo.f18_trigger_probe VALUES(1);"));
        Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == 51452);
        Assert.Equal("This day is already in batch 42. Reject that unexported batch before preparing another.", DesktopFriendlyError.Describe(error));
    }

    private Task<SqlException> ThrowAsync(int number, string message) => Assert.ThrowsAsync<SqlException>(() =>
        db.ExecuteAsync($"THROW {number},N'{message.Replace("'", "''")}',1;"));
}
