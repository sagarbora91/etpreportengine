using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Modules.Imports;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// IF-031 (decision 66: WONTFIX with clearer wording). An empty raw export has no dated rows, so History lists it under the
/// day it was exported; the wording says so, so nobody reads it as "the days it was asked for were checked".
/// </summary>
public sealed class ImportHistoryEmptyExportWordingTests
{
    private static readonly DateOnly ExportDay = new(2026, 10, 3);

    [Fact]
    public void Saved_empty_export_explains_its_date()
    {
        var message = ImportHistoryMessages.ForSavedOutcome(ImportAttemptOutcomes.EmptyExport, 0, null);

        Assert.Equal(ImportHistoryMessages.EmptyExport, message);
        Assert.Contains("listed under the day it was exported", message, StringComparison.Ordinal);
        Assert.Contains("does not say which days it covers", message, StringComparison.Ordinal);
        // The other outcomes keep their guidance.
        Assert.Equal(ImportHistoryMessages.Persisted, ImportHistoryMessages.ForSavedOutcome(ImportAttemptOutcomes.Imported, 0, null));
        Assert.Equal(ImportHistoryMessages.AlreadyImported, ImportHistoryMessages.ForSavedOutcome(ImportAttemptOutcomes.DuplicateContent, 0, null));
    }

    [Fact]
    public void Empty_export_verdict_names_the_export_day_basis()
    {
        var empty = new FolderImportFileResult("202610031040_AdvanceOrder Collection.xlsx", "R001", "TST01", ExportDay, ExportDay,
            ImportAttemptOutcomes.EmptyExport);

        Assert.Equal(ImportHistoryView.EmptyExportVerdict, ImportHistoryView.Verdict(empty));
        // Still counted as imported: nothing was refused.
        Assert.Equal("Imported", ImportHistoryView.ResultWord(empty));
        Assert.Equal("Imported - no new rows", ImportHistoryView.Verdict(new("a.xlsx", "R025", "TST01", ExportDay, ExportDay, ImportAttemptOutcomes.Imported)));
    }

    [Fact]
    public void A_failed_empty_file_is_still_refused_first()
    {
        var failed = new FolderImportFileResult("x.xlsx", "R001", "TST01", ExportDay, ExportDay, ImportAttemptOutcomes.EmptyExport)
        {
            Failure = new ImportFailure("IMPORT_PROCESSING_FAILED", FailureStage.Apply, "The workbook could not be imported.")
        };

        Assert.StartsWith("Refused - ", ImportHistoryView.Verdict(failed), StringComparison.Ordinal);
    }

    // 1.12.1 review M-3: a re-dated retire is in the Verdict, not hidden behind "Already imported - nothing changed".
    [Fact]
    public void A_re_dated_retire_is_said_in_the_verdict_with_the_day_that_lost_its_reading()
    {
        var message = ImportDiagnosticCatalogue.RedatedRetiredMessage(10092, new(2026, 10, 2), new(2026, 10, 2), new(2026, 10, 3), new(2026, 10, 3),
            [new(2026, 10, 2)], 0, []);
        var issue = new ImportIssue(ImportIssueSeverity.Warning, ImportCodes.ImportRedatedRetired, message);
        var duplicate = new FolderImportFileResult("202610031800_Closing Stock.xlsx", "CLOSING_STOCK", "TST01", ExportDay, ExportDay,
            ImportAttemptOutcomes.Duplicate, Diagnostics: [issue]);
        Assert.Equal("Already imported under this date - " + message, ImportHistoryView.Verdict(duplicate));
        Assert.Contains("02 Oct 2026 now has no stock reading.", ImportHistoryView.Verdict(duplicate), StringComparison.Ordinal);
        var imported = duplicate with { Status = ImportAttemptOutcomes.Imported, NewRows = 3 };
        Assert.Equal("Imported - 3 new rows - " + message, ImportHistoryView.Verdict(imported));
        // Without the issue the verdicts are unchanged.
        Assert.Equal("Already imported - nothing changed", ImportHistoryView.Verdict(duplicate with { Diagnostics = null }));
    }
}
