using Etp.Reporting.Desktop;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportWorkspaceTests
{
    [Fact]
    public void Every_catalogue_report_belongs_to_exactly_one_workspace()
    {
        var assigned = ProductReportCatalogue.All.SelectMany(report => ReportWorkspaceDefinition.ForReport(report.Code).Reports).ToArray();

        Assert.Equal(ProductReportCatalogue.All.Count, assigned.Length);
        Assert.Equal(ProductReportCatalogue.All.Count, assigned.Select(report => report.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ProductReportCatalogue.All, report => Assert.Contains(assigned, assignedReport => assignedReport.Code == report.Code));
    }

    [Theory]
    [InlineData("dsr")]
    [InlineData("stock-closing")]
    [InlineData("tender")]
    [InlineData("service")]
    [InlineData("staff")]
    [InlineData("exceptions")]
    [InlineData("management-trend")]
    [InlineData("invoice-lineage")]
    public void Report_resolves_to_its_purpose_built_workspace(string reportCode)
    {
        Assert.Equal(reportCode, ReportWorkspaceDefinition.ForReport(reportCode).Id);
        Assert.Single(ReportWorkspaceDefinition.ForReport(reportCode).Reports);
    }

    [Theory]
    [InlineData(2026, 8, 25, "FTD 25 Aug 2026", "MTD 01 Aug–25 Aug 2026", "YTD 01 Apr 2026–25 Aug 2026")]
    [InlineData(2026, 1, 10, "FTD 10 Jan 2026", "MTD 01 Jan–10 Jan 2026", "YTD 01 Apr 2025–10 Jan 2026")]
    public void Period_labels_follow_indian_financial_year(int year, int month, int day, params string[] expectedParts)
    {
        var label = ReportingPeriodLabels.ForDate(new DateOnly(year, month, day));

        Assert.All(expectedParts, part => Assert.Contains(part, label));
    }

    [Fact]
    public void Workspace_actions_include_all_fixed_toolbar_operations()
    {
        var actions = Enum.GetValues<ReportWorkspaceAction>();

        Assert.Contains(ReportWorkspaceAction.Refresh, actions);
        Assert.Contains(ReportWorkspaceAction.ExportPdf, actions);
        Assert.Contains(ReportWorkspaceAction.ExportExcel, actions);
        Assert.Contains(ReportWorkspaceAction.GenerateReportPack, actions);
        Assert.Contains(ReportWorkspaceAction.OpenExportFolder, actions);
        Assert.Contains(ReportWorkspaceAction.OpenManualEntry, actions);
    }
}
