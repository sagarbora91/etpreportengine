namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record DocumentIntakeOutcome(SourceDocumentRow Document, DocumentExtractionResult? Extraction, bool Duplicate);

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
        if (existing is not null) return new(existing, null, true);

        var extension = Path.GetExtension(sourcePath);
        var sourceType = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "PDF" : "IMAGE";
        var document = await repository.RegisterDocumentAsync(Path.GetFileName(sourcePath), stored.ManagedPath, stored.Sha256, stored.Size,
            sourceType, documentType, storeCode, businessDate, "RECEIVED", "Document received and integrity checked.", cancellationToken).ConfigureAwait(false);

        DocumentExtractionResult? extraction = null;
        try
        {
            if (sourceType == "PDF")
            {
                extraction = await new NativePdfTextExtractor().ExtractAsync(stored.ManagedPath, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(extraction.Text) && !string.IsNullOrWhiteSpace(settings.OcrHelperPath))
                    extraction = await new PaddleOcrProcessExtractor(settings.OcrHelperPath, settings.OcrModelPath)
                        .ExtractAsync(stored.ManagedPath, cancellationToken).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(settings.OcrHelperPath))
                extraction = await new PaddleOcrProcessExtractor(settings.OcrHelperPath, settings.OcrModelPath)
                    .ExtractAsync(stored.ManagedPath, cancellationToken).ConfigureAwait(false);

            extraction ??= new("NONE", "none/1", string.Empty, null, ReviewStatus: "REVIEW_REQUIRED");
            await repository.RecordExtractionAsync(document.Id, extraction, cancellationToken).ConfigureAwait(false);
            document = await repository.LoadDocumentAsync(document.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            extraction = new("NONE", "failed/1", string.Empty, null, StructuredFieldsJson: null, ReviewStatus: "REVIEW_REQUIRED");
            await repository.RecordExtractionAsync(document.Id, extraction, cancellationToken).ConfigureAwait(false);
            document = await repository.LoadDocumentAsync(document.Id, cancellationToken).ConfigureAwait(false);
        }
        return new(document, extraction, false);
    }
}
