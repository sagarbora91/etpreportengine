using System.Collections;
using System.Reflection;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class MatchedImportEnvelopeTests
{
    [Fact]
    public void Approved_registry_contains_only_the_six_evidenced_v1_profiles()
    {
        Assert.Equal(
            ["R025", "R022", "R013", "R003", "STOCK_LEDGER", "CLOSING_STOCK"],
            ApprovedImportProfileRegistry.All.Select(profile => profile.ReportCode));
        Assert.All(ApprovedImportProfileRegistry.All, profile =>
        {
            Assert.Equal("ETP_2026_08", profile.LayoutVersion);
            Assert.Equal("1", profile.ProfileVersion);
            Assert.Same(profile, ApprovedImportProfileRegistry.Resolve(profile.Identity));
        });
    }

    [Fact]
    public void Accepted_envelope_carries_the_exact_profile_sheet_staging_diagnostics_and_lineage_source()
    {
        var cells = RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(header switch
        {
            "TRANS_TYPE" => "INV",
            "STORE CODE" => "WLMHW",
            "ITEMNUMBER" => "ITEM",
            "INVNUMBER" => "DOC",
            "INVDATE" => new DateTime(2026, 8, 25),
            "QTY" => 1m,
            "NETAMOUNT" => 100m,
            "NETVALUE" => 118m,
            _ => null
        })).ToArray();
        var workbook = new WorkbookSnapshot(
            "sales.xlsx",
            1,
            new string('a', 64),
            [new("Sales", 3, RetailSalesProfiles.R025Headers, [new(4, cells)])]);

        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);

        var accepted = Assert.IsType<MatchedImportEnvelope>(inspection.AcceptedImport);
        Assert.NotSame(workbook, accepted.Workbook);
        Assert.Equal(workbook.FileName, accepted.Workbook.FileName);
        Assert.Equal(workbook.Sha256, accepted.Workbook.Sha256);
        Assert.Equal("Sales", accepted.MatchedSheet.Name);
        Assert.Equal(3, accepted.MatchedSheet.HeaderRowNumber);
        Assert.Equal(RetailSalesProfiles.R025.Identity, accepted.ProfileIdentity);
        Assert.Equal(RetailSalesProfiles.R025.HeaderSignatureSha256, accepted.ProfileIdentity.HeaderSignatureSha256);
        Assert.Equal(4, Assert.Single(accepted.Staging.Rows).SourceRowNumber);
        Assert.DoesNotContain(accepted.Diagnostics, diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Blocker);
    }

    [Fact]
    public void Unknown_header_signature_is_fail_closed_and_never_creates_an_envelope()
    {
        var headers = RetailSalesProfiles.R025Headers.ToArray();
        headers[0] = "UNAPPROVED_TRANS_TYPE";
        var workbook = new WorkbookSnapshot(
            "unknown.xlsx",
            1,
            new string('b', 64),
            [new("Sales", 1, headers, [])]);

        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);

        Assert.False(inspection.Accepted);
        Assert.Null(inspection.AcceptedImport);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == "LAYOUT_UNKNOWN");
    }

    [Fact]
    public void Stock_business_rules_are_part_of_acceptance_diagnostics()
    {
        var values = new object?[]
        {
            "UNKNOWN", "STORE", "Store", "ITEM", "HSN", "BR", "Brand", "Cluster", "U", "DOC",
            new DateTime(2026, 8, 25), null, "STORE", null, null, 1m, -1m, 0m, "City", "State", "Location"
        };
        var workbook = new WorkbookSnapshot(
            "stock.xlsx",
            1,
            new string('c', 64),
            [new("Stock", 1, StockImportProfiles.VariantStockLedgerHeaders,
                [new(2, values.Select(value => new WorkbookCell(value)).ToArray())])]);

        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);

        Assert.False(inspection.Accepted);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == "UNKNOWN_STOCK_TRANSACTION_TYPE");
    }

    [Fact]
    public void Malformed_required_stock_cell_is_rejected_by_staging_before_semantic_parsing()
    {
        var values = new object?[]
        {
            "INV", "STORE", "Store", "ITEM", "HSN", "BR", "Brand", "Cluster", "U", "DOC",
            new DateTime(2026, 8, 25), null, "STORE", null, null, 8m, "not-a-quantity", 7m,
            "City", "State", "Location"
        };
        var workbook = new WorkbookSnapshot(
            "malformed-stock.xlsx",
            1,
            new string('d', 64),
            [new("Stock", 1, StockImportProfiles.VariantStockLedgerHeaders,
                [new(2, values.Select(value => new WorkbookCell(value)).ToArray())])]);

        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);

        Assert.False(inspection.Accepted);
        Assert.Null(inspection.AcceptedImport);
        Assert.Equal(1, inspection.StagedRows);
        Assert.Contains(inspection.Diagnostics, diagnostic =>
            diagnostic.Code == "VALUE_INVALID" &&
            diagnostic.Severity == ImportDiagnosticSeverity.Blocker &&
            diagnostic.RowNumber == 2 &&
            diagnostic.ColumnName == "TRANS_QTY");
    }

    [Fact]
    public void Envelope_is_factory_constructed_and_exposes_only_get_only_snapshots()
    {
        var headers = RetailSalesProfiles.R025Headers.ToArray();
        var cells = headers.Select(header => new WorkbookCell(header switch
        {
            "TRANS_TYPE" => "INV",
            "STORE CODE" => "STORE",
            "ITEMNUMBER" => "ITEM",
            "INVNUMBER" => "DOC",
            "INVDATE" => new DateTime(2026, 8, 25),
            "QTY" => 1m,
            "NETAMOUNT" => 100m,
            "NETVALUE" => 118m,
            _ => null
        })).ToArray();
        var sheets = new[] { new WorkbookSheet("Sales", 1, headers, [new(2, cells)]) };
        var workbook = new WorkbookSnapshot("sales.xlsx", 1, new string('e', 64), sheets);

        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
        headers[0] = "TAMPERED";
        cells[0] = new WorkbookCell("TAMPERED");
        sheets[0] = new WorkbookSheet("Tampered", 1, ["TAMPERED"], []);

        Assert.Equal("TRANS_TYPE", accepted.Workbook.Sheets[0].Headers[0]);
        Assert.Equal("INV", accepted.Workbook.Sheets[0].Rows[0].Cells[0].Value);
        Assert.Equal("Sales", accepted.MatchedSheet.Name);
        Assert.Equal("INV", accepted.Staging.Rows[0].Values["source_transaction_type"]);
        Assert.Empty(typeof(MatchedImportEnvelope).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(typeof(MatchedImportEnvelope).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance),
            method => method.Name == "<Clone>$");
        Assert.All(typeof(MatchedImportEnvelope).GetProperties(), property => Assert.False(property.CanWrite));

        AssertReadOnly((IList)accepted.Workbook.Sheets);
        AssertReadOnly((IList)accepted.MatchedSheet.Headers);
        AssertReadOnly((IList)accepted.Staging.Rows);
        AssertReadOnly((IDictionary)accepted.Staging.Rows[0].Values);
        AssertReadOnly((IList)accepted.Diagnostics);
    }

    [Fact]
    public void Approved_registry_does_not_expose_a_castable_backing_array()
    {
        Assert.IsNotType<ImportProfile[]>(ApprovedImportProfileRegistry.All);
        var profiles = Assert.IsAssignableFrom<IList<ImportProfile>>(ApprovedImportProfileRegistry.All);

        Assert.True(profiles.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => profiles[0] = RetailSalesProfiles.R022);
        Assert.Same(RetailSalesProfiles.R025, ApprovedImportProfileRegistry.All[0]);
    }

    private static void AssertReadOnly(IList values)
    {
        Assert.True(values.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => values.Clear());
    }

    private static void AssertReadOnly(IDictionary values)
    {
        Assert.True(values.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => values.Clear());
    }
}
