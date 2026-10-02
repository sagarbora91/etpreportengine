using Etp.Reporting.Application.Imports;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// The import transaction committed, but a step after the commit failed (IF-014, IF-017): checking which
/// batch owns the file, or loading its row outcome. The attempt is recorded as committed when
/// <see cref="BatchId"/> is known to be ours, and as unknown otherwise, never as rolled back. The inner
/// exception says what failed. The state and batch are also carried the way <see cref="SqlTransactionGuard"/>
/// records them, so the classifier and diagnostics read every commit outcome the same way.
/// </summary>
public sealed class ImportCommittedException : Exception
{
    public ImportCommittedException(Guid? batchId, Exception innerException)
        : base("The import committed, but a step after the commit failed.", innerException)
    {
        BatchId = batchId;
        Data[SqlTransactionGuard.CommitStateKey] = batchId is null ? CommitState.Unknown : CommitState.Committed;
        if (batchId is { } id) Data[SqlTransactionGuard.ImportBatchIdKey] = id;
    }

    /// <summary>The committed batch; null when the file's batch could not be checked.</summary>
    public Guid? BatchId { get; }
}
