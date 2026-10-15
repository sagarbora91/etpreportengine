using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Morning;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.Morning;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12.1 lane P4 (decision 64 #3): a Morning Fix opens the one file's problem. The fix screen reads the history of the
/// Fix's own day and store and keeps only that file's rows; any other argument keeps U2's rule. Synthetic codes only.
/// </summary>
public sealed class MorningFixTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);
    private static readonly DateOnly Header = new(2026, 10, 10);
    private static readonly string Sha = new('D', 64);

    private static ImportProblem Problem(string file, string? store, string? sha = null) =>
        ImportProblems.Describe(new FolderImportFileResult(file, "R025", store, Day, Day, "Failed") { SourceSha256 = sha });

    [Fact]
    public void The_fix_screen_reads_the_cards_day_and_store_not_the_header()
    {
        var fix = MorningFix.Read("fix:20261009:SC902:sales.xlsx");
        Assert.NotNull(fix);
        Assert.Equal(new ImportHistoryScope(Day, Day, "SC902"), MorningFix.Scope(fix, Header, "SC901"));
        // Any other argument (U2's file name or SHA-256, an older history key) keeps the header's scope.
        Assert.Null(MorningFix.Read("sales.xlsx"));
        Assert.Null(MorningFix.Read("attempt:41"));
        Assert.Equal(new ImportHistoryScope(Header, Header, null), MorningFix.Scope(null, Header, null));
    }

    [Fact]
    public void Only_the_one_files_rows_are_kept()
    {
        var problems = new[]
        {
            Problem("sales.xlsx", "SC902"),
            Problem("sales.xlsx", "SC903"),
            Problem("sales.xlsx", null),
            Problem("tenders.xlsx", "SC902"),
            Problem("renamed.xlsx", "SC904", Sha)
        };

        var byName = MorningFix.Filter(problems, MorningFix.Read("fix:20261009:SC902:sales.xlsx")!);
        Assert.Equal([("sales.xlsx", "SC902"), ("sales.xlsx", "")], byName.Select(problem => (problem.File, problem.Store)));

        var byHash = MorningFix.Filter(problems, MorningFix.Read("fix:20261009:SC902:" + Sha.ToLowerInvariant())!);
        Assert.Equal("renamed.xlsx", Assert.Single(byHash).File);

        Assert.Empty(MorningFix.Filter(problems, MorningFix.Read("fix:20261009:SC902:stock.xlsx")!));
    }
}
