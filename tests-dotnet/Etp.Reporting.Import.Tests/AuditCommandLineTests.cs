using Etp.Reporting.Import.Audit;

namespace Etp.Reporting.Import.Tests;

public sealed class AuditCommandLineTests
{
    [Fact]
    public void No_arguments_is_a_usage_error_and_never_a_seed()
    {
        var result = AuditCommandLine.Parse([]);

        Assert.Null(result.Command);
        Assert.Equal(AuditExitCodes.Usage, result.ExitCode);
        Assert.Contains("Usage:", AuditCommandLine.Usage);
    }

    [Theory]
    [InlineData("inspect", AuditCommandKind.Inspect)]
    [InlineData("validate-contract", AuditCommandKind.ValidateContract)]
    [InlineData("--validate-contract", AuditCommandKind.ValidateContract)]
    public void Path_commands_parse_with_their_aliases(string verb, AuditCommandKind kind)
    {
        var result = AuditCommandLine.Parse([verb, @"C:\in\a.xlsx", @"C:\in\pack.zip", "--format", "json", "--detail", "documents",
            "--family", "r030,R022", "--store", "wlmhw", "--quiet", "--strict"]);

        var command = Assert.IsType<AuditCommand>(result.Command);
        Assert.Equal(kind, command.Kind);
        Assert.Equal([@"C:\in\a.xlsx", @"C:\in\pack.zip"], command.Paths);
        Assert.Equal(AuditFormat.Json, command.Format);
        Assert.Equal(AuditDetail.Documents, command.Detail);
        Assert.Equal(["R030", "R022"], command.Families);
        Assert.Equal("WLMHW", command.Store);
        Assert.True(command.Quiet);
        Assert.True(command.Strict);
        Assert.Null(result.Notice);
    }

    [Fact]
    public void Validate_contract_takes_raw_and_require_contract()
    {
        var command = AuditCommandLine.Parse(["validate-contract", "w.xlsx", "--raw", @"D:\raw", "--require-contract"]).Command!;

        Assert.Equal(@"D:\raw", command.RawFolder);
        Assert.True(command.RequireContract);
    }

    [Theory]
    [InlineData("check-import")]
    [InlineData("--check-import")]
    public void Check_import_and_its_alias_need_a_database(string verb)
    {
        var parsed = AuditCommandLine.Parse([verb, "held", "--database", "EtpReporting", "--planner", "1", "--expect", "e.json"]);
        var command = Assert.IsType<AuditCommand>(parsed.Command);
        Assert.Equal(AuditCommandKind.CheckImport, command.Kind);
        Assert.Equal("EtpReporting", command.Database);
        Assert.Equal(@".\SQLEXPRESS", command.Server);
        Assert.Equal("e.json", command.ExpectFile);

        Assert.Equal(AuditExitCodes.Usage, AuditCommandLine.Parse([verb, "held"]).ExitCode);
    }

    [Fact]
    public void Baseline_parses_with_compare_and_takes_no_paths()
    {
        var command = AuditCommandLine.Parse(["baseline", "--database", "EtpReporting", "--compare", "b.json"]).Command!;
        Assert.Equal(AuditCommandKind.Baseline, command.Kind);
        Assert.Equal("b.json", command.CompareFile);

        Assert.Equal(AuditExitCodes.Usage, AuditCommandLine.Parse(["baseline", "--database", "EtpReporting", "stray"]).ExitCode);
    }

