using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>
/// The Service Centre entries of the catalogue (Service interim S-2, decision 15, 3 Oct 2026; SERVICE-INTERIM-DESIGN.md
/// sections 3 and 4). Names and types are frozen to scripts/service-centre/families.spec.json; roles and identity come
/// from scripts/propose-etp-catalogue-roles.ps1, which EtpReportFamilyCatalogueTests runs with -Check.
/// </summary>
public sealed class ServiceCatalogueTests
{
    private static readonly string[] ServiceCodes = [.. Enumerable.Range(1, 41).Where(n => n != 38).Select(n => $"S{n:000}")];

    // Mirrors ServiceInterimFamilies.Importable (lane L0; S041 GPRC CLAIM added by decision 16): the 36 families with a
    // landing table in 0048.
    private static readonly string[] Importable = [.. ServiceCodes.Except(["S001", "S005", "S027", "S028"])];

    private static readonly string[] ConsolidationColumns = ["SourcePeriodFrom", "SourcePeriodTo", "SourceFile"];

    // Columns that name another place (an endpoint) and must never be the exporting centre's store_code.
    private static readonly string[] Endpoints =
    [
        "ToStoreCode", "SRNStoreCode", "PendingStore", "FROM STORE", "TO STORE", "FromStore", "ToStore", "SRNRepairStore",
        "Repair Location", "StoreSAPCode", "DealerSAPCode", "Invent Location ID From", "Invent Location ID To", "TO_LOCATION",
        "To Location", "ToLocation", "FROM_LOCATION", "Purchase Location", "BrandStore"
    ];

    // RulesetVersion and the fingerprint of the roles and identity it was pinned with (as EtpReportFamilyCatalogueTests).
    private static readonly Dictionary<string, (int Version, string Fingerprint)> Rulesets = new(StringComparer.Ordinal)
    {
        ["S001"] = (1, "091c5461d487"), ["S002"] = (1, "1bdb0dc16ea3"), ["S003"] = (1, "9e2c59fe6978"),
        ["S004"] = (1, "b24392e75863"), ["S005"] = (1, "4675ebcf51af"), ["S006"] = (1, "7f2a23709ce4"),
        ["S007"] = (1, "d292cfbf662e"), ["S008"] = (1, "d292cfbf662e"), ["S009"] = (1, "2311bf91e6fb"),
        ["S010"] = (1, "287fd33df05a"), ["S011"] = (1, "f8d6cc329c94"), ["S012"] = (1, "46bd3181957b"),
        ["S013"] = (1, "d19c9d353fb0"), ["S014"] = (1, "091c5461d487"), ["S015"] = (1, "091c5461d487"),
        ["S016"] = (1, "091c5461d487"), ["S017"] = (1, "091c5461d487"), ["S018"] = (1, "091c5461d487"),
        ["S019"] = (1, "5a8f829d2e63"), ["S020"] = (1, "b35a6c618bb3"), ["S021"] = (1, "301a7c5f522d"),
        ["S022"] = (1, "b56ed9f80eb8"), ["S023"] = (1, "7b963a3fb06c"), ["S024"] = (1, "fde7b51791b2"),
        ["S025"] = (1, "fde7b51791b2"), ["S026"] = (1, "fde7b51791b2"), ["S027"] = (1, "ee9359ee8f9c"),
        ["S028"] = (1, "cd7a9e13b661"), ["S029"] = (1, "d2a05582a588"), ["S030"] = (1, "e0c6464ed6d1"),
        ["S031"] = (1, "091c5461d487"), ["S032"] = (1, "091c5461d487"), ["S033"] = (1, "091c5461d487"),
        ["S034"] = (1, "091c5461d487"), ["S035"] = (1, "091c5461d487"), ["S036"] = (1, "1feac6ec9629"),
        ["S037"] = (1, "25bf70cb0989"), ["S039"] = (1, "f55f619a335d"), ["S040"] = (1, "f55f619a335d"),
        ["S041"] = (1, "92659efbc1d8")
    };

