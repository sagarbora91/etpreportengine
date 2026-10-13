using System.Text.Json;
using System.Text.Json.Nodes;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// The shop result file (decision 44 #3): verdict mapping, the zip-name rule, the atomic write, and the shared fixtures that
/// <c>Test-OneDriveScripts.ps1</c> signs with Publish-ShopResults.ps1 and verifies with Show-ShopResults.ps1 (cross-layer).
/// </summary>
public sealed class ShopResultTests
{
    private static readonly DateOnly Day = new(2026, 10, 11);
    private static readonly DateTime Produced = new(2026, 10, 12, 2, 10, 0, DateTimeKind.Utc);

    private static FolderImportFileResult Imported(string name, string status = "Imported", IReadOnlyList<ImportIssue>? diagnostics = null) =>
        new(name, "D001", "HEMW", Day, Day, status, Diagnostics: diagnostics) { SourceSha256 = new string('a', 64) };

    private static FolderImportFileResult Failed(string name, string code) =>
        new(name, "D001", "HEMW", Day, Day, "Failed") { SourceSha256 = new string('a', 64), Failure = new ImportFailure(code, FailureStage.Apply, "text that must never be copied: 1234567") };

    // ----- verdict mapping -----

    public static IEnumerable<object[]> RefusalCodes() =>
        DailyInputsCodes.All.Where(code => code is not DailyInputsCodes.NameUnusual and not DailyInputsCodes.NameDateDiffers).Select(code => new object[] { code });

    [Theory]
    [MemberData(nameof(RefusalCodes))]
    public void Every_daily_inputs_refusal_code_becomes_a_refused_verdict_with_catalogue_text_only(string code)
    {
        var file = ShopResultBuilder.Map(Failed("sheet.xlsx", code))!;

        Assert.Equal(ShopResultVerdict.Refused, file.Verdict);
        Assert.Equal(code, file.Code);
        var template = ImportDiagnosticCatalogue.Template(code);
        Assert.Equal(template, (file.Message + " " + file.WhatToDo).Trim());
        Assert.DoesNotContain("1234567", file.Message + file.WhatToDo);
        Assert.NotEmpty(file.Message);
    }

    [Fact]
    public void A_name_date_difference_is_accepted_with_a_warning_and_says_what_to_do()
    {
        var warning = new ImportIssue(ImportIssueSeverity.Warning, DailyInputsCodes.NameDateDiffers, "ignored", null, "Value");
        var file = ShopResultBuilder.Map(Imported("sheet.xlsx", diagnostics: [warning]))!;

        Assert.Equal(ShopResultVerdict.AcceptedWithWarning, file.Verdict);
        Assert.Equal(DailyInputsCodes.NameDateDiffers, file.Code);
        Assert.Contains("imported under its own date", file.Message);
        Assert.StartsWith("Next time save the file", file.WhatToDo);
    }

    [Fact]
    public void A_warning_the_shop_cannot_act_on_stays_accepted()
    {
        var warning = new ImportIssue(ImportIssueSeverity.Warning, "STOCK_ROW_REPEATED", "x");
        Assert.Equal(ShopResultVerdict.Accepted, ShopResultBuilder.Map(Imported("stock.csv", diagnostics: [warning]))!.Verdict);
    }

    [Theory]
    [InlineData("Imported", ShopResultVerdict.Accepted)]
    [InlineData("Not needed", ShopResultVerdict.Accepted)]
    [InlineData("Already present", ShopResultVerdict.Duplicate)]
    [InlineData("Duplicate", ShopResultVerdict.Duplicate)]
    [InlineData("Duplicate content", ShopResultVerdict.Duplicate)]
    public void Statuses_map_to_verdicts(string status, ShopResultVerdict expected) =>
        Assert.Equal(expected, ShopResultBuilder.Map(Imported("a.xlsx", status))!.Verdict);

    [Fact]
    public void A_cancelled_file_has_no_verdict_yet() => Assert.Null(ShopResultBuilder.Map(Imported("a.xlsx", "Cancelled")));

