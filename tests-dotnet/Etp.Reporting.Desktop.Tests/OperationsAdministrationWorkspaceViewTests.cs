using System.Threading;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Distribution;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;

namespace Etp.Reporting.Desktop.Tests;

public sealed class OperationsAdministrationWorkspaceViewTests
{
    [Fact]
    public void MainWindow_contains_only_compact_hosts_and_global_relays()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml"));
        var main = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var productisationPath = Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.Productisation.cs");
        Assert.False(File.Exists(productisationPath));
        const string productisation = "";
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));
        var moduleRoot = Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "OperationsAdministration");
        var moduleXaml = string.Join(Environment.NewLine,
            File.ReadAllText(Path.Combine(moduleRoot, "OperationsWorkspaceView.xaml")),
            File.ReadAllText(Path.Combine(moduleRoot, "InvestigationApprovalsWorkspaceView.xaml")),
            File.ReadAllText(Path.Combine(moduleRoot, "AdministrationWorkspaceView.xaml")));

        Assert.Contains("<ContentControl x:Name=\"OperationsHost\"/>", mainXaml, StringComparison.Ordinal);
        Assert.Contains("<ContentControl x:Name=\"InvestigationHost\"/>", mainXaml, StringComparison.Ordinal);
        Assert.Contains("<ContentControl x:Name=\"AdministrationHost\"/>", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"OperationsFromInput\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"GlobalSearchInput\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"MasterTypeInput\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshOperations_Click", main, StringComparison.Ordinal);
        Assert.DoesNotContain("RunGlobalSearch_Click", productisation, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveUserAccess_Click", main, StringComparison.Ordinal);
        Assert.DoesNotContain("operationsAdministrationServiceFactory", main, StringComparison.Ordinal);
        Assert.DoesNotContain("investigationQueryFactory", main, StringComparison.Ordinal);
        Assert.Contains("new OperationsWorkspaceView", composition, StringComparison.Ordinal);
        Assert.Contains("new InvestigationApprovalsWorkspaceView", composition, StringComparison.Ordinal);
        Assert.Contains("new AdministrationWorkspaceView", composition, StringComparison.Ordinal);
        Assert.True(Count(moduleXaml, "automation:AutomationProperties.Name=") >= 29,
            "Operations, investigation, approval and administration controls need accessible names.");
    }

    [Fact]
    public void Operations_view_enforces_access_and_loads_dashboard_through_application_service()
    {
        RunSta(async () =>
        {
            var service = new FakeOperationsService();
            var view = new OperationsWorkspaceView(
                new OperationsAdministrationPresentationSession(),
                () => "connection",
                _ => service,
                (_, _) => Task.FromResult(new MaintenanceOperationResult(true, "done")));

            await view.RefreshAsync();
            Assert.Equal(0, service.DashboardLoads);
            Assert.Equal("Operations center could not be refreshed: This Windows account does not have application access.", view.StatusText);

            view.UpdateAccess(new(true, true, true));
            await view.RefreshAsync();
            Assert.Equal(1, service.DashboardLoads);
            Assert.Equal(1, view.TrendRowCount);
            Assert.Equal(1, view.IssueRowCount);
            Assert.Contains("1 daily store result(s), 1 governed quality issue(s), and 1 recent unattended run(s)", view.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Investigation_and_administration_views_preserve_viewer_and_owner_gates()
    {
        RunSta(async () =>
        {
            var operations = new FakeOperationsService();
            var investigation = new InvestigationApprovalsWorkspaceView(
                () => "connection", _ => operations, _ => new FakeInvestigationQuery());
            await investigation.RefreshApprovalsAsync();
            Assert.Equal("Your Windows account does not have permission for this action.", investigation.StatusText);
            investigation.UpdateAccess(new(true, false, false));
            await investigation.RefreshApprovalsAsync();
            Assert.Equal(1, investigation.ApprovalRowCount);
            Assert.Equal("1 approval(s) pending.", investigation.StatusText);

            var administrationService = new FakeAdministrationService();
            var administration = new AdministrationWorkspaceView(
                new OperationsAdministrationPresentationSession(), () => "connection", _ => administrationService);
            await administration.RefreshAsync();
            Assert.Equal(0, administrationService.Loads);
            Assert.Equal("Master administration could not be loaded: Owner permission is required.", administration.StatusText);
            administration.UpdateAccess(new(true, true, true));
            await administration.RefreshAsync();
            Assert.Equal(1, administrationService.Loads);
            Assert.Equal(1, administration.MasterRowCount);
            Assert.Equal(1, administration.UserRowCount);
            Assert.Equal("Controlled masters and Windows-integrated access are ready for Owner administration.", administration.StatusText);
        });
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

    private sealed class FakeInvestigationQuery : IInvestigationQuery
    {
        public Task<IReadOnlyList<InvestigationHit>> SearchAsync(string term, int limit = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvestigationHit>>([new("Invoice", "INV-1", "WLMHW", new DateOnly(2026, 8, 27), "Found", "Sales Reports")]);
    }

    private sealed class FakeOperationsService : IOperationsAdministrationService
    {
        public int DashboardLoads { get; private set; }

        public Task<OperationsDashboard> LoadDashboardAsync(OperationsPeriod period, CancellationToken cancellationToken = default)
        {
            DashboardLoads++;
            return Task.FromResult(new OperationsDashboard(
                new WatchFolderConfiguration("in", "done", "failed", "reports", 5, true, DateTime.UtcNow, "owner"),
                [new ManagementTrendPoint(new DateOnly(2026, 8, 27), "WLMHW", 100m, 2m, 1, 0m, 0)],
                [new DataQualityFinding("Warning", "Sales", "Q1", 1, null, "Review")],
                [new DataQualityIssue(1, "Sales", "Warning", "WLMHW", new DateOnly(2026, 8, 27), "Passed", "OPEN", "Review", null, DateTime.UtcNow, null)],
                [new ReportSchedule(1, "Morning", new TimeOnly(8, 0), true, true, true, null, null, null, null)],
                [new AutomationRun(1, "Scheduled", null, "WLMHW", new DateOnly(2026, 8, 27), "Succeeded", "Done", DateTime.UtcNow, DateTime.UtcNow, "system")]));
        }

        public Task<IReadOnlyList<ApprovalRequest>> LoadApprovalsAsync(string? status = "PENDING", CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalRequest>>([new(1, "Adjustment", "StoreDay", "1", "WLMHW", new DateOnly(2026, 8, 27), "manager", DateTime.UtcNow, "PENDING", null, null, null)]);
        public Task<AutomationExecution> RunAutomationOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AutomationExecution(1, 0, 0, 1, "Done"));
        public Task SaveWatchFoldersAsync(SaveWatchFolderConfiguration command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveScheduleAsync(SaveReportSchedule command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateIssueAsync(UpdateDataQualityIssue command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> SubmitAdjustmentAsync(SubmitAdjustment command, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task DecideApprovalAsync(DecideApproval command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAdministrationService : IAdministrationService
    {
        public int Loads { get; private set; }
        public Task<AdministrationDashboard> LoadAsync(string masterType, CancellationToken cancellationToken = default)
        {
            Loads++;
            return Task.FromResult(new AdministrationDashboard(
                [new ControlledMaster("Store", "WLMHW", "Titan World", "APPROVED", true, null, null)],
                [new ApplicationUser(1, @"DOMAIN\owner", "Owner", AccessRole.Owner, true, DateTime.UtcNow, "seed")],
                [new KpiDefinition("SALES", "Sales", "Net sales", "SUM", "ETP", new DateOnly(2026, 4, 1), 1, "APPROVED", "owner", true)],
                [new ProductHealth("Database", "Healthy", "Ready")],
                new ProductConfiguration("docs", "share", null, null, null, null, true, null, 20, DateTime.UtcNow, "owner")));
        }
        public Task SaveMasterAsync(SaveControlledMaster command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveUserAsync(SaveApplicationUser command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveProductConfigurationAsync(SaveProductConfiguration command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
