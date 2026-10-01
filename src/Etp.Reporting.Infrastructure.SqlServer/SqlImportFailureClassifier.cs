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

    // An import whose COMMIT could not be confirmed is not retried automatically: the retry's own
    // failure would replace the one that says the outcome is unknown.
    public bool IsTransient(Exception exception) => !CommitOutcomeUnknown(exception) &&
        (SqlErrorNumber(exception) == TimeoutNumber || inner.IsTransient(exception));

    public (string Code, string SafeMessage) Describe(Exception exception) => exception switch
    {
        ImportSourceException => inner.Describe(exception),
        _ when CommitOutcomeUnknown(exception) => (ImportCodes.CommitOutcomeUnknown,
            "The database did not confirm whether this import was saved. Import the file again: if it was saved, it is reported as already imported."),
        _ when SqlErrorNumber(exception) == TimeoutNumber => (ImportCodes.ImportTimeout, "The import timed out and can be retried."),
        _ => inner.Describe(exception)
    };

    /// <summary>
    /// The failure as an attempt records it, with the SQL error number, and the COMMIT stage when
    /// the COMMIT itself failed (IF-014).
    /// </summary>
    public ImportFailure DescribeDetailed(Exception exception, FailureStage stage)
    {
        var (code, message) = Describe(exception);
        var described = ((IImportFailureClassifier)inner).DescribeDetailed(exception, stage);
        return described with
        {
            Code = code,
            SafeMessage = message,
            Stage = SqlTransactionGuard.FailedAtCommit(exception) ? FailureStage.Commit : described.Stage,
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
