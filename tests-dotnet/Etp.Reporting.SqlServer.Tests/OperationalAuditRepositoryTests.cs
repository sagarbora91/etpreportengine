using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class OperationalAuditRepositoryTests
{
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
