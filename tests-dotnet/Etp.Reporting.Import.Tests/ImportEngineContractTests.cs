using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Tests;

/// <summary>The shared import-engine types keep the shapes the database and the catalogue rely on.</summary>
public sealed class ImportEngineContractTests
{
    [Fact]
    public void Shipped_catalogue_still_loads()
    {
        var families = EtpReportFamilyRegistry.Families;

        Assert.Contains(families, family => family.ReportCode == "R025");
        Assert.Contains(families, family => family.ReportCode == "SOR_AGEING");
        Assert.All(families.Where(family => family.FamilyCode.StartsWith('R')), family => Assert.Equal(BusinessUnit.Retail, family.BusinessUnit));
        Assert.All(families.SelectMany(family => family.Columns), column => Assert.True(Enum.IsDefined(column.Role)));
    }

    [Fact]
    public void Catalogue_reads_roles_and_identity_and_defaults_what_is_absent()
    {
        const string json = """
            [{
              "FamilyCode": "R999", "ReportCode": "R999", "Name": "Synthetic", "IsTyped": false, "TableName": "etp_landing_r999",
              "PrimaryDateHeader": "INVDATE", "Headers": ["INVNUMBER", "INVDATE", "QTY", "CUSTOMERNAME", "STORETIMESTAMP"],
              "Columns": [
                { "SourceHeader": "INVNUMBER", "CanonicalField": "invoice_number", "DataType": "Identifier", "IsRequired": true, "Role": "Key" },
                { "SourceHeader": "INVDATE", "CanonicalField": "transaction_date", "DataType": "Date", "IsRequired": true },
                { "SourceHeader": "QTY", "CanonicalField": "source_quantity", "DataType": "Decimal", "IsRequired": true },
                { "SourceHeader": "CUSTOMERNAME", "CanonicalField": "customer_name", "DataType": "Text", "IsRequired": false, "Role": "Descriptive" },
                { "SourceHeader": "STORETIMESTAMP", "CanonicalField": "source_store_timestamp", "DataType": "Text", "IsRequired": false, "Role": "Ignored" }
              ],
              "Identity": { "Scope": "Document", "DocumentKey": ["invoice_number"], "YearRule": "FinancialYearOfPrimaryDate",
                "RowRule": "Multiset", "ChangePolicy": "Review", "Route": "Sales", "RulesetVersion": 3 }
            },
            {
              "FamilyCode": "R998", "ReportCode": "R998", "Name": "Undated", "IsTyped": false, "TableName": "etp_landing_r998",
              "PrimaryDateHeader": null, "Headers": ["ITEM"],
              "Columns": [ { "SourceHeader": "ITEM", "CanonicalField": "itemnumber", "DataType": "Identifier", "IsRequired": true } ],
              "BusinessUnit": "Service", "Derived": true, "ConsolidationColumns": ["SourceFile"],
              "Identity": { "Scope": "Snapshot", "SnapshotDate": "Block", "RowRule": "SnapshotItems", "RowKey": ["itemnumber"],
                "ChangePolicy": "LatestReadingWins" }
            }]
            """;

        var families = EtpReportFamilyRegistry.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        var sales = families[0];
        Assert.Equal([ColumnRole.Key, ColumnRole.Fact, ColumnRole.Fact, ColumnRole.Descriptive, ColumnRole.Ignored], sales.Columns.Select(c => c.Role));
        Assert.Equal(["customer_name"], sales.ColumnsWithRole(ColumnRole.Descriptive).Select(c => c.CanonicalField));
        Assert.Equal(BusinessUnit.Retail, sales.BusinessUnit);
        Assert.False(sales.Derived);
        Assert.Empty(sales.ConsolidationColumns);
        var identity = sales.Identity!;
        Assert.Equal(DocumentScope.Document, identity.Scope);
        Assert.Equal(["invoice_number"], identity.DocumentKey);
        Assert.Equal(YearRule.FinancialYearOfPrimaryDate, identity.YearRule);
        Assert.Equal(FamilyRoute.Sales, identity.Route);
        Assert.True(identity.HasTypedFacts);
        Assert.Equal(3, identity.RulesetVersion);
        Assert.Null(identity.SnapshotDateColumn);

        var snapshot = families[1];
        Assert.Equal(BusinessUnit.Service, snapshot.BusinessUnit);
        Assert.True(snapshot.Derived);
        Assert.Equal(["SourceFile"], snapshot.ConsolidationColumns);
        var snapshotIdentity = snapshot.Identity!;
        Assert.True(snapshotIdentity.SnapshotDateFromBlock);
        Assert.Equal(FamilyRoute.Landing, snapshotIdentity.Route);
        Assert.False(snapshotIdentity.HasTypedFacts);
        Assert.Equal(1, snapshotIdentity.RulesetVersion);
        Assert.Equal("snapshot_date", new EtpFamilyIdentity { SnapshotDate = "Column:snapshot_date" }.SnapshotDateColumn);
    }

