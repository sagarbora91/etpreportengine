using System.Text.Json;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using static Etp.Reporting.SqlServer.IntegrationTests.ImportDiagnosticsSqlTests;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// Diagnostics never store or show a customer value (spec 11.1): issue messages come from the catalogue,
/// columns from the approved headers, and History cleans what older or foreign writers left behind.
/// </summary>
public sealed class ImportDiagnosticsPrivacyTests
{
    private static readonly string[] Values = [CustomerName, CustomerPhone, LoyaltyNumber];

    [Fact]
    public async Task Recorder_keeps_codes_locations_and_catalogue_messages_only()
    {
        await WithDatabase(async database =>
        {
            var history = new SqlServerImportHistoryQuery(database.ConnectionString);
            await history.RecordAttemptAsync(new("poisoned.xlsx", "R025", "HEMW", new(2026, 8, 25), new(2026, 8, 25), "Failed",
                Diagnostics:
                [
                    new(ImportIssueSeverity.Blocker, "VALUE_INVALID", $"Value {CustomerName} cannot be converted.", 4, "QTY"),
                    new(ImportIssueSeverity.Warning, "SOMETHING_NEW", $"Customer {CustomerPhone}", 5, $"{LoyaltyNumber} column"),
                    new(ImportIssueSeverity.Warning, "UNKNOWN_SALES_TRANSACTION_TYPE", "Unrecognised transaction type; this row was skipped.", 6, "TRANS_TYPE")
                ])
            {
                Failure = new("VALUE_INVALID", FailureStage.Match, "A value cannot be converted to the type its column requires.")
            });

            var issues = (string)(await database.ExecuteAsync("""
                SELECT STRING_AGG(CONCAT(seq,'|',code,'|',message,'|',column_name,'|',source_row_number),'#') WITHIN GROUP(ORDER BY seq)
                FROM dbo.import_attempt_issues
                """))!;
            Assert.Equal(string.Join('#',
                $"1|VALUE_INVALID|A value cannot be converted to the type its column requires.|QTY|4",
                $"2|SOMETHING_NEW|{ImportDiagnosticCatalogue.GenericMessage}||5",
                "3|UNKNOWN_SALES_TRANSACTION_TYPE|Unrecognised transaction type; this row was skipped.|TRANS_TYPE|6"), issues);
            var attempt = (string)(await database.ExecuteAsync(
                "SELECT CONCAT(failure_code,'|',failure_stage,'|',failure_message,'|',summary_json,'|',CONVERT(nvarchar(max),diagnostics_json)) FROM dbo.import_attempts"))!;
            Assert.All(Values, value => Assert.DoesNotContain(value, attempt));
            Assert.Contains("VALUE_INVALID|MATCH|", attempt);
        });
    }

    [Fact]
    public async Task History_cleans_issue_rows_written_outside_the_recorder()
    {
        await WithDatabase(async database =>
        {
            var history = new SqlServerImportHistoryQuery(database.ConnectionString);
            await history.RecordAttemptAsync(new("foreign.xlsx", "R025", "HEMW", new(2026, 8, 25), new(2026, 8, 25), "Failed",
                Diagnostics: [new(ImportIssueSeverity.Blocker, "VALUE_REQUIRED", "A required value is missing.", 2, "QTY")]));
            await database.ExecuteAsync($"""
                UPDATE dbo.import_attempt_issues SET message=N'{CustomerName} {CustomerPhone}',column_name=N'{LoyaltyNumber}';
                UPDATE dbo.import_attempts SET summary_json=N'{"{}"}';
                """);
            var entry = Assert.Single(await history.LoadAsync(new(new(2026, 8, 25), new(2026, 8, 25))));
            var issue = Assert.Single(entry.Result.Diagnostics!);
            Assert.Equal("A required value is missing.", issue.Message);
            Assert.Null(issue.SourceColumn);
            Assert.Equal(2, issue.SourceRow);
            var shown = JsonSerializer.Serialize(entry);
            Assert.All(Values, value => Assert.DoesNotContain(value, shown));
        });
    }

    [Fact]
    public async Task Older_attempts_without_issue_rows_still_show_their_cleaned_diagnostics()
    {
        await WithDatabase(async database =>
        {
            await database.ExecuteAsync($"""
                EXEC dbo.record_import_attempt @name=N'older.xlsx',@report='R025',@store='HEMW',@start='20260825',@end='20260825',
                  @outcome='Failed',@rows=0,@new=0,@present=0,@conflicts=0,
                  @diagnostics=N'[{"{"}"Severity":2,"Code":"VALUE_INVALID","Message":"{CustomerName}","SourceRow":3,"SourceColumn":"{CustomerPhone}"{"}"}]';
                """);
            var entry = Assert.Single(await new SqlServerImportHistoryQuery(database.ConnectionString)
                .LoadAsync(new(new(2026, 8, 25), new(2026, 8, 25))));
            Assert.Null(entry.Result.Failure);
            var issue = Assert.Single(entry.Result.Diagnostics!);
            Assert.Equal(ImportDiagnosticCatalogue.Template("VALUE_INVALID"), issue.Message);
            Assert.Equal(3, issue.SourceRow);
            Assert.Null(issue.SourceColumn);
            Assert.All(Values, value => Assert.DoesNotContain(value, JsonSerializer.Serialize(entry)));
        });
    }

    [Fact]
    public async Task Failure_messages_keep_only_code_written_text_when_recorded_and_when_shown()
    {
        await WithDatabase(async database =>
        {
            var history = new SqlServerImportHistoryQuery(database.ConnectionString);
            await history.RecordAttemptAsync(new("layout.xlsx", "R025", "HEMW", new(2026, 8, 25), new(2026, 8, 25), "Failed")
                { Failure = new("IMPORT_LAYOUT_BLOCKED", FailureStage.Match, $"Workbook validation was blocked: {CustomerName}.") });
            await history.RecordAttemptAsync(new("database.xlsx", "R025", "HEMW", new(2026, 8, 25), new(2026, 8, 25), "Failed")
                { Failure = new("SQL_2627", FailureStage.Apply, $"Violation of key. The duplicate key value is ({CustomerPhone}).", "SqlException", 2627) });
            var stored = (string)(await database.ExecuteAsync(
                "SELECT STRING_AGG(failure_message,'#') WITHIN GROUP(ORDER BY file_name) FROM dbo.import_attempts"))!;
            Assert.Equal($"The import failed with a database error.#{ImportDiagnosticCatalogue.Template("IMPORT_LAYOUT_BLOCKED")}", stored);

            // A row written by something other than the recorder is cleaned when History shows it.
            await database.ExecuteAsync($"""
                UPDATE dbo.import_attempts SET failure_message=N'{CustomerName} {LoyaltyNumber}';
                """);
            var entries = await history.LoadAsync(new(new(2026, 8, 25), new(2026, 8, 25)));
            Assert.Equal(2, entries.Count);
            Assert.Contains(entries, entry => entry.Result.Failure!.SafeMessage == ImportDiagnosticCatalogue.Template("IMPORT_LAYOUT_BLOCKED"));
            Assert.Contains(entries, entry => entry.Result.Failure!.SafeMessage == "The import failed with a database error.");
            Assert.All(Values, value => Assert.DoesNotContain(value, JsonSerializer.Serialize(entries)));
        });
    }
}