    [Fact]
    public void A_failure_without_a_code_uses_the_first_blocker_then_the_generic_message()
    {
        var blocker = new ImportIssue(ImportIssueSeverity.Blocker, "HEADER_INVALID", "x");
        var file = new FolderImportFileResult("a.xlsx", null, null, null, null, "Failed", Diagnostics: [blocker]);
        Assert.Equal("HEADER_INVALID", ShopResultBuilder.Map(file)!.Code);

        var bare = ShopResultBuilder.Map(new FolderImportFileResult("a.xlsx", null, null, null, null, "Failed"))!;
        Assert.Equal(ShopResultVerdict.Refused, bare.Verdict);
        Assert.Equal(ImportDiagnosticCatalogue.GenericMessage, (bare.Message + " " + bare.WhatToDo).Trim());
    }

    [Fact]
    public void The_overall_verdict_is_the_worst_one()
    {
        ShopResult Of(params ShopResultVerdict[] verdicts) => new("TITAN", Day, 1, "1.0", Produced,
            verdicts.Select(v => new ShopResultFile("f", "", v, "", "", "")).ToArray());
        Assert.Equal(ShopResultVerdict.Refused, Of(ShopResultVerdict.Accepted, ShopResultVerdict.Refused, ShopResultVerdict.AcceptedWithWarning).Overall);
        Assert.Equal(ShopResultVerdict.AcceptedWithWarning, Of(ShopResultVerdict.Accepted, ShopResultVerdict.AcceptedWithWarning).Overall);
        Assert.Equal(ShopResultVerdict.Duplicate, Of(ShopResultVerdict.Duplicate, ShopResultVerdict.Duplicate).Overall);
        Assert.Equal(ShopResultVerdict.Accepted, Of(ShopResultVerdict.Duplicate, ShopResultVerdict.Accepted).Overall);
    }

    [Fact]
    public void File_names_carry_no_path_and_no_odd_characters()
    {
        Assert.Equal("a_b_.xlsx", ShopResultBuilder.SafeName(@"C:\Users\Someone\a&b;.xlsx"));
        Assert.Equal(120, ShopResultBuilder.SafeName(new string('x', 400)).Length);
    }

    // ----- zip names -----

    [Theory]
    [InlineData(@"C:\Inbound\TITAN-2026-10-11.zip", "TITAN", "2026-10-11", 1)]
    [InlineData("helios-2026-10-11-r2.zip", "HELIOS", "2026-10-11", 2)]
    [InlineData("SERVICE-2026-10-11-r13.ZIP", "SERVICE", "2026-10-11", 13)]
    public void A_shop_zip_name_gives_role_day_and_sequence(string path, string role, string day, int sequence)
    {
        Assert.True(ShopResultBuilder.TryParseShopZip(path, out var r, out var d, out var s));
        Assert.Equal((role, DateOnly.Parse(day), sequence), (r, d, s));
    }

    [Theory]
    [InlineData("TITAN-2026-10-11.xlsx")]
    [InlineData("OTHER-2026-10-11.zip")]
    [InlineData("TITAN-2026-13-45.zip")]
    [InlineData("TITAN-2026-10-11-r0.zip")]
    [InlineData("my TITAN-2026-10-11.zip")]
    [InlineData(null)]
    public void Any_other_name_is_not_a_shop_zip(string? path) => Assert.False(ShopResultBuilder.TryParseShopZip(path, out _, out _, out _));

    // ----- writer -----

    [Fact]
    public void The_writer_names_the_file_by_role_day_and_sequence_and_leaves_no_temp_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "etp-shopresults-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = ShopResultBuilder.Build("HELIOS", Day, 2, "1.12.0.0", Produced, [Imported("a.xlsx")]);
            var path = new ShopResultWriter(folder).Write(result);

