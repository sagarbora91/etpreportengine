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
                "Extraction completed; human verification is required.")
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
                @"STORE\Manager", received, "Extraction completed; human verification is required."),
            document);
        Assert.Equal("REVIEW_REQUIRED", observedStatus);
        Assert.Equal(75, observedLimit);
        Assert.Equal(cancellation.Token, observedToken);
    }

    [Fact]
    public async Task Extraction_load_maps_human_review_history_without_losing_fields()
    {
        var reviewed = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
        var created = reviewed.AddMinutes(-10);
        IReadOnlyList<DocumentExtractionRow> rows =
        [
            new(23, 18, "PADDLE_OCR", "paddle/3", "Invoice text", .94m, "VERIFIED", @"STORE\Manager",
                reviewed, "Checked against original", created)
        ];
        long observedDocument = 0;
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            loadExtractions: (documentId, token) =>
            {
                observedDocument = documentId;
                observedToken = token;
                return Task.FromResult(rows);
            });

        var extraction = Assert.Single(await service.LoadExtractionsAsync(18, cancellation.Token));

        Assert.Equal(
            new SourceDocumentExtraction(23, 18, "PADDLE_OCR", "paddle/3", "Invoice text", .94m, "VERIFIED",
                @"STORE\Manager", reviewed, "Checked against original", created),
            extraction);
        Assert.Equal(18, observedDocument);
        Assert.Equal(cancellation.Token, observedToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Extraction_review_preserves_verified_or_quarantine_decision_reason_and_token(bool verified)
    {
        long observedId = 0;
        bool? observedDecision = null;
        string? observedReason = null;
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            reviewExtraction: (id, decision, reason, token) =>
            {
                observedId = id;
                observedDecision = decision;
                observedReason = reason;
                observedToken = token;
                return Task.CompletedTask;
            });

        await service.ReviewExtractionAsync(23, verified, "Compared with retained original", cancellation.Token);

        Assert.Equal(23, observedId);
        Assert.Equal(verified, observedDecision);
        Assert.Equal("Compared with retained original", observedReason);
        Assert.Equal(cancellation.Token, observedToken);
    }

    [Fact]
    public async Task Intake_maps_request_document_extraction_and_duplicate_semantics()
    {
        var date = new DateOnly(2026, 8, 27);
        var received = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc);
        var row = new SourceDocumentRow(31, "receipt.pdf", @"C:\managed\hash.pdf", new string('b', 64), 4096,
            "PDF", "SERVICE_RECEIPT", "WLMHW", date, "REVIEW_REQUIRED", null, null, null,
            @"STORE\Manager", received, "Extraction completed; human verification is required.");
        var result = new DocumentExtractionResult(
            "NATIVE_PDF", "native-pdf-lite/1", "Invoice receipt text", 1m, 1, "[]", "{}", "REVIEW_REQUIRED");
        SourceDocumentIntakeRequest? observed = null;
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            intake: (path, store, businessDate, type, token) =>
            {
                observed = new(path, store, businessDate, type);
                observedToken = token;
                return Task.FromResult(new DocumentIntakeOutcome(row, result, false));
            });
        var request = new SourceDocumentIntakeRequest(@"C:\incoming\receipt.pdf", "WLMHW", date, "SERVICE_RECEIPT");

        var outcome = await service.IntakeAsync(request, cancellation.Token);

        Assert.Equal(request, observed);
        Assert.Equal(cancellation.Token, observedToken);
        Assert.False(outcome.Duplicate);
        Assert.Equal(SqlServerSourceInboxService.Map(row), outcome.Document);
        Assert.Equal(SqlServerSourceInboxService.Map(result), outcome.Extraction);
    }

    [Fact]
    public async Task Duplicate_intake_returns_existing_immutable_document_without_new_extraction()
    {
        var row = new SourceDocumentRow(31, "renamed.pdf", @"C:\managed\hash.pdf", new string('c', 64), 4096,
            "PDF", null, null, null, "RECEIVED", null, null, null, @"STORE\Manager", DateTime.UtcNow, null);
        var service = CreateService(
            intake: (_, _, _, _, _) => Task.FromResult(new DocumentIntakeOutcome(row, null, true)));

        var outcome = await service.IntakeAsync(new(@"C:\incoming\renamed.pdf", null, null, null));

        Assert.True(outcome.Duplicate);
        Assert.Equal(row.Id, outcome.Document.Id);
        Assert.Equal(row.Sha256, outcome.Document.Sha256);
        Assert.Null(outcome.Extraction);
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
        Func<long, CancellationToken, Task<IReadOnlyList<DocumentExtractionRow>>>? loadExtractions = null,
        Func<long, bool, string, CancellationToken, Task>? reviewExtraction = null,
        Func<string, string?, DateOnly?, string?, CancellationToken, Task<DocumentIntakeOutcome>>? intake = null,
        Func<string, string, CancellationToken, Task<bool>>? verifyIntegrity = null) =>
        new(
            loadDocuments ?? ((_, _, _) => Task.FromResult<IReadOnlyList<SourceDocumentRow>>([])),
            loadExtractions ?? ((_, _) => Task.FromResult<IReadOnlyList<DocumentExtractionRow>>([])),
            reviewExtraction ?? ((_, _, _, _) => Task.CompletedTask),
            intake ?? ((_, _, _, _, _) => Task.FromException<DocumentIntakeOutcome>(new InvalidOperationException("Unexpected intake."))),
            verifyIntegrity ?? ((_, _, _) => Task.FromResult(false)));
}