    // SHA-256 of EtpReportFamilies.json at v1.9.3 (6a55913) up to the end of the SOR_AGEING entry, its last Retail entry.
    private const string RetailCatalogueSha256 = "a174e4fc99abb3117361f727fdd79ddf6761d281224ae8c83f86444474b7b0a3";

    private static IReadOnlyList<EtpReportFamily> Service =>
        EtpReportFamilyRegistry.Families.Where(family => family.BusinessUnit == BusinessUnit.Service).ToArray();

    [Fact]
    public void The_catalogue_holds_40_service_entries_appended_after_the_retail_ones()
    {
        Assert.Equal(ServiceCodes, Service.Select(family => family.FamilyCode));
        Assert.DoesNotContain(EtpReportFamilyRegistry.Families, family => family.FamilyCode == "S038");
        // Appended after SOR_AGEING: every Retail entry comes first.
        var units = EtpReportFamilyRegistry.Families.Select(family => family.BusinessUnit).ToArray();
        Assert.Equal(units.Order(), units);
        Assert.Equal(["S001"], Service.Where(family => family.Derived).Select(family => family.FamilyCode).ToArray());
        Assert.Equal(36, Importable.Length);
        Assert.All(Importable, code => Assert.False(EtpReportFamilyRegistry.Resolve(code).Derived));
        Assert.All(Service, family =>
        {
            Assert.Equal(family.FamilyCode, family.ReportCode);
            Assert.False(family.IsTyped);
            Assert.Equal($"etp_landing_{family.FamilyCode.ToLowerInvariant()}", family.TableName);
            Assert.Equal(family.Headers, family.Columns.Select(column => column.SourceHeader));
            Assert.All(family.Columns, column =>
            {
                Assert.False(column.IsRequired);
                Assert.Matches("^[a-z0-9_]+$", column.CanonicalField);
            });
            Assert.Equal(family.Columns.Count, family.Columns.Select(column => column.CanonicalField).Distinct().Count());
        });
        var families = EtpReportFamilyRegistry.Families;
        Assert.Equal(families.Count, families.Select(family => family.TableName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(families.Count, families.Select(family => family.FamilyCode).Distinct().Count());
    }

    [Fact]
    public void Retail_entries_are_byte_identical_to_v1_9_3()
    {
        using var stream = typeof(EtpReportFamilyRegistry).Assembly.GetManifestResourceStream(
            "Etp.Reporting.Import.Profiles.EtpReportFamilies.json")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var marker = Encoding.UTF8.GetBytes(",\n  {\n    \"FamilyCode\": \"S001\"");
        var end = bytes.AsSpan().IndexOf(marker);
        Assert.True(end > 0, "The S entries must follow the Retail entries, starting with S001.");
        Assert.Equal(RetailCatalogueSha256, Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, end))));
    }

    [Fact]
    public void Every_service_column_states_its_role_and_every_entry_its_service_fields()
    {
        using var stream = typeof(EtpReportFamilyRegistry).Assembly.GetManifestResourceStream(
            "Etp.Reporting.Import.Profiles.EtpReportFamilies.json")!;
        using var json = JsonDocument.Parse(stream);
        var entries = json.RootElement.EnumerateArray()
            .Where(family => ServiceCodes.Contains(family.GetProperty("FamilyCode").GetString())).ToArray();
        Assert.Equal(ServiceCodes.Length, entries.Length);
        foreach (var family in entries)
        {
            var code = family.GetProperty("FamilyCode").GetString();
            foreach (var column in family.GetProperty("Columns").EnumerateArray())
                Assert.True(column.TryGetProperty("Role", out var role) && Enum.TryParse<ColumnRole>(role.GetString(), out _),
                    $"{code}.{column.GetProperty("CanonicalField")} has no role.");
            // The enum spelling, not a case-insensitive "SERVICE" (review C1(a)).
            Assert.Equal("Service", family.GetProperty("BusinessUnit").GetString());
            foreach (var property in new[] { "Identity", "Derived", "ConsolidationColumns", "RawNamePatterns" })
                Assert.True(family.TryGetProperty(property, out _), $"{code} has no {property}.");
        }
    }

    [Fact]
    public void Names_and_types_are_the_frozen_families_spec()
    {
        // L2 builds the landing tables and L4 the views against these names; a wrong type is recorded, never fixed here.
        using var stream = File.OpenRead(Path.Combine(RepositoryRoot(), "scripts", "service-centre", "families.spec.json"));
        using var spec = JsonDocument.Parse(stream);
        var frozen = spec.RootElement.EnumerateArray().ToDictionary(family => family.GetProperty("FamilyCode").GetString()!);
        Assert.Equal(ServiceCodes, frozen.Keys.Order(StringComparer.Ordinal));
        foreach (var family in Service)
        {
            var entry = frozen[family.FamilyCode];
            Assert.Equal(entry.GetProperty("Name").GetString(), family.Name);
            var columns = entry.GetProperty("Columns").EnumerateArray().Select(column =>
                $"{column.GetProperty("SourceHeader").GetString()}|{column.GetProperty("CanonicalField").GetString()}|" +
                column.GetProperty("DataType").GetString()).ToArray();
            var headers = entry.GetProperty("Headers").EnumerateArray().Select(header => header.GetString()!).ToArray();
            if (family.FamilyCode is "S027" or "S028")
            {
                // The spec holds the consolidated header; the catalogue holds the raw one (review M2).
                Assert.Equal(ConsolidationColumns, headers[^3..]);
                (columns, headers) = (columns[..^3], headers[..^3]);
            }
            Assert.Equal(headers, family.Headers);
            Assert.Equal(columns, family.Columns.Select(column => $"{column.SourceHeader}|{column.CanonicalField}|{column.DataType}"));
        }
    }

    [Fact]
    public void Every_service_family_is_a_dated_snapshot_in_the_interim()
    {
        // Planner 1 treats a family without a row date as one dated snapshot per file (design section 4).
        Assert.All(Service, family =>
        {
            Assert.Null(family.PrimaryDateHeader);
            var identity = family.Identity!;
            Assert.Equal(DocumentScope.Snapshot, identity.Scope);
            Assert.Equal("Block", identity.SnapshotDate);
            Assert.True(identity.SnapshotDateFromBlock);
            Assert.Equal(family.FamilyCode == "S006" ? RowRule.SnapshotItems : RowRule.Multiset, identity.RowRule);
            Assert.Equal(ChangePolicy.LatestReadingWins, identity.ChangePolicy);
            Assert.Equal(FamilyRoute.Landing, identity.Route);
            Assert.False(identity.HasTypedFacts);
            Assert.Equal(YearRule.None, identity.YearRule);
            Assert.Empty(identity.DocumentKey);
            Assert.Empty(identity.RowKey);
            Assert.Empty(identity.LegacyNullable);
            Assert.Equal(1, identity.RulesetVersion);
        });
    }

    [Fact]
    public void S027_and_S028_carry_the_raw_header_and_name_the_consolidation_columns()
    {
        foreach (var code in new[] { "S027", "S028" })
        {
            var family = EtpReportFamilyRegistry.Resolve(code);
            Assert.Equal(ConsolidationColumns, family.ConsolidationColumns);
            Assert.DoesNotContain(family.Headers, header => ConsolidationColumns.Contains(header, StringComparer.OrdinalIgnoreCase));
        }
        Assert.All(Service.Where(family => family.FamilyCode is not ("S027" or "S028")), family => Assert.Empty(family.ConsolidationColumns));
    }

    [Fact]
    public void Exactly_the_exporting_centre_column_maps_to_store_code()
    {
        Assert.All(Service, family =>
        {
            var stores = family.Columns.Where(column => column.CanonicalField == "store_code").ToArray();
            Assert.Equal(family.FamilyCode is "S011" or "S013" ? 0 : 1, stores.Length);
            Assert.All(stores, column =>
            {
                Assert.Equal(CanonicalDataType.Identifier, column.DataType);
                Assert.Equal(ColumnRole.Key, column.Role);
                Assert.DoesNotContain(column.SourceHeader, Endpoints, StringComparer.OrdinalIgnoreCase);
            });
        });
    }

    [Fact]
    public void No_descriptive_or_customer_column_is_ever_a_key()
    {
        Assert.All(Service, family =>
        {
            // store_code is the only key in the interim; the identity names no field of its own.
            Assert.Equal(family.Columns.Where(column => column.CanonicalField == "store_code").Select(column => column.CanonicalField),
                family.ColumnsWithRole(ColumnRole.Key).Select(column => column.CanonicalField));
            Assert.Empty(family.Identity!.NamedFields());
            Assert.All(family.Columns.Where(column => IsCustomerColumn(column.CanonicalField)),
                column => Assert.True(column.Role == ColumnRole.Descriptive, $"{family.FamilyCode}.{column.CanonicalField} is {column.Role}."));
            // The interim has no timestamp column; if one appears it is Ignored, never Descriptive.
            Assert.All(family.Columns, column => Assert.Equal(
                column.CanonicalField.Contains("timestamp", StringComparison.Ordinal), column.Role == ColumnRole.Ignored));
            Assert.DoesNotContain(family.Columns, column => column.Role == ColumnRole.Label);
        });
        // Not vacuous: the 83-column views hold customer, mobile, e-mail and loyalty columns, and money stays a fact.
        var view = EtpReportFamilyRegistry.Resolve("S018");
        foreach (var field in new[] { "customername", "mobilenumber", "email", "endcustomername", "endcustomercontactnumber", "encirclenumber" })
            Assert.Equal(ColumnRole.Descriptive, Role(view, field));
        foreach (var field in new[] { "sparevalue", "labourcharge", "advanceamount", "radcdeductioncharge", "empowermentvalue" })
            Assert.Equal(ColumnRole.Fact, Role(view, field));
        Assert.Equal(ColumnRole.Attribute, Role(view, "repairstatus"));
        Assert.Equal(ColumnRole.Attribute, Role(view, "deliverydate"));
        Assert.Equal(ColumnRole.Descriptive, Role(EtpReportFamilyRegistry.Resolve("S003"), "cust_name"));
        Assert.Equal(ColumnRole.Descriptive, Role(EtpReportFamilyRegistry.Resolve("S029"), "landline_no"));
        Assert.Equal(ColumnRole.Fact, Role(EtpReportFamilyRegistry.Resolve("S004"), "totalamount"));
    }

    [Fact]
    public void Read_rule_dates_are_facts_and_job_progress_dates_are_attributes()
    {
        string[][] dateLogs =
        [
            ["S003", "trans_date"], ["S004", "billingdate"], ["S007", "grn_date"], ["S008", "grn_date"], ["S013", "stm_date"],
            ["S019", "repairdate"], ["S022", "invoice_date"], ["S023", "transdate"], ["S029", "repair_date"], ["S039", "transaction_date"],
            ["S041", "transaction_date"]
        ];
        foreach (var pair in dateLogs) Assert.Equal(ColumnRole.Fact, Role(EtpReportFamilyRegistry.Resolve(pair[0]), pair[1]));
        Assert.Equal(ColumnRole.Fact, Role(EtpReportFamilyRegistry.Resolve("S009"), "jodate"));
        Assert.Equal(ColumnRole.Attribute, Role(EtpReportFamilyRegistry.Resolve("S009"), "edd"));
        Assert.Equal(ColumnRole.Attribute, Role(EtpReportFamilyRegistry.Resolve("S009"), "jostatus"));
        Assert.Equal(ColumnRole.Attribute, Role(EtpReportFamilyRegistry.Resolve("S007"), "invoice_date"));
    }

    [Fact]
    public void Ruleset_version_rises_with_every_role_or_identity_change()
    {
        Assert.Equal(ServiceCodes, Rulesets.Keys.Order(StringComparer.Ordinal));
        Assert.All(Service, family =>
        {
            var (version, fingerprint) = Rulesets[family.FamilyCode];
            var actual = RulesFingerprint(family);
            Assert.True(actual == fingerprint,
                $"The roles or identity of {family.FamilyCode} no longer match fingerprint {fingerprint}, pinned with RulesetVersion " +
                $"{version}. Raise RulesetVersion in EtpReportFamilies.json, then pin the new version with fingerprint {actual}.");
            Assert.Equal(version, family.Identity!.RulesetVersion);
        });
    }

    [Fact]
    public void Shared_signatures_are_exactly_the_known_service_groups()
    {
        var groups = EtpReportFamilyRegistry.Families.GroupBy(family => ImportProfileMatcher.CreateHeaderSignature(family.Headers))
            .Where(group => group.Count() > 1 && group.Any(family => family.BusinessUnit == BusinessUnit.Service))
            .Select(group => string.Join(",", group.Select(family => family.FamilyCode).Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["S001,S014,S015,S016,S017,S018,S031,S032,S033,S034,S035", "S007,S008", "S024,S025,S026", "S039,S040"], groups);
        var retail = EtpReportFamilyRegistry.Families.Where(family => family.BusinessUnit == BusinessUnit.Retail)
            .Select(family => ImportProfileMatcher.CreateHeaderSignature(family.Headers)).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(Service, family => retail.Contains(ImportProfileMatcher.CreateHeaderSignature(family.Headers)));
    }

    [Fact]
    public void The_approved_registry_resolves_every_service_profile_including_the_shared_signatures()
    {
        foreach (var family in Service)
        {
            var profile = family.CreateProfile();
            var resolved = ApprovedImportProfileRegistry.Resolve(profile.Identity);
            Assert.Equal(family.ReportCode, resolved.ReportCode);
        }
        Assert.Equal(ApprovedImportProfileRegistry.All.Count, ApprovedImportProfileRegistry.All.Select(profile => profile.Identity).Distinct().Count());
    }

    [Theory]
    [InlineData("CLOSING STOCK 03.10.2026.csv", "S006")]
    [InlineData("PENDING REPORT 03.10.2026.csv", "S009")]
    [InlineData("PENDING DELIVERY 03.10.2026.csv", "S010")]
    [InlineData("JOB REPORT 30.09.2026 TO 03.10.2026.csv", "S002")]
    [InlineData("RENVENUE REPORT 30.09.2026 TO 03.10.2026.csv", "S003")]
    [InlineData("TENDER COLLECTION 30.09.2026 TO 03.10.2026.csv", "S004")]
    [InlineData("TENDER COLLECTIN SUMMARY 30.09.2026 TO 03.10.2026.csv", "S005")]
    [InlineData("PURCHSE REGISTER INVOICED CREATED DATE 30.09.2026 TO 03.10.2026.csv", "S007")]
    [InlineData("PURCHASE REGISTER  INNVOICED RECCCIVED DATE 30.09.2026 TO 03.10.2026.csv", "S008")]
    [InlineData("R R INDENET RAISED 30.09.2026 TO 03.10.2026.csv", "S015")]
    [InlineData("R R RWR 30.09.2026 TO 03.10.2026 .csv", "S017")]
    [InlineData("R R DELIVERD 30.09.2026 TO 03.10.2026.csv", "S018")]
    [InlineData("R R PENDING DELIVERY 30.09.2026 TO 03.10.2026.csv", "S031")]
    [InlineData("R R PENDING REPAIR 30.09.2026 TO 03.10.2026.csv", "S032")]
    [InlineData("R R REPAIRED 30.09.2026 TO 03.10.2026.csv", "S034")]
    [InlineData("EMPOWERMENT REPORT 30.09.2026 TO 03.10.2026.xlsx", "S022")]
    [InlineData("EmpowermentReport_20261003144656.xlsx", "S022")]
    [InlineData("DEFTRAN REPORT 30.09.2026 TO 03.10.2026.csv", "S029")]
    [InlineData("DELIVERY REPORT 30.09.2026 TO 03.10.2026.csv", "S036")]
    [InlineData("REPAIR REPORT 30.09.2026 TO 03.10.2026 .csv", "S037")]
    [InlineData("WDC CLAIM REPORT 30.09.2026 TO 03.10.2026.xlsx", "S039")]
    [InlineData("WRA CLAIM REPORT 30.09.2026 TO 03.10.2026.xlsx", "S040")]
    [InlineData("GPRC CLAIM 30.09.2026 TO 03.10.2026.xlsx", "S041")]
    [InlineData("TECHNICIAN PRODUCIVITY REPORT 30.09.2026 TO 03.10.2026.xlsx", "S028")]
    [InlineData("TATA REPORT 03.09.2026 TO 03.10.2026 .xlsx", "S027")]
    // The correct spellings are accepted beside Sagar's export spellings.
    [InlineData("REVENUE REPORT 30.09.2026 TO 03.10.2026.csv", "S003")]
    [InlineData("PURCHASE REGISTER INVOICED RECEIVED DATE 30.09.2026 TO 03.10.2026.csv", "S008")]
    [InlineData("PURCHASE REGISTER INVOICED CREATED DATE 30.09.2026 TO 03.10.2026.csv", "S007")]
    [InlineData("R R INDENT RAISED 30.09.2026 TO 03.10.2026.csv", "S015")]
    [InlineData("R R DELIVERED 30.09.2026 TO 03.10.2026.csv", "S018")]
    [InlineData("TECHNICIAN PRODUCTIVITY REPORT 30.09.2026 TO 03.10.2026.xlsx", "S028")]
    [InlineData("SRN REPORT 03.10.2026.csv", "S011")]
    [InlineData("SRN STATUS REPORT 03.10.2026.csv", "S011")]
    public void Raw_export_names_resolve_among_the_families_whose_headers_matched(string fileName, string expected)
    {
        // Design section 2, "raw file -> family": the raw files carry no code, so the name decides a shared layout.
        var family = EtpReportFamilyRegistry.Resolve(expected);
        var candidates = SameSignature(family);
        Assert.Equal(expected, EtpReportFamilyRegistry.IdentifyName(fileName, candidates)?.FamilyCode);
        Assert.Equal(expected, new ImportProfileMatcher().Match(family.Headers, ApprovedImportProfileRegistry.All, fileName, "Data")?.ReportCode);
    }

    [Fact]
    public void Pending_delivery_and_its_R_R_status_view_are_told_apart()
    {
        // Same words, different headers: S010 is the pending-delivery state list, S031 the PD status view.
        var s010 = EtpReportFamilyRegistry.Resolve("S010");
        var s031 = EtpReportFamilyRegistry.Resolve("S031");
        Assert.NotEqual(ImportProfileMatcher.CreateHeaderSignature(s010.Headers), ImportProfileMatcher.CreateHeaderSignature(s031.Headers));
        var matcher = new ImportProfileMatcher();
        Assert.Equal("S010", matcher.Match(s010.Headers, ApprovedImportProfileRegistry.All, "PENDING DELIVERY 03.10.2026.csv")?.ReportCode);
        Assert.Equal("S031", matcher.Match(s031.Headers, ApprovedImportProfileRegistry.All,
            "R R PENDING DELIVERY 30.09.2026 TO 03.10.2026.csv")?.ReportCode);
        // Even with both families as candidates the longest raw name wins, and the shorter one names S010 alone.
        Assert.Equal("S010", EtpReportFamilyRegistry.IdentifyName("PENDING DELIVERY 03.10.2026.csv", [s010, s031])?.FamilyCode);
        Assert.Equal("S031", EtpReportFamilyRegistry.IdentifyName("R R PENDING DELIVERY 30.09.2026 TO 03.10.2026.csv", [s010, s031])?.FamilyCode);
    }

    [Fact]
    public void Raw_names_are_not_used_without_the_header_matched_families()
    {
        // Patterns are matched only among the families whose headers matched; a bare name keeps the 1.9.3 Name rule.
        Assert.Equal("R022", EtpReportFamilyRegistry.IdentifyName("REVENUE REPORT 30.09.2026 TO 03.10.2026.csv")?.FamilyCode);
        Assert.Equal("R011", EtpReportFamilyRegistry.IdentifyName("CLOSING STOCK 03.10.2026.csv")?.FamilyCode);
        Assert.Null(EtpReportFamilyRegistry.IdentifyName("R R DELIVERD 30.09.2026 TO 03.10.2026.csv"));
    }

    [Fact]
    public void Every_family_with_a_raw_export_has_raw_name_patterns_and_no_two_collide_in_a_shared_layout()
    {
        Assert.All(Service.Where(family => !family.Derived), family => Assert.NotEmpty(family.RawNamePatterns));
        Assert.Empty(EtpReportFamilyRegistry.Resolve("S001").RawNamePatterns);
        foreach (var group in Service.GroupBy(family => ImportProfileMatcher.CreateHeaderSignature(family.Headers)).Where(group => group.Count() > 1))
        {
            var patterns = group.SelectMany(family => family.RawNamePatterns.Select(pattern => (family.FamilyCode, pattern))).ToArray();
            // Each pattern, as a file name, names its own family among its layout's families.
            Assert.All(patterns, entry => Assert.Equal(entry.FamilyCode,
                EtpReportFamilyRegistry.IdentifyName($"{entry.pattern} 01.10.2026.csv", group.ToArray())?.FamilyCode));
        }
    }

    private static EtpReportFamily[] SameSignature(EtpReportFamily family)
    {
        var signature = ImportProfileMatcher.CreateHeaderSignature(family.Headers);
        return [.. EtpReportFamilyRegistry.Families.Where(candidate => ImportProfileMatcher.CreateHeaderSignature(candidate.Headers) == signature)];
    }

    private static ColumnRole Role(EtpReportFamily family, string field) =>
        Assert.Single(family.Columns, column => column.CanonicalField == field).Role;

    // Customer, loyalty, contact and GST-number columns (spec 7.4 patterns plus the Service ones of review M1).
    private static bool IsCustomerColumn(string field) =>
        field.Contains("customer", StringComparison.Ordinal) || field.StartsWith("cust_", StringComparison.Ordinal) ||
        field == "custname" || (field.Contains("phone", StringComparison.Ordinal) && !field.Contains("phonepe", StringComparison.Ordinal)) ||
        field.Contains("mobile", StringComparison.Ordinal) || field.Contains("landline", StringComparison.Ordinal) ||
        field.Contains("contact", StringComparison.Ordinal) || field.StartsWith("ulp", StringComparison.Ordinal) ||
        field.Contains("gstin", StringComparison.Ordinal) || field.Contains("gstn", StringComparison.Ordinal) ||
        field.StartsWith("encircle", StringComparison.Ordinal) || field.Contains("address", StringComparison.Ordinal) ||
        field.Contains("email", StringComparison.Ordinal);

    private static string RulesFingerprint(EtpReportFamily family)
    {
        var identity = family.Identity!;
        var text = $"Scope={identity.Scope}; DocumentKey={string.Join(',', identity.DocumentKey)}; YearRule={identity.YearRule}; " +
            $"RowRule={identity.RowRule}; RowKey={string.Join(',', identity.RowKey)}; ChangePolicy={identity.ChangePolicy}; " +
            $"SnapshotDate={identity.SnapshotDate}; LegacyNullable={string.Join(',', identity.LegacyNullable)}; Route={identity.Route}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            family.Columns.Select(column => $"{column.CanonicalField}={column.Role}").Append(text)))))[..12];
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }
}