    [Fact]
    public void Document_keys_follow_the_key_text_of_spec_7_1()
    {
        var invoice = DocumentKey.ForDocument(" r025", "wlmhw ", new DateOnly(2026, 4, 1), " inv-68 ");
        Assert.Equal("R025", invoice.ReportCode);
        Assert.Equal("WLMHW", invoice.StoreCode);
        Assert.Equal(DocumentScope.Document, invoice.Scope);
        Assert.Equal("2027|INV-68", invoice.KeyText);
        Assert.Equal(Sha("R025|WLMHW|2027|INV-68"), invoice.Hash);
        Assert.Equal(Convert.FromHexString(invoice.Hash), invoice.HashBytes());
        Assert.Equal(invoice, DocumentKey.ForDocument("R025", "WLMHW", 2027, "INV-68"));

        Assert.Equal(2026, DocumentKey.FinancialYearEnd(new DateOnly(2026, 3, 31)));
        Assert.Equal(2027, DocumentKey.FinancialYearEnd(new DateOnly(2026, 4, 1)));
        Assert.Equal("2026-08-25", DocumentKey.ForDate("R024", "WLMHW", new DateOnly(2026, 8, 25)).KeyText);
        Assert.Equal("2026-09-29|R010", DocumentKey.ForSnapshot("r010", "HEMW", new DateOnly(2026, 9, 29)).KeyText);
        Assert.Equal("2024-09-01..2024-11-30", DocumentKey.ForPeriod("S027", "AW330", new(2024, 9, 1), new(2024, 11, 30)).KeyText);
        Assert.Throws<ArgumentException>(() => DocumentKey.ForPeriod("S027", "AW330", new(2024, 11, 30), new(2024, 9, 1)));
        Assert.NotEqual(DocumentKey.ForDate("R024", "WLMHW", new(2026, 8, 25)).Hash, DocumentKey.ForDate("R024", "HEMW", new(2026, 8, 25)).Hash);
    }

    [Fact]
    public void Enum_values_have_the_database_codes_and_fit_their_columns()
    {
        Assert.Equal("ROLLED_BACK", CommitState.RolledBack.ToDatabaseCode());
        Assert.Equal("ALREADY_HELD", EvidenceState.AlreadyHeld.ToDatabaseCode());
        Assert.Equal("EVIDENCE", FailureStage.Evidence.ToDatabaseCode());
        Assert.Equal("CONSOLIDATED_LEGACY", SourceKind.ConsolidatedLegacy.ToDatabaseCode());
        Assert.Equal("INFO_BLOCK", SnapshotDateBasis.InfoBlock.ToDatabaseCode());
        Assert.Equal("WHOLE_FILE", BlockOrigin.WholeFile.ToDatabaseCode());
        Assert.Equal("ATTRIBUTE_UPDATED", DocumentDecision.AttributeUpdated.ToDatabaseCode());
        Assert.Equal("NEW_ON_LOCKED_DAY", ChangeReason.NewOnLockedDay.ToDatabaseCode());
        Assert.Equal("CANONICAL_ONLY", VersionBasis.CanonicalOnly.ToDatabaseCode());
        Assert.Equal("MINUTE", ExportBasis.Minute.ToDatabaseCode());

        // Widths of the columns that store them (spec 5.1, 5.2).
        AssertCodes<FailureStage>(12); AssertCodes<CommitState>(12); AssertCodes<EvidenceState>(16);
        AssertCodes<SourceKind>(24); AssertCodes<BlockCompleteness>(10); AssertCodes<BlockOrigin>(14);
        AssertCodes<PeriodBasis>(10); AssertCodes<SnapshotDateBasis>(14); AssertCodes<ExportBasis>(8);
        AssertCodes<DocumentScope>(8); AssertCodes<DocumentStatus>(10); AssertCodes<ReviewState>(8);
        AssertCodes<VersionState>(10); AssertCodes<VersionBasis>(14); AssertCodes<VersionChangeKind>(12);
        AssertCodes<DocumentDecision>(24); AssertCodes<ChangeReason>(24); AssertCodes<ChangeAction>(8);
        AssertCodes<ChangeMode>(6); AssertCodes<ChangeItemStatus>(10);
        Assert.Equal(21, Enum.GetValues<DocumentDecision>().Length);
        Assert.False(ImportDatabaseCodes.TryParseDatabaseCode<CommitState>("rolled_back", out _));
    }

