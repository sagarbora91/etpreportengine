using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>How each failure is recorded on its attempt (spec 11.1, IF-017).</summary>
public sealed class SafeImportFailureClassifierTests
{
    private const string Secret = "Synthetic Customer Secret 9876500999";

    [Fact]
    public void Importer_refusal_keeps_its_code_and_message_and_the_callers_stage()
    {
        var failure = new SafeImportFailureClassifier().DescribeDetailed(
            new ImportSourceException("SCOPE_NOT_DETECTED", "Store or date could not be detected."), FailureStage.Scope);
        Assert.Equal("SCOPE_NOT_DETECTED", failure.Code);
        Assert.Equal(FailureStage.Scope, failure.Stage);
        Assert.Equal("Store or date could not be detected.", failure.SafeMessage);
        Assert.Equal(nameof(ImportSourceException), failure.ExceptionType);
        Assert.Null(failure.SqlNumber);
    }

    [Fact]
    public void Importer_refusal_that_names_its_stage_overrides_the_callers()
    {
        var failure = new SafeImportFailureClassifier().DescribeDetailed(
            new ImportSourceException("IMPORT_PERIOD_ALREADY_PRESENT", "Already imported.") { Stage = FailureStage.Plan }, FailureStage.Apply);
        Assert.Equal(FailureStage.Plan, failure.Stage);
    }

    [Fact]
    public void Conflict_keeps_count_and_at_most_twenty_samples_at_the_apply_stage()
    {
        var samples = Enumerable.Range(1, 25).Select(row => new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.ImportConflict,
            "Invoice date differs. Review and request a controlled restatement.", row, DocumentRef: $"R025 HEMW|2027|1000000{row:D2}")).ToArray();
        var exception = new ImportConflictException(25, samples);
        var failure = new SafeImportFailureClassifier().DescribeDetailed(exception, FailureStage.Scope);
        Assert.Equal(ImportCodes.ImportConflict, failure.Code);
        Assert.Equal(FailureStage.Apply, failure.Stage);
        Assert.StartsWith("25 conflicting rows.", failure.SafeMessage);
        Assert.Equal(ImportConflictException.MaximumSamples, failure.Issues.Count);
        Assert.Equal(samples.Take(20), failure.Issues);
        Assert.Equal(25, exception.Count);
    }

    [Theory]
    [MemberData(nameof(FixedMessages))]
    public void Other_exceptions_get_a_fixed_message_never_their_own_text(Exception exception, string code)
    {
        var failure = new SafeImportFailureClassifier().DescribeDetailed(exception, FailureStage.Read);
        Assert.Equal(code, failure.Code);
        Assert.Equal(FailureStage.Read, failure.Stage);
        Assert.Equal(exception.GetType().Name, failure.ExceptionType);
        Assert.DoesNotContain("Secret", failure.SafeMessage);
        Assert.Empty(failure.Issues);
    }

    public static TheoryData<Exception, string> FixedMessages() => new()
    {
        { new UnauthorizedAccessException(Secret), "IMPORT_ACCESS_DENIED" },
        { new IOException(Secret), "IMPORT_IO_FAILURE" },
        { new FileNotFoundException(Secret), "IMPORT_IO_FAILURE" },
        { new TimeoutException(Secret), ImportCodes.ImportTimeout },
        { new InvalidOperationException(Secret), "IMPORT_PROCESSING_FAILED" },
        { new FormatException(Secret), "IMPORT_PROCESSING_FAILED" },
        { new InvalidDataException(Secret), "IMPORT_PROCESSING_FAILED" }
    };

    [Fact]
    public void Client_timeout_minus_two_is_IMPORT_TIMEOUT()
    {
        var failure = SqlImportFailures.DescribeSqlError(-2, "Execution Timeout Expired. " + Secret, null, 0, FailureStage.Apply);
        Assert.Equal(ImportCodes.ImportTimeout, failure.Code);
        Assert.Equal(-2, failure.SqlNumber);
        Assert.Equal("The import timed out and can be retried.", failure.SafeMessage);
        Assert.Equal(FailureStage.Apply, failure.Stage);
    }

    [Theory]
    [InlineData(50000)]
    [InlineData(51021)]
    [InlineData(51750)]
    [InlineData(59999)]
    public void Our_own_errors_keep_their_message(int number)
    {
        const string message = "This business day is finalised. Reopen it before importing.";
        var failure = SqlImportFailures.DescribeSqlError(number, message, "dbo.persist_sales_line", 12, FailureStage.Apply);
        Assert.Equal(ImportCodes.Sql(number), failure.Code);
        Assert.Equal(message, failure.SafeMessage);
        Assert.Equal(number, failure.SqlNumber);
        Assert.Equal("SqlException", failure.ExceptionType);
    }

    [Theory]
    [InlineData(2627, "dbo.persist_sales_line", "Database error 2627 in dbo.persist_sales_line, line 7.")]
    [InlineData(8134, null, "Database error 8134, line 7.")]
    [InlineData(49999, "", "Database error 49999, line 7.")]
    [InlineData(60000, "p", "Database error 60000 in p, line 7.")]
    public void Other_errors_keep_only_number_procedure_and_line(int number, string? procedure, string expected)
    {
        var failure = SqlImportFailures.DescribeSqlError(number, $"Violation, duplicate key value is ({Secret}).", procedure, 7, FailureStage.Apply);
        Assert.Equal(ImportCodes.Sql(number), failure.Code);
        Assert.Equal(expected, failure.SafeMessage);
    }

    [Fact]
    public void Long_messages_fit_the_failure_message_column()
    {
        var failure = SqlImportFailures.DescribeSqlError(51000, new string('x', 1500), null, 1, FailureStage.Apply);
        Assert.Equal(1000, failure.SafeMessage.Length);
    }

    [Fact]
    public void Without_a_database_error_the_import_classifier_describes_the_failure()
    {
        Assert.Null(SqlImportFailures.DescribeDatabaseError(new IOException(Secret), FailureStage.Read));
        var failure = SqlImportFailures.Describe(new IOException(Secret), FailureStage.Read);
        Assert.Equal("IMPORT_IO_FAILURE", failure.Code);
        var refusal = SqlImportFailures.Describe(new ImportSourceException("DATE_OVERRIDE_MISMATCH", "The date override does not match the file."), FailureStage.Scope);
        Assert.Equal("DATE_OVERRIDE_MISMATCH", refusal.Code);
        Assert.Equal(FailureStage.Scope, refusal.Stage);
    }
}
