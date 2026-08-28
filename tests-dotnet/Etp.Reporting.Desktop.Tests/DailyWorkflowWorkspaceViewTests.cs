using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DailyWorkflowWorkspaceViewTests
{
    [Fact]
    public void MainWindow_hosts_daily_workspace_without_daily_controls_or_handlers()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "Etp.Reporting.Desktop");
        var mainXaml = File.ReadAllText(Path.Combine(desktop, "MainWindow.xaml"));
        var main = File.ReadAllText(Path.Combine(desktop, "MainWindow.xaml.cs"));
        var viewXaml = File.ReadAllText(Path.Combine(desktop, "Modules", "DailyWorkflow", "DailyWorkflowWorkspaceView.xaml"));
        var view = File.ReadAllText(Path.Combine(desktop, "Modules", "DailyWorkflow", "DailyWorkflowWorkspaceView.xaml.cs"));

        var start = mainXaml.IndexOf("<ContentControl x:Name=\"DailyWorkflowPanel\"", StringComparison.Ordinal);
        var end = mainXaml.IndexOf("<Border x:Name=\"ImportPanel\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.Equal(1, Count(mainXaml[start..end], "x:Name=\""));
        Assert.DoesNotContain("DailyBusinessDateInput", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualFieldInput", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StockGroupInput", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StaffTargetCroInput", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveManualInput_Click", main, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveStockCount_Click", main, StringComparison.Ordinal);
        Assert.DoesNotContain("FinaliseDay_Click", main, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportDailyPackExcel_Click", main, StringComparison.Ordinal);
        Assert.DoesNotContain("dailyWorkflowQueryFactory(connectionState", main, StringComparison.Ordinal);
        Assert.Contains("DailyWorkflowPanel.Content = dailyWorkflowWorkspace", main, StringComparison.Ordinal);
        Assert.Contains("destination is \"Daily Workflow\" or \"Manual Entry\" ? Visibility.Visible", main, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Daily Workflow\" or \"Manual Entry\" or \"Dashboard\"", main, StringComparison.Ordinal);
        Assert.Contains("DashboardRefreshRequestedAsync = RefreshDashboardAsync", main, StringComparison.Ordinal);

        Assert.Equal(37, Count(viewXaml, "x:Name=\""));
        Assert.Equal(37, Count(viewXaml, "automation:AutomationProperties.Name="));
        Assert.Contains("Walk-ins feed the combined conversion calculation; zero remains different from missing", viewXaml, StringComparison.Ordinal);
        Assert.Contains("presentation.CreateManualInput", view, StringComparison.Ordinal);
        Assert.Contains("DailyWorkflowPresentationSession.CreateFinalise", view, StringComparison.Ordinal);
        Assert.Contains("DailyWorkflowPresentationSession.CreateReopen", view, StringComparison.Ordinal);
    }

    [Fact]
    public void View_preserves_access_manual_zero_audit_refresh_and_finalisation_flow()
    {
        RunSta(async () =>
        {
            var scopeDate = DateTime.Today.AddDays(-1);
            var manual = new DailyManualInput("WALK_INS", "Walk-ins", "NUMBER", null, null, true, null, null);
            var query = new FakeQuery(new DailyWorkflowState(
                "WLMHW", DateOnly.FromDateTime(scopeDate), DailyWorkflowStatus.ReadyWithWarnings,
                ["R025"], [], [manual], [], true, "Ready with warnings."));
            var commands = new FakeCommands();
            var generator = new FakePackGenerator(DateOnly.FromDateTime(scopeDate));
            var audits = new List<string>();
            var dashboardRefreshes = 0;
            var currentAccess = new DailyWorkflowWorkspaceAccess(true, true, false);
            var view = new DailyWorkflowWorkspaceView(
                new DailyWorkflowPresentationSession(),
                () => "Integrated Security=True",
                _ => query,
                _ => commands,
                _ => generator,
                () => currentAccess,
                () => true,
                (operation, status, _) => { audits.Add($"{operation}:{status}"); return Task.CompletedTask; },
                (_, _) => { },
                (_, _) => { })
            {
                BusinessDate = scopeDate,
                StoreCode = "WLMHW",
                DashboardRefreshRequestedAsync = () => { dashboardRefreshes++; return Task.CompletedTask; }
            };

            await view.RefreshAsync();
            Assert.Equal("Ready with warnings.", view.StatusText);
            Assert.True(FindButton(view, "Finalise business day").IsEnabled);
            Assert.False(FindButton(view, "Reopen finalised business day").IsEnabled);

            FindTextBox(view, "Manual input value").Text = "0";
            FindTextBox(view, "Manual input change reason").Text = "Daily store count";
            await view.SaveManualInputAsync();

            Assert.NotNull(commands.ManualInput);
            Assert.Equal(0m, commands.ManualInput!.NumericValue);
            Assert.Null(commands.ManualInput.TextValue);
            Assert.Equal("Daily store count", commands.ManualInput.Reason);
            Assert.Contains("ManualInput:Succeeded", audits);
            Assert.Equal(1, dashboardRefreshes);

            await view.FinaliseDayAsync();
            Assert.NotNull(commands.Finalise);
            Assert.False(commands.Finalise!.HasBlockingReconciliationExceptions);
            Assert.Contains("DayFinalised:Succeeded", audits);
            Assert.Equal(2, dashboardRefreshes);

            await view.ReopenDayAsync();
            Assert.Null(commands.Reopen);
            Assert.Contains("Owner permission is required", view.StatusText, StringComparison.Ordinal);

            currentAccess = currentAccess with { CanAdminister = true };
            view.RefreshAccessState();
            FindTextBox(view, "Reopen reason").Text = "Approved correction";
            await view.ReopenDayAsync();
            Assert.True(commands.Reopen?.AdministratorApproved);
            Assert.Equal("Approved correction", commands.Reopen?.Reason);
            Assert.Contains("DayReopened:Succeeded", audits);
            Assert.Equal(3, dashboardRefreshes);
        });
    }

    [Fact]
    public void Generated_pack_is_invalidated_when_its_store_or_business_date_scope_changes()
    {
        RunSta(async () =>
        {
            var date = DateTime.Today.AddDays(-1);
            var query = new FakeQuery(new DailyWorkflowState(
                "WLMHW", DateOnly.FromDateTime(date), DailyWorkflowStatus.ReadyWithWarnings,
                ["R025"], [], [], [], true, "Ready."));
            var view = new DailyWorkflowWorkspaceView(
                new DailyWorkflowPresentationSession(),
                () => "Integrated Security=True",
                _ => query,
                _ => new FakeCommands(),
                _ => new FakePackGenerator(DateOnly.FromDateTime(date)),
                () => new(true, true, true),
                () => true,
                (_, _, _) => Task.CompletedTask,
                (_, _) => { },
                (_, _) => { })
            {
                BusinessDate = date,
                StoreCode = "WLMHW"
            };

            await view.GenerateDailyPackAsync();
            Assert.True(FindButton(view, "Export complete report pack Excel").IsEnabled);

            view.StoreCode = "HEMW";
            Assert.False(FindButton(view, "Export complete report pack Excel").IsEnabled);

            await view.GenerateDailyPackAsync();
            Assert.True(FindButton(view, "Export complete report pack Excel").IsEnabled);
            view.BusinessDate = date.AddDays(1);
            Assert.False(FindButton(view, "Export complete report pack Excel").IsEnabled);
        });
    }

    private static Button FindButton(DependencyObject root, string automationName) =>
        Find<Button>(root, automationName);

    private static TextBox FindTextBox(DependencyObject root, string automationName) =>
        Find<TextBox>(root, automationName);

    private static T Find<T>(DependencyObject root, string automationName) where T : DependencyObject
    {
        if (root is T match && AutomationProperties.GetName(match) == automationName) return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            try { return Find<T>(child, automationName); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"{typeof(T).Name} '{automationName}' was not found.");
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA test failed.", failure);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }

    private sealed class FakeQuery(DailyWorkflowState state) : IDailyWorkflowQuery
    {
        public Task<DailyWorkflowState> LoadAsync(DailyWorkflowScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(state with { StoreCode = scope.StoreCode, BusinessDate = scope.BusinessDate });

        public Task<IReadOnlyList<DailyManualStockCount>> LoadStockCountsAsync(DailyWorkflowScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DailyManualStockCount>>([]);

        public Task<IReadOnlyList<DailyStaffSalesTarget>> LoadStaffTargetsAsync(DailyStaffTargetSearch search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DailyStaffSalesTarget>>([]);
    }

    private sealed class FakeCommands : IDailyWorkflowCommands
    {
        public SaveDailyManualInput? ManualInput { get; private set; }
        public FinaliseDailyWorkflow? Finalise { get; private set; }
        public ReopenDailyWorkflow? Reopen { get; private set; }

        public Task SaveManualInputAsync(SaveDailyManualInput command, CancellationToken cancellationToken = default)
        { ManualInput = command; return Task.CompletedTask; }

        public Task SaveStockCountAsync(SaveDailyStockCount command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveStaffTargetAsync(SaveDailyStaffTarget command, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FinaliseAsync(FinaliseDailyWorkflow command, CancellationToken cancellationToken = default)
        { Finalise = command; return Task.CompletedTask; }

        public Task ReopenAsync(ReopenDailyWorkflow command, CancellationToken cancellationToken = default)
        { Reopen = command; return Task.CompletedTask; }
    }

    private sealed class FakePackGenerator(DateOnly businessDate) : IDailyReportPackGenerator<ReportPackDocument>
    {
        private readonly ReportPackDocument document = new(
            "Daily pack", businessDate, businessDate, "Passed", "test", "Passed", DateTimeOffset.UtcNow, []);

        public Task<DailyPackGeneration<ReportPackDocument>> GenerateAsync(
            DailyWorkflowScope scope, string? generatedBy = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DailyPackGeneration<ReportPackDocument>(
                scope.StoreCode, scope.BusinessDate, DailyControlStatus.Passed,
                [new("Sales", DailyControlStatus.Passed, 10m, 0m, "Passed")],
                "Passed", DateTimeOffset.UtcNow, document, 1, new string('a', 64)));

        public Task<ReportPackDocument> GenerateCombinedAsync(
            DateOnly businessDate, string? generatedBy = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(document with { DateFrom = businessDate, DateTo = businessDate });
    }
}
