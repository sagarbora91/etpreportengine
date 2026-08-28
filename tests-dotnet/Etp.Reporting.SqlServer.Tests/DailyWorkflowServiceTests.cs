using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class DailyWorkflowServiceTests
{
    public static TheoryData<DailyReadinessStatus, DailyWorkflowStatus> WorkflowStatuses => new()
    {
        { DailyReadinessStatus.NotReady, DailyWorkflowStatus.NotReady },
        { DailyReadinessStatus.Partial, DailyWorkflowStatus.Partial },
        { DailyReadinessStatus.ReadyWithWarnings, DailyWorkflowStatus.ReadyWithWarnings },
        { DailyReadinessStatus.Reconciled, DailyWorkflowStatus.Reconciled },
        { DailyReadinessStatus.Locked, DailyWorkflowStatus.Locked },
        { (DailyReadinessStatus)999, DailyWorkflowStatus.NotReady }
    };

    [Theory]
    [MemberData(nameof(WorkflowStatuses))]
    public void Workflow_status_mapping_is_complete_and_unknown_values_fail_closed(
        DailyReadinessStatus source,
        DailyWorkflowStatus expected)
    {
        Assert.Equal(expected, SqlServerDailyWorkflowService.Map(source));
    }

    public static TheoryData<ReconciliationStatus, DailyControlStatus> ControlStatuses => new()
    {
        { ReconciliationStatus.NotRun, DailyControlStatus.NotRun },
        { ReconciliationStatus.Passed, DailyControlStatus.Passed },
        { ReconciliationStatus.Failed, DailyControlStatus.Failed },
        { ReconciliationStatus.Blocked, DailyControlStatus.Blocked },
        { (ReconciliationStatus)999, DailyControlStatus.NotRun }
    };

    [Theory]
    [MemberData(nameof(ControlStatuses))]
    public void Pack_status_mapping_is_complete_and_unknown_values_fail_closed(
        ReconciliationStatus source,
        DailyControlStatus expected)
    {
        Assert.Equal(expected, SqlServerDailyWorkflowService.Map(source));
    }

    [Fact]
    public void Workflow_mapping_preserves_missing_and_explicit_zero_as_different_states()
    {
        var missing = new ManualInputValue("WALK_INS", "Walk-ins", "NUMBER", null, null, true, null, null);
        var zero = new ManualInputValue("SERVICE_CASH", "Service cash", "NUMBER", 0m, null, true,
            new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc), @"STORE\Manager");
        var source = new DailyWorkflowSnapshot(
            "WLMHW",
            new DateOnly(2026, 8, 25),
            DailyReadinessStatus.Partial,
            ["R025"],
            ["R022"],
            [missing, zero],
            ["WALK_INS"],
            false,
            "Inputs are incomplete.");

        var mapped = SqlServerDailyWorkflowService.Map(source);

        Assert.False(mapped.ManualInputs[0].IsPresent);
        Assert.True(mapped.ManualInputs[1].IsPresent);
        Assert.Equal(0m, mapped.ManualInputs[1].NumericValue);
        Assert.Equal(source.ImportedReports, mapped.ImportedReports);
        Assert.Equal(source.MissingReports, mapped.MissingReports);
        Assert.Equal(source.MissingRequiredInputs, mapped.MissingRequiredInputs);
        Assert.False(mapped.CanFinalise);
        Assert.Equal(DailyWorkflowStatus.Partial, mapped.Status);
    }

    [Fact]
    public void Stock_mapping_preserves_component_and_physical_evidence()
    {
        var source = new ManualStockCount(
            "HEMW",
            new DateOnly(2026, 8, 25),
            "WATCHES",
            2m,
            null,
            1m,
            0m,
            5m,
            "Counted",
            new DateTime(2026, 8, 25, 13, 0, 0, DateTimeKind.Utc),
            @"STORE\Manager");

        var mapped = SqlServerDailyWorkflowService.Map(source);

        Assert.Equal(source.StoreCode, mapped.StoreCode);
        Assert.Equal(source.InventoryGroupCode, mapped.InventoryGroupCode);
        Assert.Equal(3m, mapped.ComponentTotal);
        Assert.Equal(2m, mapped.CompositionVariance);
        Assert.Equal(source.CountedPhysicalQuantity, mapped.CountedPhysicalQuantity);
        Assert.Equal(source.Remarks, mapped.Remarks);
    }

    [Fact]
    public void Stock_contract_keeps_all_missing_components_unavailable()
    {
        var mapped = new DailyManualStockCount(
            "HEMW", new DateOnly(2026, 8, 25), "WATCHES",
            null, null, null, null, 0m, null, DateTime.UtcNow, "Manager");

        Assert.Null(mapped.ComponentTotal);
        Assert.Null(mapped.CompositionVariance);
    }

    [Fact]
    public void Staff_target_mapping_preserves_period_value_and_audit_identity()
    {
        var source = new StaffSalesTarget(
            "WLMHW",
            "CRO-1",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            250000m,
            new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
            @"STORE\Owner");

        var mapped = SqlServerDailyWorkflowService.Map(source);

        Assert.Equal(source.StoreCode, mapped.StoreCode);
        Assert.Equal(source.CroNumber, mapped.CroNumber);
        Assert.Equal(source.PeriodStart, mapped.PeriodStart);
        Assert.Equal(source.PeriodEnd, mapped.PeriodEnd);
        Assert.Equal(source.TargetSales, mapped.TargetSales);
        Assert.Equal(source.ModifiedUtc, mapped.ModifiedUtc);
        Assert.Equal(source.ModifiedBy, mapped.ModifiedBy);
    }

    [Fact]
    public void Pack_mapping_preserves_control_sections_generation_and_document()
    {
        var document = new ReportPackDocument(
            "Daily Pack",
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 8, 25),
            "Failed",
            "rule",
            "Visible blockers remain.",
            DateTimeOffset.UtcNow,
            []);
        var source = new DailyReportPackResult(
            "WLMHW",
            new DateOnly(2026, 8, 25),
            ReconciliationStatus.Failed,
            [new DailyReportPackSection("Tender", ReconciliationStatus.Blocked, 100m, 2m, "Blocked")],
            "Visible blockers remain.",
            document.GeneratedUtc,
            document,
            4,
            "control-sha");

        var mapped = SqlServerDailyWorkflowService.Map(source);

        Assert.Equal(DailyControlStatus.Failed, mapped.Status);
        Assert.Equal(DailyControlStatus.Blocked, mapped.Sections.Single().Status);
        Assert.Equal(100m, mapped.Sections.Single().ControlTotal);
        Assert.Equal(2m, mapped.Sections.Single().Variance);
        Assert.Same(document, mapped.Document);
        Assert.Equal(source.GenerationNumber, mapped.GenerationNumber);
        Assert.Equal(source.ContentSha256, mapped.ContentSha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Adapter_rejects_blank_connections(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new SqlServerDailyWorkflowService(connectionString));
    }
}
