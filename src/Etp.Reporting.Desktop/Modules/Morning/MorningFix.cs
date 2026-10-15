extern alias EtpApplication;

using Etp.Reporting.Desktop.Modules.Imports;
using HistoryScope = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryScope;
using MorningFixArgument = EtpApplication::Etp.Reporting.Application.Morning.MorningFixArgument;

namespace Etp.Reporting.Desktop.Modules.Morning;

/// <summary>
/// 1.12.1 lane P4 (decision 64 #3): what the "import-problems" fix screen does with a Morning card's Fix argument. The
/// history is read for the card's own day and store (not the header's, which may be another store or date), and only the
/// rows of the one file are kept. Any other argument keeps U2's rule (<c>DesktopCompositionRoot.FilterProblems</c>).
/// </summary>
public static class MorningFix
{
    /// <summary>The argument of a Morning Fix button, or null for any other route argument.</summary>
    public static MorningFixArgument? Read(string? argument) => MorningFixArgument.TryParse(argument);

    /// <summary>The import history scope the fix screen reads: the Fix's day and store, else the header's.</summary>
    public static HistoryScope Scope(MorningFixArgument? fix, DateOnly headerDate, string? headerStore) =>
        fix is null ? new(headerDate, headerDate, headerStore) : new(fix.BusinessDate, fix.BusinessDate, fix.StoreCode);

    /// <summary>Only the problem rows of the Fix's file.</summary>
    public static IReadOnlyList<ImportProblem> Filter(IReadOnlyList<ImportProblem> problems, MorningFixArgument fix)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(fix);
        return problems.Where(problem => fix.Matches(problem.File, problem.Store, problem.Source?.SourceSha256)).ToArray();
    }
}
