using System.Text.RegularExpressions;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Tests;

public sealed class ServiceCentreCatalogueTests
{
    private static readonly string[] RowDateFamilies = ["S003", "S004", "S019", "S023", "S024", "S025", "S026", "S039", "S040"];
    private static IReadOnlyList<EtpReportFamily> Service => EtpReportFamilyRegistry.Families
        .Where(family => family.BusinessUnit == EtpReportFamily.ServiceBusinessUnit).ToArray();

    [Fact]
    public void Catalogue_holds_32_retail_and_39_service_families_with_unique_identities()
    {
        var families = EtpReportFamilyRegistry.Families;
        Assert.Equal(71, families.Count);
        Assert.Equal(32, families.Count(family => family.BusinessUnit == EtpReportFamily.RetailBusinessUnit));
        Assert.Equal(Enumerable.Range(1, 40).Where(i => i != 38).Select(i => $"S{i:000}"), Service.Select(family => family.FamilyCode));
        Assert.Equal(families.Count, families.Select(family => family.FamilyCode).Distinct().Count());
        Assert.Equal(families.Count, families.Select(family => family.ReportCode).Distinct().Count());
        Assert.Equal(families.Count, families.Select(family => family.TableName).Distinct().Count());
        Assert.DoesNotContain(families, family => family.FamilyCode == "S038");
        Assert.True(EtpReportFamilyRegistry.IsRetail("R025"));
        Assert.True(EtpReportFamilyRegistry.IsRetail("CLOSING_STOCK"));
        Assert.True(EtpReportFamilyRegistry.IsRetail("LEGACY_PROFILE"));
        Assert.False(EtpReportFamilyRegistry.IsRetail("S001"));
    }

    [Fact]
    public void Service_families_are_untyped_landing_families_with_safe_optional_columns()
    {
        foreach (var family in Service)
        {
            Assert.Equal(family.FamilyCode, family.ReportCode);
            Assert.False(family.IsTyped);
            Assert.Equal($"etp_landing_{family.FamilyCode.ToLowerInvariant()}", family.TableName);
            Assert.Equal(family.Headers, family.Columns.Select(column => column.SourceHeader));
            Assert.All(family.Columns, column =>
            {
                Assert.False(column.IsRequired);
                // One unparsable cell blocks a row, so boolean-like values stay Text.
                Assert.NotEqual(CanonicalDataType.Boolean, column.DataType);
                Assert.Matches("^[a-z0-9_]+$", column.CanonicalField);
                // Content identity ignores fields named like a timestamp; no Service field may be dropped by accident.
                Assert.DoesNotContain("timestamp", column.CanonicalField);
            });
            Assert.Equal(family.Columns.Count, family.Columns.Select(column => column.CanonicalField).Distinct().Count());
        }
    }

    [Fact]
    public void Only_immutable_money_and_claim_logs_take_their_period_from_a_row_date()
    {
        foreach (var family in Service)
        {
            if (!RowDateFamilies.Contains(family.FamilyCode))
            {
                Assert.Null(family.PrimaryDateHeader);
                continue;
            }
            var column = Assert.Single(family.Columns, column => column.SourceHeader == family.PrimaryDateHeader);
            Assert.Equal(CanonicalDataType.Date, column.DataType);
        }
    }

