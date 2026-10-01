namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// The import transaction committed, but a step after the commit failed (IF-017): checking which batch
/// owns the file, or loading its row outcome. The attempt is recorded as committed when
/// <see cref="BatchId"/> is known, and as unknown otherwise, never as rolled back. The inner exception
/// says what failed.
/// </summary>
public sealed class ImportCommittedException(Guid? batchId, Exception innerException)
    : Exception("The import committed, but a step after the commit failed.", innerException)
{
    /// <summary>The committed batch; null when the file's batch could not be checked.</summary>
    public Guid? BatchId { get; } = batchId;
}
