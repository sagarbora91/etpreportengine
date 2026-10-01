using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ImportFailureDiagnosticsTests
{
    [Fact]
    public void Import_failure_is_logged_with_stage_family_sql_number_commit_state_and_batch_but_no_file_name_or_message()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpImportFailureLog_" + Guid.NewGuid().ToString("N"));
        var batchId = Guid.NewGuid();
        try
        {
            ImportFailureDiagnostics.Record(new("Customer Secret R003.xlsx", FailureStage.Commit, "R003", "HEMW", new(2026, 8, 25), batchId, -2,
                new InvalidOperationException("Customer Secret message")) { CommitState = CommitState.Unknown }, directory);

            var line = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "diagnostics-*.jsonl")));
            Assert.Contains("\"EventId\":\"IMPORT_COMMIT_FAILED.R003.SQL-2.UNKNOWN\"", line);
            Assert.Contains("\"Source\":\"Imports.Folder\"", line);
            Assert.Contains(batchId.ToString("N"), line);
            Assert.Contains("System.InvalidOperationException", line);
            Assert.DoesNotContain("Secret", line);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Read_failure_without_family_or_sql_error_keeps_a_plain_event_id() =>
        Assert.Equal("IMPORT_READ_FAILED", ImportFailureDiagnostics.EventId(
            new("book.xlsx", FailureStage.Read, null, null, null, null, null, new IOException())));

    [Fact]
    public void Failure_before_the_commit_names_the_apply_stage_and_its_rollback() =>
        Assert.Equal("IMPORT_APPLY_FAILED.STOCK_LEDGER.SQL547.ROLLED_BACK", ImportFailureDiagnostics.EventId(
            new("ledger.xlsx", FailureStage.Apply, "stock_ledger", "HEMW", null, Guid.NewGuid(), 547, new InvalidOperationException())
            { CommitState = CommitState.RolledBack }));
}
