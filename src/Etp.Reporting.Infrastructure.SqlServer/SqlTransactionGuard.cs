using Etp.Reporting.Application.Imports;
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

    /// <summary>Exception.Data key holding the <see cref="CommitState"/> of the transaction the exception ended.</summary>
    internal const string CommitStateKey = "EtpCommitState";

    /// <summary>Exception.Data key set when the COMMIT itself failed, rather than the work before it.</summary>
    internal const string CommitFailedKey = "EtpCommitFailed";

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
    /// Records on a failure that ended a guarded transaction before any COMMIT was sent that
    /// nothing was committed. A failed COMMIT has already been given its state by
    /// <see cref="CommitOrVerifyAsync{T}"/>, which this keeps.
    /// </summary>
    internal static void MarkRolledBack(Exception failure)
    {
        if (!failure.Data.Contains(CommitStateKey)) failure.Data[CommitStateKey] = CommitState.RolledBack;
    }

    internal static Task CommitOrVerifyAsync(Func<Task> commit, Func<Task> release, Func<Task<bool>> committed) =>
        CommitOrVerifyAsync(true, commit, release, committed);

    /// <summary>
    /// Commits, and if the commit reports a failure, releases the connection and asks the
    /// database whether the work landed anyway. SQL Server can finish a COMMIT after the client
    /// stopped waiting for it, and a timed-out COMMIT cannot be issued again on the same
    /// transaction. When the work is present the result is returned. Otherwise the original
    /// commit exception is rethrown unchanged, marked <see cref="CommitState.RolledBack"/> when
    /// the check found nothing, or <see cref="CommitState.Unknown"/> when the check itself failed.
    /// </summary>
    internal static async Task<T> CommitOrVerifyAsync<T>(T result, Func<Task> commit, Func<Task> release,
        Func<Task<bool>> committed)
    {
        try
        {
            await commit().ConfigureAwait(false);
            return result;
        }
        // DbTransaction.CommitAsync, which SqlClient does not override, observes the token only
        // before it starts the synchronous COMMIT, so a cancellation means no COMMIT was sent.
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            failure.Data[CommitFailedKey] = true;
            // Closing the connection rolls back a transaction the server never committed, so
            // the check below cannot wait on this session's own locks.
            try { await release().ConfigureAwait(false); }
            catch (Exception releaseFailure) { failure.Data[RollbackFailureKey] = $"{releaseFailure.GetType().FullName}: {releaseFailure.Message}"; }
            bool? landed;
            try { landed = await committed().ConfigureAwait(false); }
            catch (Exception checkFailure)
            {
                landed = null;
                failure.Data[CommitCheckFailureKey] = $"{checkFailure.GetType().FullName}: {checkFailure.Message}";
            }
            if (landed is true) return result;
            failure.Data[CommitStateKey] = landed is false ? CommitState.RolledBack : CommitState.Unknown;
            throw;
        }
    }

    /// <summary>
    /// Closes the connection of a transaction whose COMMIT failed. Closing rolls back whatever
    /// the server did not commit. The session is then discarded rather than pooled: after a lost
    /// reply it cannot be trusted, and shared memory cannot detect a dead pooled session.
    /// </summary>
    internal static Task ReleaseAsync(SqlConnection connection)
    {
        SqlConnection.ClearPool(connection);
        return connection.CloseAsync();
    }

    /// <summary>
    /// Asks a yes/no question on a new, unpooled session after a failed COMMIT. The question must
    /// lock-read (<c>WITH(READCOMMITTEDLOCK)</c>) the rows the transaction wrote, so a COMMIT still
    /// finishing on the server is waited for rather than read as missing.
    /// </summary>
    internal static async Task<bool> CheckAsync(string connectionString, string sql, Action<SqlCommand> bind)
    {
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.ValidateForCommitCheck(connectionString));
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = LocalSqlConnectionPolicy.CommitBudgetSeconds };
        bind(command);
        return (bool)(await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false))!;
    }

    /// <summary>The commit state a guarded transaction recorded on the exception or an inner one.</summary>
    internal static CommitState? CommitStateOf(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current.Data[CommitStateKey] is CommitState state) return state;
        return null;
    }

    /// <summary>Whether the exception, or an inner one, is a failed COMMIT rather than a failure before it.</summary>
    internal static bool FailedAtCommit(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current.Data[CommitFailedKey] is true) return true;
        return false;
    }

    /// <summary>The import batch whose transaction the exception, or an inner one, ended.</summary>
    internal static Guid? BatchIdOf(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current.Data[ImportBatchIdKey] is Guid batchId) return batchId;
        return null;
    }
}