    [Theory]
    [InlineData("inspect", "a.xlsx", "--bogus")]
    [InlineData("inspect", "a.xlsx", "--database", "EtpReporting")]
    [InlineData("inspect", "a.xlsx", "--format")]
    [InlineData("inspect", "a.xlsx", "--format", "xml")]
    [InlineData("inspect", "a.xlsx", "--detail", "1")]
    [InlineData("inspect", "a.xlsx", "--out", "a", "--out", "b")]
    [InlineData("check-import", "a.xlsx", "--database", "EtpReporting", "--state", "s.json")]
    [InlineData("check-import", "a.xlsx", "--database", "EtpReporting", "--planner", "2")]
    [InlineData("check-import", "a.xlsx", "--database", "EtpReporting", "--planner", "both")]
    [InlineData("check-import", "a.xlsx", "--database", "Etp;DROP")]
    [InlineData("dump-state", "--database", "EtpReporting")]
    [InlineData("preview", "a.xlsx")]
    [InlineData("frobnicate")]
    [InlineData("inspect")]
    public void Unknown_missing_or_conflicting_arguments_exit_2(params string[] args)
    {
        var result = AuditCommandLine.Parse(args);

        Assert.Null(result.Command);
        Assert.Equal(AuditExitCodes.Usage, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void State_with_database_is_refused()
    {
        var result = AuditCommandLine.Parse(["check-import", "a.xlsx", "--state", "s.json", "--database", "EtpReporting"]);

        Assert.Equal(AuditExitCodes.Usage, result.ExitCode);
    }

    [Fact]
    public void The_legacy_form_maps_to_seed_with_a_deprecation_notice()
    {
        var result = AuditCommandLine.Parse(["--database", "EtpReportingHelios", "--rebuild", "--folder", @"C:\src\HEMW"]);

        var command = Assert.IsType<AuditCommand>(result.Command);
        Assert.Equal(AuditCommandKind.Seed, command.Kind);
        Assert.Equal("EtpReportingHelios", command.Database);
        Assert.True(command.Rebuild);
        Assert.Equal([@"C:\src\HEMW"], command.Folders);
        Assert.Equal(AuditCommandLine.DeprecatedNotice, result.Notice);
    }

    [Fact]
    public void Seed_repeats_folders_and_takes_migrate_only()
    {
        var command = AuditCommandLine.Parse(["seed", "--database", "EtpAccept_P1", "--folder", "a", "--folder", "b"]).Command!;
        Assert.Equal(["a", "b"], command.Folders);

        Assert.True(AuditCommandLine.Parse(["seed", "--database", "EtpAccept_P1", "--migrate-only"]).Command!.MigrateOnly);
        Assert.Equal(AuditExitCodes.Usage, AuditCommandLine.Parse(["seed", "--database", "EtpAccept_P1", "--migrate-only", "--folder", "a"]).ExitCode);
    }

    [Theory]
    [InlineData("EtpReporting")]
    [InlineData("EtpAccept_P1;DROP")]
    [InlineData("EtpAcceptX")]
    [InlineData("EtpAccept_")]
    [InlineData("etpaccept_p1")]
    [InlineData("master")]
    public void Seed_refuses_every_non_scratch_name(string database)
    {
        Assert.False(AuditCommandLine.IsScratchDatabase(database));
        Assert.Equal(AuditExitCodes.Usage, AuditCommandLine.Parse(["seed", "--database", database]).ExitCode);
        Assert.Equal(AuditExitCodes.Usage, AuditCommandLine.Parse(["--database", database, "--rebuild"]).ExitCode);
    }

    [Theory]
    [InlineData("EtpAccept_P1")]
    [InlineData("EtpPhase1Test_X")]
    [InlineData("EtpReportingHelios")]
    public void Seed_accepts_scratch_names(string database)
    {
        Assert.True(AuditCommandLine.IsScratchDatabase(database));
        Assert.Equal(AuditCommandKind.Seed, AuditCommandLine.Parse(["seed", "--database", database]).Command!.Kind);
    }

    [Fact]
    public void Seed_without_a_database_is_refused_rather_than_defaulting()
    {
        Assert.Equal(AuditExitCodes.Usage, AuditCommandLine.Parse(["seed", "--folder", "a"]).ExitCode);
        Assert.Equal(AuditExitCodes.Usage, AuditCommandLine.Parse(["--folder", "a"]).ExitCode);
    }

    [Fact]
    public void A_refused_path_is_echoed_only_in_part()
    {
        var longPath = @"C:\Users\someone\Documents\" + new string('x', 80) + ".xlsx";

        var result = AuditCommandLine.Parse(["baseline", "--database", "EtpReporting", longPath]);

        Assert.DoesNotContain(longPath, result.Error);
    }
}
