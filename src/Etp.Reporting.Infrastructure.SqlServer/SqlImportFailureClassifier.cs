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

    public bool IsTransient(Exception exception) => SqlErrorNumber(exception) == TimeoutNumber || inner.IsTransient(exception);

    public (string Code, string SafeMessage) Describe(Exception exception) =>
        exception is not ImportSourceException && SqlErrorNumber(exception) == TimeoutNumber
            ? ("IMPORT_TIMEOUT", "The import timed out and can be retried.")
            : inner.Describe(exception);

    /// <summary>The number of the first SqlException in the exception or its inner exceptions, if any.</summary>
    public static int? SqlErrorNumber(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql) return sql.Number;
        return null;
    }
}
