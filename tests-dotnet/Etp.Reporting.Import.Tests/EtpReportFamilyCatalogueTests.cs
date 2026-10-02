using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// Pins the roles and identity of every Retail family (spec 7.1, 7.3, 7.4). The catalogue is proposed by
/// <c>scripts/propose-etp-catalogue-roles.ps1</c> and reviewed column by column. A role or identity change changes how
/// documents are identified, so it must raise that family's <c>RulesetVersion</c> in the same change (spec 7.1).
/// </summary>
public sealed class EtpReportFamilyCatalogueTests
{
    private static readonly string[] RetailCodes = [.. Enumerable.Range(1, 31).Select(n => $"R{n:000}"), "SOR_AGEING"];

    // Spec 7.4: every dated family other than the core ones is one document per store and business day.
    private static readonly string[] DatedLandingCodes =
    [
        "R001", "R002", "R004", "R005", "R006", "R007", "R008", "R009", "R012", "R014", "R015", "R016", "R017",
        "R018", "R019", "R020", "R021", "R024", "R026", "R027", "R028", "R029", "R031"
    ];

    // Spec 0: the families whose rows become typed facts, by report code.
    private static readonly Dictionary<string, FamilyRoute> TypedRoutes = new(StringComparer.Ordinal)
    {
        ["R025"] = FamilyRoute.Sales, ["R022"] = FamilyRoute.Revenue, ["R003"] = FamilyRoute.Enrichment,
        ["R013"] = FamilyRoute.Enrichment, ["STOCK_LEDGER"] = FamilyRoute.StockMovement,
        ["CLOSING_STOCK"] = FamilyRoute.StockSnapshot, ["R010"] = FamilyRoute.StockSnapshot
    };

    // RulesetVersion and a fingerprint of the roles and identity it was pinned with (see RulesFingerprint).
    private static readonly Dictionary<string, (int Version, string Fingerprint)> Rulesets = new(StringComparer.Ordinal)
    {
        ["R001"] = (1, "bd909d312dca"), ["R002"] = (1, "9bb6177adfcf"), ["R003"] = (1, "4fbd9baba230"),
        ["R004"] = (1, "76fa1ed4cff3"), ["R005"] = (1, "796c69c53451"), ["R006"] = (1, "713067f5259e"),
        ["R007"] = (1, "a78823d54b72"), ["R008"] = (1, "5ada244ef423"), ["R009"] = (1, "72f0ef55d195"),
        ["R010"] = (1, "4ff5452c46e8"), ["R011"] = (1, "27be9a0f6c5a"), ["R012"] = (1, "f5d3064afdb5"),
        ["R013"] = (1, "7a9fa2a2e7e5"), ["R014"] = (1, "bd8bd1d309d4"), ["R015"] = (1, "89b6d17ffd9a"),
        ["R016"] = (1, "e5365fed16ff"), ["R017"] = (1, "4de9a576b823"), ["R018"] = (1, "d70de48f6059"),
        ["R019"] = (1, "35764f892fdd"), ["R020"] = (1, "e2b0f3a5043f"), ["R021"] = (1, "a2cb20433c37"),
        ["R022"] = (1, "833968e82ed8"), ["R023"] = (1, "6a840b558cf2"), ["R024"] = (1, "fc688cd37a45"),
        ["R025"] = (1, "cf5c4e94e59d"), ["R026"] = (1, "8e8ece5d1faf"), ["R027"] = (1, "8fc83ae621ce"),
        ["R028"] = (1, "c6921f63ccb6"), ["R029"] = (1, "831e81a473be"), ["R030"] = (1, "f4e6e5b71fed"),
        ["R031"] = (1, "2d968d11caa4"), ["SOR_AGEING"] = (1, "5e230c29d09b")
    };

    private static IEnumerable<EtpReportFamily> Retail =>
        EtpReportFamilyRegistry.Families.Where(family => family.BusinessUnit == BusinessUnit.Retail);

