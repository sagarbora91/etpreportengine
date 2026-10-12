using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// ETP-DESIGN-SYSTEM.md rule C1 / section 12 item 1 (UIUX audit DS-01, 1.11.3 integration): colours come from the theme
/// tokens, so a Desktop view or control outside <c>Themes\</c> writes no <c>"#RRGGBB"</c> literal and no <c>Brushes.X</c>.
/// The files below still do, for the counts given; the list may only shrink. Removing a literal from a listed file fails
/// this test until the count here is lowered (or the entry removed), so the allowance never grows back silently.
/// </summary>
public sealed class DesignGuardTests
{
    // Path relative to src\Etp.Reporting.Desktop (forward slashes) -> number of literals still allowed.
    private static readonly IReadOnlyDictionary<string, int> Allowed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        // Help Centre pictures and callouts (HS work, 1.12 DS lane).
        ["HelpCentreControls.cs"] = 22,
        // Evening DSR screen and the legacy report document renderer (Sys DS-03 / G7, not owned by a 1.11.3 lane).
        ["Modules/Reports/EveningDsrView.cs"] = 6,
        ["Modules/Reports/ReportPresentationControl.cs"] = 8,
        // White metric text on the dashboard's dark card.
        ["Modules/Dashboard/DashboardView.cs"] = 1,
        // Not colours a person reads: the transparent read-only Ref box, a FormattedText measure brush, the capture background.
        ["MainWindow.Diagnostics.cs"] = 1,
        ["MainWindow.Shell.cs"] = 1,
        ["ReviewCapture.cs"] = 1
    };

    private static readonly Regex CsHex = new(@"""#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?""", RegexOptions.Compiled);
    private static readonly Regex XamlHex = new(@"""#[0-9A-Fa-f]{3,8}""", RegexOptions.Compiled);
    private static readonly Regex BrushesMember = new(@"(?<![A-Za-z0-9_])Brushes\.", RegexOptions.Compiled);

    [Fact]
    public void Desktop_views_use_theme_tokens_not_colour_literals_beyond_the_shrinking_allowlist()
    {
        var desktop = Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop");
        var counts = Directory.EnumerateFiles(desktop, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Select(file => (Relative: Path.GetRelativePath(desktop, file).Replace(Path.DirectorySeparatorChar, '/'), File: file))
            .Where(x => !x.Relative.Split('/').Any(part => part is "obj" or "bin" or "Themes"))
            .Select(x =>
            {
                var text = File.ReadAllText(x.File);
                var hex = x.File.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? XamlHex : CsHex;
                return (x.Relative, Count: hex.Matches(text).Count + BrushesMember.Matches(text).Count);
            })
            .Where(x => x.Count > 0)
            .ToDictionary(x => x.Relative, x => x.Count, StringComparer.OrdinalIgnoreCase);

        var grown = counts.Where(x => x.Value > Allowed.GetValueOrDefault(x.Key)).Select(x => $"{x.Key} ({x.Value}, allowed {Allowed.GetValueOrDefault(x.Key)})").ToArray();
        Assert.True(grown.Length == 0, "Use a theme token (DynamicResource / SetResourceReference) instead of a colour literal: " + string.Join(", ", grown));

        var shrunk = Allowed.Where(x => counts.GetValueOrDefault(x.Key) < x.Value).Select(x => $"{x.Key} (now {counts.GetValueOrDefault(x.Key)}, listed {x.Value})").ToArray();
        Assert.True(shrunk.Length == 0, "Lower the allowance in DesignGuardTests.Allowed to the new count: " + string.Join(", ", shrunk));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
