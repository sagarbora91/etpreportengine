using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using static Etp.Reporting.Import.Tests.ProjectorTestCatalogue;

namespace Etp.Reporting.Import.Tests;

public sealed class FactCanonicalizerTests
{
    private static readonly FactCanonicalizer Canonicalizer = FactCanonicalizer.Instance;

    public static TheoryData<object?, string> FormatVectors => new()
    {
        { null, "" },
        { DBNull.Value, "" },
        { new DateOnly(2026, 8, 29), "2026-08-29" },
        { new DateTime(2026, 8, 29, 14, 49, 30), "2026-08-29" },
        { 12.5m, "12.5" },
        { 12.5000m, "12.5" },
        { 1000m, "1000" },
        { 1.23456m, "1.2346" },
        { 1.23445m, "1.2345" },
        { -2.00005m, "-2.0001" },
        { 0.00001m, "0" },
        { -0.0000m, "0" },
        { 2027L, "2027" },
        { 2027, "2027" },
        { "  INV-0001 ", "INV-0001" },
        { true, "True" }
    };

    [Theory]
    [MemberData(nameof(FormatVectors))]
    public void Values_print_as_today(object? value, string expected) => Assert.Equal(expected, Canonicalizer.Format(value));

    [Theory]
    [InlineData("recipient_state_code", "7", "07")]
    [InlineData("issue_state_code", 27L, "27")]
    [InlineData("issue_state_code", "MH", "MH")]
    [InlineData("state", "7", "7")]
    public void State_codes_print_with_two_digits(string field, object value, string expected) =>
        Assert.Equal(expected, Canonicalizer.FormatField(field, value));