            Assert.Equal("HELIOS-2026-10-11-2.json", Path.GetFileName(path));
            Assert.Equal([path], Directory.GetFiles(folder));
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal("Accepted", json.RootElement.GetProperty("overall").GetString());
            Assert.Equal(2, json.RootElement.GetProperty("sequence").GetInt32());
            // Writing again (a re-import of the same zip name) replaces the file and still leaves one.
            new ShopResultWriter(folder).Write(result);
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_write_that_fails_returns_null_instead_of_throwing()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "etp-shopresults-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "x");
        try
        {
            var result = ShopResultBuilder.Build("TITAN", Day, 1, "1", Produced, [Imported("a.xlsx")]);
            Assert.Null(new ShopResultWriter(Path.Combine(blocker, "pending")).TryWrite(result));
        }
        finally { File.Delete(blocker); }
    }

    [Fact]
    public void A_result_holds_only_the_allowed_properties()
    {
        var result = ShopResultBuilder.Build("TITAN", Day, 1, "1", Produced, [Failed("a.xlsx", DailyInputsCodes.DayFinalised)]);
        var root = JsonNode.Parse(ShopResultBuilder.ToJson(result))!.AsObject();
        Assert.Equal(["schemaVersion", "role", "businessDate", "etpVersion", "producedUtc", "sequence", "overall", "files"], root.Select(p => p.Key));
        Assert.Equal(["name", "sha256", "verdict", "code", "message", "whatToDo"], root["files"]!.AsArray()[0]!.AsObject().Select(p => p.Key));
    }

    // ----- shared fixtures (cross-layer with Test-OneDriveScripts.ps1) -----

    private static string FixtureFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests-dotnet", "fixtures", "shop-results");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests-dotnet/fixtures/shop-results not found above " + AppContext.BaseDirectory);
    }

    private static ShopResultFile Row(string name, char sha, ShopResultVerdict verdict, string code, string message, string whatToDo) =>
        new(name, new string(sha, 64), verdict, code, message, whatToDo);

    public static IEnumerable<object[]> Fixtures() =>
    [
        ["TITAN-2026-10-11-1.json", new ShopResult("TITAN", Day, 1, "1.12.0.0", Produced,
        [
            Row("ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11.xlsx", 'A', ShopResultVerdict.Accepted, "", "", ""),
            Row("sales.xlsx", 'B', ShopResultVerdict.Duplicate, "", "ETP already has this exact file.", "")
        ])],
        ["HELIOS-2026-10-11-1.json", new ShopResult("HELIOS", Day, 1, "1.12.0.0", Produced,
        [
            Row("ETP-Daily-Inputs-WLMHW-RETAIL-2026-10-11.xlsx", 'C', ShopResultVerdict.Refused, DailyInputsCodes.DayFinalised,
                "This business day is finalised, so its manual entries cannot change.", "Reopen the day, then put the file in the watch folder again."),
            Row("stock.csv", 'D', ShopResultVerdict.Accepted, "", "", "")
        ])],
        ["SERVICE-2026-10-11-2.json", new ShopResult("SERVICE", Day, 2, "1.12.0.0", Produced,
        [
            Row("ETP-Daily-Inputs-HEMW-SERVICE-2026-10-10.xlsx", 'A', ShopResultVerdict.AcceptedWithWarning, DailyInputsCodes.NameDateDiffers,
                "The date in the file name differs from the date on the sheet, so the sheet was imported under its own date.",
                "Next time save the file under a name with the sheet's date.")
        ])]
    ];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_fixture_file_is_exactly_what_the_writer_produces(string fixture, ShopResult expected)
    {
        var fromFile = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureFolder(), fixture)));
        var fromCode = JsonNode.Parse(ShopResultBuilder.ToJson(expected));
        Assert.True(JsonNode.DeepEquals(fromFile, fromCode), $"{fixture} differs from ShopResultBuilder.ToJson. Regenerate the fixture from the builder output.");
        Assert.Equal(fixture, ShopResultWriter.FileNameFor(expected));
    }

    [Fact]
    public void The_fixtures_are_the_only_files_the_PowerShell_test_signs()
    {
        Assert.Equal(Fixtures().Select(f => (string)f[0]).Order(), Directory.GetFiles(FixtureFolder(), "*.json").Select(Path.GetFileName).Order());
    }
}
