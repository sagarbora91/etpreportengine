using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Keeps the first failure of a SQL transaction visible. A rollback or a lost COMMIT reply
/// must never replace the exception that explains what actually went wrong.
/// </summary>
internal static class SqlTransactionGuard
{
    /// <summary>Exception.Data key holding a rollback failure that followed the original failure.</summary>
    internal const string RollbackFailureKey = "EtpRollbackFailure";

    /// <summary>Exception.Data key holding a failure of the post-commit check itself.</summary>
    internal const string CommitCheckFailureKey = "EtpCommitCheckFailure";

    /// <summary>Exception.Data key holding the import batch whose transaction failed.</summary>
    internal const string ImportBatchIdKey = "EtpImportBatchId";

    /// <summary>Rolls back after <paramref name="failure"/> when the transaction is still open; the caller then rethrows the failure.</summary>
    internal static Task RollBackAsync(Exception failure, SqlTransaction transaction) =>
        RollBackAsync(failure, transaction.Connection is not null, () => transaction.RollbackAsync(CancellationToken.None));

    internal static async Task RollBackAsync(Exception failure, bool transactionOpen, Func<Task> rollback)
    {
        // A transaction that completed or lost its connection has nothing left to roll back,
        // and SqlClient would throw "This SqlTransaction has completed" in its place.
        if (!transactionOpen) return;
        try { await rollback().ConfigureAwait(false); }
        catch (Exception rollbackFailure)
        {
            failure.Data[RollbackFailureKey] = $"{rollbackFailure.GetType().FullName}: {rollbackFailure.Message}";
        }
    }

    /// <summary>
    /// Commits, and if the commit reports a failure, releases the connection and asks the
    /// database whether the work landed anyway. SQL Server can finish a COMMIT after the client
    /// stopped waiting for it, and a timed-out COMMIT cannot be issued again on the same
    /// transaction. When the work is present the result is returned; otherwise the original
    /// commit exception is rethrown unchanged.
    /// </summary>
    internal static async Task<T> CommitOrVerifyAsync<T>(T result, Func<Task> commit, Func<Task> release,
        Func<Task<bool>> committed)
    {
        try
        {
            await commit().ConfigureAwait(false);
            return result;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Closing the connection rolls back a transaction the server never committed, so
            // the check below cannot wait on this session's own locks.
            try { await release().ConfigureAwait(false); }
            catch (Exception releaseFailure) { failure.Data[RollbackFailureKey] = $"{releaseFailure.GetType().FullName}: {releaseFailure.Message}"; }
            bool landed;
            try { landed = await committed().ConfigureAwait(false); }
            catch (Exception checkFailure)
            {
                landed = false;
                failure.Data[CommitCheckFailureKey] = $"{checkFailure.GetType().FullName}: {checkFailure.Message}";
            }
            if (landed) return result;
            throw;
        }
    }
}
