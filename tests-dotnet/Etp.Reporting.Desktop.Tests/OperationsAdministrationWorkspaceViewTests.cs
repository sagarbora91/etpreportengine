using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Distribution;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;

namespace Etp.Reporting.Desktop.Tests;

public sealed class OperationsAdministrationWorkspaceViewTests
{

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
            Assert.Contains("1 daily store result(s), 1 approved quality issue(s), and 1 recent unattended run(s)", view.StatusText, StringComparison.Ordinal);
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

            // TaskNavigator selects this view's children by position, and TaskBodyLayout gives a
            // named tab only to a DataGrid that is a DIRECT child of the panel. Both have broken
            // before: a section inserted in the middle shifted every index below it, and the
            // recovery grid, nested inside a Border, lost its tab and fell into the catch-all.
            // Pin the positions so the next insert fails here rather than on the shop counter.
            var root = administration.Content is Border border ? border.Child : administration.Content;
            var children = Assert.IsAssignableFrom<Panel>(root).Children;
            foreach (var (index, name) in new[]
            {
                (3, "ControlledMastersGrid"), (8, "ApplicationUsersGrid"), (11, "KpiCatalogueGrid"),
                (13, "ProductHealthGrid"), (14, "AdministrationStatus"),
                (16, "DatabaseRecoveryStatus"), (17, "DatabaseRecoveryGrid"), (18, "DatabaseRecoveryActions"),
            })
            {
                Assert.Equal(name, Assert.IsAssignableFrom<FrameworkElement>(children[index]).Name);
            }
            // The invariant behind the tab, not merely its position.
            Assert.IsType<DataGrid>(children[17]);
            Assert.Equal("Database and recovery", AutomationProperties.GetName(children[17]));
        });
    }

    [Fact]
    public void Schedule_edits_survive_row_change_refresh_failed_save_and_discard()
    {
        RunSta(async () =>
        {
            var service = new FakeOperationsService();
            var view = new OperationsWorkspaceView(new OperationsAdministrationPresentationSession(), () => "connection", _ => service,
                (_, _) => Task.FromResult(new MaintenanceOperationResult(true, "done")));
            view.UpdateAccess(new(true, true, true)); await view.RefreshAsync();
            var grid = (DataGrid)view.FindName("ReportSchedulesGrid");
            var time = (TextBox)view.FindName("ScheduleTimeInput");
            grid.SelectedIndex = 0; time.Text = "09:15";
            grid.SelectedIndex = 1; time.Text = "19:30";
            await view.RefreshAsync(); Assert.Equal("19:30", time.Text);
            grid.SelectedIndex = 0; Assert.Equal("09:15", time.Text);
            Assert.Equal(new[] {1, 2}, view.UnsavedSchedules);
            var pending = new TaskCompletionSource(); service.ScheduleCompletion = pending.Task;
            var save = view.SaveScheduleDraftAsync(); Assert.True(view.IsBusy); Assert.False(view.IsEnabled);
            Assert.False(await view.SaveScheduleDraftAsync()); Assert.Equal(1, service.ScheduleSaves);
            pending.SetException(new InvalidOperationException("Synthetic failure"));
            Assert.False(await save); Assert.Equal("09:15", time.Text); Assert.False(view.IsBusy);
            view.DiscardScheduleDraft(1); Assert.Equal("08:00", time.Text); Assert.Equal(new[] {2}, view.UnsavedSchedules);
            view.DiscardScheduleDraft(2); Assert.Empty(view.UnsavedSchedules);
        });
    }

    [Fact]
    public void Watch_folder_draft_survives_refresh_and_save_failure()
    {
        RunSta(async () =>
        {
            var service = new FakeOperationsService { FailWatchSave = true };
            var view = new OperationsWorkspaceView(new OperationsAdministrationPresentationSession(), () => "connection", _ => service,
                (_, _) => Task.FromResult(new MaintenanceOperationResult(true, "done")));
            view.UpdateAccess(new(true, true, true)); await view.RefreshAsync();
            var input = (TextBox)view.FindName("WatchInboundInput"); input.Text = "new-inbound";
            await view.RefreshAsync(); Assert.Equal("new-inbound", input.Text); Assert.True(view.HasWatchDraft);
            Assert.False(await view.SaveWatchDraftAsync()); Assert.Equal("new-inbound", input.Text);
            view.DiscardWatchDraft(); Assert.False(view.HasWatchDraft); Assert.Equal("in", input.Text);
        });
    }

    [Fact]
    public void Master_types_keep_separate_drafts_and_failure_does_not_clear_them()
    {
        RunSta(async () =>
        {
            var service = new FakeAdministrationService { FailMasterSave = true };
            var view = new AdministrationWorkspaceView(new OperationsAdministrationPresentationSession(), () => "connection", _ => service);
            view.UpdateAccess(new(true, true, true));
            var code = (TextBox)view.FindName("MasterCodeInput");
            code.Text = "STORE-TEST"; view.SelectTask("tender-rules"); code.Text = "TENDER-TEST";
            view.SelectTask("stores"); Assert.Equal("STORE-TEST", code.Text);
            Assert.Equal(2, view.UnsavedDrafts.Count); Assert.False(await view.SaveMasterDraftAsync()); Assert.Equal("STORE-TEST", code.Text);
            view.DiscardDraft("Master: Store"); Assert.Equal("", code.Text); Assert.Single(view.UnsavedDrafts);
            view.SelectTask("tender-rules"); Assert.Equal("TENDER-TEST", code.Text);
            view.DiscardDraft("Master: Tender"); Assert.Empty(view.UnsavedDrafts);
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

    private sealed class FakeInvestigationQuery : IInvestigationQuery
    {
        public Task<IReadOnlyList<InvestigationHit>> SearchAsync(string term, int limit = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvestigationHit>>([new("Invoice", "INV-1", "WLMHW", new DateOnly(2026, 8, 27), "Found", "Sales Reports")]);
    }

    private sealed class FakeOperationsService : IOperationsAdministrationService
    {
        public int DashboardLoads { get; private set; }
        public Task? ScheduleCompletion { get; set; }
        public int ScheduleSaves { get; private set; }
        public bool FailWatchSave { get; set; }

        public Task<OperationsDashboard> LoadDashboardAsync(OperationsPeriod period, CancellationToken cancellationToken = default)
        {
            DashboardLoads++;
            return Task.FromResult(new OperationsDashboard(
                new WatchFolderConfiguration("in", "done", "failed", "reports", 5, true, DateTime.UtcNow, "owner"),
                [new ManagementTrendPoint(new DateOnly(2026, 8, 27), "WLMHW", 100m, 2m, 1, 0m, 0)],
                [new DataQualityFinding("Warning", "Sales", "Q1", 1, null, "Review")],
                [new DataQualityIssue(1, "Sales", "Warning", "WLMHW", new DateOnly(2026, 8, 27), "Passed", "OPEN", "Review", null, DateTime.UtcNow, null)],
                [new ReportSchedule(1, "Morning", new TimeOnly(8, 0), true, true, true, null, null, null, null),
                 new ReportSchedule(2, "Evening", new TimeOnly(18, 0), true, true, true, null, null, null, null)],
                [new AutomationRun(1, "Scheduled", null, "WLMHW", new DateOnly(2026, 8, 27), "Succeeded", "Done", DateTime.UtcNow, DateTime.UtcNow, "system")]));
        }

        public Task<IReadOnlyList<ApprovalRequest>> LoadApprovalsAsync(string? status = "PENDING", CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalRequest>>([new(1, "Adjustment", "StoreDay", "1", "WLMHW", new DateOnly(2026, 8, 27), "manager", DateTime.UtcNow, "PENDING", null, null, null)]);
        public Task<AutomationExecution> RunAutomationOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AutomationExecution(1, 0, 0, 1, "Done"));
        public Task SaveWatchFoldersAsync(SaveWatchFolderConfiguration command, CancellationToken cancellationToken = default) => FailWatchSave ? Task.FromException(new InvalidOperationException("Synthetic failure")) : Task.CompletedTask;
        public Task SaveScheduleAsync(SaveReportSchedule command, CancellationToken cancellationToken = default) { ScheduleSaves++; return ScheduleCompletion ?? Task.CompletedTask; }
        public Task UpdateIssueAsync(UpdateDataQualityIssue command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> SubmitAdjustmentAsync(SubmitAdjustment command, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task DecideApprovalAsync(DecideApproval command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAdministrationService : IAdministrationService
    {
        public bool FailMasterSave { get; set; }
        public int Loads { get; private set; }
        public Task<AdministrationDashboard> LoadAsync(string masterType, CancellationToken cancellationToken = default)
        {
            Loads++;
            return Task.FromResult(new AdministrationDashboard(
                [new ControlledMaster("Store", "WLMHW", "Titan World", "APPROVED", true, null, null)],
                [new ApplicationUser(1, @"DOMAIN\owner", "Owner", AccessRole.Owner, true, DateTime.UtcNow, "seed")],
                [new KpiDefinition("SALES", "Sales", "Net sales", "SUM", "ETP", new DateOnly(2026, 4, 1), 1, "APPROVED", "owner", true)],
                [new ProductHealth("Database", "Healthy", "Ready")],
                new ProductConfiguration("docs", "share", null, null, true, null, 20, DateTime.UtcNow, "owner")));
        }
        public Task SaveMasterAsync(SaveControlledMaster command, CancellationToken cancellationToken = default) => FailMasterSave ? Task.FromException(new InvalidOperationException("Synthetic failure")) : Task.CompletedTask;
        public Task SaveUserAsync(SaveApplicationUser command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveProductConfigurationAsync(SaveProductConfiguration command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