    [Fact]
    public void Every_retail_family_is_described()
    {
        Assert.Equal(RetailCodes, Retail.Select(family => family.FamilyCode).Order(StringComparer.Ordinal));
        Assert.All(Retail, family =>
        {
            Assert.NotNull(family.Identity);
            Assert.False(family.Derived);
            Assert.Empty(family.ConsolidationColumns);
            Assert.Empty(family.RawNamePatterns);
        });
    }

    [Fact]
    public void Every_retail_column_states_its_role()
    {
        // A role that only defaulted would let a new column become a fact unreviewed.
        using var stream = typeof(EtpReportFamilyRegistry).Assembly.GetManifestResourceStream(
            "Etp.Reporting.Import.Profiles.EtpReportFamilies.json")!;
        using var json = JsonDocument.Parse(stream);
        foreach (var family in json.RootElement.EnumerateArray()
                     .Where(family => RetailCodes.Contains(family.GetProperty("FamilyCode").GetString())))
        {
            foreach (var column in family.GetProperty("Columns").EnumerateArray())
                Assert.True(column.TryGetProperty("Role", out var role) && Enum.TryParse<ColumnRole>(role.GetString(), out _),
                    $"{family.GetProperty("FamilyCode")}.{column.GetProperty("CanonicalField")} has no role.");
            Assert.True(family.TryGetProperty("Identity", out _));
            Assert.True(family.TryGetProperty("Derived", out _));
            Assert.True(family.TryGetProperty("ConsolidationColumns", out _));
        }
    }

