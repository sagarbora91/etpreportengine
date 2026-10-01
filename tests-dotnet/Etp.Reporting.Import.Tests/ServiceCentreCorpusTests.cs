using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Xunit.Abstractions;

namespace Etp.Reporting.Import.Tests;

/// <summary>Runs only where the consolidated Service Centre folder of the 29 Sep 2026 import package exists.
/// It reads the workbooks in place and reports file names, codes, counts and scope only, never cell values.</summary>
public sealed class ServiceCentreCorpusFactAttribute : FactAttribute
{
    // ETP_SERVICE_CENTRE_CORPUS names the folder explicitly; otherwise it is found by walking up from the
    // test output folder to the ETP root that holds Installers\Consolidated data import package (29 Sep 2026).
    public const string PackageFolder = @"Installers\Consolidated data import package (29 Sep 2026)\Not imported\Service Centre (no importer profile)";
    public static readonly string? Configured = Environment.GetEnvironmentVariable("ETP_SERVICE_CENTRE_CORPUS") is { Length: > 0 } value ? Path.GetFullPath(value) : null;
    public static readonly string? Folder = Configured ?? FindBesideCheckout();

    public ServiceCentreCorpusFactAttribute()
    {
        // A folder someone named but that is missing fails the test instead of skipping it.
        if (Configured is null && Folder is null) Skip = "Optional Service Centre consolidated folder is absent (set ETP_SERVICE_CENTRE_CORPUS or keep the 29 Sep 2026 import package beside the checkout); the sanitised S fixtures still run.";
    }

    static string? FindBesideCheckout()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, PackageFolder);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}

public sealed class ServiceCentreCorpusTests(ITestOutputHelper output)
{
    // The golden store list stays Retail-only: AW330 must come from the workbook, not from a configured store.
    private static readonly string[] FixtureStores = ["HEMW", "WLMHW"];

    // Staged rows per consolidated file, measured from the 29 Sep 2026 package (S038 is retired and empty).
    public static readonly IReadOnlyDictionary<string, int> ExpectedRows = new Dictionary<string, int>
    {
        ["S001"] = 3655, ["S002"] = 3954, ["S003"] = 4968, ["S004"] = 2218, ["S005"] = 1865, ["S006"] = 497, ["S007"] = 1562,
        ["S008"] = 1539, ["S009"] = 63, ["S010"] = 128, ["S011"] = 145, ["S012"] = 172, ["S013"] = 37, ["S014"] = 110,
        ["S015"] = 156, ["S016"] = 31, ["S017"] = 450, ["S018"] = 3593, ["S019"] = 6, ["S020"] = 22, ["S021"] = 110,
        ["S022"] = 190, ["S023"] = 53, ["S024"] = 44, ["S025"] = 97, ["S026"] = 36, ["S027"] = 1184, ["S028"] = 1486,
        ["S029"] = 3861, ["S030"] = 1522, ["S031"] = 39, ["S032"] = 41, ["S033"] = 12, ["S034"] = 5, ["S035"] = 6,
        ["S036"] = 783, ["S037"] = 828, ["S038"] = 0, ["S039"] = 13, ["S040"] = 13
    };

    // Row-date families take their period from the rows; every other family is a snapshot of the package date.
    public static readonly IReadOnlyDictionary<string, (DateOnly Start, DateOnly End)> RowDatePeriods = new Dictionary<string, (DateOnly, DateOnly)>
    {
        ["S003"] = (new(2024, 10, 1), new(2026, 9, 28)), ["S004"] = (new(2024, 10, 1), new(2026, 9, 28)),
        ["S019"] = (new(2025, 4, 21), new(2026, 9, 8)), ["S023"] = (new(2024, 11, 5), new(2026, 8, 5)),
        ["S024"] = (new(2025, 2, 3), new(2025, 11, 9)), ["S025"] = (new(2024, 11, 5), new(2026, 7, 1)),
        ["S026"] = (new(2024, 11, 5), new(2026, 7, 1)), ["S039"] = (new(2026, 7, 1), new(2026, 9, 8)),
        ["S040"] = (new(2026, 7, 1), new(2026, 9, 8))
    };

    public static readonly DateOnly SnapshotDate = new(2026, 9, 29);

    [ServiceCentreCorpusFact]
    public async Task Whole_service_centre_consolidated_folder_inspects_clean()
    {
        var files = Directory.GetFiles(ServiceCentreCorpusFactAttribute.Folder!, "S*.xlsx").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(40, files.Length);
        var failures = new List<string>();
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var code = name[..4];
            var inspection = new MatchedImportEnvelopeFactory(FixtureStores).Inspect(await new OpenXmlWorkbookReader().ReadAsync(path));
            if (!inspection.Accepted)
            {
                // Column names and row numbers only: a failing cell value never reaches the log.
                failures.Add(name + ": " + string.Join("; ", inspection.Diagnostics.Where(d => d.Severity == ImportDiagnosticSeverity.Blocker)
                    .GroupBy(d => (d.Code, d.ColumnName)).Select(g => $"{g.Key.Code} column {g.Key.ColumnName} x{g.Count()}")));
                continue;
            }
            var accepted = inspection.AcceptedImport!;
            output.WriteLine($"{name} {accepted.Profile.ReportCode} rows={accepted.Staging.Rows.Count} store={accepted.Scope.StoreCode ?? "-"} {accepted.Scope.PeriodStart}..{accepted.Scope.PeriodEnd}");
            Assert.DoesNotContain(accepted.Diagnostics, d => d.Severity == ImportDiagnosticSeverity.Blocker);
            // S038 is retired: its file repeats S011's layout, so it matches S011 as an empty export.
            Assert.Equal(code == "S038" ? "S011" : code, accepted.Profile.ReportCode);
            Assert.Equal(ExpectedRows[code], accepted.Staging.Rows.Count);
            if (code == "S038") Assert.Contains(accepted.Diagnostics, d => d.Code == "EMPTY_EXPORT");
            Assert.Equal(code is "S011" or "S013" or "S038" ? null : "AW330", accepted.Scope.StoreCode);
            if (code == "S038") continue;
            var (start, end) = RowDatePeriods.TryGetValue(code, out var period) ? period : (SnapshotDate, SnapshotDate);
            Assert.Equal(start, accepted.Scope.PeriodStart);
            Assert.Equal(end, accepted.Scope.PeriodEnd);
            Assert.Equal(EtpReportFamily.ServiceBusinessUnit, EtpReportFamilyRegistry.Resolve(code).BusinessUnit);
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
