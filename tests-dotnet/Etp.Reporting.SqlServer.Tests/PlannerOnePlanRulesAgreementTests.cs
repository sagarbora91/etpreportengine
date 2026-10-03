using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Infrastructure.SqlServer;
using Store = Etp.Reporting.Infrastructure.SqlServer.SqlServerTransactionalImportStore;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.3 integration: ImportAudit's <see cref="PlannerOnePlanRules"/>.Decide is a copy of the file decision at the end
/// of <c>SqlServerTransactionalImportStore.PlanImportAsync</c> (PhaseOneImportPersistence.cs). The import path was not
/// switched to call it, because <c>PlanImportAsync</c> needs SQL Server and only the SQL integration suite covers it,
/// which has not run on this release yet. Until it is switched, these tests keep the two copies in agreement: the
/// first proves the two blocks are the same statements under a fixed renaming of their inputs, so for every input they
/// give the same answer; the second pins that answer on a table of cases, one for each outcome of the decision.
/// </summary>
public sealed class PlannerOnePlanRulesAgreementTests
{
    // PlanImportAsync's inputs, and the parameter of Decide each one is passed as.
    private static readonly (string PlanImport, string Decide)[] Renaming =
    [
        ("keys.Values.ToHashSet(", "incomingKeys.ToHashSet("),
        ("package.Restatement?.PreviousImportFileId", "restatementTarget"),
        ("package.Restatement", "restatementTarget"),
        ("restating.PreviousImportFileId", "restating"),
        ("replacing.PreviousImportFileId", "replacing"),
        ("(file.PeriodStart??file.BusinessDate)", "periodStart"),
        ("(file.PeriodEnd??file.BusinessDate)", "periodEnd"),
        ("file.SourceSha256", "sourceSha256"),
        ("returnnew(null,true,previous,keys);", "returntrue;"),
        ("returnnew(null,false,previous,keys);", "returnfalse;"),
    ];

    [Fact]
    public void PlanImportAsync_and_Decide_hold_the_same_decision_statement_for_statement()
    {
        var planImport = Block(Source("PhaseOneImportPersistence.cs"), "var incoming=keys.Values.ToHashSet", "return new(null,false,previous,keys);");
        var decide = Block(Source("PlannerOnePlanRules.cs"), "var incoming = incomingKeys.ToHashSet", "return false;");

        foreach (var (from, to) in Renaming)
            planImport = planImport.Replace(from, to, StringComparison.Ordinal);

        Assert.Equal(decide, planImport);
    }

    private static readonly DateOnly July1 = new(2026, 7, 1), July31 = new(2026, 7, 31), August31 = new(2026, 8, 31);
    private const string Hash = "1111111111111111111111111111111111111111111111111111111111111111";

    // Each case: the file period, incoming keys, previous files, restatement target and expected outcome. An outcome is
    // DUPLICATE_CONTENT (PlanImportAsync returns DuplicateContent), PROMOTE (imported over the previous files)
    // or the refusal code PlanImportAsync throws at the Plan stage.
    private sealed record Case(DateOnly Start, DateOnly End, string[] Incoming, Store.PreviousFile[] Previous, long? Target, string Expected);

    public static TheoryData<string> Cases
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var name in Table.Keys) names.Add(name);
            return names;
        }
    }

    private static readonly Dictionary<string, Case> Table = new()
    {
        ["subset of one file"] = new(July1, July31, ["a"], [File(1, July1, July31, ["a", "b"])], null, "DUPLICATE_CONTENT"),
        ["subset spread over two files"] = new(July1, July31, ["a", "b"], [File(1, July1, July1, ["a"]), File(2, July31, July31, ["b"])], null, "DUPLICATE_CONTENT"),
        ["nothing current"] = new(July1, July31, ["a"], [], null, "PROMOTE"),
        ["covering superset"] = new(July1, August31, ["a", "b", "c"], [File(1, July1, July1, ["a"]), File(2, July31, July31, ["b"])], null, "PROMOTE"),
        ["overlap without cover"] = new(July31, August31, ["a", "c"], [File(1, July1, July31, ["a", "b"])], null, "IMPORT_PERIOD_ALREADY_PRESENT"),
        ["cover that drops a row"] = new(July1, August31, ["a", "c"], [File(1, July1, July31, ["a", "b"])], null, "IMPORT_PERIOD_ALREADY_PRESENT"),
        ["v0 file with another hash"] = new(July1, August31, ["a", "b"], [File(1, July1, July31, ["a"], version: 0, hash: new string('2', 64))], null, "IMPORT_LEGACY_RESTATEMENT_REQUIRED"),
        ["v0 file with the same hash"] = new(July1, August31, ["a", "b"], [File(1, July1, July31, ["a"], version: 0)], null, "PROMOTE"),
        ["explicit restatement target"] = new(July1, July31, ["z"], [File(7, July1, July31, ["a"])], 7L, "PROMOTE"),
        ["restatement of a subset still restates"] = new(July1, July31, ["a"], [File(7, July1, July31, ["a", "b"])], 7L, "PROMOTE"),
        ["restatement partly over another import"] = new(July1, July31, ["z"], [File(7, July1, July31, ["a"]), File(8, July31, August31, ["b"])], 7L, ImportCodes.RestatementTargetNotCovered),
        ["restatement changing another covered import"] = new(July1, August31, ["z"], [File(7, July1, July31, ["a"]), File(8, August31, August31, ["b"])], 7L, ImportCodes.RestatementOtherImportChanged),
        ["restatement keeping another covered import"] = new(July1, August31, ["z", "b"], [File(7, July1, July31, ["a"]), File(8, August31, August31, ["b"])], 7L, "PROMOTE"),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Decide_gives_planner_one_outcome_for_each_case(string name)
    {
        var (start, end, incoming, previous, restatementTarget, expected) = Table[name];
        string outcome;
        try
        {
            outcome = PlannerOnePlanRules.Decide(start, end, Hash, incoming.Select(Key), previous, restatementTarget) ? "DUPLICATE_CONTENT" : "PROMOTE";
        }
        catch (ImportSourceException refusal)
        {
            Assert.Equal(FailureStage.Plan, refusal.Stage);
            outcome = refusal.Code;
        }

        Assert.True(expected == outcome, $"{name}: expected {expected}, got {outcome}.");
    }

    private static string Key(string c) => new string(c[0], 64) + ":1";

    private static Store.PreviousFile File(long id, DateOnly start, DateOnly end, string[] keys, int version = 1, string hash = Hash) =>
        new(id, hash, start, end, new DateTime(2026, 8, 1), keys.Select(Key).ToHashSet(StringComparer.Ordinal), version);

    private static string Source(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return System.IO.File.ReadAllText(Path.Combine(dir.FullName, "src", "Etp.Reporting.Infrastructure.SqlServer", name));
    }

    // The text from the first marker to the end of the second, without comment lines, namespace qualifiers or white
    // space, so that layout and fully qualified names do not count as a difference.
    private static string Block(string source, string first, string last)
    {
        var start = source.IndexOf(first, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{first}' not found.");
        var end = source.IndexOf(last, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{last}' not found after '{first}'.");
        var text = source[start..(end + last.Length)];
        text = string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        text = text.Replace("Etp.Reporting.Import.Batch.", "", StringComparison.Ordinal)
                   .Replace("Etp.Reporting.Application.Imports.", "", StringComparison.Ordinal);
        return Regex.Replace(text, @"\s+", "");
    }
}
