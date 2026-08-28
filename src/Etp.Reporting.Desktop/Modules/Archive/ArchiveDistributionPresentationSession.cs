extern alias EtpApplication;

using System.IO;
using Etp.Reporting.Reporting;
using ArchivedReportComparisonSection = EtpApplication::Etp.Reporting.Application.Archive.ArchivedReportComparisonSection;
using ArchivedReportGenerationSummary = EtpApplication::Etp.Reporting.Application.Archive.ArchivedReportGenerationSummary;
using ReportArchiveQuery = EtpApplication::Etp.Reporting.Application.Archive.IReportArchiveQuery<Etp.Reporting.Reporting.ReportPackDocument>;
using ReportArchiveSearch = EtpApplication::Etp.Reporting.Application.Archive.ReportArchiveSearch;
using CreateReportPackage = EtpApplication::Etp.Reporting.Application.Distribution.CreateReportPackage<Etp.Reporting.Reporting.ReportPackDocument>;
using EmailAttachmentPolicy = EtpApplication::Etp.Reporting.Application.Distribution.EmailAttachmentPolicy;
using RecordDistributionAttempt = EtpApplication::Etp.Reporting.Application.Distribution.RecordDistributionAttempt;
using ReportDistributionService = EtpApplication::Etp.Reporting.Application.Distribution.IReportDistributionService<Etp.Reporting.Reporting.ReportPackDocument>;
using ReportPackageReceipt = EtpApplication::Etp.Reporting.Application.Distribution.ReportPackageReceipt;
using SharingContact = EtpApplication::Etp.Reporting.Application.Sharing.SharingContact;
using SharingContactDraft = EtpApplication::Etp.Reporting.Application.Sharing.SharingContactDraft;
using SharingContactsService = EtpApplication::Etp.Reporting.Application.Sharing.ISharingContactsService;

namespace Etp.Reporting.Desktop.Modules.Archive;

public sealed record ArchivedDocumentSection(string Name, string Status, int Rows, string Message);

public sealed record OpenedArchivedGeneration(
    ArchivedReportGenerationSummary Generation,
    ReportPackDocument Document,
    IReadOnlyList<ArchivedDocumentSection> Sections);

public sealed class ArchiveDistributionPresentationSession
{
    private readonly Func<string, ReportArchiveQuery> archiveFactory;
    private readonly Func<string, SharingContactsService> contactsFactory;
    private readonly Func<string, ReportDistributionService> distributionFactory;
    private readonly Func<string, bool> fileExists;
    private long? openGenerationId;
    private long? shareGenerationId;

    public ArchiveDistributionPresentationSession(
        Func<string, ReportArchiveQuery> archiveFactory,
        Func<string, SharingContactsService> contactsFactory,
        Func<string, ReportDistributionService> distributionFactory,
        Func<string, bool>? fileExists = null)
    {
        this.archiveFactory = archiveFactory ?? throw new ArgumentNullException(nameof(archiveFactory));
        this.contactsFactory = contactsFactory ?? throw new ArgumentNullException(nameof(contactsFactory));
        this.distributionFactory = distributionFactory ?? throw new ArgumentNullException(nameof(distributionFactory));
        this.fileExists = fileExists ?? File.Exists;
    }

    public ReportPackDocument? CurrentDocument { get; private set; }
    public string? CurrentShareFile { get; private set; }

    public async Task<IReadOnlyList<ArchivedReportGenerationSummary>> SearchAsync(
        string connectionString,
        ReportArchiveSearch search,
        CancellationToken cancellationToken = default)
    {
        ClearOpenedGeneration();
        var rows = await archiveFactory(connectionString).SearchAsync(search, cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public async Task<OpenedArchivedGeneration> OpenAsync(
        string connectionString,
        ArchivedReportGenerationSummary generation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ClearOpenedGeneration();
        var document = await archiveFactory(connectionString).OpenAsync(generation.Id, cancellationToken).ConfigureAwait(false);
        CurrentDocument = document;
        openGenerationId = generation.Id;
        ClearShareFile();
        var sections = document.Tables
            .Select(table => new ArchivedDocumentSection(table.Name, table.Status, table.Data.Rows.Count, table.Message))
            .ToArray();
        return new(generation, document, sections);
    }

    public async Task<IReadOnlyList<ArchivedReportComparisonSection>> CompareAsync(
        string connectionString,
        ArchivedReportGenerationSummary first,
        ArchivedReportGenerationSummary second,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Id == second.Id) throw new InvalidOperationException("Select exactly two report generations.");
        ClearOpenedGeneration();
        var rows = await archiveFactory(connectionString).CompareAsync(first.Id, second.Id, cancellationToken).ConfigureAwait(false);
        return rows;
    }

    public ReportPackDocument DocumentForExport(ArchivedReportGenerationSummary generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        if (CurrentDocument is null || openGenerationId != generation.Id)
            throw new InvalidOperationException("Open the selected generation before exporting it.");
        return CurrentDocument;
    }

    public async Task<ReportPackageReceipt> CreatePackageAsync(
        string connectionString,
        ArchivedReportGenerationSummary generation,
        string outputPath,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generation);
        if (CurrentDocument is null || openGenerationId != generation.Id)
            throw new InvalidOperationException("Open the selected generation before packaging it.");

        var receipt = await distributionFactory(connectionString).CreatePackageAsync(
            new CreateReportPackage(generation.Id, outputPath, CurrentDocument, generation.GenerationNumber,
                generation.StoreCode, generation.IsFinal, createdBy), cancellationToken).ConfigureAwait(false);
        CurrentShareFile = receipt.Path;
        shareGenerationId = generation.Id;
        return receipt;
    }

    public string ShareFileFor(ArchivedReportGenerationSummary generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        if (CurrentShareFile is null || shareGenerationId != generation.Id || !fileExists(CurrentShareFile))
            throw new InvalidOperationException("Export the selected generation as ZIP first.");
        return CurrentShareFile;
    }

    public async Task<EmailAttachmentPolicy> ValidateEmailAttachmentAsync(
        string connectionString,
        ArchivedReportGenerationSummary generation,
        CancellationToken cancellationToken = default)
    {
        var shareFile = ShareFileFor(generation);
        return await distributionFactory(connectionString).ValidateEmailAttachmentAsync(shareFile, cancellationToken).ConfigureAwait(false);
    }

    public Task RecordAttemptAsync(
        string connectionString,
        RecordDistributionAttempt command,
        CancellationToken cancellationToken = default) =>
        distributionFactory(connectionString).RecordAttemptAsync(command, cancellationToken);

    public Task<IReadOnlyList<SharingContact>> LoadContactsAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        contactsFactory(connectionString).LoadAsync(cancellationToken);

    public Task<int> SaveContactAsync(
        string connectionString,
        SharingContactDraft contact,
        string reason,
        CancellationToken cancellationToken = default) =>
        contactsFactory(connectionString).SaveAsync(contact, reason, cancellationToken);

    private void ClearOpenedGeneration()
    {
        CurrentDocument = null;
        openGenerationId = null;
        ClearShareFile();
    }

    private void ClearShareFile()
    {
        CurrentShareFile = null;
        shareGenerationId = null;
    }
}
