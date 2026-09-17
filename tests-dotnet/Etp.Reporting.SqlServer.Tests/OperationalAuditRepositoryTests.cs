using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class OperationalAuditRepositoryTests
{
    [Theory]
    [InlineData("3 files imported", false)]
    [InlineData("42 rows skipped.", false)]
    [InlineData("0 reports generated", false)]
    [InlineData("invoice 12345", true)]
    [InlineData("Invoice１２３", true)]
    [InlineData("３ files imported", true)]
    [InlineData("1234567890 files imported", true)]
    [InlineData("3 files imported from C:\\private", true)]
    [InlineData("3 files imported 9876543210", true)]
    [InlineData("٣ files imported", true)]
    public void Numeric_details_accept_only_aggregate_count_messages(string detail, bool rejected)
    {
        Assert.Equal(rejected, OperationalAuditRepository.ContainsPathOrIdentifier(detail));
    }

    [Fact]
    public void Outcome_catalogue_is_the_closed_database_contract()
    {
        Assert.Equal(["Blocked", "Cancelled", "Failed", "Succeeded"],
            OperationalAuditRepository.SupportedOutcomes.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("DocumentExtractionReview")]
    [InlineData("SharingContactChange")]
    [InlineData("VisualRender")]
    public void Productisation_and_visual_events_are_supported(string eventType)
    {
        Assert.True(OperationalAuditRepository.SupportsEventType(eventType));
    }

    [Theory]
    [InlineData("ReportRun", "Succeeded", "Daily report")]
    [InlineData("ImportBatch", "Failed", "Aggregate failure")]
    public async Task Record_reaches_connection_only_after_privacy_validation(string type, string outcome, string detail)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => new OperationalAuditRepository("Server=invalid;Connect Timeout=1").RecordAsync(type, outcome, detail));
    }

    [Theory]
    [InlineData("C:\\secret.xlsx")]
    [InlineData("invoice 12345")]
    [InlineData("folder/file")]
    public async Task Record_rejects_paths_and_identifier_shaped_details(string detail)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new OperationalAuditRepository("unused").RecordAsync("ReportRun", "Succeeded", detail));
    }
}
