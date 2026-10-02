using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// Planner 2 labels the sales lines and enrichments it projects exactly as planner 1 does (spec 7.1), so for a
/// file without stale copies the projector's labels equal <see cref="EtpInvoiceIdentity.LineKeys"/> row for row.
/// </summary>
public sealed class PlannerOneLabelParityTests
{
    [Theory]
    [InlineData("R025", FamilyRoute.Sales)]
    [InlineData("R003", FamilyRoute.Enrichment)]
    [InlineData("R013", FamilyRoute.Enrichment)]
    public void Projected_labels_equal_planner_one_line_keys(string reportCode, FamilyRoute route)
    {
        var registered = EtpReportFamilyRegistry.Resolve(reportCode);
        var family = registered with
        {
            Columns = registered.Columns.Select(column => column with
            {
                Role = column.CanonicalField.Contains("timestamp", StringComparison.Ordinal) ? ColumnRole.Ignored
                    : column.CanonicalField.StartsWith("customer", StringComparison.Ordinal) ? ColumnRole.Descriptive
                    : ColumnRole.Fact
            }).ToArray(),
            Identity = new() { Scope = DocumentScope.Document, DocumentKey = ["invoice_number"], YearRule = YearRule.FinancialYearOfPrimaryDate, Route = route }
        };
        // Synthetic rows: two invoices, a genuine repeat, a repeat that differs only in its timestamp, a second product.
        var rows = new[]
        {
            Staged(family, 2, "INV-1", "ITEM-1", 1m, "11:00"), Staged(family, 3, "INV-1", "ITEM-1", 1m, "11:00"),
            Staged(family, 4, "INV-2", "ITEM-1", 1m, "11:01"), Staged(family, 5, "INV-1", "ITEM-2", 2m, "11:02"),
            Staged(family, 6, "INV-1", "ITEM-1", 1m, "11:03"), Staged(family, 7, "INV-2", "ITEM-1", -1m, "11:04")
        };

        var expected = EtpInvoiceIdentity.LineKeys(rows);
        var projection = new DocumentProjector().Project(new(family, "TST01",
            new(1, "Sheet1", 2, 7, rows.Length, BlockCompleteness.Complete, BlockOrigin.Raw, ExportTime.AtMinute(new DateTime(2026, 9, 29, 14, 49, 0))),
            rows.Select(row => new SourceRow(new(1, "Sheet1", row.SourceRowNumber), row.Values)).ToArray()));

        var actual = projection.Documents.SelectMany(document => document.Rows)
            .ToDictionary(row => row.Source.SourceRowNumber, row => row.LineLabel);
        Assert.Equal(rows.Length, actual.Count);
        Assert.All(rows, row => Assert.Equal(expected[row.SourceRowNumber], actual[row.SourceRowNumber]));
    }

    [Fact]
    public void Planner_one_content_hash_is_the_shared_canonicaliser_and_keeps_its_vectors()
    {
        // Vectors of the text rules planner 1 shipped in 1.9.2 (checked with sha256sum), before they moved into
        // FactCanonicalizer.
        Assert.Equal("ea5b0a576611dc22f6e3cb1cecb1057720a3ff29614b83b7cadb4eb02933f63d",
            EtpInvoiceIdentity.ContentHash([new("b", 2m), new("a", "x")]));
        Assert.Equal("b6161b131b62ec066b55cf8ddbad2faa673153998b3adda6f6fed9d230f9d966", EtpInvoiceIdentity.ContentHash(
            [new("transaction_date", new DateOnly(2026, 8, 29)), new("source_quantity", 1.2500m), new("invoice_number", " INV-0001 ")]));
        KeyValuePair<string, object?>[] values = [new("invoice_number", "INV-1"), new("source_net_value", 847.46m)];
        Assert.Equal(Etp.Reporting.Import.Identity.FactCanonicalizer.Instance.Hash(values), EtpInvoiceIdentity.ContentHash(values));
    }

    private static StagedImportRow Staged(EtpReportFamily family, int row, string invoice, string product, decimal quantity, string time)
    {
        var values = family.Columns.ToDictionary(column => column.CanonicalField, column => column.DataType switch
        {
            CanonicalDataType.Decimal => (object?)100m,
            CanonicalDataType.Date => new DateOnly(2026, 8, 29),
            CanonicalDataType.Integer => 2027L,
            _ => (object?)"X"
        }, StringComparer.Ordinal);
        values["source_transaction_type"] = "INV";
        values["store_code"] = "TST01";
        values["invoice_number"] = invoice;
        values["product_code"] = product;
        values["source_quantity"] = quantity;
        foreach (var column in family.Columns.Where(column => column.CanonicalField.Contains("timestamp", StringComparison.Ordinal)))
            values[column.CanonicalField] = $"2026-08-29 {time}:00";
        return new(row, values);
    }
}
