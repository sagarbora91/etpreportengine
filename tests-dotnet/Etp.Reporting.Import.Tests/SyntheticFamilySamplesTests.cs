using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.HealthCheck;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// Health check IMP-01: one synthetic sample per registered export family, each recognised as its own family. A profile or
/// header change that breaks recognition must fail here. The samples hold only invented values.
/// </summary>
public sealed class SyntheticFamilySamplesTests
{
    private static readonly IReadOnlyList<EtpReportFamily> Families = EtpReportFamilyRegistry.Families;

    [Fact]
    public void One_sample_per_registered_family()
    {
        var samples = SyntheticFamilySamples.CreateAll();

        Assert.Equal(Families.Count, samples.Count);
        Assert.Equal(Families.Select(f => f.FamilyCode), samples.Select(s => s.Family.FamilyCode));
        Assert.Equal(samples.Count, samples.Select(s => s.Workbook.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(samples, s => Assert.Equal(1, s.Workbook.Sheets.Single().Rows.Count));
    }

    [Fact]
    public void Every_registered_family_is_recognised_as_itself_with_no_blocker()
    {
        var checks = SyntheticFamilySamples.CheckAll();

        Assert.Equal(Families.Count, checks.Count);
        Assert.All(checks, check => Assert.True(check.Recognised,
            $"{check.FamilyCode} matched {check.MatchedReportCode ?? "nothing"}; blockers: {string.Join(",", check.Blockers)}"));
    }

    [Theory]
    [InlineData("S001")]
    [InlineData("S014")]
    [InlineData("S018")]
    [InlineData("S031")]
    [InlineData("S035")]
    [InlineData("SOR_AGEING")]
    public void Families_sharing_a_header_or_without_a_family_code_are_settled_by_the_file_name(string familyCode)
    {
        var family = Families.Single(f => f.FamilyCode == familyCode);
        var check = SyntheticFamilySamples.CheckAll([SyntheticFamilySamples.Create(family)]).Single();

        Assert.True(check.Recognised, string.Join(",", check.Blockers));
        Assert.Equal(family.ReportCode, check.MatchedReportCode);
    }

    [Fact]
    public void A_profile_whose_header_changed_fails_its_family_and_only_that_family()
    {
        var target = Families.Single(f => f.FamilyCode == "R025");
        var profiles = ApprovedImportProfileRegistry.All.Select(profile => profile.ReportCode == target.ReportCode
            ? new ImportProfile(profile.ReportCode, profile.LayoutVersion, profile.ProfileVersion,
                ImportProfileMatcher.CreateHeaderSignature([.. target.Headers, "ADDED_COLUMN"]), profile.Fields, profile.ExpectedSourceHeaders)
            : profile).ToArray();

        var checks = SyntheticFamilySamples.CheckAll(profiles);

        var broken = Assert.Single(checks, check => !check.Recognised);
        Assert.Equal("R025", broken.FamilyCode);
        Assert.Contains("LAYOUT_UNKNOWN", broken.Blockers);
    }

    [Fact]
    public void A_sample_with_an_edited_header_is_not_recognised()
    {
        var family = Families.Single(f => f.FamilyCode == "R022");
        var edited = family with { Headers = [.. family.Headers.Take(family.Headers.Count - 1), "RENAMED_BY_A_NEW_EXPORT"] };

        var check = SyntheticFamilySamples.CheckAll([SyntheticFamilySamples.Create(edited)]).Single();

        Assert.False(check.Recognised);
        Assert.Contains("LAYOUT_UNKNOWN", check.Blockers);
    }

    [Fact]
    public void Samples_hold_only_invented_values()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            SyntheticFamilySamples.TextValue, SyntheticFamilySamples.ItemCode, SyntheticFamilySamples.StoreCode
        };
        foreach (var sample in SyntheticFamilySamples.CreateAll())
        foreach (var cell in sample.Workbook.Sheets.Single().Rows.Single().Cells)
        {
            switch (cell.Value)
            {
                case string text: Assert.Contains(text, allowed); break;
                case DateTime date: Assert.Equal(new DateTime(2001, 1, 1), date); break;
                case decimal number: Assert.True(number is 1m or 1.00m); break;
                case bool flag: Assert.False(flag); break;
                default: Assert.Fail($"{sample.Family.FamilyCode}: unexpected value type {cell.Value?.GetType().Name ?? "null"}"); break;
            }
        }
    }

    [Fact]
    public void Sample_file_names_and_hashes_are_well_formed_and_nothing_is_read_from_disk()
    {
        foreach (var sample in SyntheticFamilySamples.CreateAll())
        {
            Assert.EndsWith(".xlsx", sample.Workbook.FileName, StringComparison.Ordinal);
            Assert.Null(sample.Workbook.SourcePath);
            Assert.Null(sample.Workbook.Content);
            Assert.Equal(64, sample.Workbook.Sha256.Length);
            Assert.True(sample.Workbook.FileSizeBytes > 0);
        }
    }

    [Fact]
    public void Daily_inputs_sample_is_recognised_by_content_as_D001()
    {
        Assert.True(SyntheticFamilySamples.CheckDailyInputs());
        var sample = SyntheticFamilySamples.CreateDailyInputsSample();
        Assert.Contains("ZZSYN", sample.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void Daily_inputs_sample_with_a_changed_sheet_name_is_not_recognised()
    {
        var sample = SyntheticFamilySamples.CreateDailyInputsSample();
        var renamed = sample with { Sheets = [sample.Sheets.Single() with { Name = "Sheet1" }] };

        Assert.False(Etp.Reporting.Import.DailyInputs.DailyInputsTemplate.IsDailyInputsWorkbook(renamed));
    }
}
