using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Import failures as an attempt records them (spec 11.1), with SQL Server errors added: Etp.Reporting.Import
/// has no SqlClient, so <see cref="SafeImportFailureClassifier"/> cannot see a SqlException. The one
/// classification path is <see cref="SqlImportFailureClassifier.DescribeDetailed"/>, which uses these rules.
/// <list type="bullet">
/// <item>Our own THROWs (50000-59999) keep their text: the database code wrote it.</item>
/// <item>Any other error keeps only its number, procedure and line, never SQL Server's message, which can
/// quote a value.</item>
/// <item>SqlClient's own timeout (-2) is <see cref="ImportCodes.ImportTimeout"/>.</item>
/// </list>
/// </summary>
public static class SqlImportFailures
{
    /// <summary>The number SqlClient gives its own client-side timeout.</summary>
    public const int ClientTimeout = -2;

    private const int MessageLength = 1000;

    public static ImportFailure Describe(Exception exception, FailureStage stage) =>
        new SqlImportFailureClassifier().DescribeDetailed(exception, stage);

    /// <summary>The failure for the first SqlException in the exception or its inner exceptions; null when there is none.</summary>
    public static ImportFailure? DescribeDatabaseError(Exception exception, FailureStage stage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        // An importer refusal or conflict explains itself, even when a database error lies underneath.
        if (exception is ImportSourceException or ImportConflictException || Find(exception) is not { } sql) return null;
        var error = sql.Errors.Count > 0 ? sql.Errors[0] : null;
        return DescribeSqlError(sql.Number, error?.Message ?? sql.Message, error?.Procedure ?? sql.Procedure,
            error?.LineNumber ?? sql.LineNumber, stage);
    }

    /// <summary>The failure for one SQL Server error, from its number, message, procedure and line.</summary>
    public static ImportFailure DescribeSqlError(int number, string? message, string? procedure, int line, FailureStage stage)
    {
        if (number == ClientTimeout)
            return new(ImportCodes.ImportTimeout, stage, "The import timed out and can be retried.", nameof(SqlException), number);
        var text = number is >= 50000 and <= 59999 && !string.IsNullOrWhiteSpace(message)
            ? message
            : string.IsNullOrWhiteSpace(procedure)
                ? $"Database error {number}, line {line}."
                : $"Database error {number} in {procedure}, line {line}.";
        return new(ImportCodes.Sql(number), stage, text.Length > MessageLength ? text[..MessageLength] : text,
            nameof(SqlException), number);
    }

    private static SqlException? Find(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql) return sql;
        return null;
    }
}