    [Fact]
    public void Hash_vectors_are_pinned()
    {
        Assert.Equal("ea5b0a576611dc22f6e3cb1cecb1057720a3ff29614b83b7cadb4eb02933f63d",
            Canonicalizer.Hash([new("b", 2m), new("a", "x")]));
        Assert.Equal(Sha256("1:a:1:x\n1:b:1:2"), Canonicalizer.Hash([new("a", "x"), new("b", 2.0000m)]));
        Assert.Equal("b6161b131b62ec066b55cf8ddbad2faa673153998b3adda6f6fed9d230f9d966", Canonicalizer.Hash(
            [new("transaction_date", new DateOnly(2026, 8, 29)), new("source_quantity", 1.2500m), new("invoice_number", " INV-0001 ")]));
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Canonicalizer.Hash([]));
        Assert.Equal(1, Canonicalizer.ContentHashVersion);
    }

    [Fact]
    public void Multiset_hash_ignores_order_and_counts_repeats()
    {
        Assert.Equal(Canonicalizer.MultisetHash(["b", "a", "a"]), Canonicalizer.MultisetHash(["a", "b", "a"]));
        Assert.NotEqual(Canonicalizer.MultisetHash(["a", "b"]), Canonicalizer.MultisetHash(["a", "a", "b"]));
        Assert.Equal(Sha256("a\na\nb"), Canonicalizer.MultisetHash(["b", "a", "a"]));
        Assert.Equal(Sha256(""), Canonicalizer.MultisetHash([]));
    }

    [Fact]
    public void Each_role_moves_only_its_own_hash()
    {
        var row = Values(Row(R025, 2));
        var canonical = Canonicalizer.Canonicalize(R025, row);

        var timestamp = Canonicalizer.Canonicalize(R025, With(row, "source_store_timestamp", "2026-08-29 18:30:00"));
        Assert.Equal(canonical, timestamp with { Facts = canonical.Facts, Attributes = canonical.Attributes });
        Assert.Equal(canonical.Facts, timestamp.Facts);

        var customer = Canonicalizer.Canonicalize(R025, With(row, "customer_phone", "PHONE-B"));
        Assert.Equal((canonical.FactRowHash, canonical.AttributeHash), (customer.FactRowHash, customer.AttributeHash));
        Assert.NotEqual(canonical.DescriptiveHash, customer.DescriptiveHash);
        Assert.NotEqual(canonical.ContentHash, customer.ContentHash);

        var brand = Canonicalizer.Canonicalize(R025, With(row, "source_brand_name", "Other brand"));
        Assert.Equal((canonical.FactRowHash, canonical.DescriptiveHash), (brand.FactRowHash, brand.DescriptiveHash));
        Assert.NotEqual(canonical.AttributeHash, brand.AttributeHash);

        var amount = Canonicalizer.Canonicalize(R025, With(row, "source_net_value", 847.47m));
        Assert.NotEqual(canonical.FactRowHash, amount.FactRowHash);
        Assert.Equal((canonical.AttributeHash, canonical.DescriptiveHash), (amount.AttributeHash, amount.DescriptiveHash));

        var revenue = Values(Row(R022, 2));
        var label = Canonicalizer.Canonicalize(R022, With(revenue, "invoice_year", 2026L));
        Assert.Equal(Canonicalizer.Canonicalize(R022, revenue).FactRowHash, label.FactRowHash);
        Assert.NotEqual(Canonicalizer.Canonicalize(R022, revenue).ContentHash, label.ContentHash);
    }

    [Fact]
    public void Hashes_cover_exactly_the_roles_fields()
    {
        var row = Values(Row(R025, 2, ("invoice_number", "INV-0001"), ("transaction_date", new DateOnly(2026, 8, 29))));

        var canonical = Canonicalizer.Canonicalize(R025, row);

        Assert.Equal(Canonicalizer.Hash(Fields(R025, row, ColumnRole.Key, ColumnRole.Fact)), canonical.FactRowHash);
        Assert.Equal(Canonicalizer.Hash(Fields(R025, row, ColumnRole.Attribute)), canonical.AttributeHash);
        Assert.Equal(Canonicalizer.Hash(Fields(R025, row, ColumnRole.Descriptive)), canonical.DescriptiveHash);
        Assert.Equal("INV-0001", canonical.Facts["invoice_number"]);
        Assert.Equal("2026-08-29", canonical.Facts["transaction_date"]);
        Assert.Equal("847.46", canonical.Facts["source_net_value"]);
        Assert.DoesNotContain("customer_name", canonical.Facts.Keys);
        Assert.DoesNotContain("source_store_timestamp", canonical.Facts.Keys);
        Assert.Equal("Brand", canonical.Attributes["source_brand_name"]);
        // R025's Ignored columns are exactly its timestamps, so here the row hash equals today's landing content key.
        Assert.Equal(Canonicalizer.Hash(row.Where(field => !field.Key.Contains("timestamp", StringComparison.OrdinalIgnoreCase))),
            canonical.ContentHash);
        Assert.Equal(Canonicalizer.ContentKeyHash("R025", row), canonical.ContentHash);
    }

    [Fact]
    public void Content_key_hash_is_planner_ones_landing_content_key()
    {
        // As PhaseOneImportPersistence.ContentKeys has always computed it, written out independently.
        var sale = Values(Row(R025, 2));
        Assert.Equal(Canonicalizer.Hash(sale.Where(field => !field.Key.Contains("timestamp", StringComparison.OrdinalIgnoreCase))),
            Canonicalizer.ContentKeyHash("R025", sale));
        Assert.Equal(Canonicalizer.ContentKeyHash("R025", sale),
            Canonicalizer.ContentKeyHash("R025", With(sale, "source_store_timestamp", "2026-08-29 18:30:00")));

        // STOCK_LEDGER: only the ten stock identity fields, so brand, references and store details never move it.
        var stock = Values(Row(StockLedger, 2, ("document_number", "STM-1"), ("document_date", new DateOnly(2026, 8, 29))));
        string[] stockFields = ["store_code", "document_number", "document_date", "product_code", "source_transaction_type",
            "from_location", "to_location", "opening_quantity", "transaction_quantity", "closing_quantity"];
        Assert.Equal(Canonicalizer.Hash(stock.Where(field => stockFields.Contains(field.Key))), Canonicalizer.ContentKeyHash("STOCK_LEDGER", stock));
        foreach (var field in new[] { "brand", "hsn_code", "ref_documentnumber", "store_name", "city", "location" })
            Assert.Equal(Canonicalizer.ContentKeyHash("STOCK_LEDGER", stock), Canonicalizer.ContentKeyHash("STOCK_LEDGER", With(stock, field, "CHANGED")));
        Assert.NotEqual(Canonicalizer.ContentKeyHash("STOCK_LEDGER", stock),
            Canonicalizer.ContentKeyHash("STOCK_LEDGER", With(stock, "closing_quantity", 7m)));
        // The row hash covers every non-Ignored column, so for STOCK_LEDGER it is not the content key.
        Assert.NotEqual(Canonicalizer.ContentKeyHash("STOCK_LEDGER", stock), Canonicalizer.Canonicalize(StockLedger, stock).ContentHash);

        // The two-digit state code rule of ContentKeys.
        Assert.Equal(Canonicalizer.ContentKeyHash("R018", [new("recipient_state_code", "07")]),
            Canonicalizer.ContentKeyHash("R018", [new("recipient_state_code", 7L)]));
        Assert.Equal(Sha256("20:recipient_state_code:2:07"), Canonicalizer.ContentKeyHash("R018", [new("recipient_state_code", "7")]));
    }

    [Fact]
    public void Typed_landing_rows_canonicalise_like_the_staged_rows()
    {
        foreach (var (family, row) in new[]
        {
            (R025, Row(R025, 2, ("source_quantity", -1m), ("source_net_value", -847.456789m), ("source_transaction_type", "SR"), ("customer_phone", null))),
            (R022, Row(R022, 2, ("invoice_year", 2027L), ("tender_card", 12.5m))),
            (R003, Row(R003, 2, ("invoice_ref_date", new DateOnly(2026, 7, 1)))),
            (StockLedger, Row(StockLedger, 2, ("document_number", "STM-1"), ("document_date", new DateOnly(2026, 8, 29)))),
            (ClosingStock, Row(ClosingStock, 2, ("snapshot_date", new DateOnly(2026, 9, 1)), ("source_uid", "UID-1")))
        })
        {
            var staged = Values(row);
            var landed = Landed(family, staged);

            var expected = Canonicalizer.Canonicalize(family, staged);
            var actual = Canonicalizer.Canonicalize(family, landed);
            Assert.Equal((expected.FactRowHash, expected.AttributeHash, expected.DescriptiveHash, expected.ContentHash),
                (actual.FactRowHash, actual.AttributeHash, actual.DescriptiveHash, actual.ContentHash));
            Assert.Equal(expected.Facts, actual.Facts);
            Assert.Equal(expected.Attributes, actual.Attributes);

            var snapshotDate = new DateOnly(2026, 9, 1);
            Assert.Equal(CanonicalHashes(family, staged, snapshotDate),
                CanonicalHashes(family, StagedValues.Normalize(family, landed), snapshotDate));
        }
    }

    [Fact]
    public void Typed_landing_rows_project_like_the_staged_rows()
    {
        var rows = new[]
        {
            Row(R025, 2, ("invoice_number", "INV-0001"), ("transaction_date", new DateOnly(2026, 8, 29))),
            Row(R025, 3, ("invoice_number", "INV-0001"), ("transaction_date", new DateOnly(2026, 8, 29))),
            Row(R025, 4, ("invoice_number", "INV-0001"), ("transaction_date", new DateOnly(2026, 8, 29)), ("product_code", "ITEM-2"))
        };
        var landed = rows.Select(row => row with { Values = Landed(R025, Values(row)) }).ToArray();
        var projector = new DocumentProjector();

        var expected = Assert.Single(projector.Project(new(R025, Store, Block(3), rows)).Documents);
        var actual = Assert.Single(projector.Project(new(R025, Store, Block(3), landed)).Documents);

        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.DocumentDate, actual.DocumentDate);
        Assert.Equal((expected.FactSha256, expected.AttributeSha256, expected.CanonicalSha256), (actual.FactSha256, actual.AttributeSha256, actual.CanonicalSha256));
        Assert.Equal(expected.Rows.Select(row => row.LineLabel), actual.Rows.Select(row => row.LineLabel));
        Assert.Equal(expected.Rows.Select(row => row.Canonical.FactRowHash), actual.Rows.Select(row => row.Canonical.FactRowHash));
    }

    [Fact]
    public void Canonical_rows_are_named_by_the_fact_tables_columns()
    {
        var sale = Values(Row(R025, 2, ("invoice_number", "INV-0001"), ("transaction_date", new DateOnly(2026, 8, 29))));
        var line = Assert.Single(CanonicalFactProjection.Rows(R025, Store, null, sale));
        Assert.Equal("sales_lines", line[CanonicalFactProjection.FactTable]);
        Assert.Equal(1000m, line["source_gross_amount"]);
        Assert.Equal(847.46m, line["source_net_amount"]);
        Assert.Equal("INV-0001", line["document_number"]);
        Assert.DoesNotContain("source_brand_code", line.Keys);
        Assert.DoesNotContain("customer_name", line.Keys);

        var revenue = CanonicalFactProjection.Rows(R022, Store, null, Values(Row(R022, 2, ("tender_card", 250m), ("tender_cash", 750m))));
        Assert.Equal(["sales_invoice_controls", "sales_tenders", "sales_tenders"], revenue.Select(row => row[CanonicalFactProjection.FactTable]));
        Assert.Equal(["CASH", "CARD"], revenue.Skip(1).Select(row => row["tender_type"]));

        var buyBack = Assert.Single(CanonicalFactProjection.Rows(R013, Store, null,
            Values(Row(R013, 2, ("source_transaction_type", "BC"), ("source_quantity", 1m), ("source_net_value", 847.46m)))));
        Assert.Equal(-1m, buyBack["source_quantity"]);
        Assert.Equal(-847.46m, buyBack["source_net_value"]);
        Assert.Equal("R013", buyBack["enrichment_type"]);
        Assert.DoesNotContain("staff_name", buyBack.Keys);

        var bin = Assert.Single(CanonicalFactProjection.Rows(R010, Store, new DateOnly(2026, 9, 7),
            Values(Row(R010, 2, ("closingbalance", null), ("lotnumber", "LOT-1")))));
        Assert.Equal(0m, bin["quantity"]);
        Assert.Equal("R010", bin["source_report_code"]);
        Assert.Equal("LOT-1", bin["batch_number"]);
        Assert.Equal(new DateOnly(2026, 9, 7), bin["snapshot_date"]);

        Assert.Empty(CanonicalFactProjection.Rows(Landing(), Store, null, Values(Row(Landing(), 2))));
    }

    private static IReadOnlyList<string> CanonicalHashes(EtpReportFamily family, IReadOnlyDictionary<string, object?> values, DateOnly snapshotDate) =>
        CanonicalFactProjection.Rows(family, Store, snapshotDate, values).Select(Canonicalizer.Hash).ToArray();

    private static IEnumerable<KeyValuePair<string, object?>> Fields(EtpReportFamily family, IReadOnlyDictionary<string, object?> values, params ColumnRole[] roles) =>
        family.Columns.Where(column => roles.Contains(column.Role))
            .Select(column => new KeyValuePair<string, object?>(column.CanonicalField, values[column.CanonicalField]));

    private static IReadOnlyDictionary<string, object?> Values(SourceRow row) => row.Values;

    private static IReadOnlyDictionary<string, object?> With(IReadOnlyDictionary<string, object?> values, string field, object? value) =>
        new Dictionary<string, object?>(values, StringComparer.Ordinal) { [field] = value };

    /// <summary>The row as SqlDataReader returns it from the family's landing table (migration 0018).</summary>
    internal static IReadOnlyDictionary<string, object?> Landed(EtpReportFamily family, IReadOnlyDictionary<string, object?> staged)
    {
        var landed = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["etp_row_id"] = 17L, ["import_file_id"] = 3L, ["source_lineage_id"] = 41L, ["content_key"] = "0123abcd:1"
        };
        foreach (var (field, value) in staged)
            landed[field] = value switch
            {
                null => DBNull.Value,
                DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                // decimal(19,4): rounded, and read back with four places.
                decimal number => decimal.Round(number, 4, MidpointRounding.AwayFromZero) + 0.0000m,
                long number => (int)number,
                _ => value
            };
        return landed;
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
