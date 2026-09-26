using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Tests;

public sealed class StoreLiteralGuardTests
{
    [Fact]
    public void Runtime_source_uses_the_store_catalogue_instead_of_seed_store_codes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var offenders = FindStoreLiterals(Path.Combine(root.FullName, "src"));
        Assert.True(offenders.Count == 0, "Store-code literals in runtime source:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Guard_reports_source_file_and_line_but_ignores_bin_and_obj()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpStoreLiteralGuard_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            File.WriteAllText(Path.Combine(root, "View.cs"), "// Synthetic source\nconst string Store = \"WLMHW\";\n");
            File.WriteAllText(Path.Combine(root, "View.xaml"), "<TextBlock Text=\"HEMW\" />");
            File.WriteAllText(Path.Combine(root, "bin", "Generated.cs"), "WLMHW");
            File.WriteAllText(Path.Combine(root, "obj", "Generated.xaml"), "HEMW");
            var offenders = FindStoreLiterals(root);
            Assert.Equal(2, offenders.Count);
            Assert.Contains("View.cs:2: WLMHW", offenders);
            Assert.Contains("View.xaml:1: HEMW", offenders);
        }
        finally
        {
            var full = Path.GetFullPath(root);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("EtpStoreLiteralGuard_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe store-literal fixture cleanup path.");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }

    private static IReadOnlyList<string> FindStoreLiterals(string sourceRoot)
    {
        var pattern = new Regex(@"\b(WLMHW|HEMW)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml")
            .Where(path => !Path.GetRelativePath(sourceRoot, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase) || part.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (line, index))
                .Select(item => (item.index, match: pattern.Match(item.line)))
                .Where(item => item.match.Success)
                .Select(item => $"{Path.GetRelativePath(sourceRoot, path)}:{item.index + 1}: {item.match.Value}"))
            .Order(StringComparer.Ordinal).ToArray();
    }
}
