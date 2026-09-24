using App = Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class AccountingServiceBoundaryTests
{
    [Fact]
    public async Task Rejection_requires_owner_and_a_reason()
    {
        var gateway=new FakeGateway();
        foreach(var role in new[]{ApplicationRole.Viewer,ApplicationRole.StoreManager})
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Create(gateway,role).RejectAsync(new(5,"Correction")));
        await Assert.ThrowsAsync<ArgumentException>(()=>Create(gateway,ApplicationRole.Owner).RejectAsync(new(5," ")));
        Assert.Empty(gateway.Calls);
        await Create(gateway,ApplicationRole.Owner).RejectAsync(new(5,"Correction"));
        Assert.Equal(new[]{"reject:5"},gateway.Calls);
    }
    [Fact]
    public void Production_adapter_requires_windows_integrated_security()
    {
        _ = new SqlServerAccountingService(
            @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True");

        Assert.Throws<ArgumentException>(() => new SqlServerAccountingService(
            @"Server=.\SQLEXPRESS;Database=EtpReporting;User ID=sa;Password=secret"));
    }

    [Fact]
    public async Task Preview_uses_final_source_and_approved_mappings_without_losing_balance_controls()
    {
        var gateway = new FakeGateway
        {
            Source = (17,
            [
                new("NET_SALES", 100m, "WLMHW/20260825", "Net sales"),
                new("RETURN", -10m, "WLMHW/20260825", "Sales return")
            ]),
            Mappings =
            [
                new("NET_SALES", "Tender Control", "Sales", "{description} {reference}"),
                new("RETURN", "Sales Return", "Tender Control", "{description}")
            ]
        };
        var service = Create(gateway, ApplicationRole.Owner);

        var preview = await service.PreviewAsync(new("WLMHW", new(2026, 8, 25)));

        Assert.Equal(17, preview.ReportGenerationId);
        Assert.True(preview.Batch.IsBalanced);
        Assert.Equal(110m, preview.Batch.DebitTotal);
        Assert.Equal(110m, preview.Batch.CreditTotal);
        Assert.True(App.AccountingBatchControls.IsBalancedAndComplete(preview.Batch));
    }

    [Fact]
    public async Task Save_requires_owner_access_and_recomputed_balanced_totals()
    {
        var gateway = new FakeGateway();
        var unbalanced = new App.AccountingBatchDraft(
            [new(1, "NET_SALES", "Sales", 10m, 0m, "Sale", null, "source")],
            10m,
            10m,
            true,
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(gateway, ApplicationRole.StoreManager)
            .SaveAsync(new(new("WLMHW", new(2026, 8, 25)), 17, unbalanced)));
        Assert.Equal(0, gateway.SaveCalls);

        var balanced = BalancedDraft();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(gateway, ApplicationRole.Viewer)
            .SaveAsync(new(new("WLMHW", new(2026, 8, 25)), 17, balanced)));
        Assert.Equal(0, gateway.SaveCalls);

        Assert.Equal(91, await Create(gateway, ApplicationRole.Owner)
            .SaveAsync(new(new("WLMHW", new(2026, 8, 25)), 17, balanced)));
        Assert.Equal(1, gateway.SaveCalls);
    }

    [Fact]
    public async Task Mapping_approval_is_owner_only_and_submits_one_atomic_operation()
    {
        var gateway = new FakeGateway();
        var command = new App.ApproveAccountingMapping(
            new("WLMHW", new(2026, 8, 25)),
            "net_sales",
            "Tender Control",
            "Sales",
            "{description} {reference}",
            "Owner-approved mapping");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Create(gateway, ApplicationRole.StoreManager).ApproveMappingAsync(command));
        Assert.Empty(gateway.Calls);

        await Create(gateway, ApplicationRole.Owner).ApproveMappingAsync(command);

        Assert.Equal(["mapping:NET_SALES"], gateway.Calls);
    }

    [Fact]
    public async Task Export_is_owner_only_requires_approved_balanced_data_and_records_audit_after_file_generation()
    {
        var gateway = new FakeGateway
        {
            Batches =
            [
                new(5, "WLMHW", new(2026, 8, 25), 17, 1, 10m, 10m, "APPROVED_READY",
                    "OWNER", null, null, new(2026, 8, 25, 10, 0, 0))
            ],
            Entries =
            [
                new(1, "NET_SALES", "Tender Control", 10m, 0m, "Sale", null, "source"),
                new(2, "NET_SALES", "Sales", 0m, 10m, "Sale", null, "source")
            ]
        };
        var output = Path.Combine(Path.GetTempPath(), "EtpAccountingTests", "batch.xml");
        var service = Create(gateway, ApplicationRole.Owner, (_, _, _, _, _) =>
        {
            gateway.Calls.Add("export");
            return Task.FromResult(new string('a', 64));
        });

        var receipt = await service.ExportAsync(new(5, "Saagar Traders", output));

        Assert.Equal(5, receipt.BatchId);
        Assert.Equal(new string('a', 64), receipt.Sha256);
        Assert.Equal(["export", "audit:5"], gateway.Calls);

        gateway.Calls.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Create(gateway, ApplicationRole.StoreManager).ExportAsync(new(5, "Saagar Traders", output)));
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Destination_requires_owner_and_explicit_production_confirmation_and_live_exports_are_blocked()
    {
        var gateway = new FakeGateway();
        var owner = Create(gateway,ApplicationRole.Owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Create(gateway,ApplicationRole.StoreManager).LoadDestinationAsync());
        await Assert.ThrowsAsync<ArgumentException>(()=>owner.SaveDestinationAsync(new("Firm", "PRODUCTION", "firm", "Checked")));
        await owner.SaveDestinationAsync(new("Firm", "PRODUCTION", "Firm", "Checked"));
        var live = await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ExportAsync(new(5,"", "test.xml")));
        Assert.Contains("D18",live.Message);
        gateway.Destination = new(null,"TEST");
        var unset = await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ExportAsync(new(5,"", "test.xml")));
        Assert.Contains("D12",unset.Message);
        Assert.Empty(gateway.Calls);
    }

    private static SqlServerAccountingService Create(
        FakeGateway gateway,
        ApplicationRole role,
        Func<string, string, DateOnly, AccountingBatchDraft, CancellationToken, Task<string>>? export = null) =>
        new(
            gateway,
            _ => Task.FromResult(new ApplicationAccess("STORE\\User", "User", role, true)),
            export ?? ((_, _, _, _, _) => Task.FromResult(new string('b', 64))));

    private static App.AccountingBatchDraft BalancedDraft() =>
        new(
        [
            new(1, "NET_SALES", "Tender Control", 10m, 0m, "Sale", null, "source"),
            new(2, "NET_SALES", "Sales", 0m, 10m, "Sale", null, "source")
        ],
        10m,
        10m,
        true,
        []);

    private sealed class FakeGateway : IAccountingSqlGateway
    {
        public (long GenerationId, IReadOnlyList<AccountingBusinessEvent> Events) Source { get; set; } =
            (1, Array.Empty<AccountingBusinessEvent>());
        public IReadOnlyList<AccountingMapping> Mappings { get; set; } = [];
        public IReadOnlyList<AccountingBatchRow> Batches { get; set; } = [];
        public IReadOnlyList<AccountingEntryDraft> Entries { get; set; } = [];
        public List<string> Calls { get; } = [];
        public int SaveCalls { get; private set; }

        public Task<(long GenerationId, IReadOnlyList<AccountingBusinessEvent> Events)> LoadSourceAsync(
            string storeCode, DateOnly businessDate, CancellationToken cancellationToken) => Task.FromResult(Source);

        public Task<IReadOnlyList<AccountingMapping>> LoadMappingsAsync(
            string storeCode, DateOnly businessDate, CancellationToken cancellationToken) => Task.FromResult(Mappings);

        public Task<IReadOnlyList<AccountingBatchRow>> LoadBatchesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Batches);

        public Task<IReadOnlyList<AccountingEntryDraft>> LoadEntriesAsync(
            long batchId, CancellationToken cancellationToken) => Task.FromResult(Entries);

        public Task<long> SaveBatchAsync(
            string storeCode, DateOnly businessDate, long reportGenerationId,
            AccountingBatchDraft batch, CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.FromResult(91L);
        }

        public Task ApproveBatchAsync(long batchId, string reason, CancellationToken cancellationToken)
        {
            Calls.Add($"approve:{batchId}");
            return Task.CompletedTask;
        }

        public Task RejectBatchAsync(long batchId, string reason, CancellationToken cancellationToken)
        { Calls.Add($"reject:{batchId}"); return Task.CompletedTask; }

        public Task ApproveMappingAsync(App.ApproveAccountingMapping command, CancellationToken cancellationToken)
        {
            Calls.Add($"mapping:{command.BusinessEvent}");
            return Task.CompletedTask;
        }

        public App.AccountingDestination Destination { get; set; } = new("Saagar Traders", "TEST");
        public Task<App.AccountingDestination> LoadDestinationAsync(CancellationToken token) => Task.FromResult(Destination);
        public Task SaveDestinationAsync(App.SaveAccountingDestination command, CancellationToken token) { Destination = new(command.CompanyName,command.EnvironmentLabel); return Task.CompletedTask; }
        public Task<IReadOnlyList<App.AccountingExportReceipt>> LoadExportHistoryAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<App.AccountingExportReceipt>>([]);
        public async Task<App.AccountingExportReceipt> ExportBatchAsync(long batchId,string outputPath,App.AccountingDestination destination,Func<CancellationToken,Task<string>> writeFile,CancellationToken token)
        {
            var hash=await writeFile(token);
            Calls.Add($"audit:{batchId}");
            return new(batchId,outputPath,hash,destination.CompanyName!,destination.EnvironmentLabel);
        }
    }
}
