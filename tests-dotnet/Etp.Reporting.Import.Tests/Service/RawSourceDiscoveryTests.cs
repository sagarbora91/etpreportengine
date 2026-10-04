using System.IO.Compression;
using Etp.Reporting.Import.Audit;
using Etp.Reporting.Import.Batch;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>Lane L9: folder, ZIP and single-file import admit .csv exports beside .xlsx workbooks.</summary>
public sealed class RawSourceDiscoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "EtpRawDiscovery-" + Guid.NewGuid().ToString("N"));

    public RawSourceDiscoveryTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task A_folder_yields_its_csv_exports_and_workbooks_and_skips_other_files()
    {
        File.WriteAllText(Path.Combine(root, "JOB REPORT 06.10.2026 TO 09.10.2026.csv"), "A\n1\n");
        File.WriteAllBytes(Path.Combine(root, "R025_sales.xlsx"), [1]);
        File.WriteAllText(Path.Combine(root, "notes.txt"), "ignored");
        File.WriteAllText(Path.Combine(root, "~$JOB REPORT.csv"), "lock");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllText(Path.Combine(root, "nested", "PENDING REPORT 09.10.2026.CSV"), "A\n1\n");

        await using var source = await BatchImportSource.OpenAsync(root);

        Assert.Equal(
            new[] { "JOB REPORT 06.10.2026 TO 09.10.2026.csv", "R025_sales.xlsx", "PENDING REPORT 09.10.2026.CSV" },
            source.WorkbookPaths.Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_single_csv_file_is_a_source()
    {
        var path = Path.Combine(root, "PENDING REPORT 09.10.2026.csv");
        File.WriteAllText(path, "A\n1\n");

        await using var source = await BatchImportSource.OpenAsync(path);

        Assert.Equal(path, Assert.Single(source.WorkbookPaths));
    }

    [Fact]
    public async Task A_zip_may_hold_csv_exports()
    {
        var zip = Path.Combine(root, "Service raw 09.10.2026.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("JOB REPORT 06.10.2026 TO 09.10.2026.csv").Open())) writer.Write("A\n1\n");
            using (var writer = new StreamWriter(archive.CreateEntry("sub/PENDING REPORT 09.10.2026.csv").Open())) writer.Write("A\n1\n");
        }

        await using var source = await BatchImportSource.OpenAsync(zip);

        Assert.Equal(new[] { "JOB REPORT 06.10.2026 TO 09.10.2026.csv", "PENDING REPORT 09.10.2026.csv" },
            source.WorkbookPaths.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Other_file_types_are_still_refused()
    {
        var path = Path.Combine(root, "legacy.xls");
        File.WriteAllBytes(path, [1]);

        var error = await Assert.ThrowsAsync<ImportSourceException>(() => BatchImportSource.OpenAsync(path));

        Assert.Equal("IMPORT_TYPE_UNSUPPORTED", error.Code);
        Assert.Contains(".csv", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_policy_that_excludes_csv_keeps_the_workbook_only_behaviour()
    {
        File.WriteAllText(Path.Combine(root, "JOB REPORT 06.10.2026 TO 09.10.2026.csv"), "A\n1\n");
        File.WriteAllBytes(Path.Combine(root, "R025_sales.xlsx"), [1]);
        var policy = new ImportPathPolicy(ImportPathPolicyOptions.Default with { AdmitCsv = false });

        await using var source = await BatchImportSource.OpenAsync(root, policy);

        Assert.Equal("R025_sales.xlsx", Path.GetFileName(Assert.Single(source.WorkbookPaths)));
        Assert.False(policy.IsSupportedSource("a.csv"));
        Assert.True(new ImportPathPolicy().IsSupportedSource("a.CSV"));
    }

    [Fact]
    public void The_audit_inspector_still_counts_csv_exports_instead_of_reading_them()
    {
        Assert.False(new SourceInspector().PathPolicy.IsSupportedSource("JOB REPORT 06.10.2026 TO 09.10.2026.csv"));
    }
}
