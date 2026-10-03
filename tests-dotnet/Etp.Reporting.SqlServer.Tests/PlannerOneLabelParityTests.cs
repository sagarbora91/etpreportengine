using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public void Line_keys_keep_the_vectors_planner_one_shipped_in_1_9_2()
    {
        // Literal labels of the 1.9.2 code (computed outside .NET with SHA-256): planner 1 and the projector share
        // PlannerOneLabels, so the parity test above cannot see a change to its field list. A dropped or renamed line
        // field changes every stored line_identifier and enrichment content_key; these vectors catch it.
        var line = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["store_code"] = "TST01", ["invoice_year"] = 2027L, ["invoice_number"] = "INV-0001",
            ["transaction_date"] = new DateOnly(2026, 8, 29), ["product_code"] = "ITEM-1", ["source_transaction_type"] = "INV",
            ["source_quantity"] = 1.0000m, ["source_net_amount"] = 1000m, ["source_net_value"] = 847.4576m,
            ["source_tax_amount"] = 152.5424m, ["cro_number"] = "CRO-1", ["scheme_discount"] = 0m, ["user_discount"] = 12.5m,
            ["pre_discount"] = 0m,
            // Not line fields: never hashed.
            ["customer_name"] = "Synthetic customer A", ["hsn_code"] = "91021100", ["source_store_timestamp"] = "2026-08-29 11:00:00"
        };
        var other = new Dictionary<string, object?>(line, StringComparer.Ordinal) { ["product_code"] = "ITEM-2" };

        var labels = EtpInvoiceIdentity.LineKeys([new(2, line), new(3, line), new(4, other)]);

        const string first = "92711aaac75abbcf8b3859b99c10a86e8f2b8d1148b235c04f5094e816b04c75";
        const string second = "c90f302dda3404b360aae3e766e5d0ef764060ef3c04ecd45d35f79563d23813";
        Assert.Equal([(2, $"{first}:1"), (3, $"{first}:2"), (4, $"{second}:1")], labels.OrderBy(label => label.Key).Select(label => (label.Key, label.Value)));
    }

    [Theory]
    [InlineData("R025")]
    [InlineData("R003")]
    [InlineData("R013")]
    public void Line_keys_equal_the_1_9_2_computation_over_each_family_staged_rows(string reportCode)
    {
        var family = EtpReportFamilyRegistry.Resolve(reportCode);
        var rows = new[]
        {
            Staged(family, 2, "INV-1", "ITEM-1", 1m, "11:00"), Staged(family, 3, "INV-1", "ITEM-1", 1m, "11:01"),
            Staged(family, 4, "INV-2", "ITEM-2", -1m, "11:02")
        };

        Assert.Equal(LineKeys192(rows).OrderBy(label => label.Key).Select(label => (label.Key, label.Value)),
            EtpInvoiceIdentity.LineKeys(rows).OrderBy(label => label.Key).Select(label => (label.Key, label.Value)));
    }

    // EtpInvoiceIdentity.LineKeys as 1.9.2 shipped it, kept here as an independent copy of its field list and text rules.
    private static Dictionary<int, string> LineKeys192(IEnumerable<StagedImportRow> rows)
    {
        string[] keys = ["store_code", "invoice_year", "invoice_number", "transaction_date", "product_code",
            "source_transaction_type", "source_quantity", "source_net_amount", "source_net_value", "source_tax_amount",
            "cro_number", "scheme_discount", "user_discount", "pre_discount"];
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new Dictionary<int, string>();
        foreach (var row in rows)
        {
            var hash = ContentHash192(row.Values.Where(x => keys.Contains(x.Key, StringComparer.Ordinal)));
            occurrences.TryGetValue(hash, out var sequence);
            occurrences[hash] = ++sequence;
            result[row.SourceRowNumber] = $"{hash}:{sequence}";
        }
        return result;

        static string ContentHash192(IEnumerable<KeyValuePair<string, object?>> values) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
                values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x =>
                    $"{x.Key.Length}:{x.Key}:{Format(x.Value).Length}:{Format(x.Value)}")))));

        static string Format(object? value) => value switch
        {
            null => "",
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal d => decimal.Round(d, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()?.Trim() ?? ""
        };
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
