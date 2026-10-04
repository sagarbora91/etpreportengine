using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>
/// Lane L9: each synthetic raw export (tests-dotnet/fixtures/service-interim/raw) matches its Service family, is dated by
/// the end of the window in its name (2026-10-09) and carries store AW330. The S catalogue entries and RawNamePatterns are
/// lane L1's (merged through feature/service-interim).
/// "Not needed" for S005/S028 is lane L3's folder gate; ServiceRawImportSqlTests checks it through the folder import.
/// </summary>
public sealed class RawServiceRoutingTests
{
    // Remove the Skip once L1 (S catalogue + RawNamePatterns + d-M-yyyy dates) is merged and pulled.
    private const string WaitsForL1 = "Waits for lane L1's S catalogue (RawNamePatterns, d-M-yyyy dates); un-skip after pulling feature/service-interim.";

    private static readonly DateOnly WindowEnd = new(2026, 10, 9);

    private static async Task<MatchedImportInspection> Inspect(string fileName) =>
        new MatchedImportEnvelopeFactory(["WLMHW", "HEMW", "AW330"]).Inspect(await new SourceFileReader().ReadAsync(RawServiceFixtures.Path(fileName)));

    [Theory]
    [InlineData("JOB REPORT 06.10.2026 TO 09.10.2026.csv", "S002")]
    [InlineData("TENDER COLLECTION 05.10.2026 TO 09.10.2026.csv", "S004")]
    [InlineData("PENDING REPORT 09.10.2026.csv", "S009")]
    [InlineData("PENDING DELIVERY 09.10.2026.csv", "S010")]
    [InlineData("R R DELIVERD 06.10.2026 TO 09.10.2026.csv", "S018")]
    [InlineData("R R PENDING DELIVERY 06.10.2026 TO 09.10.2026.csv", "S031")]
    [InlineData("RENVENUE REPORT 06.10.2026 TO 09.10.2026.csv", "S003")]
    [InlineData("EMPOWERMENT REPORT 06.10.2026 TO 09.10.2026.xlsx", "S022")]
    public async Task An_importable_raw_export_matches_its_family_dated_by_its_window_end_for_AW330(string fileName, string family)
    {
        var inspection = await Inspect(fileName);

        Assert.Equal(family, inspection.MatchedProfile?.ReportCode);
        var accepted = inspection.AcceptedImport;
        Assert.True(accepted is not null, string.Join(" ", inspection.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct()));
        Assert.Equal(WindowEnd, accepted.Scope.PeriodEnd);
        Assert.Equal("AW330", accepted.Scope.StoreCode);
        Assert.True(inspection.StagedRows > 0);
    }

    [Theory]
    [InlineData("TENDER COLLECTIN SUMMARY 06.10.2026 TO 09.10.2026.csv", "S005")]
    [InlineData("TECHNICIAN PRODUCIVITY REPORT 06.10.2026 TO 09.10.2026.xlsx", "S028")]
    public async Task A_raw_export_of_a_family_the_interim_does_not_import_is_still_recognised(string fileName, string family)
    {
        // Recognised by header (S028 by its raw header, without the consolidation columns), so L3 can report it Not needed.
        Assert.Equal(family, (await Inspect(fileName)).MatchedProfile?.ReportCode);
    }

    [Fact]
    public async Task The_empowerment_export_matches_below_its_title_rows()
    {
        var inspection = await Inspect("EMPOWERMENT REPORT 06.10.2026 TO 09.10.2026.xlsx");

        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == ImportPreflight.HeaderBelowTitleRows);
        Assert.Equal(12, inspection.AcceptedImport?.MatchedSheet.HeaderRowNumber);
    }

    [Fact]
    public async Task The_new_GPRC_claim_layout_is_an_unknown_layout_not_a_failure()
    {
        var inspection = await Inspect("GPRC CLAIM 06.10.2026 TO 09.10.2026.xlsx");

        Assert.Null(inspection.MatchedProfile);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == "LAYOUT_UNKNOWN");
    }

    [Fact]
    public void Every_fixture_is_named_with_its_window_end()
    {
        var files = Directory.EnumerateFiles(RawServiceFixtures.Folder)
            .Where(path => SourceFileReader.IsCsv(path) || Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName).ToArray();

        Assert.Equal(11, files.Length);
        Assert.All(files, name => Assert.Equal(WindowEnd, Sources.ExportNameParser.Parse(name).ExportDate));
    }
}
