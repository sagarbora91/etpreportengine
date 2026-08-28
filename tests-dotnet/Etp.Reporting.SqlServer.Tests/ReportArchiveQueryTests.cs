using Etp.Reporting.Application.Archive;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class ReportArchiveQueryTests
{
    [Fact]
    public void Generation_mapping_preserves_every_archive_control_field()
    {
        var source = new ArchivedReportGeneration(
            47,
            "COMBINED",
            new DateOnly(2026, 8, 25),
            3,
            "control-sha",
            "document-sha",
            new DateTime(2026, 8, 26, 10, 30, 0, DateTimeKind.Utc),
            @"STORE\Owner",
            true,
            41,
            true);

        var mapped = SqlServerReportArchiveQuery.Map(source);

        Assert.Equal(source.Id, mapped.Id);
        Assert.Equal(source.StoreCode, mapped.StoreCode);
        Assert.Equal(source.BusinessDate, mapped.BusinessDate);
        Assert.Equal(source.GenerationNumber, mapped.GenerationNumber);
        Assert.Equal(source.ControlSha256, mapped.ControlSha256);
        Assert.Equal(source.DocumentSha256, mapped.DocumentSha256);
        Assert.Equal(source.GeneratedUtc, mapped.GeneratedUtc);
        Assert.Equal(source.GeneratedBy, mapped.GeneratedBy);
        Assert.Equal(source.IsFinal, mapped.IsFinal);
        Assert.Equal(source.SupersedesGenerationId, mapped.SupersedesGenerationId);
        Assert.Equal(source.CanReExport, mapped.CanReExport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Comparison_mapping_preserves_status_rows_and_changed_state(bool changed)
    {
        var source = new ReportGenerationComparisonRow(
            "Daily Sales",
            12,
            13,
            "PASSED",
            "PASSED",
            changed);

        var mapped = SqlServerReportArchiveQuery.Map(source);

        Assert.Equal(source.Table, mapped.Table);
        Assert.Equal(source.FirstRows, mapped.FirstRows);
        Assert.Equal(source.SecondRows, mapped.SecondRows);
        Assert.Equal(source.FirstStatus, mapped.FirstStatus);
        Assert.Equal(source.SecondStatus, mapped.SecondStatus);
        Assert.Equal(changed, mapped.Changed);
    }

    [Fact]
    public void Search_defaults_preserve_the_existing_archive_limit()
    {
        var search = new ReportArchiveSearch();

        Assert.Null(search.StoreCode);
        Assert.Null(search.BusinessDate);
        Assert.Equal(200, search.Limit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Sql_adapter_rejects_a_blank_connection(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new SqlServerReportArchiveQuery(connectionString));
    }
}
