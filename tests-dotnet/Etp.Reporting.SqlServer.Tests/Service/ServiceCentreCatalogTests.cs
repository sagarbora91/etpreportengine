using System.Text.RegularExpressions;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Stores;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// 1.12 wave 2, lane A4: stores, brands and the Service Centre are data. Migration 0067 (unit_kind, service_job_prefix,
/// THROW 51970-51973), the pure catalogue rules, the guarded catalogue reads, the catalogue-driven v_service_job, the
/// absence of the old store-code literal from A4's own files, and DB-06's cold and warm timing. The SQL behaviour is
/// IntegrationTests/ServiceCentreCatalogSqlTests (SQL suite).
/// </summary>
public sealed class ServiceCentreCatalogTests
{
    private static readonly ServiceCentre Fixture = new("AW330", "Service Centre AW330", "JOAW330");
    private static readonly ServiceCentre Second = new("SC999", "Synthetic Service Centre", "JOSC999");

    // ------------------------------------------------------------------ migration 0067

    private static string Migration0067() =>
        File.ReadAllText(Path.Combine(SchemaSourceText.MigrationsDirectory(), "0067_stores_service_as_data.sql")).Replace("\r\n", "\n");

    [Fact]
    public void Migration_0067_adds_unit_kind_and_the_job_prefix_with_their_checks()
    {
        var sql = Migration0067();
        Assert.Contains("ALTER TABLE dbo.business_units ADD unit_kind varchar(10) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("DEFAULT('RETAIL')", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE dbo.stores ADD service_job_prefix varchar(20) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK(unit_kind IN(''RETAIL'',''SERVICE''))", sql, StringComparison.Ordinal);
        Assert.Contains("COLLATE Latin1_General_BIN2 NOT LIKE ''%[^A-Z0-9]%''", sql, StringComparison.Ordinal);
        Assert.Contains("DATALENGTH(service_job_prefix) BETWEEN 2 AND 20", sql, StringComparison.Ordinal);
        // The seeded unit becomes SERVICE and its stores get JO + code: no store code is written in the migration.
        Assert.Contains("SET unit_kind=''SERVICE'' WHERE business_unit_code=''SERVICE''", sql, StringComparison.Ordinal);
        Assert.Contains("SET service_job_prefix=''JO''+s.store_code", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AW330", sql, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN TRAN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_0067_throws_only_inside_the_A4_block()
    {
        var numbers = Regex.Matches(Migration0067(), @"THROW\s+(\d+)").Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal([51970, 51971, 51972, 51973], numbers.Order());
        Assert.All(numbers, number => Assert.InRange(number, 51970, 51979));
        // The store editor's own refusals take the next numbers of the same block.
        Assert.Contains("THROW 51974", StoreCatalogRepository.SaveServiceCentreSql, StringComparison.Ordinal);
        Assert.Contains("THROW 51975", StoreCatalogRepository.SaveServiceCentreSql, StringComparison.Ordinal);
    }

    [Fact]
    public void No_migration_from_0067_on_inserts_stores_business_units_or_brand_rows()
    {
        // From 0067, stores, business units, brand rows and brand row codes are added in Settings, never by a migration.
        var insert = new Regex(@"\b(INSERT\s+(INTO\s+)?|MERGE\s+(INTO\s+)?)(\[?dbo\]?\.)?\[?(stores|business_units|brand_rows|brand_row_codes|brands)\]?(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var offenders = Directory.EnumerateFiles(SchemaSourceText.MigrationsDirectory(), "*.sql")
            .Select(path => Path.GetFileName(path))
            .Where(name => int.TryParse(name[..4], out var number) && number >= 67)
            .Where(name => insert.IsMatch(File.ReadAllText(Path.Combine(SchemaSourceText.MigrationsDirectory(), name))))
            .ToArray();
        Assert.Empty(offenders);
        // The guard sees the forms a migration would use.
        Assert.Matches(insert, "INSERT dbo.stores(store_code) VALUES('X')");
        Assert.Matches(insert, "EXEC(N'INSERT INTO dbo.brand_rows(x) VALUES(1)')");
        Assert.Matches(insert, "MERGE dbo.business_units WITH(HOLDLOCK) t USING");
        Assert.DoesNotMatch(insert, "INSERT dbo.store_targets(store_code) VALUES('X')");
    }

    // ------------------------------------------------------------------ catalogue rules (pure)

    [Fact]
    public void The_job_number_pattern_comes_from_the_prefix()
    {
        Assert.Equal("^JOSC999[0-9]{8,9}$", Second.JobNumberPattern);
        Assert.True(ServiceCentreRules.IsWellFormedJobNumber([Fixture, Second], "JOSC999252600001"));
        Assert.True(ServiceCentreRules.IsWellFormedJobNumber([Fixture, Second], " JOAW33000001234 "));
        Assert.False(ServiceCentreRules.IsWellFormedJobNumber([Fixture], "JOSC999252600001"));
        Assert.False(ServiceCentreRules.IsWellFormedJobNumber([Second], "JOSC999SYN0001"));
        Assert.False(ServiceCentreRules.IsWellFormedJobNumber([new("BAD", "Bad", "jo-1")], "jo-112345678"));
    }

    [Fact]
    public void A_job_belongs_to_the_centre_with_the_longest_matching_prefix()
    {
        var shortPrefix = new ServiceCentre("SC9", "Short", "JOSC9");
        Assert.Equal("SC999", ServiceCentreRules.CentreForJob([shortPrefix, Second], "JOSC999000000001")!.StoreCode);
        Assert.Equal("SC9", ServiceCentreRules.CentreForJob([shortPrefix, Second], "JOSC9100000001")!.StoreCode);
        Assert.Null(ServiceCentreRules.CentreForJob([Second], "JOAW330252600001"));
        Assert.Null(ServiceCentreRules.CentreForJob([Second], null));
    }

    [Fact]
    public void A_file_with_no_store_goes_to_the_only_centre_and_is_refused_otherwise()
    {
        Assert.Equal("SC999", ServiceCentreRules.DefaultFor([Second]).StoreCode);
        var none = ServiceCentreRules.DefaultFor([]);
        Assert.True(none.IsRefused);
        Assert.Equal(ServiceCentreRules.StoreNotSetUpCode, none.RefusalCode);
        var two = ServiceCentreRules.DefaultFor([Fixture, Second]);
        Assert.True(two.IsRefused);
        Assert.Equal(ServiceCentreRules.StoreAmbiguousCode, two.RefusalCode);
        Assert.Equal(ServiceCentreRules.StoreAmbiguousMessage, two.RefusalMessage);
        foreach (var message in new[] { ServiceCentreRules.StoreAmbiguousMessage, ServiceCentreRules.StoreNotSetUpMessage, ServiceCentreRules.StoreDefaultedMessage })
        {
            Assert.True(message.Length <= 160, message);
            Assert.DoesNotMatch(@"\b[A-Z]{2}\d{3}\b", message); // no store code in a sentence
        }
    }

    [Theory]
    [InlineData("JOSC999", true)]
    [InlineData("JO", true)]
    [InlineData("J", false)]
    [InlineData("josc999", false)]
    [InlineData("JO-SC999", false)]
    [InlineData("JO SC999", false)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", false)] // 21 characters
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_prefix_is_two_to_twenty_capital_letters_or_digits(string? prefix, bool valid) =>
        Assert.Equal(valid, ServiceCentreRules.IsValidPrefix(prefix));

    [Fact]
    public void The_store_editor_refuses_a_bad_or_overlapping_prefix_in_plain_words()
    {
        Assert.Null(ServiceCentreRules.PrefixProblem("JOSC999", [Fixture]));
        Assert.Contains("Enter the job-number prefix", ServiceCentreRules.PrefixProblem(" ", [Fixture])!, StringComparison.Ordinal);
        Assert.Contains("capital letters A-Z and digits only", ServiceCentreRules.PrefixProblem("jo-1", [Fixture])!, StringComparison.Ordinal);
        Assert.Contains("overlaps the prefix of Service Centre AW330", ServiceCentreRules.PrefixProblem("JOAW3", [Fixture])!, StringComparison.Ordinal);
        Assert.Contains("overlaps", ServiceCentreRules.PrefixProblem("JOAW3301", [Fixture])!, StringComparison.Ordinal);
        Assert.Equal("JOSC999", ServiceCentreRules.SuggestedPrefix(" sc999 "));
        Assert.Equal("", ServiceCentreRules.SuggestedPrefix(""));
    }

    [Fact]
    public void Screens_name_the_centre_from_the_catalogue()
    {
        Assert.Equal("Service Centre", ServiceCentreRules.ScreenLabel(null));
        Assert.Equal("Service Centre", ServiceCentreRules.ScreenLabel([]));
        Assert.Equal("Synthetic Service Centre", ServiceCentreRules.ScreenLabel([Second]));
        Assert.Equal("Service Centre SC999", ServiceCentreRules.ScreenLabel([Second with { Name = " " }]));
        Assert.Equal("All Service Centres", ServiceCentreRules.ScreenLabel([Fixture, Second]));
    }

    [Fact]
    public async Task The_fixed_catalogue_returns_its_list()
    {
        Assert.Equal(new[] { Fixture, Second }, await new FixedServiceCentreCatalog([Fixture, Second]).LoadAsync());
        Assert.Empty(await new FixedServiceCentreCatalog([]).LoadAsync());
    }

    // ------------------------------------------------------------------ guarded catalogue reads

    [Fact]
    public void The_catalogue_reads_are_guarded_for_a_database_before_0067()
    {
        foreach (var sql in new[] { SqlServerServiceCentreCatalog.LoadSql, StoreCatalogRepository.LoadSql })
        {
            Assert.Contains("COL_LENGTH(N'dbo.stores', N'service_job_prefix') IS NULL", sql, StringComparison.Ordinal);
            Assert.Contains("COL_LENGTH(N'dbo.business_units', N'unit_kind') IS NULL", sql, StringComparison.Ordinal);
            // The 0067 columns are read only in the dynamic statement, which compiles where they exist.
            var direct = sql[..sql.LastIndexOf("EXEC sp_executesql", StringComparison.Ordinal)];
            Assert.DoesNotContain("s.service_job_prefix", direct, StringComparison.Ordinal);
            Assert.DoesNotContain("u.unit_kind", direct, StringComparison.Ordinal);
            // No existence check is ANDed with a read of the object (AND does not short-circuit).
            Assert.DoesNotMatch(@"IS NOT NULL\s+AND\s*\(SELECT", sql);
        }
        Assert.Contains("u.business_unit_code='SERVICE'", SqlServerServiceCentreCatalog.LoadSql, StringComparison.Ordinal);
        Assert.Contains("u.unit_kind=''SERVICE''", SqlServerServiceCentreCatalog.LoadSql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_store_of_a_SERVICE_kind_unit_is_a_service_centre_whatever_its_unit_code()
    {
        Assert.True(new StoreCatalogEntry("SC999", "Synthetic Service Centre", false, "OTHERUNIT", UnitKind: "SERVICE").IsServiceCentre);
        Assert.True(new StoreCatalogEntry("AW330", "Service Centre AW330", false, "SERVICE").IsServiceCentre);
        Assert.False(new StoreCatalogEntry("WLMHW", "Titan World", true, null, UnitKind: "RETAIL").IsServiceCentre);
        Assert.False(new StoreCatalogEntry("HEMW", "Helios", true).IsServiceCentre);
    }

    [Fact]
    public void The_service_centre_save_is_one_audited_transaction_that_keeps_the_store_inactive()
    {
        var sql = StoreCatalogRepository.SaveServiceCentreSql;
        Assert.Contains("SET XACT_ABORT ON;", sql, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", sql, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES(@code,@name,@unit,0,@prefix,", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("is_active=", sql[sql.IndexOf("WHEN MATCHED", StringComparison.Ordinal)..sql.IndexOf("WHEN NOT MATCHED", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("record_operational_audit 'MasterDataChange'", sql, StringComparison.Ordinal);
        Assert.Contains("role_code='OWNER'", StoreCatalogRepository.OwnerCheckSql, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ v_service_job and the literal

    [Fact]
    public void V_service_job_takes_the_service_centre_from_the_catalogue()
    {
        var view = SchemaSourceText.Current("dbo.v_service_job").Replace("\r\n", "\n");
        Assert.DoesNotContain("AW330", view, StringComparison.Ordinal);
        // Review m-1: a Service Centre without a saved prefix uses 'JO'+store code, as the C# catalogue suggests.
        Assert.Contains("COALESCE(s.service_job_prefix,CONVERT(varchar(20),'JO'+UPPER(LTRIM(RTRIM(s.store_code))))) service_job_prefix", view, StringComparison.Ordinal);
        Assert.Contains("WHERE u.unit_kind='SERVICE'\n), only_centre AS (", view, StringComparison.Ordinal);
        Assert.Contains("a.job_order_number LIKE CONVERT(nvarchar(20),c.service_job_prefix)+N'%'", view, StringComparison.Ordinal);
        Assert.Contains("ORDER BY LEN(c.service_job_prefix) DESC", view, StringComparison.Ordinal);
        Assert.Contains("ELSE jc.store_code END pending_at", view, StringComparison.Ordinal);
        Assert.Contains("COALESCE(a.st9_s010_store,N'')<>COALESCE(jc.store_code,N'') THEN 'IN_TRANSIT_BACK'", view, StringComparison.Ordinal);
        Assert.Contains("jc.store_code service_store_code", view, StringComparison.Ordinal);
        // The final select keeps its shape: one aggregate, no LEFT JOIN, the snapshot cross join first.
        var finalSelect = view[view.LastIndexOf("\nSELECT a.job_order_number", StringComparison.Ordinal)..];
        Assert.DoesNotContain("LEFT JOIN", finalSelect, StringComparison.Ordinal);
        Assert.Contains("FROM agg a\nCROSS JOIN snap\nCROSS JOIN only_centre oc\nOUTER APPLY", finalSelect, StringComparison.Ordinal);
    }

    [Fact]
    public void No_service_object_file_names_a_store_code()
    {
        var files = Directory.EnumerateFiles(SchemaSourceText.ObjectsDirectory(), "*v_service_*.sql").ToArray();
        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.DoesNotContain("AW330", File.ReadAllText(file), StringComparison.Ordinal));
    }

    [Fact]
    public void A4s_own_source_files_hold_no_service_centre_store_code()
    {
        // The last-to-merge lane (U2) owns the repository-wide test; this one covers A4's files only.
        string[] files =
        [
            "src/Etp.Reporting.Application/Service/ServiceMoneyCheck.cs",
            "src/Etp.Reporting.Application/Service/ServiceUiRules.cs",
            "src/Etp.Reporting.Application/Stores/IServiceCentreCatalog.cs",
            "src/Etp.Reporting.Import/Service/ServiceInterimFamilies.cs",
            "src/Etp.Reporting.Import/Preflight/ImportScope.cs",
            "src/Etp.Reporting.Infrastructure.SqlServer/StoreCatalogRepository.cs",
            "src/Etp.Reporting.Infrastructure.SqlServer/FolderImportService.cs",
            "src/Etp.Reporting.Infrastructure.SqlServer/HealthCheck/L9Probes.cs",
            "src/Etp.Reporting.Desktop/StoreScopeCatalog.cs",
            "src/Etp.Reporting.Desktop/Composition/DesktopCompositionRoot.Service.cs",
            "src/Etp.Reporting.Desktop/Modules/OperationsAdministration/AdministrationWorkspaceView.xaml",
            "src/Etp.Reporting.Desktop/Modules/OperationsAdministration/AdministrationWorkspaceView.xaml.cs",
            "src/Etp.Reporting.Desktop/Modules/OperationsAdministration/AdministrationDrafts.cs",
            "src/Etp.Reporting.Desktop/Modules/OperationsAdministration/OperationsAdministrationPresentationSession.cs",
        ];
        var root = SchemaSourceText.RepositoryRoot();
        var service = Directory.EnumerateFiles(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Service"), "*.cs")
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));
        var offenders = files.Concat(service)
            .Where(relative => File.ReadAllText(Path.Combine(root, relative)).Contains("AW330", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(offenders);
    }

    // ------------------------------------------------------------------ DB-06 cold and warm

    [Fact]
    public void DB06_counts_six_service_views_including_job_history()
    {
        Assert.Equal(["JOBS", "PENDING", "STATUS", "CLAIMS", "PARTS", "JOB_HISTORY"], ServiceScreenTimingProbe.Views.Select(view => view.Key));
        Assert.Equal("dbo.v_service_job_list_events", ServiceScreenTimingProbe.Views.Single(view => view.Key == "JOB_HISTORY").View);
    }

    [Fact]
    public void DB06_a_slow_first_read_with_a_fast_repeat_read_is_a_pass_with_both_numbers()
    {
        var result = ServiceScreenTimingProbe.Evaluate(
            [("JOBS", TimeSpan.FromMilliseconds(2700), TimeSpan.FromMilliseconds(180)), ("PARTS", TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(60))],
            TimeSpan.Zero);
        Assert.Equal(HealthOutcome.Pass, result.Outcome);
        Assert.Equal(180m, result.MetricValue);
        Assert.Contains("first read after start took 2700 ms (compile), repeat read 180 ms", result.Explanation, StringComparison.Ordinal);
        var jobs = Assert.Single(result.SubResults!, sub => sub.Key == "JOBS");
        Assert.Equal(HealthOutcome.Pass, jobs.Outcome);
        Assert.Equal("2700", jobs.Fields![ServiceScreenTimingProbe.ColdField]);
        Assert.Equal("180", jobs.Fields[ServiceScreenTimingProbe.WarmField]);
        Assert.Equal(180m, jobs.MetricValue);
    }

    [Fact]
    public void DB06_warns_only_when_the_repeat_read_is_slow()
    {
        var result = ServiceScreenTimingProbe.Evaluate(
            [("JOBS", TimeSpan.FromMilliseconds(3000), TimeSpan.FromMilliseconds(1500)), ("CLAIMS", TimeSpan.FromMilliseconds(90), TimeSpan.FromMilliseconds(40)),
             ("JOB_HISTORY", null, null)],
            TimeSpan.Zero);
        Assert.Equal(HealthOutcome.Warning, result.Outcome);
        Assert.Equal("SERVICE_VIEW_SLOW", result.FindingCode);
        Assert.Equal(1500m, result.MetricValue);
        Assert.Contains("on a repeat read", result.Explanation, StringComparison.Ordinal);
        Assert.Contains("first read after start took 3000 ms (compile), repeat read 1500 ms", result.Explanation, StringComparison.Ordinal);
        Assert.Equal(HealthOutcome.Warning, Assert.Single(result.SubResults!, sub => sub.Key == "JOBS").Outcome);
        Assert.Equal(HealthOutcome.Skipped, Assert.Single(result.SubResults!, sub => sub.Key == "JOB_HISTORY").Outcome);
        Assert.Equal(HealthOutcome.Skipped, ServiceScreenTimingProbe.Evaluate([("JOBS", null, null)], TimeSpan.Zero).Outcome);
    }

    [Fact]
    public void DB06_sub_result_fields_are_tokens_the_run_store_keeps()
    {
        var result = ServiceScreenTimingProbe.Evaluate([("JOBS", TimeSpan.FromMilliseconds(1234.4), TimeSpan.FromMilliseconds(56.6))], TimeSpan.Zero);
        var sub = HealthCheckRunner.NormaliseSubResult(Assert.Single(result.SubResults!));
        Assert.Equal("1234", sub.Fields![ServiceScreenTimingProbe.ColdField]);
        Assert.Equal("57", sub.Fields[ServiceScreenTimingProbe.WarmField]);
    }
}