    [Theory]
    [InlineData("R001", "store_code invoicedate", "referencenumber",
        "store_name store_type channel region state city customer_name customer_phone encircle",
        "invoice_year referenceyear", "storetimestamp")]
    [InlineData("R002", "store_code invdate", "brand cluster gender hsncode invrefno invrefdate brandname scheme_reference_number",
        "storetype region storename city state customernumber customer_name customer_phone ulpnumber channel",
        "", "storetimestamp")]
    [InlineData("R003", "store_code invoice_number",
        "brand brand_name cluster gender activation_details user_discount_details invoice_ref_no invoice_ref_date",
        "store_name store_type channel region city customernumber customer_name customer_phone ulp_no",
        "", "eastimestamp storetimestamp")]
    [InlineData("R004", "store_code documentdate", "brand hsncode purinvno purinvdate",
        "region state city to_region to_state to_city",
        "invoice_year", "")]
    [InlineData("R005", "store_code documentdate", "couriername couriernumber courierdate airwaybillno dispatchtype",
        "region state city to_region to_state to_city",
        "invoice_year", "")]
    [InlineData("R006", "store_code documentdate", "refdocnumber refdocdate",
        "region state city from_region from_state from_city",
        "invoice_year", "")]
    [InlineData("R007", "store_code documentdate", "hsncode brand refdocdate refdocnumber purinvno purinvdate",
        "region state city from_region from_state from_city",
        "invoice_year", "")]
    [InlineData("R008", "store_code invoice_date", "",
        "storename",
        "invoice_year", "")]
    [InlineData("R009", "store_code transactiondate", "",
        "store_name",
        "invoice_year", "")]
    [InlineData("R010", "store_code", "hsn_code brand brandname cluster gender ean_category",
        "store_name store_type channel region city",
        "", "")]
    [InlineData("R011", "store_code snapshot_date", "hsn_code brand_code cluster gender",
        "store_name store_type channel region state city itemdescription",
        "", "")]
    [InlineData("R012", "store_code creditnotedate", "ref_grnno cn_refdocno redeemed_date redeemed_by issued_cnno issued_cnamt",
        "store_name store_type channel region state city issueto",
        "", "")]
    [InlineData("R013", "store_code invoice_number", "brand brandname cluster gender invrefno invrefdate",
        "store_name store_type channel region city cro_name customer_name customer_phone",
        "", "")]
    [InlineData("R014", "store_code invoice_date", "",
        "store_name",
        "invoice_year", "")]
    [InlineData("R015", "store_code invoicedate", "",
        "store_name channel region state city encircle_no encircle_enrol_date customernumber customer_name customer_phone",
        "", "")]
    [InlineData("R016", "store_code transaction_date", "refinvoicenumber",
        "store_name store_type channel region state city encircle_number customer_name otp_number",
        "", "")]
    [InlineData("R017", "store_code invdate", "",
        "store_name store_type channel region city",
        "", "")]
    [InlineData("R018", "store_code doc_invoice_date", "reference_doc reference_date hsn_code uom",
        "store_name issue_state_name issue_gstn_no recipient_state_name recipient_gstn_no customer_name customernumber",
        "invoice_year", "")]
    [InlineData("R019", "store_code doc_invoice_date", "reference_doc reference_date hsn_code uom",
        "store_name issue_state_name issue_gstn_no recipient_state_name recipient_gstn_no",
        "", "")]
    [InlineData("R020", "store_code invdate", "",
        "channel region storename city state creditcardno",
        "", "")]
    [InlineData("R021", "store_code physc_recv_date", "ref_document_number ref_document_date",
        "store_name region state city location from_region from_state from_city",
        "invoice_year", "")]
    [InlineData("R022", "store_code invoice_number", "reference_invoice_number",
        "store_name store_type channel region state city customer_name customer_phone encircle",
        "invoice_year referenceyear", "source_store_timestamp")]
    [InlineData("R023", "", "businessrulename brand_name strategyname",
        "",
        "", "")]
    [InlineData("R024", "store_code inv_date", "invrefno invrefdate",
        "storename store_type channel region city customer_no customer_name customer_phone ulp_number customer_gstin_no customer_address",
        "", "store_timestamp eas_timestamp")]
    [InlineData("R025", "store_code invoice_number",
        "hsn_code source_brand_code source_brand_name brand_segment_code gender_code reference_invoice_number reference_invoice_date",
        "storename storetype channel region city customer_name customer_phone ulpnumber",
        "", "source_store_timestamp")]
    [InlineData("R026", "store_code prp_doc_date", "jo_no jo_date",
        "store_sap_code store_type channel city state region",
        "", "")]
    [InlineData("R027", "prp_doc_date",
        "etp_invoice_no etp_invoice_date stock_reciept_doc_no stock_reciept_doc_date stm_purchase_return_doc_no stm_purchase_return_doc_date stm_courier_doc_no stm_courier_doc_date",
        "store_sap_code store_type channel city state region",
        "", "")]
    [InlineData("R028", "store_code invdate", "brand brandname cluster gender hsn_code purinvno purinvdate invoicereferencenumber",
        "store_name store_type channel region state city",
        "", "")]
    [InlineData("R029", "store_code trans_date", "",
        "store_name",
        "invoice_year", "")]
    [InlineData("R030", "store_code document_number", "hsn_code brand brandname cluster gender ref_documentnumber ref_documentdate",
        "store_name city state location",
        "", "")]
    [InlineData("R031", "store_code invdate",
        "hsncode brand brandname cluster gender invrefno invrefdate etpadvorder etpadvdate ocshipmentno",
        "storename storetype channel region city customer_name customer_phone ulpnumber",
        "", "storetimestamp")]
    [InlineData("SOR_AGEING", "store_code", "hsn_code brand brandname cluster gender purchaseinvno purchaseinvdt",
        "store_name",
        "", "")]
    public void Roles_are_pinned(string code, string key, string attribute, string descriptive, string label, string ignored)
    {
        // Every column not listed here is a Fact.
        var family = Family(code);
        Assert.Equal(Split(key), Fields(family, ColumnRole.Key));
        Assert.Equal(Split(attribute), Fields(family, ColumnRole.Attribute));
        Assert.Equal(Split(descriptive), Fields(family, ColumnRole.Descriptive));
        Assert.Equal(Split(label), Fields(family, ColumnRole.Label));
        Assert.Equal(Split(ignored), Fields(family, ColumnRole.Ignored));
    }

