using Etp.Reporting.Application.SourceInbox;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class SourceInboxServiceAdapterTests
{
    private const string IntegratedConnection =
        @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";

    [Fact]
    public async Task Inbox_load_maps_all_document_fields_and_preserves_filter_limit_and_token()
    {
        var received = new DateTime(2026, 8, 28, 9, 15, 0, DateTimeKind.Utc);
        IReadOnlyList<SourceDocumentRow> rows =
        [
            new(18, "invoice.pdf", @"C:\managed\abc.pdf", new string('a', 64), 2048, "PDF", "VENDOR_INVOICE",
                "WLMHW", new(2026, 8, 27), "REVIEW_REQUIRED", null, null, null, @"STORE\Manager", received,
                "Document attached to the business day.")
        ];
        string? observedStatus = null;
        var observedLimit = 0;
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            loadDocuments: (status, limit, token) =>
            {
                observedStatus = status;
                observedLimit = limit;
                observedToken = token;
                return Task.FromResult(rows);
            });

        var document = Assert.Single(await service.LoadDocumentsAsync("REVIEW_REQUIRED", 75, cancellation.Token));

        Assert.Equal(
            new SourceInboxDocument(18, "invoice.pdf", @"C:\managed\abc.pdf", new string('a', 64), 2048, "PDF",
                "VENDOR_INVOICE", "WLMHW", new(2026, 8, 27), "REVIEW_REQUIRED", null, null, null,
                @"STORE\Manager", received, "Document attached to the business day."),
            document);
        Assert.Equal("REVIEW_REQUIRED", observedStatus);
        Assert.Equal(75, observedLimit);
        Assert.Equal(cancellation.Token, observedToken);
    }

    [Fact]
    public async Task Intake_preserves_document_day_hash_and_duplicate_semantics()
    {
        var date = new DateOnly(2026, 8, 27);
        var received = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc);
        var row = new SourceDocumentRow(31, "receipt.pdf", @"C:\managed\hash.pdf", new string('b', 64), 4096,
            "PDF", "SERVICE_RECEIPT", "WLMHW", date, "REVIEW_REQUIRED", null, null, null,
            @"STORE\Manager", received, "Document attached to the business day.");
        SourceDocumentIntakeRequest? observed = null;
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            intake: (path, store, businessDate, type, token) =>
            {
                observed = new(path, store, businessDate, type);
                observedToken = token;
                return Task.FromResult(new DocumentIntakeOutcome(row, false));
            });
        var request = new SourceDocumentIntakeRequest(@"C:\incoming\receipt.pdf", "WLMHW", date, "SERVICE_RECEIPT");

        var outcome = await service.IntakeAsync(request, cancellation.Token);

        Assert.Equal(request, observed);
        Assert.Equal(cancellation.Token, observedToken);
        Assert.False(outcome.Duplicate);
        Assert.Equal(SqlServerSourceInboxService.Map(row), outcome.Document);
    }

    [Fact]
    public async Task Duplicate_intake_returns_existing_retained_document()
    {
        var row = new SourceDocumentRow(31, "renamed.pdf", @"C:\managed\hash.pdf", new string('c', 64), 4096,
            "PDF", null, null, null, "RECEIVED", null, null, null, @"STORE\Manager", DateTime.UtcNow, null);
        var service = CreateService(
            intake: (_, _, _, _, _) => Task.FromResult(new DocumentIntakeOutcome(row, true)));

        var outcome = await service.IntakeAsync(new(@"C:\incoming\renamed.pdf", null, null, null));

        Assert.True(outcome.Duplicate);
        Assert.Equal(row.Id, outcome.Document.Id);
        Assert.Equal(row.Sha256, outcome.Document.Sha256);
    }

    [Fact]
    public async Task Integrity_check_uses_the_retained_path_and_expected_sha256()
    {
        var document = new SourceInboxDocument(31, "receipt.pdf", @"C:\managed\hash.pdf", new string('d', 64),
            4096, "PDF", null, null, null, "RECEIVED", null, null, null, @"STORE\Manager", DateTime.UtcNow, null);
        string? observedPath = null;
        string? observedHash = null;
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            verifyIntegrity: (path, hash, token) =>
            {
                observedPath = path;
                observedHash = hash;
                observedToken = token;
                return Task.FromResult(true);
            });

        Assert.True(await service.VerifyIntegrityAsync(document, cancellation.Token));
        Assert.Equal(document.ManagedFilePath, observedPath);
        Assert.Equal(document.Sha256, observedHash);
        Assert.Equal(cancellation.Token, observedToken);
    }

    [Fact]
    public async Task Intake_integrity_failures_propagate_without_becoming_successful_outcomes()
    {
        var service = CreateService(
            intake: (_, _, _, _, _) => Task.FromException<DocumentIntakeOutcome>(
                new IOException("The managed document failed its integrity check.")));

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            service.IntakeAsync(new(@"C:\incoming\receipt.pdf", null, null, null)));

        Assert.Equal("The managed document failed its integrity check.", exception.Message);
    }

    [Fact]
    public async Task Inactive_users_are_rejected_before_source_rows_are_loaded()
    {
        var repositoryCalled = false;
        var service = CreateService(
            loadDocuments: (_, _, _) =>
            {
                repositoryCalled = true;
                return Task.FromResult<IReadOnlyList<SourceDocumentRow>>([]);
            },
            loadAccess: _ => Task.FromResult(new ApplicationAccess("user", "User", ApplicationRole.Viewer, false)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadDocumentsAsync());

        Assert.False(repositoryCalled);
    }

    [Fact]
    public async Task Viewers_are_rejected_before_source_intake_writes()
    {
        var writeCalled = false;
        var service = CreateService(
            intake: (_, _, _, _, _) =>
            {
                writeCalled = true;
                return Task.FromException<DocumentIntakeOutcome>(new InvalidOperationException());
            },
            loadAccess: _ => Task.FromResult(new ApplicationAccess("viewer", "Viewer", ApplicationRole.Viewer, true)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.IntakeAsync(new("source.pdf", null, null, null)));

        Assert.False(writeCalled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Server=.;Database=EtpReporting;User ID=reporter;Password=not-used")]
    [InlineData("Server=.;Database=EtpReporting;Integrated Security=True;User ID=reporter;Password=not-used")]
    public void Public_adapter_rejects_missing_or_sql_authenticated_connections(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new SqlServerSourceInboxService(connectionString));
    }

    [Fact]
    public void Public_adapter_accepts_windows_integrated_connections()
    {
        _ = new SqlServerSourceInboxService(IntegratedConnection);
    }

    private static SqlServerSourceInboxService CreateService(
        Func<string?, int, CancellationToken, Task<IReadOnlyList<SourceDocumentRow>>>? loadDocuments = null,
        Func<string, string?, DateOnly?, string?, CancellationToken, Task<DocumentIntakeOutcome>>? intake = null,
        Func<string, string, CancellationToken, Task<bool>>? verifyIntegrity = null,
        Func<CancellationToken, Task<ApplicationAccess>>? loadAccess = null) =>
        new(
            loadDocuments ?? ((_, _, _) => Task.FromResult<IReadOnlyList<SourceDocumentRow>>([])),
            intake ?? ((_, _, _, _, _) => Task.FromException<DocumentIntakeOutcome>(new InvalidOperationException("Unexpected intake."))),
            verifyIntegrity ?? ((_, _, _) => Task.FromResult(false)),
            loadAccess ?? (_ => Task.FromResult(new ApplicationAccess("owner", "Owner", ApplicationRole.Owner, true))));
}
