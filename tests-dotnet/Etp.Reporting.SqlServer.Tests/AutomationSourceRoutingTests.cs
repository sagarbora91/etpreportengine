using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>Where watch-folder automation files a source after its run, and which dates get a report pack.</summary>
public sealed class AutomationSourceRoutingTests
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    private static FolderImportFileResult File(string status, CommitState? commit = null, int conflicts = 0) =>
        new($"{status}.xlsx", "R025", "HEMW", Day, Day, status, ConflictRows: conflicts) { CommitState = commit };

    [Fact]
    public void A_saved_import_whose_read_back_failed_is_imported_not_failed()
    {
        var batch = new FolderImportSummary([File("Failed", CommitState.Committed)]);
        Assert.Equal(AutomationSourceRoute.Processed, AutomatedOperationsService.RouteOf(batch));
        Assert.Equal(new[] { Day }, AutomatedOperationsService.ImportedDates(batch));
    }

    [Fact]
    public void A_cancel_after_the_commit_counts_as_imported()
    {
        var batch = new FolderImportSummary([File("Cancelled", CommitState.Committed)]);
        Assert.Equal(AutomationSourceRoute.Processed, AutomatedOperationsService.RouteOf(batch));
        Assert.Equal(new[] { Day }, AutomatedOperationsService.ImportedDates(batch));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CommitState.RolledBack)]
    [InlineData(CommitState.Unknown)]
    public void A_failure_that_did_not_commit_moves_the_source_to_failed(CommitState? commit)
    {
        var batch = new FolderImportSummary([File("Imported", CommitState.Committed), File("Failed", commit)]);
        Assert.Equal(AutomationSourceRoute.Failed, AutomatedOperationsService.RouteOf(batch));
        Assert.Equal(new[] { Day }, AutomatedOperationsService.ImportedDates(batch));
    }

    [Fact]
    public void A_committed_file_with_conflicting_rows_still_needs_review()
    {
        var batch = new FolderImportSummary([File("Failed", CommitState.Committed, conflicts: 3)]);
        Assert.Equal(AutomationSourceRoute.Failed, AutomatedOperationsService.RouteOf(batch));
        Assert.Empty(AutomatedOperationsService.ImportedDates(batch));
    }

    [Fact]
    public void A_source_the_cancel_left_part_way_stays_in_inbound()
    {
        // The first file imported; the cancel left the second unhandled. Moving the source would lose it.
        var batch = new FolderImportSummary([File("Imported", CommitState.Committed), File("Cancelled")]);
        Assert.Equal(AutomationSourceRoute.Inbound, AutomatedOperationsService.RouteOf(batch));
        var rolledBack = new FolderImportSummary([File("Cancelled", CommitState.RolledBack)]);
        Assert.Equal(AutomationSourceRoute.Inbound, AutomatedOperationsService.RouteOf(rolledBack));
    }

    [Fact]
    public void Duplicates_alone_go_to_the_duplicate_folder_and_unknown_layouts_to_failed()
    {
        Assert.Equal(AutomationSourceRoute.Duplicate, AutomatedOperationsService.RouteOf(new([File("Duplicate")])));
        Assert.Equal(AutomationSourceRoute.Processed, AutomatedOperationsService.RouteOf(new([File("Duplicate"), File("Imported", CommitState.Committed)])));
        Assert.Equal(AutomationSourceRoute.Failed, AutomatedOperationsService.RouteOf(new([File("Unknown layout")])));
        Assert.Equal(AutomationSourceRoute.Processed, AutomatedOperationsService.RouteOf(new([File("Not needed")])));
    }

    [Fact]
    public void The_shop_result_failure_sentence_survives_the_500_character_cut()
    {
        // Review IR-6: a long classifier message must not push the B-1 sentence past automation_runs' 500 characters.
        var note = ShopResultWriter.FailureSentence(ShopResultWriter.FolderDenied);
        Assert.Equal(500, Phase2OperationsRepository.AutomationMessageLimit);
        foreach (var length in new[] { 0, 10, 500 - note.Length, 501 - note.Length, 499, 500, 2000 })
        {
            var message = AutomatedOperationsService.WithNote(new string('m', length), note);
            Assert.True(message.Length <= 500, $"{length}: {message.Length}");
            Assert.EndsWith(note, message, StringComparison.Ordinal);
            if (length + note.Length > 500) Assert.EndsWith("..." + note, message, StringComparison.Ordinal);
            else Assert.Equal(new string('m', length) + note, message);
        }
        Assert.Equal("Base.", AutomatedOperationsService.WithNote("Base.", ""));
        Assert.Equal(new string('m', 900), AutomatedOperationsService.WithNote(new string('m', 900), ""));
    }
}
