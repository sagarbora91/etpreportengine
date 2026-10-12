using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// UI/UX audit FW (decision 45 #5, rulebook R1/R6): a date or whole-number figure a person reads goes through
/// <c>IndianFormat</c>, never <c>CultureInfo.InvariantCulture</c> (which prints "Sep", not the app's "Sept"), and the
/// navigation name is "Operations Centre". Invariant stays for files meant for machines (Tally XML, JSON, file names).
/// </summary>
public sealed class FormatGuardTests
{
    private static readonly Regex InvariantDisplayFormat = new(
        @"ToString\(""((d|dd) MMM[^""]*|MMM yyyy|N0|N2)"",\s*(System\.Globalization\.)?CultureInfo\.InvariantCulture\)",
        RegexOptions.Compiled);

    [Fact]
    public void No_display_date_or_number_is_formatted_with_the_invariant_culture()
    {
        var offenders = SourceFiles()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(x => InvariantDisplayFormat.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.number}")
            .ToArray();
        Assert.True(offenders.Length == 0, "Use IndianFormat for text a person reads: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_navigation_name_is_spelled_Operations_Centre()
    {
        var offenders = SourceFiles().Where(file => File.ReadAllText(file).Contains("Operations Center", StringComparison.Ordinal)).Select(Path.GetFileName).ToArray();
        Assert.True(offenders.Length == 0, "Write Operations Centre: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