    [Fact]
    public void Contract_values_are_lowercase_and_exact()
    {
        Assert.Equal("delta", BlockCompleteness.Delta.ToContractText());
        Assert.True(ConsolidationContractLayout.TryParseContractText<ContractRule>("transactional", out var rule));
        Assert.Equal(ContractRule.Transactional, rule);
        Assert.False(ConsolidationContractLayout.TryParseContractText<BlockCompleteness>("Delta", out _));
        Assert.Equal(18, ConsolidationContractLayout.BlockTableColumns.Count);
        Assert.Equal(19, ContractKeys.Known.Count);
        Assert.All(ContractKeys.Required, key => Assert.Contains(key, ContractKeys.Known));
    }

    [Fact]
    public void Contract_layout_rows_read_their_cells()
    {
        var header = new ConsolidationContractHeader("1",
        [
            new(2, "family_code", "R010"), new(3, "store_code", " WLMHW "), new(4, "rule", "snapshot"),
            new(5, "data_rows", "2402"), new(6, "history_extra_columns", "Snapshot_As_Of, SourceFile"),
            new(7, "built_at", "2026-10-05T10:15:00+05:30"), new(8, "coverage_from", "05-10-2026"), new(9, "block_count", "")
        ]);
        Assert.Equal(1, header.Version);
        Assert.Equal("R010", header.FamilyCode);
        Assert.Equal("WLMHW", header.StoreCode);
        Assert.Equal(ContractRule.Snapshot, header.Rule);
        Assert.Equal(2402, header.DataRows);
        Assert.Equal(["Snapshot_As_Of", "SourceFile"], header.HistoryExtraColumns);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 10, 15, 0, TimeSpan.FromMinutes(330)), header.BuiltAt);
        Assert.Null(header.CoverageFrom);
        Assert.Null(header.BlockCount);
        Assert.Null(header.Value("block_count"));

        var cells = ConsolidationContractLayout.BlockTableColumns.Zip(
            new[] { "2", "Data", "846", "1698", "853", "202608071858_BinWise Stock.xlsx", "xlsx", new string('a', 64), "2026-08-07T18:58",
             "", "", "none", "2026-08-07", "853", "0", "0", "complete", "Snapshot 07-Aug retained" })
            .ToDictionary(x => x.First, x => x.Second);
        var block = new ContractBlockRow(21, cells);
        Assert.Equal(2, block.Block);
        Assert.Equal(846, block.FirstRow);
        Assert.Equal(ExportTime.AtMinute(new DateTime(2026, 8, 7, 18, 58, 0)), block.ExportTime);
        Assert.Null(block.PeriodFrom);
        Assert.Equal(PeriodBasis.None, block.PeriodBasis);
        Assert.Equal(new DateOnly(2026, 8, 7), block.SnapshotDate);
        Assert.Equal(BlockCompleteness.Complete, block.Completeness);
        Assert.Null(new ContractBlockRow(22, new Dictionary<string, string> { ["export_time"] = "07 Aug" }).ExportTime);

        var excluded = new ContractExcludedRow(2, new Dictionary<string, string> { ["block"] = "4", ["sheet"] = "Data", ["row"] = "5361" });
        Assert.Equal(4, excluded.Block);
        Assert.Equal("Data", excluded.Sheet);
        Assert.Equal(5361, excluded.Row);
    }

    [Fact]
    public void Older_issue_json_reads_with_one_occurrence()
    {
        var issue = Assert.Single(JsonSerializer.Deserialize<ImportIssue[]>(
            """[{"Severity":1,"Code":"UNEXPECTED_COLUMN","Message":"Review","SourceRow":1,"SourceColumn":"ITEMNUMBER"}]""")!);

        Assert.Equal(1, issue.Occurrences);
        Assert.Null(issue.BlockNo);
        Assert.Null(issue.DocumentRef);
        var round = JsonSerializer.Deserialize<ImportIssue>(JsonSerializer.Serialize(issue with { BlockNo = 3, DocumentRef = "INV-1", Occurrences = 4 }))!;
        Assert.Equal(3, round.BlockNo);
        Assert.Equal("INV-1", round.DocumentRef);
        Assert.Equal(4, round.Occurrences);
    }

    [Fact]
    public void Import_codes_are_distinct_and_carry_their_severity()
    {
        var codes = Constants(typeof(ImportCodes)).Concat(Constants(typeof(ImportCodes.Contract))).ToArray();
        Assert.Equal(codes.Length - 1, codes.Distinct(StringComparer.Ordinal).Count()); // ContractUnreadable is listed twice.
        Assert.All(codes, code => Assert.Matches("^[A-Z0-9_]{1,80}$", code));
        Assert.Contains(ImportCodes.Contract.ExcludedTwinReused, codes);

        Assert.Equal(ImportIssueSeverity.Warning, ImportCodes.DefaultSeverity(ImportCodes.StockRowRepeated));
        Assert.Equal(ImportIssueSeverity.Warning, ImportCodes.DefaultSeverity(ImportCodes.SnapshotDateFromFolder));
        Assert.Equal(ImportIssueSeverity.Information, ImportCodes.DefaultSeverity(ImportCodes.InvoiceYearDiffers));
        Assert.Equal(ImportIssueSeverity.Information, ImportCodes.DefaultSeverity(ImportCodes.Contract.KeyUnknown));
        Assert.Equal(ImportIssueSeverity.Blocker, ImportCodes.DefaultSeverity(ImportCodes.ContractUnreadable));
        Assert.Equal(ImportIssueSeverity.Blocker, ImportCodes.DefaultSeverity(ImportCodes.SnapshotMultipleDates));
        // Document holds (Appendix B "document held"): the document waits for the Owner, the file goes ahead.
        Assert.Equal(ImportIssueSeverity.Warning, ImportCodes.DefaultSeverity(ImportCodes.InSourceConflict));
        Assert.Equal(ImportIssueSeverity.Warning, ImportCodes.DefaultSeverity(ImportCodes.LegacyBlocksDiffer));
        Assert.Equal(ImportIssueSeverity.Warning, ImportCodes.DefaultSeverity(ImportCodes.HeaderDateMismatch));
        Assert.Equal("SQL_51700", ImportCodes.Sql(ImportCodes.SqlErrors.FinancialYearPrecheck));

        var numbers = typeof(ImportCodes.SqlErrors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral).Select(field => (int)field.GetRawConstantValue()!).ToArray();
        Assert.Equal(17, numbers.Length);
        Assert.Equal(numbers.Length, numbers.Distinct().Count());
        Assert.All(numbers, number => Assert.True(ImportCodes.SqlErrors.IsImportEngine(number)));

        Assert.Equal(13, ImportAttemptOutcomes.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ImportAttemptOutcomes.All, outcome => Assert.InRange(outcome.Length, 1, 40));
    }

    [Fact]
    public void Diagnostics_map_onto_attempt_issues_and_failures()
    {
        Assert.Equal(Enum.GetNames<ImportIssueSeverity>(), Enum.GetNames<ImportDiagnosticSeverity>());
        var diagnostic = new ImportDiagnostic(ImportCodes.StockRowRepeated, ImportDiagnosticSeverity.Warning, "Rows 4, 9 repeat.",
            "Data", 4, "ITEMNUMBER") { BlockNo = 2, DocumentRef = "STM-9 2026-09-06 ITEM-1", Occurrences = 2 };

        Assert.Equal(new ImportIssue(ImportIssueSeverity.Warning, ImportCodes.StockRowRepeated, "Rows 4, 9 repeat.", 4, "ITEMNUMBER",
            2, "STM-9 2026-09-06 ITEM-1", 2), diagnostic.ToImportIssue());

        IImportFailureClassifier classifier = new SafeImportFailureClassifier();
        var failure = classifier.DescribeDetailed(new ImportSourceException("SNAPSHOT_MULTIPLE_DATES", "Split the snapshot."), FailureStage.Scope);
        Assert.Equal("SNAPSHOT_MULTIPLE_DATES", failure.Code);
        Assert.Equal(FailureStage.Scope, failure.Stage);
        Assert.Equal("Split the snapshot.", failure.SafeMessage);
        Assert.Equal(nameof(ImportSourceException), failure.ExceptionType);
        Assert.Null(failure.SqlNumber);
        Assert.Empty(failure.Issues);
    }

    private static void AssertCodes<TEnum>(int width) where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var code = value.ToDatabaseCode();
            Assert.Matches("^[A-Z]+(_[A-Z]+)*$", code);
            Assert.True(code.Length <= width, $"{typeof(TEnum).Name}.{value} is {code.Length} characters");
            Assert.True(ImportDatabaseCodes.TryParseDatabaseCode<TEnum>(code, out var parsed));
            Assert.Equal(value, parsed);
        }
    }

    private static IEnumerable<string> Constants(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string)field.GetRawConstantValue()!);

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
