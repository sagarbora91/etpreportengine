using System.Globalization;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DailyWorkflowPresentationTests
{
    [Fact]
    public void Snapshot_is_projected_to_stable_ui_state_and_cleared_on_failure()
    {
        var session = new DailyWorkflowPresentationSession();
        var manual = new DailyManualInput("WALK_INS", "Walk-ins", "NUMBER", 12m, null, true, null, null);
        var state = new DailyWorkflowState(
            "WLMHW", new DateOnly(2026, 8, 25), DailyWorkflowStatus.ReadyWithWarnings,
            ["R025"], ["R022"], [manual], ["MONTHLY_TARGET"], true, "Ready with one warning.");

        var presentation = session.Show(state, []);

        Assert.True(session.HasSnapshot);
        Assert.Equal(DailyWorkflowTone.Warning, presentation.Tone);
        Assert.Equal("Missing ETP sources: R022", presentation.SourceStatus);
        Assert.Equal("Missing manual inputs: MONTHLY_TARGET", presentation.InputStatus);
        Assert.Same(manual, Assert.Single(presentation.ManualInputs));
        Assert.True(presentation.CanFinalise);

        var unavailable = session.ShowUnavailable("SQL unavailable");
        Assert.False(session.HasSnapshot);
        Assert.False(unavailable.IsAvailable);
        Assert.Equal("Unavailable", unavailable.Status);
        Assert.False(unavailable.CanFinalise);
    }

    [Fact]
    public void Scope_and_manual_input_validation_preserve_zero_missing_and_walk_in_rules()
    {
        var session = new DailyWorkflowPresentationSession();
        var scope = session.SelectScope(" WLMHW ", new DateTime(2026, 8, 25));

        Assert.Equal("WLMHW", scope.StoreCode);
        Assert.Equal(new DateOnly(2026, 8, 25), scope.BusinessDate);
        var zero = session.CreateManualInput(scope, "WALK_INS", "0", "owner", "Daily count", CultureInfo.InvariantCulture);
        Assert.Equal(0m, zero.NumericValue);
        Assert.Null(zero.TextValue);
        var remark = session.CreateManualInput(scope, "OPERATIONAL_REMARK", "  Store event  ", "owner", "Note", CultureInfo.InvariantCulture);
        Assert.Null(remark.NumericValue);
        Assert.Equal("Store event", remark.TextValue);

        Assert.Throws<InvalidOperationException>(() => session.SelectScope(null, new DateTime(2026, 8, 25)));
        Assert.Throws<InvalidOperationException>(() => session.SelectScope("WLMHW", null));
        Assert.Throws<InvalidOperationException>(() => session.CreateManualInput(scope, "WALK_INS", "1.5", "owner", "Count", CultureInfo.InvariantCulture));
        Assert.Throws<InvalidOperationException>(() => session.CreateManualInput(scope, "WALK_INS", "-1", "owner", "Count", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Finalise_transition_uses_application_pack_status_without_recalculating_reports()
    {
        var scope = new DailyWorkflowScope("HEMW", new DateOnly(2026, 8, 25));
        var sections = new[]
        {
            new DailyPackSection("Sales", DailyControlStatus.Passed, 10m, 0m, "Passed"),
            new DailyPackSection("Tender", DailyControlStatus.Blocked, null, null, "Missing")
        };

        var command = DailyWorkflowPresentationSession.CreateFinalise(scope, "manager", sections);

        Assert.True(command.HasBlockingReconciliationExceptions);
        Assert.Equal(scope, command.Scope);
    }

    [Fact]
    public void MainWindow_has_no_daily_snapshot_or_inline_input_parsing_state()
    {
        var mainWindow = Source("MainWindow.xaml.cs");
        var session = Source("Modules", "DailyWorkflow", "DailyWorkflowPresentationSession.cs");
        var workspace = Source("Modules", "DailyWorkflow", "DailyWorkflowWorkspaceView.xaml.cs");

        Assert.DoesNotContain("currentDailySnapshot", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("decimal.Truncate(parsed)", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Missing ETP sources:", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DailyWorkflowWorkspaceView dailyWorkflowWorkspace", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateManualInput", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateStockCount", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateStaffTarget", mainWindow, StringComparison.Ordinal);
        Assert.Contains("presentation.CreateManualInput", workspace, StringComparison.Ordinal);
        Assert.Contains("presentation.CreateStockCount", workspace, StringComparison.Ordinal);
        Assert.Contains("presentation.CreateStaffTarget", workspace, StringComparison.Ordinal);
        Assert.Contains("private DailyWorkflowState? snapshot", session, StringComparison.Ordinal);
    }

    private static string Source(params string[] path) =>
        File.ReadAllText(path.Aggregate(Path.Combine(FindRepositoryRoot(), "src", "Etp.Reporting.Desktop"), Path.Combine));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
