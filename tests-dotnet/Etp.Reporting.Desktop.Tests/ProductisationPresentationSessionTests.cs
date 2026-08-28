using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Application.Archive;
using Etp.Reporting.Application.Distribution;
using Etp.Reporting.Application.Registers;
using Etp.Reporting.Application.Sharing;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Desktop.Modules.Archive;
using Etp.Reporting.Desktop.Modules.Registers;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ProductisationPresentationSessionTests
{
    [Fact]
    public async Task Archive_package_and_share_remain_bound_to_the_opened_generation()
    {
        var archive = new FakeArchiveQuery();
        var distribution = new FakeDistributionService();
        var session = ArchiveSession(archive, distribution);
        var first = Generation(1, 2);
        var second = Generation(2, 3);

        await session.OpenAsync("connection", first);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.CreatePackageAsync("connection", second, "second.zip", "Owner"));
        Assert.Equal(0, distribution.PackageCalls);

        var receipt = await session.CreatePackageAsync("connection", first, "first.zip", "Owner");

        Assert.Equal("first.zip", receipt.Path);
        Assert.Equal("first.zip", session.ShareFileFor(first));
        Assert.Throws<InvalidOperationException>(() => session.ShareFileFor(second));
        Assert.Equal(first.Id, distribution.LastPackage!.GenerationId);
        Assert.Same(archive.Document, distribution.LastPackage.Document);
    }

    [Fact]
    public async Task Archive_search_and_comparison_clear_document_and_prepared_share_state()
    {
        var archive = new FakeArchiveQuery();
        var distribution = new FakeDistributionService();
        var session = ArchiveSession(archive, distribution);
        var first = Generation(1, 2);
        var second = Generation(2, 3);
        await session.OpenAsync("connection", first);
        await session.CreatePackageAsync("connection", first, "first.zip", "Owner");

        await session.CompareAsync("connection", first, second);

        Assert.Null(session.CurrentDocument);
        Assert.Null(session.CurrentShareFile);
        Assert.Throws<InvalidOperationException>(() => session.ShareFileFor(first));

        await session.OpenAsync("connection", first);
        await session.CreatePackageAsync("connection", first, "first.zip", "Owner");
        await session.SearchAsync("connection", new ReportArchiveSearch("WLMHW", new DateOnly(2026, 8, 25)));
        Assert.Null(session.CurrentDocument);
        Assert.Null(session.CurrentShareFile);
    }

    [Fact]
    public async Task Failed_archive_open_cannot_leave_an_older_document_exportable()
    {
        var archive = new FakeArchiveQuery();
        var session = ArchiveSession(archive, new FakeDistributionService());
        await session.OpenAsync("connection", Generation(1, 2));
        archive.OpenFailure = new InvalidDataException("Hash mismatch");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            session.OpenAsync("connection", Generation(2, 3)));

        Assert.Null(session.CurrentDocument);
        Assert.Throws<InvalidOperationException>(() => session.DocumentForExport(Generation(2, 3)));
    }

    [Fact]
    public async Task Archive_export_cannot_reuse_an_open_document_after_the_grid_selection_changes()
    {
        var session = ArchiveSession(new FakeArchiveQuery(), new FakeDistributionService());
        var opened = Generation(1, 2);
        var newlySelected = Generation(2, 3);
        await session.OpenAsync("connection", opened);

        Assert.Same(session.CurrentDocument, session.DocumentForExport(opened));
        Assert.Throws<InvalidOperationException>(() => session.DocumentForExport(newlySelected));
    }

    [Fact]
    public async Task Archive_email_policy_and_attempt_audit_cross_the_distribution_contract_unchanged()
    {
        var distribution = new FakeDistributionService();
        var session = ArchiveSession(new FakeArchiveQuery(), distribution);
        var generation = Generation(1, 2);
        await session.OpenAsync("connection", generation);
        await session.CreatePackageAsync("connection", generation, "first.zip", "Owner");
        var attempt = new RecordDistributionAttempt(generation.Id, null, "EMAIL", "Configured recipient",
            "first.zip", "INITIATED", "Email draft opened; delivery is not claimed.");

        var policy = await session.ValidateEmailAttachmentAsync("connection", generation);
        await session.RecordAttemptAsync("connection", attempt);

        Assert.Equal(12, policy.MaximumAttachmentMb);
        Assert.Equal("first.zip", distribution.ValidatedAttachment);
        Assert.Equal(attempt, distribution.LastAttempt);
    }

    [Fact]
    public async Task Accounting_preview_owns_the_exact_draft_and_generation_used_for_save()
    {
        var service = new FakeAccountingService();
        var session = new AccountingPresentationSession(_ => service);
        var scope = new AccountingScope("WLMHW", new DateOnly(2026, 8, 25));

        var preview = await session.PreviewAsync("connection", scope);
        var id = await session.SaveCurrentAsync("connection", scope);

        Assert.Equal(41, id);
        Assert.Equal(preview.ReportGenerationId, session.Current.ReportGenerationId);
        Assert.Same(preview.Batch, session.Current.Draft);
        Assert.Equal(preview.ReportGenerationId, service.LastSave!.ReportGenerationId);
        Assert.Equal(scope, service.LastSave.Scope);
        Assert.Same(preview.Batch, service.LastSave.Batch);
        Assert.Equal(preview.Batch.DebitTotal, service.LastSave.Batch.CreditTotal);
    }

    [Fact]
    public async Task Failed_accounting_preview_clears_stale_draft_and_export_requires_approval()
    {
        var service = new FakeAccountingService();
        var session = new AccountingPresentationSession(_ => service);
        var scope = new AccountingScope("WLMHW", new DateOnly(2026, 8, 25));
        await session.PreviewAsync("connection", scope);
        service.PreviewFailure = new InvalidOperationException("blocked");

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PreviewAsync("connection", scope));

        Assert.Null(session.Current.Draft);
        Assert.Null(session.Current.ReportGenerationId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SaveCurrentAsync("connection", scope));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ExportAsync("connection", Batch("DRAFT"), "Saagar Traders", "batch.xml"));
        Assert.Equal(0, service.ExportCalls);
    }

    [Fact]
    public async Task Accounting_save_cannot_reuse_a_preview_after_scope_changes_or_refreshes()
    {
        var service = new FakeAccountingService();
        var session = new AccountingPresentationSession(_ => service);
        var previewed = new AccountingScope("WLMHW", new DateOnly(2026, 8, 25));
        var changed = new AccountingScope("HEMW", new DateOnly(2026, 8, 26));
        await session.PreviewAsync("connection", previewed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveCurrentAsync("connection", changed));
        Assert.Null(service.LastSave);

        await session.RefreshAsync("connection");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveCurrentAsync("connection", previewed));
        Assert.Null(service.LastSave);
    }

    [Fact]
    public async Task Registers_session_owns_refreshed_rows_and_relays_the_audited_save()
    {
        var service = new FakeRegisterService();
        var session = new RegistersPresentationSession(_ => service);
        var draft = new DigitalRegisterEntryDraft("INWARD", 7, "WLMHW", new DateOnly(2026, 8, 25),
            "INV-1", null, "Vendor", 2, 100, "ref", "Owner", "DRAFT", "remarks");

        var rows = await session.RefreshAsync("connection", "INV-1");
        var id = await session.SaveAsync("connection", draft, "Verified source");

        Assert.Same(rows, session.Entries);
        Assert.Equal(17, id);
        Assert.Equal("INV-1", service.LastSearch);
        Assert.Equal(draft, service.LastDraft);
        Assert.Equal("Verified source", service.LastReason);
    }

    private static ArchiveDistributionPresentationSession ArchiveSession(
        FakeArchiveQuery archive,
        FakeDistributionService distribution) =>
        new(_ => archive, _ => new FakeSharingContactsService(), _ => distribution, _ => true);

    private static ArchivedReportGenerationSummary Generation(long id, int number) =>
        new(id, "WLMHW", new DateOnly(2026, 8, 25), number, "control", "document",
            new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc), "Owner", true, null, true);

    private static AccountingBatchSummary Batch(string status) =>
        new(9, "WLMHW", new DateOnly(2026, 8, 25), 31, 2, 100, 100, status,
            null, null, null, new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

    private sealed class FakeArchiveQuery : IReportArchiveQuery<ReportPackDocument>
    {
        public Exception? OpenFailure { get; set; }
        public ReportPackDocument Document { get; } = new("Daily Pack", new(2026, 8, 25), new(2026, 8, 25),
            "Passed", "v1", "Complete", DateTimeOffset.UtcNow, []);

        public Task<IReadOnlyList<ArchivedReportGenerationSummary>> SearchAsync(
            ReportArchiveSearch search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArchivedReportGenerationSummary>>([]);

        public Task<ReportPackDocument> OpenAsync(long generationId, CancellationToken cancellationToken = default) =>
            OpenFailure is null ? Task.FromResult(Document) : Task.FromException<ReportPackDocument>(OpenFailure);

        public Task<IReadOnlyList<ArchivedReportComparisonSection>> CompareAsync(
            long firstGenerationId, long secondGenerationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArchivedReportComparisonSection>>([]);
    }

    private sealed class FakeDistributionService : IReportDistributionService<ReportPackDocument>
    {
        public int PackageCalls { get; private set; }
        public CreateReportPackage<ReportPackDocument>? LastPackage { get; private set; }
        public string? ValidatedAttachment { get; private set; }
        public RecordDistributionAttempt? LastAttempt { get; private set; }

        public Task<ReportPackageReceipt> CreatePackageAsync(
            CreateReportPackage<ReportPackDocument> command, CancellationToken cancellationToken = default)
        {
            PackageCalls++;
            LastPackage = command;
            return Task.FromResult(new ReportPackageReceipt(command.OutputPath, new string('a', 64), "{}", []));
        }

        public Task<EmailAttachmentPolicy> ValidateEmailAttachmentAsync(
            string attachmentPath, CancellationToken cancellationToken = default)
        {
            ValidatedAttachment = attachmentPath;
            return Task.FromResult(new EmailAttachmentPolicy("share", 12));
        }

        public Task RecordAttemptAsync(RecordDistributionAttempt command, CancellationToken cancellationToken = default)
        {
            LastAttempt = command;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSharingContactsService : ISharingContactsService
    {
        public Task<IReadOnlyList<SharingContact>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SharingContact>>([]);

        public Task<int> SaveAsync(SharingContactDraft contact, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }

    private sealed class FakeRegisterService : IDigitalRegisterService
    {
        private readonly IReadOnlyList<DigitalRegisterEntry> rows =
        [
            new(17, "INWARD", 7, "WLMHW", new DateOnly(2026, 8, 25), "INV-1", null, "Vendor", 2,
                100, "ref", "Owner", "DRAFT", "remarks", "Owner", new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc))
        ];

        public string? LastSearch { get; private set; }
        public DigitalRegisterEntryDraft? LastDraft { get; private set; }
        public string? LastReason { get; private set; }

        public Task<IReadOnlyList<DigitalRegisterEntry>> LoadAsync(
            string? search = null, int limit = 500, CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            return Task.FromResult(rows);
        }

        public Task<long> SaveAsync(
            DigitalRegisterEntryDraft entry, string reason, CancellationToken cancellationToken = default)
        {
            LastDraft = entry;
            LastReason = reason;
            return Task.FromResult(17L);
        }
    }

    private sealed class FakeAccountingService : IAccountingService
    {
        private readonly AccountingBatchDraft draft = new(
            [new AccountingEntry(1, "SALES", "Sales", 100, 0, "Sale", null, "R025"),
             new AccountingEntry(2, "SALES", "Cash", 0, 100, "Sale", null, "R025")],
            100, 100, true, []);

        public Exception? PreviewFailure { get; set; }
        public SaveAccountingBatch? LastSave { get; private set; }
        public int ExportCalls { get; private set; }

        public Task<AccountingSource> LoadSourceAsync(AccountingScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountingSource(31, []));

        public Task<IReadOnlyList<ApprovedAccountingMapping>> LoadApprovedMappingsAsync(
            AccountingScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovedAccountingMapping>>([]);

        public Task<AccountingPreview> PreviewAsync(AccountingScope scope, CancellationToken cancellationToken = default) =>
            PreviewFailure is null
                ? Task.FromResult(new AccountingPreview(31, draft))
                : Task.FromException<AccountingPreview>(PreviewFailure);

        public Task<IReadOnlyList<AccountingBatchSummary>> LoadBatchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountingBatchSummary>>([]);

        public Task<IReadOnlyList<AccountingEntry>> LoadEntriesAsync(long batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountingEntry>>(draft.Entries);

        public Task<long> SaveAsync(SaveAccountingBatch command, CancellationToken cancellationToken = default)
        {
            LastSave = command;
            return Task.FromResult(41L);
        }

        public Task ApproveAsync(ApproveAccountingBatch command, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApproveMappingAsync(ApproveAccountingMapping command, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<AccountingExportReceipt> ExportAsync(ExportAccountingBatch command, CancellationToken cancellationToken = default)
        {
            ExportCalls++;
            return Task.FromResult(new AccountingExportReceipt(command.BatchId, command.OutputPath, new string('b', 64)));
        }
    }
}
