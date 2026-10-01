namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record DocumentIntakeOutcome(SourceDocumentRow Document, bool Duplicate);

public sealed class ProductisationOperationsService(string connectionString)
{
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
