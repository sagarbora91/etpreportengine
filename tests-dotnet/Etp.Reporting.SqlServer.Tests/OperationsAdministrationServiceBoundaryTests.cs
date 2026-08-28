using Etp.Reporting.Application.Access;
using App = Etp.Reporting.Application.OperationsAdministration;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class OperationsAdministrationServiceBoundaryTests
{
    [Fact]
    public void Production_adapters_require_windows_integrated_security()
    {
        const string integrated = @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";
        _ = new SqlServerOperationsAdministrationService(integrated);
        _ = new SqlServerAdministrationService(integrated);

        const string sqlLogin = @"Server=.\SQLEXPRESS;Database=EtpReporting;User ID=sa;Password=secret";
        Assert.Throws<ArgumentException>(() => new SqlServerOperationsAdministrationService(sqlLogin));
        Assert.Throws<ArgumentException>(() => new SqlServerAdministrationService(sqlLogin));
    }

    [Fact]
    public async Task Dashboard_maps_current_sources_and_only_operational_roles_sync_issues()
    {
        var gateway = new FakeOperationsGateway();
        var period = new App.OperationsPeriod(new(2026, 8, 1), new(2026, 8, 28), 25);

        var viewerDashboard = await Operations(gateway, ApplicationRole.Viewer).LoadDashboardAsync(period);

        Assert.Equal("inbound", viewerDashboard.WatchFolders.InboundPath);
        Assert.Equal("WLMHW", Assert.Single(viewerDashboard.Trend).StoreCode);
        Assert.Equal("MISSING_SOURCE", Assert.Single(viewerDashboard.Quality).Code);
        Assert.Equal("OPEN", Assert.Single(viewerDashboard.Issues).WorkflowStatus);
        Assert.Equal(25, gateway.LastRunLimit);
        Assert.DoesNotContain("sync", gateway.Calls);

        gateway.Calls.Clear();
        await Operations(gateway, ApplicationRole.StoreManager).LoadDashboardAsync(period);
        Assert.True(gateway.Calls.IndexOf("quality") < gateway.Calls.IndexOf("sync"));
        Assert.True(gateway.Calls.IndexOf("sync") < gateway.Calls.IndexOf("issues"));
    }

    [Fact]
    public async Task Store_manager_can_run_and_resolve_but_cannot_change_owner_configuration_or_approvals()
    {
        var gateway = new FakeOperationsGateway();
        var service = Operations(gateway, ApplicationRole.StoreManager);

        Assert.Equal(3, (await service.RunAutomationOnceAsync()).SourcesProcessed);
        await service.UpdateIssueAsync(new(8, "ACKNOWLEDGED", "Investigating source"));
        Assert.Equal(44, await service.SubmitAdjustmentAsync(
            new("wlmhw", new(2026, 8, 28), "sales", 10m, "Controlled correction")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveScheduleAsync(
            new(1, new(18, 0), true, true, true, "Owner schedule change")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveWatchFoldersAsync(
            new("in", "processed", "failed", "reports", 5, true, "Owner config change")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DecideApprovalAsync(
            new(12, true, "Owner decision")));

        Assert.Equal(["run", "issue:8:ACKNOWLEDGED", "adjust:WLMHW"], gateway.Calls);
    }

    [Fact]
    public async Task Owner_configuration_and_approval_commands_reach_existing_audited_repository_seams()
    {
        var gateway = new FakeOperationsGateway();
        var service = Operations(gateway, ApplicationRole.Owner);

        await service.SaveWatchFoldersAsync(new("in", "processed", "failed", "reports", 5, true, "Changed paths"));
        await service.SaveScheduleAsync(new(2, new(7, 30), true, true, false, "Changed schedule"));
        await service.DecideApprovalAsync(new(91, false, "Evidence insufficient"));

        Assert.Equal(["watch:Changed paths", "schedule:2:Changed schedule", "approval:91:False"], gateway.Calls);
    }

    [Fact]
    public async Task Administration_is_owner_only_and_role_mapping_is_fail_closed()
    {
        var gateway = new FakeAdministrationGateway();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Administration(gateway, ApplicationRole.StoreManager).LoadAsync("STORE"));
        Assert.Empty(gateway.Calls);

        var dashboard = await Administration(gateway, ApplicationRole.Owner).LoadAsync("STORE");
        Assert.Equal(AccessRole.StoreManager, Assert.Single(dashboard.Users).Role);
        Assert.Equal(AccessRole.None, SqlServerAdministrationService.MapRole("unexpected"));

        gateway.Calls.Clear();
        var owner = Administration(gateway, ApplicationRole.Owner);
        await owner.SaveUserAsync(new(@"STORE\Manager", "Manager", AccessRole.StoreManager, true, "Access approved"));
        await owner.SaveMasterAsync(new("STORE", "WLMHW", "WLM Highway", "APPROVED", true, "Master approved"));
        await owner.SaveProductConfigurationAsync(new("documents", "share", null, null, null, null, true, null, 20, "Settings approved"));

        Assert.Equal(["user:STORE_MANAGER", "master:STORE:WLMHW", "settings:Settings approved"], gateway.Calls);
        await Assert.ThrowsAsync<ArgumentException>(() => owner.SaveUserAsync(
            new(@"STORE\Unknown", "Unknown", AccessRole.None, true, "Invalid role")));
    }

    private static SqlServerOperationsAdministrationService Operations(
        FakeOperationsGateway gateway,
        ApplicationRole role) =>
        new(gateway, _ => Task.FromResult(new ApplicationAccess(@"STORE\User", "User", role, true)));

    private static SqlServerAdministrationService Administration(
        FakeAdministrationGateway gateway,
        ApplicationRole role) =>
        new(gateway, _ => Task.FromResult(new ApplicationAccess(@"STORE\User", "User", role, true)));

    private sealed class FakeOperationsGateway : IOperationsAdministrationSqlGateway
    {
        public List<string> Calls { get; } = [];
        public int LastRunLimit { get; private set; }

        public Task<WatchFolderSettings> LoadWatchFoldersAsync(CancellationToken token)
        {
            Calls.Add("watch-load");
            return Task.FromResult(new WatchFolderSettings("inbound", "processed", "failed", "reports", 5, true, DateTime.MinValue, "owner"));
        }

        public Task<IReadOnlyList<ManagementTrendRow>> LoadTrendAsync(DateOnly from, DateOnly to, CancellationToken token)
        {
            Calls.Add("trend");
            return Task.FromResult<IReadOnlyList<ManagementTrendRow>>(
                [new(new(2026, 8, 28), "WLMHW", 100m, 2m, 1, 0m, 0)]);
        }

        public Task<IReadOnlyList<DataQualitySummaryRow>> LoadQualityAsync(CancellationToken token)
        {
            Calls.Add("quality");
            return Task.FromResult<IReadOnlyList<DataQualitySummaryRow>>(
                [new("CRITICAL", "IMPORT", "MISSING_SOURCE", 1, DateTime.MinValue, "Missing source")]);
        }

        public Task<IReadOnlyList<ReportPackSchedule>> LoadSchedulesAsync(CancellationToken token)
        {
            Calls.Add("schedules");
            return Task.FromResult<IReadOnlyList<ReportPackSchedule>>(
                [new(1, "Morning", new(7, 0), true, true, true, null, null, null, null)]);
        }

        public Task<IReadOnlyList<AutomationRunRow>> LoadAutomationRunsAsync(int limit, CancellationToken token)
        {
            Calls.Add("runs");
            LastRunLimit = limit;
            return Task.FromResult<IReadOnlyList<AutomationRunRow>>([]);
        }

        public Task SyncIssuesAsync(IReadOnlyList<DataQualitySummaryRow> findings, CancellationToken token)
        {
            Calls.Add("sync");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DataQualityIssueRow>> LoadIssuesAsync(CancellationToken token)
        {
            Calls.Add("issues");
            return Task.FromResult<IReadOnlyList<DataQualityIssueRow>>(
                [new(8, "MISSING_SOURCE", "CRITICAL", "WLMHW", new(2026, 8, 28), "FAIL", "OPEN", "Missing source", null, DateTime.MinValue, null)]);
        }

        public Task<IReadOnlyList<ApprovalRequestRow>> LoadApprovalsAsync(string? status, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ApprovalRequestRow>>([]);

        public Task<AutomatedOperationsSummary> RunAutomationOnceAsync(CancellationToken token)
        {
            Calls.Add("run");
            return Task.FromResult(new AutomatedOperationsSummary(3, 0, 1, 2, "Complete"));
        }

        public Task SaveWatchFoldersAsync(WatchFolderSettings settings, string reason, CancellationToken token)
        {
            Calls.Add($"watch:{reason}");
            return Task.CompletedTask;
        }

        public Task SaveScheduleAsync(int id, TimeOnly time, bool enabled, bool excel, bool pdf, string reason, CancellationToken token)
        {
            Calls.Add($"schedule:{id}:{reason}");
            return Task.CompletedTask;
        }

        public Task UpdateIssueAsync(long issueId, string status, string reason, CancellationToken token)
        {
            Calls.Add($"issue:{issueId}:{status}");
            return Task.CompletedTask;
        }

        public Task<long> SubmitAdjustmentAsync(string storeCode, DateOnly date, string type, decimal amount, string reason, long? documentId, CancellationToken token)
        {
            Calls.Add($"adjust:{storeCode.ToUpperInvariant()}");
            return Task.FromResult(44L);
        }

        public Task DecideApprovalAsync(long approvalId, bool approve, string reason, CancellationToken token)
        {
            Calls.Add($"approval:{approvalId}:{approve}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAdministrationGateway : IAdministrationSqlGateway
    {
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<ControlledMasterRow>> LoadMastersAsync(string masterType, CancellationToken token)
        {
            Calls.Add("masters");
            return Task.FromResult<IReadOnlyList<ControlledMasterRow>>([]);
        }

        public Task<IReadOnlyList<ApplicationUserRow>> LoadUsersAsync(CancellationToken token)
        {
            Calls.Add("users");
            return Task.FromResult<IReadOnlyList<ApplicationUserRow>>(
                [new(1, @"STORE\Manager", "Manager", "STORE_MANAGER", true, DateTime.MinValue, "owner")]);
        }

        public Task<IReadOnlyList<KpiCatalogueRow>> LoadKpisAsync(CancellationToken token)
        {
            Calls.Add("kpis");
            return Task.FromResult<IReadOnlyList<KpiCatalogueRow>>([]);
        }

        public Task<IReadOnlyList<ProductHealthItem>> LoadProductHealthAsync(CancellationToken token)
        {
            Calls.Add("health");
            return Task.FromResult<IReadOnlyList<ProductHealthItem>>([]);
        }

        public Task<ProductSettings> LoadProductConfigurationAsync(CancellationToken token)
        {
            Calls.Add("settings-load");
            return Task.FromResult(new ProductSettings("documents", "share", null, null, null, null, true, null, 20, DateTime.MinValue, "owner"));
        }

        public Task SaveMasterAsync(string type, string code, string name, string approval, bool active, string reason, CancellationToken token)
        {
            Calls.Add($"master:{type}:{code}");
            return Task.CompletedTask;
        }

        public Task SaveUserAsync(string identity, string name, string role, bool active, string reason, CancellationToken token)
        {
            Calls.Add($"user:{role}");
            return Task.CompletedTask;
        }

        public Task SaveProductConfigurationAsync(ProductSettings settings, string reason, CancellationToken token)
        {
            Calls.Add($"settings:{reason}");
            return Task.CompletedTask;
        }
    }
}
