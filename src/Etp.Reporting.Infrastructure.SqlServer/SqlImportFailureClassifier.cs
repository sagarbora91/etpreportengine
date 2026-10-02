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

    // IF-014 (spec 11.1, recorded deviation): a COMMIT timeout is checked on a fresh connection
    // before it is coded. Found committed, the import succeeded; found missing, it is IMPORT_TIMEOUT
    // with CommitState.RolledBack and can be retried; COMMIT_OUTCOME_UNKNOWN is only for a check that
    // could not answer. A failure after the work committed keeps its code but says the data is saved.
    public (string Code, string SafeMessage) Describe(Exception exception) => exception switch
    {
        ImportSourceException => inner.Describe(exception),
        _ when CommitOutcomeUnknown(exception) => (ImportCodes.CommitOutcomeUnknown,
            "The database did not confirm whether this import was saved. Import the file again: if it was saved, it is reported as already imported."),
        _ when SqlTransactionGuard.CommitStateOf(exception) == CommitState.Committed => (DescribeUncommitted(exception).Code,
            "The import was saved, but its result could not be read back. Import the file again to see it: it is reported as already imported."),
        _ => DescribeUncommitted(exception)
    };

    /// <summary>
    /// Records on a failure that followed a successful import COMMIT that the import is saved, so it
    /// is described, staged and retried as such. Used where the import continues outside this assembly.
    /// </summary>
    public static void MarkCommitted(Exception exception, Guid? batchId)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SqlTransactionGuard.MarkCommitted(exception, batchId);
    }

    private (string Code, string SafeMessage) DescribeUncommitted(Exception exception) =>
        SqlErrorNumber(exception) == TimeoutNumber
            ? (ImportCodes.ImportTimeout, "The import timed out and can be retried.")
            : inner.Describe(exception);

    /// <summary>
    /// The failure as an attempt records it, with the SQL error number, and the COMMIT stage when
    /// the COMMIT itself failed or the work had committed before the failure (IF-014).
    /// </summary>
    public ImportFailure DescribeDetailed(Exception exception, FailureStage stage)
    {
        var (code, message) = Describe(exception);
        var described = ((IImportFailureClassifier)inner).DescribeDetailed(exception, stage);
        return described with
        {
            Code = code,
            SafeMessage = message,
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

    private static bool CommitOutcomeUnknown(Exception exception) =>
        SqlTransactionGuard.CommitStateOf(exception) == CommitState.Unknown;
}
