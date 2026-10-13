using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave 2, lane U4: the design system's "stop doing" list (ETP-DESIGN-SYSTEM.md section 12) and the layer rule, checked on
/// the Collections screens' own files: tokens only, no literal type size, no fixed 44 or grid MaxHeight, no hex colour, no
/// SqlClient or connection string in a screen, and no bank-reference column or narration read for a role that may not see it.
/// </summary>
public sealed class CollectionsScreensGuardTests
{
    private static readonly Regex Forbidden = new(
        @"FontSize\s*=|FontWeight\s*=|FontWeights\.|MaxHeight\s*=|MinHeight\s*=\s*44|Brushes\.|""#[0-9A-Fa-f]{3,8}""|Color\.FromRgb|BrushConverter|SqlClient|ConnectionString|Padding\s*=\s*new",
        RegexOptions.CultureInvariant);

    [Fact]
    public void The_collections_screens_use_tokens_and_styles_only_and_never_touch_a_connection()
    {
        var folder = Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop", "Modules", "Collections");
        var files = Directory.GetFiles(folder, "*.cs");
        Assert.NotEmpty(files);
        var offenders = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (Name: Path.GetFileName(file), Line: line, Number: index + 1)))
            .Where(x => !x.Line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !x.Line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Where(x => Forbidden.IsMatch(x.Line))
            .Select(x => $"{x.Name}:{x.Number}")
            .ToArray();
        Assert.True(offenders.Length == 0, "Use a theme token, a style or a service: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_screens_read_money_through_the_formatter_and_a_missing_value_is_an_em_dash()
    {
        var folder = Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop", "Modules", "Collections");
        var text = string.Join('\n', Directory.GetFiles(folder, "*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("ToString(\"N2\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ToString(\"N0\"", text, StringComparison.Ordinal);
        Assert.Equal("—", Etp.Reporting.Desktop.Modules.Collections.CollectionsScreens.MissingValue);
    }

    [Fact]
    public void Only_the_Owners_deposit_screen_asks_for_proposals_which_carry_the_bank_narration()
    {
        var folder = Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop", "Modules", "Collections");
        var users = Directory.GetFiles(folder, "*.cs").Where(file => File.ReadAllText(file).Contains("LoadProposalsAsync", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToArray();
        Assert.Equal(["DepositsInTransitModel.cs"], users);
        Assert.Contains("CanAccept ? await service().LoadProposalsAsync", File.ReadAllText(Path.Combine(folder, "DepositsInTransitModel.cs")), StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
