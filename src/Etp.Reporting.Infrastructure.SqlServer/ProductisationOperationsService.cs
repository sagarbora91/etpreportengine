namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record DocumentIntakeOutcome(SourceDocumentRow Document, bool Duplicate);

public sealed class ProductisationOperationsService(string connectionString)
{
    public async Task<SourceDocumentRow> IntakeEtpEvidenceAsync(string workbookPath,string sourceSha256,string reportCode,string? storeCode,DateOnly? businessDate,CancellationToken cancellationToken=default)
    {
        var repository=new ProductisationRepository(connectionString);var settings=await repository.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var stored=await ManagedDocumentRepository.StoreAsync(workbookPath,settings.DocumentRepositoryPath,cancellationToken).ConfigureAwait(false);
        if(!string.Equals(stored.Sha256,sourceSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("The ETP evidence hash changed while it was being retained.");
        var document=await repository.RegisterDocumentAsync(Path.GetFileName(workbookPath),stored.ManagedPath,stored.Sha256,stored.Size,"ETP_WORKBOOK",reportCode,storeCode,businessDate,"VALIDATED","ETP workbook retained as immutable source evidence.",cancellationToken).ConfigureAwait(false);
        await repository.LinkDocumentToImportAsync(document.Id,stored.Sha256,reportCode,storeCode,businessDate,cancellationToken).ConfigureAwait(false);
        return await repository.LoadDocumentAsync(document.Id,cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentIntakeOutcome> IntakeDocumentAsync(
        string sourcePath,
        string? storeCode,
        DateOnly? businessDate,
        string? documentType,
        CancellationToken cancellationToken = default)
    {
        var repository = new ProductisationRepository(connectionString);
        var settings = await repository.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var stored = await ManagedDocumentRepository.StoreAsync(sourcePath, settings.DocumentRepositoryPath, cancellationToken).ConfigureAwait(false);
        var existing = await repository.FindDocumentByHashAsync(stored.Sha256, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return new(existing, true);

        var extension = Path.GetExtension(sourcePath);
        var sourceType = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "PDF" : "IMAGE";
        var document = await repository.RegisterDocumentAsync(Path.GetFileName(sourcePath), stored.ManagedPath, stored.Sha256, stored.Size,
            sourceType, documentType, storeCode, businessDate, "RECEIVED", "Scanned document attached to the business day; integrity checked.", cancellationToken).ConfigureAwait(false);

        return new(document, false);
    }
}