    [Fact]
    public void Exactly_the_exporting_centre_column_maps_to_store_code()
    {
        string[] endpoints = ["ToStoreCode", "SRNStoreCode", "PendingStore", "FROM STORE", "TO STORE", "FromStore", "ToStore",
            "SRNRepairStore", "Repair Location", "StoreSAPCode", "DealerSAPCode", "Invent Location ID From", "Invent Location ID To"];
        foreach (var family in Service)
        {
            var stores = family.Columns.Where(column => column.CanonicalField == "store_code").ToArray();
            Assert.Equal(family.FamilyCode is "S011" or "S013" ? 0 : 1, stores.Length);
            Assert.All(stores, column => Assert.Equal(CanonicalDataType.Identifier, column.DataType));
            Assert.DoesNotContain(stores, column => endpoints.Contains(column.SourceHeader, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Shared_signatures_are_exactly_the_known_view_groups_and_share_one_column_spec()
    {
        var groups = EtpReportFamilyRegistry.Families.GroupBy(family => ImportProfileMatcher.CreateHeaderSignature(family.Headers))
            .Where(group => group.Count() > 1 && group.Any(family => family.BusinessUnit == EtpReportFamily.ServiceBusinessUnit))
            .Select(group => string.Join(",", group.Select(family => family.FamilyCode).Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["S001,S014,S015,S016,S017,S018,S031,S032,S033,S034,S035", "S007,S008", "S024,S025,S026", "S039,S040"], groups);
        foreach (var group in EtpReportFamilyRegistry.Families.Where(family => family.BusinessUnit == EtpReportFamily.ServiceBusinessUnit)
            .GroupBy(family => ImportProfileMatcher.CreateHeaderSignature(family.Headers)))
        {
            var first = group.First();
            Assert.All(group, family => Assert.Equal(first.Columns, family.Columns));
            Assert.All(group, family => Assert.Equal(first.PrimaryDateHeader, family.PrimaryDateHeader));
        }
        Assert.NotEqual(ImportProfileMatcher.CreateHeaderSignature(EtpReportFamilyRegistry.Resolve("S036").Headers),
            ImportProfileMatcher.CreateHeaderSignature(EtpReportFamilyRegistry.Resolve("S037").Headers));
        // No Service layout may collide with a Retail one.
        var retail = EtpReportFamilyRegistry.Families.Where(family => family.BusinessUnit == EtpReportFamily.RetailBusinessUnit)
            .Select(family => ImportProfileMatcher.CreateHeaderSignature(family.Headers)).ToHashSet();
        Assert.DoesNotContain(Service, family => retail.Contains(ImportProfileMatcher.CreateHeaderSignature(family.Headers)));
    }

    [Fact]
    public void Migration_0038_creates_exactly_the_catalogue_tables_procedures_triggers_and_grants()
    {
        var sql = File.ReadAllText(Migration("0038_service_centre_family_tables.sql")).ReplaceLineEndings("\n");
        foreach (var family in Service)
        {
            var table = family.TableName;
            var columns = string.Join("\n", family.Columns.Select((column, index) =>
                $" [{column.CanonicalField}] {SqlType(column.DataType)} NULL" + (index < family.Columns.Count - 1 ? "," : "")));
            Assert.Contains($"IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL\nCREATE TABLE dbo.[{table}] (\n etp_row_id bigint IDENTITY PRIMARY KEY,\n" +
                " import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),\n" +
                " source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),\n" +
                $" content_key varchar(80) NOT NULL,\n{columns}\n);", sql);
            Assert.Contains($" CREATE INDEX IX_{table}_file ON dbo.[{table}](import_file_id);", sql);
            Assert.Contains($"CREATE OR ALTER TRIGGER dbo.trg_{table}_locked ON dbo.[{table}]", sql);
            var procedure = Regex.Match(sql, $@"EXEC\(N'CREATE OR ALTER PROCEDURE dbo\.append_{table}\n(?<body>.*?)\nEND'\);", RegexOptions.Singleline);
            Assert.True(procedure.Success, table);
            var body = procedure.Groups["body"].Value;
            // Parameters bind positionally in Columns order (PhaseOneImportPersistence.InsertFamilySourceAsync).
            var parameters = Regex.Matches(body, @"^ @v(\d+) ([a-z0-9(),]+?),?$", RegexOptions.Multiline);
            Assert.Equal(family.Columns.Count, parameters.Count);
            Assert.Equal(family.Columns.Select((column, index) => $"{index}:{SqlType(column.DataType)}"),
                parameters.Select(parameter => $"{parameter.Groups[1].Value}:{parameter.Groups[2].Value}"));
            Assert.Contains($"f.report_code=''{family.ReportCode}''", body);
            Assert.Contains($"INSERT dbo.[{table}](import_file_id,source_lineage_id,content_key,{string.Join(",", family.Columns.Select(c => $"[{c.CanonicalField}]"))})", body);
            Assert.Contains($"VALUES(@file,@lineage,@key,{string.Join(",", family.Columns.Select((_, index) => $"@v{index}"))});", body);
            Assert.Contains($"DENY INSERT,UPDATE,DELETE ON dbo.[{table}] TO etp_store_manager,etp_viewer;", sql);
            Assert.Contains($"GRANT EXECUTE ON dbo.append_{table} TO etp_store_manager,etp_owner;", sql);
        }
        Assert.Equal(Service.Count, Regex.Matches(sql, "CREATE TABLE ").Count);
        Assert.Equal(Service.Count, Regex.Matches(sql, "CREATE OR ALTER PROCEDURE ").Count);
        Assert.Equal(Service.Count, Regex.Matches(sql, "CREATE OR ALTER TRIGGER ").Count);
        Assert.DoesNotContain("etp_landing_s038", sql);
        Assert.DoesNotContain("dbo.stores", sql);
        Assert.DoesNotContain("import_profiles", sql);
        Assert.DoesNotContain("BEGIN TRANSACTION", sql);
    }

    [Fact]
    public void Migration_0038_guards_are_the_0018_trigger_and_0025_procedure_text()
    {
        var sql = File.ReadAllText(Migration("0038_service_centre_family_tables.sql")).ReplaceLineEndings("\n");
        var trigger = File.ReadAllText(Migration("0018_etp_family_tables.sql")).ReplaceLineEndings("\n").Split('\n')
            .Single(line => line.StartsWith("EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r001_locked", StringComparison.Ordinal));
        var procedure = File.ReadAllText(Migration("0025_phase_integration_write_boundary.sql")).ReplaceLineEndings("\n");
        var guards = Regex.Match(procedure, @"AS BEGIN\n.*?f\.report_code=''R001''\)\n.*?\n", RegexOptions.Singleline).Value;
        foreach (var family in Service)
        {
            Assert.Contains(trigger.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER").Replace("etp_landing_r001", family.TableName), sql);
            Assert.Contains(guards.Replace("''R001''", $"''{family.ReportCode}''"), sql);
        }
    }

    private static string SqlType(CanonicalDataType type) => type switch
    {
        CanonicalDataType.Text or CanonicalDataType.Identifier => "nvarchar(max)",
        CanonicalDataType.Decimal => "decimal(19,4)",
        CanonicalDataType.Date => "date",
        CanonicalDataType.Integer => "int",
        _ => throw new InvalidOperationException($"Service families never use {type}.")
    };

    private static string Migration(string name)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "database", "migrations", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("The migrations folder was not found above the test output.", name);
    }
}