    [Theory]
    [InlineData("R025", "source_transaction_type product_code transaction_date source_quantity source_ucp source_gross_ucp " +
        "scheme_discount user_discount helios_creditnote promo_gc netgross pre_discount source_net_amount " +
        "sgst_utgst sgst_utgst_value csgt csgt_value igst igst_value cess cess_value source_tax_amount source_net_value")]
    [InlineData("R022", "source_transaction_type transaction_date source_invoice_quantity tender_cash tender_card tender_cheque " +
        "tender_loyalty_points tender_gift_voucher tender_credit_note_redeemed tender_excess_gv tender_round_off tender_no_refund " +
        "tender_others tender_tata_gv tender_gift_card tender_tatacliq tender_gyftr tender_paytm tender_helios_omni " +
        "tender_advance_redeem tender_bhim_upi tender_phonepe tender_bharatpe tender_bajaj_finance tender_razorpay " +
        "tender_payment_type24 tender_payment_type25 tender_issued_credit_note tender_cash_refund tender_cheque_rtgs_refund " +
        "source_net_value")]
    [InlineData("R013", "source_transaction_type product_code transaction_date cro_number source_quantity ucp grossucp " +
        "scheme_discount netgross pre_discount source_net_amount source_net_value")]
    [InlineData("R003", "source_transaction_type product_code transaction_date source_quantity ucp grossucp scheme_discount " +
        "netgross user_discount other_charges source_net_amount tax source_net_value")]
    [InlineData("R030", "source_transaction_type product_code document_date from_location to_location opening_quantity " +
        "transaction_quantity closing_quantity")]
    [InlineData("R011", "product_code ean batch_number source_uid quantity unit_cost total_cost")]
    [InlineData("R010", "itemnumber lotnumber uid retailbin servicebin replacementbin defectivebin instibin ecomm " +
        "other1 other2 other3 other4 other5 other6 other7 other8 other9 other10 other11 other12 other13 other14 other15 " +
        "other16 other17 other18 other19 other20 other21 other22 other23 other24 other25 ucp closingbalance totalucp")]
    public void Core_family_facts_are_the_spec_7_3_lists(string code, string facts)
    {
        // Listed in the spec's order; the transaction or document date is a fact, so a re-dated invoice is a change.
        Assert.Equal(Split(facts).Order(StringComparer.Ordinal), Fields(Family(code), ColumnRole.Fact).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("R025", "Document", "invoice_number", "FinancialYearOfPrimaryDate", "Multiset", "", "Review", null,
        "source_net_amount source_tax_amount", "Sales")]
    [InlineData("R022", "Document", "invoice_number", "FinancialYearOfPrimaryDate", "SingleRowPerDocument", "", "Review", null,
        "", "Revenue")]
    [InlineData("R013", "Document", "invoice_number", "FinancialYearOfPrimaryDate", "Multiset", "", "Review", null, "", "Enrichment")]
    [InlineData("R003", "Document", "invoice_number", "FinancialYearOfPrimaryDate", "Multiset", "", "Review", null, "", "Enrichment")]
    [InlineData("R030", "Document", "document_number", "FinancialYearOfPrimaryDate", "StockUnitChain", "", "Review", null, "",
        "StockMovement")]
    [InlineData("R011", "Snapshot", "", "None", "SnapshotItems", "product_code COALESCE(source_uid,batch_number,ean)",
        "LatestReadingWins", "Column:snapshot_date", "", "StockSnapshot")]
    [InlineData("R010", "Snapshot", "", "None", "SnapshotItems", "itemnumber COALESCE(uid,lotnumber)", "LatestReadingWins", "Block",
        "", "StockSnapshot")]
    [InlineData("R023", "Snapshot", "", "None", "SnapshotItems", "businessruleid versionid strategy_id brand_code cluster itemnumber",
        "LatestReadingWins", "Block", "", "Landing")]
    [InlineData("SOR_AGEING", "Snapshot", "", "None", "SnapshotItems", "itemnumber gr_number", "LatestReadingWins", "Block", "",
        "Landing")]
    public void Core_and_undated_identities_are_pinned(string code, string scope, string documentKey, string yearRule,
        string rowRule, string rowKey, string changePolicy, string? snapshotDate, string legacyNullable, string route)
    {
        var identity = Family(code).Identity!;
        Assert.Equal(Enum.Parse<DocumentScope>(scope), identity.Scope);
        Assert.Equal(Split(documentKey), identity.DocumentKey);
        Assert.Equal(Enum.Parse<YearRule>(yearRule), identity.YearRule);
        Assert.Equal(Enum.Parse<RowRule>(rowRule), identity.RowRule);
        Assert.Equal(Split(rowKey), identity.RowKey);
        Assert.Equal(Enum.Parse<ChangePolicy>(changePolicy), identity.ChangePolicy);
        Assert.Equal(snapshotDate, identity.SnapshotDate);
        Assert.Equal(Split(legacyNullable), identity.LegacyNullable);
        Assert.Equal(Enum.Parse<FamilyRoute>(route), identity.Route);
    }

    [Fact]
    public void Dated_landing_families_are_documents_of_one_business_day()
    {
        Assert.Equal(DatedLandingCodes.Length + 9, RetailCodes.Length);
        foreach (var family in DatedLandingCodes.Select(Family))
        {
            Assert.Equal(IdentityText(new EtpFamilyIdentity()), IdentityText(family.Identity!));
            var businessDate = Assert.Single(family.Columns, column => column.SourceHeader == family.PrimaryDateHeader);
            Assert.Equal(ColumnRole.Key, businessDate.Role);
        }
    }

    [Fact]
    public void Typed_families_are_the_seven_of_spec_0()
    {
        Assert.All(Retail, family =>
        {
            var identity = family.Identity!;
            Assert.Equal(TypedRoutes.TryGetValue(family.ReportCode, out var route) ? route : FamilyRoute.Landing, identity.Route);
            Assert.Equal(TypedRoutes.ContainsKey(family.ReportCode), identity.HasTypedFacts);
        });
    }

    [Fact]
    public void Identity_names_columns_with_key_or_fact_roles()
    {
        Assert.All(Retail, family =>
        {
            var identity = family.Identity!;
            var roles = family.Columns.ToDictionary(column => column.CanonicalField, column => column.Role, StringComparer.Ordinal);
            Assert.All(identity.NamedFields(), field => Assert.True(roles.ContainsKey(field), $"{family.FamilyCode} names {field}."));
            Assert.All(identity.DocumentKey, field => Assert.Equal(ColumnRole.Key, roles[field]));
            if (identity.SnapshotDateColumn is { } dateColumn) Assert.Equal(ColumnRole.Key, roles[dateColumn]);
            // Row keys and NULL fills are read from the canonical Key and Fact text of a row.
            Assert.All(identity.RowKeyFields().Concat(identity.LegacyNullable),
                field => Assert.Contains(roles[field], new[] { ColumnRole.Key, ColumnRole.Fact }));
            if (roles.TryGetValue("store_code", out var store)) Assert.Equal(ColumnRole.Key, store);

            var document = identity.Scope == DocumentScope.Document;
            Assert.Equal(document, identity.DocumentKey.Count > 0);
            Assert.Equal(document ? YearRule.FinancialYearOfPrimaryDate : YearRule.None, identity.YearRule);
            if (identity.Scope is DocumentScope.Document or DocumentScope.Date) Assert.NotNull(family.PrimaryDateHeader);
            Assert.Equal(identity.SnapshotDateFromBlock, family.PrimaryDateHeader is null);
            var snapshot = identity.Scope == DocumentScope.Snapshot;
            Assert.Equal(snapshot, identity.SnapshotDate is not null);
            Assert.Equal(snapshot, identity.RowKey.Count > 0);
            Assert.Equal(snapshot, identity.RowRule == RowRule.SnapshotItems);
            Assert.Equal(snapshot ? ChangePolicy.LatestReadingWins : ChangePolicy.Review, identity.ChangePolicy);
            Assert.True(identity.SnapshotDateFromBlock || identity.SnapshotDateColumn is not null || !snapshot);
        });
    }

    [Fact]
    public void Descriptive_column_can_never_be_a_key()
    {
        Assert.All(Retail, family =>
        {
            Assert.Empty(KeyProblems(family));
            // Customer, loyalty and GST-number columns are Descriptive, so they never reach a key or a fact hash.
            Assert.All(family.Columns.Where(column => IsCustomerColumn(column.CanonicalField)),
                column => Assert.Equal(ColumnRole.Descriptive, column.Role));
        });
        Assert.Contains("tender_phonepe", Fields(Family("R022"), ColumnRole.Fact));
        Assert.Contains("creditcard", Fields(Family("R014"), ColumnRole.Fact));
        Assert.True(IsCustomerColumn("customer_mobile_no") && IsCustomerColumn("mobile_no"));

        // The check is not vacuous: keying on a loyalty number, or giving a phone number the Key role, is caught.
        EtpSourceColumn[] columns =
        [
            new("INVNUMBER", "invoice_number", CanonicalDataType.Identifier, true) { Role = ColumnRole.Key },
            new("INVDATE", "transaction_date", CanonicalDataType.Date, true),
            new("ENCIRCLE NO", "encircle_no", CanonicalDataType.Identifier, false) { Role = ColumnRole.Descriptive },
            new("CONTACTNO", "customer_phone", CanonicalDataType.Identifier, false) { Role = ColumnRole.Key }
        ];
        var synthetic = new EtpReportFamily("R999", "R999", "Synthetic", false, "etp_landing_r999", "INVDATE",
            columns.Select(column => column.SourceHeader).ToArray(), columns)
        {
            Identity = new() { Scope = DocumentScope.Document, DocumentKey = ["encircle_no"], YearRule = YearRule.FinancialYearOfPrimaryDate }
        };
        Assert.Equal(["encircle_no", "customer_phone"], KeyProblems(synthetic));
    }

    [Fact]
    public void Ignored_columns_are_exactly_the_timestamp_columns()
    {
        // content_key leaves out every staged field named *timestamp* (PhaseOneImportPersistence.ContentKeys); it must
        // stay unchanged when it is computed from roles (spec 7.1).
        Assert.All(Retail.SelectMany(family => family.Columns), column => Assert.Equal(
            column.CanonicalField.Contains("timestamp", StringComparison.OrdinalIgnoreCase), column.Role == ColumnRole.Ignored));
    }

    [Fact]
    public void Labels_are_ETP_year_labels_only()
    {
        Assert.All(Retail.SelectMany(family => family.Columns), column => Assert.Equal(
            column.CanonicalField is "invoice_year" or "referenceyear", column.Role == ColumnRole.Label));
    }

    [Fact]
    public void Ruleset_version_rises_with_every_role_or_identity_change()
    {
        Assert.Equal(RetailCodes, Rulesets.Keys.Order(StringComparer.Ordinal));
        Assert.All(Retail, family =>
        {
            var (version, fingerprint) = Rulesets[family.FamilyCode];
            var actual = RulesFingerprint(family);
            Assert.True(actual == fingerprint,
                $"The roles or identity of {family.FamilyCode} no longer match fingerprint {fingerprint}, pinned with RulesetVersion " +
                $"{version}. A rule change must raise RulesetVersion (spec 7.1): raise it in EtpReportFamilies.json, then pin the " +
                $"new version with fingerprint {actual}.");
            Assert.True(family.Identity!.RulesetVersion == version,
                $"{family.FamilyCode} has RulesetVersion {family.Identity.RulesetVersion}; {version} is pinned with its rules.");
        });
    }

    [Fact]
    public async Task Generator_proposes_the_catalogue_as_committed()
    {
        // The roles are proposed by the generator and reviewed there (spec 15, P2); a hand edit to the JSON that the
        // generator would not propose, or a generator change not written back, fails here.
        var root = RepositoryRoot();
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
                     Path.Combine(root, "scripts", "propose-etp-catalogue-roles.ps1"), "-Check",
                     "-CataloguePath", Path.Combine(root, "src", "Etp.Reporting.Import", "Profiles", "EtpReportFamilies.json")
                 })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The catalogue generator did not finish within 60 seconds.");
        }

        var (standardOutput, standardError) = (await output, await error);
        Assert.True(process.ExitCode == 0,
            $"propose-etp-catalogue-roles.ps1 -Check exited {process.ExitCode}: {standardOutput} {standardError}");
        Assert.Contains("The catalogue matches the proposal.", standardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_key_entries_read_coalesce_alternatives_in_order()
    {
        Assert.Equal(["product_code"], EtpFamilyIdentityFields.RowKeyAlternatives("product_code"));
        Assert.Equal(["source_uid", "batch_number", "ean"], EtpFamilyIdentityFields.RowKeyAlternatives("COALESCE(source_uid, batch_number,ean)"));
        Assert.Throws<FormatException>(() => EtpFamilyIdentityFields.RowKeyAlternatives("COALESCE()"));

        var closingStock = Family("R011").Identity!;
        Assert.Equal(["product_code", "source_uid", "batch_number", "ean", "snapshot_date"], closingStock.NamedFields());
        Assert.Equal(["ITEM-1", "BATCH-7"], closingStock.RowKeyValues(new Dictionary<string, string>
        {
            ["product_code"] = "ITEM-1", ["source_uid"] = "", ["batch_number"] = "BATCH-7", ["ean"] = "8900000000017"
        }));
        Assert.Equal(["ITEM-1", ""], closingStock.RowKeyValues(new Dictionary<string, string> { ["product_code"] = "ITEM-1" }));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }

    private static EtpReportFamily Family(string code) => EtpReportFamilyRegistry.Families.Single(family => family.FamilyCode == code);

    private static string[] Split(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string[] Fields(EtpReportFamily family, ColumnRole role) =>
        family.ColumnsWithRole(role).Select(column => column.CanonicalField).ToArray();

    // Spec 7.4 patterns for customer, loyalty and GST-number columns, plus a customer's card number and OTP. PHONEPE is
    // a tender, not a phone number; CREDITCARD and GIFTCARD without NO are tender amounts.
    private static bool IsCustomerColumn(string field) =>
        field.StartsWith("customer", StringComparison.Ordinal) ||
        (field.Contains("phone", StringComparison.Ordinal) && !field.Contains("phonepe", StringComparison.Ordinal)) ||
        field.Contains("mobile", StringComparison.Ordinal) || field.Contains("creditcardno", StringComparison.Ordinal) ||
        field.StartsWith("otp_", StringComparison.Ordinal) ||
        field.Contains("contact", StringComparison.Ordinal) || field.StartsWith("ulp", StringComparison.Ordinal) ||
        field.Contains("gstin", StringComparison.Ordinal) || field.Contains("gstn", StringComparison.Ordinal) ||
        field.StartsWith("encircle", StringComparison.Ordinal) || field.Contains("address", StringComparison.Ordinal) ||
        field.Contains("email", StringComparison.Ordinal) || field == "cro_name";

    // Fields that identify a document or pair its rows, yet are Descriptive, customer data or not columns at all.
    private static string[] KeyProblems(EtpReportFamily family)
    {
        var identity = family.Identity!;
        var roles = family.Columns.ToDictionary(column => column.CanonicalField, column => column.Role, StringComparer.Ordinal);
        return identity.DocumentKey.Concat(identity.RowKeyFields())
            .Concat(identity.SnapshotDateColumn is { } column ? new[] { column } : Array.Empty<string>())
            .Concat(Fields(family, ColumnRole.Key))
            .Where(field => !roles.TryGetValue(field, out var role) || role == ColumnRole.Descriptive || IsCustomerColumn(field))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string IdentityText(EtpFamilyIdentity identity) =>
        $"Scope={identity.Scope}; DocumentKey={string.Join(',', identity.DocumentKey)}; YearRule={identity.YearRule}; " +
        $"RowRule={identity.RowRule}; RowKey={string.Join(',', identity.RowKey)}; ChangePolicy={identity.ChangePolicy}; " +
        $"SnapshotDate={identity.SnapshotDate}; LegacyNullable={string.Join(',', identity.LegacyNullable)}; Route={identity.Route}";

    // Every column with its role, then the identity without its RulesetVersion: whatever decides how documents are formed.
    private static string RulesFingerprint(EtpReportFamily family) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            family.Columns.Select(column => $"{column.CanonicalField}={column.Role}").Append(IdentityText(family.Identity!))))))[..12];
}
