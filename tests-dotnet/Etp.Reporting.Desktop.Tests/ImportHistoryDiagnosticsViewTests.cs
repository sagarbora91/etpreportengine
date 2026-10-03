using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Modules.Imports;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>Imports > History shows each attempt's failure code, stage, message and issues (spec 12, IF-017).</summary>
[Collection(WpfViewCollection.Name)]
public sealed class ImportHistoryDiagnosticsViewTests
{
    [Fact]
    public void Selected_attempt_shows_failure_code_stage_message_and_issues()
    {
        RunSta(() =>
        {
            var issues = new[]
            {
                new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.ImportConflict, "Invoice date differs. Review and request a controlled restatement.",
                    2, DocumentRef: "R025 HEMW|2027|100000001 2026-08-26"),
                new ImportIssue(ImportIssueSeverity.Warning, "UNKNOWN_SALES_TRANSACTION_TYPE", "Unrecognised transaction type; this row was skipped.",
                    Occurrences: 12)
            };
            var failed = new FolderImportFileResult("moved.xlsx", "R025", "HEMW", new(2026, 8, 26), new(2026, 8, 26), "Failed",
                RowsProcessed: 1, ConflictRows: 1, Message: "1 conflicting rows. The complete file was rolled back.", Diagnostics: issues)
            {
                Failure = new(ImportCodes.ImportConflict, FailureStage.Apply, "1 conflicting rows. The complete file was rolled back.",
                    "ImportConflictException"),
                CommitState = CommitState.RolledBack,
                Evidence = EvidenceState.NotAttempted
            };
            var view = new ImportHistoryView(_ => Task.FromResult<IReadOnlyList<ImportHistoryEntry>>(
                [new("attempt:1", new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc), null, failed)]));

            view.ActivateAsync(new(new(2026, 8, 26), new(2026, 8, 26))).GetAwaiter().GetResult();

            Assert.Contains("Failure IMPORT_CONFLICT at Apply.", view.DetailText);
            Assert.Contains("1 conflicting rows.", view.DetailText);
            Assert.Contains("Transaction: RolledBack.", view.DetailText);
            Assert.Contains("Evidence: NotAttempted.", view.DetailText);
            var grids = Descendants<DataGrid>(view).ToArray();
            var outcomes = grids.Single(grid => grid.Columns.Any(column => (string)column.Header == "Failure"));
            Assert.Contains(outcomes.Columns, column => (string)column.Header == "Stage");
            var details = grids.Single(grid => grid.Columns.Any(column => (string)column.Header == "Guidance"));
            Assert.Equal(["Severity", "Issue", "Block", "Row", "Column", "Document", "Count", "Guidance"],
                details.Columns.Select(column => (string)column.Header));
            Assert.Same(issues, details.ItemsSource);
        });
    }

    [Theory]
    [InlineData("Duplicate content")]
    [InlineData("Duplicate")]
    public void Repeat_file_shows_the_already_imported_message(string status)
    {
        RunSta(() =>
        {
            var repeat = new FolderImportFileResult("again.xlsx", "R025", "HEMW", new(2026, 8, 26), new(2026, 8, 26), status,
                RowsProcessed: 12, AlreadyPresentRows: 12,
                Message: "Persisted import outcome. Counts describe source rows; a source row can produce multiple database facts.");
            var view = new ImportHistoryView(_ => Task.FromResult<IReadOnlyList<ImportHistoryEntry>>(
                [new("file:7", new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc), 7, repeat)]));

            view.ActivateAsync(new(new(2026, 8, 26), new(2026, 8, 26))).GetAwaiter().GetResult();

            Assert.Contains("This source was already imported. No facts were added by this attempt.", view.DetailText);
            Assert.DoesNotContain("Persisted import outcome", view.DetailText);
        });
    }

    [Fact]
    public void Saved_outcome_message_reads_duplicate_content_as_already_imported()
    {
        Assert.Equal(ImportHistoryMessages.AlreadyImported, ImportHistoryMessages.ForSavedOutcome(ImportAttemptOutcomes.DuplicateContent, 0, null));
        Assert.Equal(ImportHistoryMessages.AlreadyImported, ImportHistoryMessages.ForSavedOutcome(ImportAttemptOutcomes.Duplicate, 0, null));
        Assert.Equal(ImportHistoryMessages.Persisted, ImportHistoryMessages.ForSavedOutcome(ImportAttemptOutcomes.Imported, 0, null));
        Assert.StartsWith("3 source rows conflict", ImportHistoryMessages.ForSavedOutcome(ImportAttemptOutcomes.DuplicateContent, 3, null));
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
