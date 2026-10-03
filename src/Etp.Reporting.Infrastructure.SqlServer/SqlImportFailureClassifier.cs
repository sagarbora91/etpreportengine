using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// The import classifier with SQL Server knowledge added. Etp.Reporting.Import has no SqlClient
/// reference, so a SQL timeout would otherwise be described as a generic processing failure.
/// </summary>
public sealed class SqlImportFailureClassifier : IImportFailureClassifier
{
    // SqlClient reports its own client-side timeout as error number -2.
    private const int TimeoutNumber = -2;
    private readonly SafeImportFailureClassifier inner = new();

    // Only a COMMIT that timed out and that the check found missing is retried automatically: the
    // database then holds nothing of the attempt, and a retry is a clean second try. Any other SQL
    // timeout (one during the apply on a busy disk, say) would re-run the whole heavy import, and a
    // COMMIT that could not be confirmed or that had committed is never retried: the retry's own
    // result would replace the one that says what the first attempt left behind.
    public bool IsTransient(Exception exception) => SqlTransactionGuard.CommitStateOf(exception) switch
    {
        CommitState.Unknown or CommitState.Committed => false,
        _ when SqlErrorNumber(exception) is { } number => number == TimeoutNumber && SqlTransactionGuard.FailedAtCommit(exception) &&
            SqlTransactionGuard.CommitStateOf(exception) == CommitState.RolledBack,
        _ => inner.IsTransient(exception)
    };

    // History keeps both whatever the SQL number (ImportDiagnosticCatalogue.SafeFailureMessage).
    private const string UnknownMessage = ImportDiagnosticCatalogue.CommitOutcomeUnknownMessage;
    private const string SavedMessage = ImportDiagnosticCatalogue.SavedNotReadBackMessage;

    public (string Code, string SafeMessage) Describe(Exception exception)
    {
        var failure = DescribeDetailed(exception, FailureStage.Apply);
        return (failure.Code, failure.SafeMessage);
    }

    /// <summary>
    /// Records on a failure that followed a successful import COMMIT that the import is saved, so it
    /// is described, staged and retried as such. Used where the import continues outside this assembly.
    /// </summary>
    public static void MarkCommitted(Exception exception, Guid? batchId)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SqlTransactionGuard.MarkCommitted(exception, batchId);
    }

    /// <summary>
    /// The failure as an attempt records it (spec 11.1, IF-017), and the one classification path for import
    /// failures. Database errors are coded by <see cref="SqlImportFailures"/>, the rest by
    /// <see cref="SafeImportFailureClassifier"/>. Then the commit outcome (IF-014, recorded deviation): a COMMIT
    /// whose check could not answer is COMMIT_OUTCOME_UNKNOWN; a COMMIT the check found missing keeps its code
    /// (a timeout is IMPORT_TIMEOUT, retried); a failure after the work committed, or after a commit whose
    /// owning batch could not be checked, keeps its code but says the data is saved. Either is at the COMMIT stage.
    /// </summary>
    public ImportFailure DescribeDetailed(Exception exception, FailureStage stage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var cause = exception is ImportCommittedException { InnerException: { } wrapped } ? wrapped : exception;
        var described = SqlImportFailures.DescribeDatabaseError(cause, stage) ?? inner.DescribeDetailed(cause, stage);
        if (cause is not ImportSourceException)
            described = SqlTransactionGuard.CommitStateOf(exception) switch
            {
                CommitState.Unknown when SqlTransactionGuard.FailedAtCommit(exception) =>
                    described with { Code = ImportCodes.CommitOutcomeUnknown, SafeMessage = UnknownMessage },
                CommitState.Unknown or CommitState.Committed => described with { SafeMessage = SavedMessage },
                _ => described
            };
        return described with
        {
            Stage = SqlTransactionGuard.StageOf(exception, described.Stage),
            SqlNumber = SqlErrorNumber(exception) ?? described.SqlNumber
        };
    }

    /// <summary>The number of the first SqlException in the exception or its inner exceptions, if any.</summary>
    public static int? SqlErrorNumber(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql) return sql.Number;
        return null;
    }
}
