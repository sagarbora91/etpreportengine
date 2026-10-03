using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;

namespace Etp.Reporting.Infrastructure.SqlServer;

// IF-023 (spec 11.2): a new import keeps its bytes inside its own transaction; a Duplicate keeps them
// in a small transaction of its own when its earlier import could not.
public sealed partial class SqlServerImportPersistenceUseCase : IImportEvidenceRetainer
{
    public async Task<EvidenceState> RetainImportedSourceAsync(string sourceSha256, ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        return await ImportSourceEvidence.RetainImportedSourceAsync(connectionString, sourceSha256, content, cancellationToken)
            .ConfigureAwait(false);
    }

    // The duplicate's rows are stored either way, so a failure here is reported, not thrown: the attempt
    // records NOT_RETAINED and the folder import adds EVIDENCE_NOT_RETAINED.
    private async Task<EvidenceState> DuplicateEvidenceAsync(MatchedImportEnvelope accepted, CancellationToken token)
    {
        try
        {
            return await ImportSourceEvidence.RetainImportedSourceAsync(connectionString, accepted.Workbook.Sha256,
                accepted.Workbook.EvidenceBytes, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return EvidenceState.NotRetained; }
    }

    // Read after the commit. The import itself has succeeded, so a failed read records the state as unknown.
    private async Task<EvidenceState> ImportEvidenceAsync(MatchedImportEnvelope accepted, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken token)
    {
        try
        {
            return await ImportSourceEvidence.StateAfterImportAsync(connectionString, accepted.Workbook.Sha256,
                accepted.ProfileIdentity.ReportCode, storeCode, periodStart, periodEnd,
                !accepted.Workbook.EvidenceBytes.IsEmpty, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return EvidenceState.Unknown; }
    }
}
