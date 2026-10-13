using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave 2 (112-W2-PLAN A4/N1/U1/U2): the Service Centre is data from 0067 on (<c>business_units.unit_kind</c>,
/// <c>stores.service_job_prefix</c>, <c>IServiceCentreCatalog</c>), so no code under <c>src</c> may hold its store code as a string
/// literal any more. Tests and the Help Centre may still name it. This is lane U2's test, the last of the four lanes to merge:
/// it is green only once A4, N1, U1 and U2 have all removed theirs.
/// </summary>
public sealed class ImportServiceCentreLiteralTests
{
    private const string LegacyServiceStore = "AW330";

    // A C# string literal (regular, verbatim or raw) or a XAML attribute value holding the code. Comments may still mention it.
    private static readonly Regex QuotedLiteral = new("\"(?:[^\"\\\\\\r\\n]|\\\\.)*" + LegacyServiceStore + "(?:[^\"\\\\\\r\\n]|\\\\.)*\"", RegexOptions.Compiled);
    private static readonly Regex RawLiteral = new("\"\"\"[\\s\\S]*?" + LegacyServiceStore + "[\\s\\S]*?\"\"\"", RegexOptions.Compiled);

    [Fact]
    public void No_source_file_outside_tests_and_Help_holds_the_service_centre_store_code_as_a_literal()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Select(file => (Relative: Path.GetRelativePath(src, file).Replace(Path.DirectorySeparatorChar, '/'), File: file))
            .Where(x => !x.Relative.Split('/').Any(part => part is "obj" or "bin"))
            .Where(x => !Path.GetFileName(x.Relative).StartsWith("Help", StringComparison.OrdinalIgnoreCase))
            .Where(x => HoldsLiteral(File.ReadAllText(x.File)))
            .Select(x => x.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These files still hold the Service Centre store code as a literal; read it from IServiceCentreCatalog instead: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("var code = \"AW330\";", true)]
    [InlineData("const string Pattern = \"^JOAW330[0-9]{8,9}$\";", true)]
    [InlineData("var sql = \"\"\"\n SELECT 1 WHERE store_code='AW330'\n \"\"\";", true)]
    [InlineData("// Service rows carry store AW330.", false)]
    [InlineData("/// <summary>The Service Centre (AW330).</summary>", false)]
    [InlineData("var label = \"Service Centre\";", false)]
    public void The_rule_matches_literals_and_not_comments(string text, bool matches) => Assert.Equal(matches, HoldsLiteral(text));

    private static bool HoldsLiteral(string text)
    {
        if (!text.Contains(LegacyServiceStore, StringComparison.Ordinal)) return false;
        if (RawLiteral.IsMatch(text)) return true;
        // Line by line, with any // comment cut off first (a "//" inside a string on the same line is rare and only widens the cut).
        return text.Split('\n').Select(line => CutComment(line)).Any(line => QuotedLiteral.IsMatch(line));
    }

    private static string CutComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
            if (!inString && line[i] == '/' && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
