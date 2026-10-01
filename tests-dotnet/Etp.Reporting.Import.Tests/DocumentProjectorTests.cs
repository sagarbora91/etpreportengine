using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using static Etp.Reporting.Import.Tests.ProjectorTestCatalogue;

namespace Etp.Reporting.Import.Tests;

public sealed class DocumentProjectorTests
{
    private static readonly DocumentProjector Projector = new();
    private static readonly DateOnly SaleDate = new(2026, 8, 29);

    [Fact]
    public void Genuine_repeats_keep_their_count()
    {
        // The 29 Aug WLMHW shape: 4 groups of 5 lines that differ only in STORETIMESTAMP.
        var rows = Enumerable.Range(0, 20).Select(i => Row(R025, i + 2, ("invoice_number", "INV-0829"),
            ("product_code", $"ITEM-{i / 5 + 1}"), ("source_store_timestamp", $"2026-08-29 11:{i:00}:00"))).ToArray();

        var projection = Projector.Project(new(R025, Store, Block(rows.Length), rows));

        var document = Assert.Single(projection.Documents);
        Assert.Equal(DocumentKey.ForDocument("R025", Store, SaleDate, "INV-0829"), document.Key);
        Assert.Equal(20, document.RowCount);
        Assert.Empty(document.SetAside);
        Assert.Null(document.HoldCode);
        Assert.DoesNotContain(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.StaleCopyCollapsed);
        Assert.All(document.Rows.GroupBy(row => row.Canonical.FactRowHash), group => Assert.Equal(5, group.Count()));
        Assert.Equal(4, document.Rows.Select(row => row.Canonical.FactRowHash).Distinct().Count());
        Assert.All(document.Rows.GroupBy(row => row.LineLabel![..row.LineLabel!.IndexOf(':')]),
            group => Assert.Equal(["1", "2", "3", "4", "5"], group.Select(row => row.LineLabel![(row.LineLabel!.IndexOf(':') + 1)..])));
    }

    public static TheoryData<string> CoreSalesFamilies => new() { "R025", "R022", "R003", "R013" };

    [Theory]
    [MemberData(nameof(CoreSalesFamilies))]
    public void Contact_only_copies_collapse(string reportCode)
    {
        // The 17 Aug shape: an earlier export's copy of an invoice (one contact number) and the later export's copy
        // (another) stacked in one block. Only the later copy is the export's own content.
        var family = Family(reportCode);
        var lines = family == R022 ? 1 : 2;
        SourceRow Line(int sheetRow, int line, string phone) => Row(family, sheetRow, ("invoice_number", "INV-0817"),
            ("transaction_date", new DateOnly(2026, 8, 17)), ("customer_phone", phone),
            (family == R022 ? "tender_cash" : "product_code", family == R022 ? 1000m : $"ITEM-{line}"));
        var stale = Enumerable.Range(1, lines).Select(line => Line(line + 1, line, "PHONE-A")).ToArray();
        var later = Enumerable.Range(1, lines).Select(line => Line(lines + line + 1, line, "PHONE-B")).ToArray();

        var projection = Projector.Project(new(family, Store, Block(lines * 2), [.. stale, .. later]));
        var alone = Assert.Single(Projector.Project(new(family, Store, Block(lines), later)).Documents);

        var document = Assert.Single(projection.Documents);
        Assert.Null(document.HoldCode);
        Assert.Equal(later.Select(row => row.Locator), document.Rows.Select(row => row.Source));
        Assert.Equal(stale.Select(row => row.Locator), document.SetAside.Select(row => row.Source));
        Assert.All(document.SetAside, row => Assert.Equal(RowDisposition.Collapsed, row.Disposition));
        Assert.Equal((alone.FactSha256, alone.AttributeSha256, alone.CanonicalSha256),
            (document.FactSha256, document.AttributeSha256, document.CanonicalSha256));
        Assert.Equal(alone.Rows.Select(row => row.LineLabel), document.Rows.Select(row => row.LineLabel));
        var collapsed = Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.StaleCopyCollapsed);
        Assert.Equal(ImportDiagnosticSeverity.Information, collapsed.Severity);
        Assert.Equal(lines, collapsed.Occurrences);
        Assert.Equal("INV-0817 2026-08-17", collapsed.DocumentRef);
        Assert.Equal(1, collapsed.BlockNo);
        Assert.DoesNotContain("PHONE", collapsed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_largest_partition_wins_and_ties_go_to_the_latest_rows()
    {
        // Two genuine repeats with one contact beat a single later copy with another.
        var rows = new[]
        {
            Row(R025, 2, ("customer_phone", "PHONE-A")), Row(R025, 3, ("customer_phone", "PHONE-A")),
            Row(R025, 4, ("customer_phone", "PHONE-B"))
        };

        var document = Assert.Single(Projector.Project(new(R025, Store, Block(3), rows)).Documents);

        Assert.Equal([2, 3], document.Rows.Select(row => row.Source.SourceRowNumber));
        Assert.Equal([4], document.SetAside.Select(row => row.Source.SourceRowNumber));
    }

    [Fact]
    public void Line_labels_over_kept_rows_never_give_a_stale_copy_the_first_label()
    {
        var later = Row(R025, 3, ("customer_phone", "PHONE-B"));
        var stale = Row(R025, 2, ("customer_phone", "PHONE-A"));

        var document = Assert.Single(Projector.Project(new(R025, Store, Block(2), [later, stale])).Documents);

        var kept = Assert.Single(document.Rows);
        Assert.Equal(later.Locator, kept.Source);
        Assert.EndsWith(":1", kept.LineLabel, StringComparison.Ordinal);
        Assert.Null(Assert.Single(document.SetAside).LineLabel);
    }

    [Fact]
    public void Line_labels_equal_planner_one_for_files_without_stale_copies()
    {
        foreach (var family in new[] { R025, R003, R013 })
        {
            var rows = new[]
            {
                Row(family, 2, ("invoice_number", "INV-1")), Row(family, 3, ("invoice_number", "INV-1")),
                Row(family, 4, ("invoice_number", "INV-2")), Row(family, 5, ("invoice_number", "INV-1"), ("product_code", "ITEM-2")),
                Row(family, 6, ("invoice_number", "INV-1"))
            };

            var projection = Projector.Project(new(family, Store, Block(rows.Length), rows));

            var labels = projection.Documents.SelectMany(document => document.Rows)
                .ToDictionary(row => row.Source.SourceRowNumber, row => row.LineLabel);
            Assert.Equal(PlannerOneLineKeys(rows), rows.Select(row => labels[row.Locator.SourceRowNumber]));
        }
    }

    [Fact]
    public void Stock_chain_numbers_rows_by_running_balance_in_any_order_and_keeps_exact_repeats()
    {
        // Openings 0/1/3/4/2 in file order (the package's shape), plus an exact repeat of the opening-3 row.
        decimal[] openings = [0m, 1m, 3m, 4m, 2m, 3m];
        SourceRow[] Ordered(IEnumerable<decimal> order) => order.Select((opening, i) => Row(StockLedger, i + 2,
            ("document_number", "STM-0829"), ("document_date", SaleDate), ("source_transaction_type", "STM Receipt"),
            ("opening_quantity", opening), ("transaction_quantity", 1m), ("closing_quantity", opening + 1m))).ToArray();

        var results = new[] { openings, Enumerable.Reverse(openings).ToArray(), openings.Order().ToArray() }.Select(order =>
        {
            var rows = Ordered(order);
            var projection = Projector.Project(new(StockLedger, Store, Block(rows.Length), rows));
            var document = Assert.Single(projection.Documents);
            Assert.Equal(6, document.RowCount);
            Assert.Empty(document.SetAside);
            var repeated = Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.StockRowRepeated);
            Assert.Equal(2, repeated.Occurrences);
            Assert.Equal(1, repeated.BlockNo);
            var byRow = rows.ToDictionary(row => row.Locator, row => (decimal)row.Values["opening_quantity"]!);
            return (document.FactSha256, document.CanonicalSha256,
                Chain: document.Rows.OrderBy(row => row.LineSeq).Select(row => (byRow[row.Source], row.LineSeq)).ToArray());
        }).ToArray();

        Assert.Equal([(0m, 1), (1m, 2), (2m, 3), (3m, 4), (3m, 5), (4m, 6)], results[0].Chain);
        Assert.All(results, result => Assert.Equal(results[0].Chain, result.Chain));
        Assert.All(results, result => Assert.Equal((results[0].FactSha256, results[0].CanonicalSha256), (result.FactSha256, result.CanonicalSha256)));
    }

    [Fact]
    public void Revenue_duplicate_that_differs_in_a_fact_holds_the_document()
    {
        var rows = new[]
        {
            Row(R022, 2, ("invoice_number", "INV-0900"), ("tender_cash", 1000m)),
            Row(R022, 3, ("invoice_number", "INV-0900"), ("tender_cash", 0m), ("tender_card", 1000m)),
            Row(R022, 4, ("invoice_number", "INV-0901"))
        };

        var projection = Projector.Project(new(R022, Store, Block(3), rows));

        var held = Assert.Single(projection.Documents, document => document.Key.KeyText == "2027|INV-0900");
        Assert.Equal(ImportCodes.InSourceConflict, held.HoldCode);
        Assert.Equal(2, held.RowCount);
        Assert.Null(Assert.Single(projection.Documents, document => document.Key.KeyText == "2027|INV-0901").HoldCode);
        var conflict = Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InSourceConflict);
        Assert.Equal(ImportDiagnosticSeverity.Blocker, conflict.Severity);
        Assert.Equal("INV-0900 2026-08-29", conflict.DocumentRef);
        Assert.Equal(2, conflict.Occurrences);
    }

    [Fact]
    public void Revenue_copies_that_differ_only_in_attributes_labels_or_timestamps_collapse_to_the_last_row()
    {
        var rows = new[]
        {
            Row(R022, 2, ("reference_invoice_number", "REF-1"), ("invoice_year", 2026L), ("source_store_timestamp", "a")),
            Row(R022, 3, ("reference_invoice_number", "REF-2"), ("invoice_year", 2027L), ("source_store_timestamp", "b"))
        };

        var document = Assert.Single(Projector.Project(new(R022, Store, Block(2), rows)).Documents);

        Assert.Null(document.HoldCode);
        Assert.Equal(3, Assert.Single(document.Rows).Source.SourceRowNumber);
        Assert.Equal("REF-2", document.Rows[0].Canonical.Attributes["reference_invoice_number"]);
    }

    public static TheoryData<string> AllCoreFamilies => new() { "R025", "R022", "R003", "R013", "STOCK_LEDGER", "CLOSING_STOCK", "R010" };

    [Theory]
    [MemberData(nameof(AllCoreFamilies))]
    public void Customer_store_and_time_fields_never_change_fact_sha256(string reportCode)
    {
        var family = Family(reportCode);
        var block = Block(2) with { SnapshotDate = new DateOnly(2026, 9, 7) };
        // Two lines of one document; R022 holds one row per invoice.
        var rows = family == R022
            ? new[] { Row(family, 2) }
            : new[] { Row(family, 2), Row(family, 3, (Has(family, "product_code") ? "product_code" : "itemnumber", "ITEM-2")) };
        var changed = rows.Select(row => row with
        {
            Values = family.Columns.Where(column => column.Role is ColumnRole.Descriptive or ColumnRole.Ignored or ColumnRole.Label)
                .Aggregate(new Dictionary<string, object?>(row.Values), (values, column) =>
                {
                    values[column.CanonicalField] = column.CanonicalField == "invoice_year" ? 2031L : "CHANGED";
                    return values;
                })
        }).ToArray();
        Assert.NotEmpty(family.ColumnsWithRole(ColumnRole.Descriptive));

        var before = Assert.Single(Projector.Project(new(family, Store, block, rows)).Documents);
        var after = Assert.Single(Projector.Project(new(family, Store, block, changed)).Documents);

        Assert.Equal(before.Key, after.Key);
        Assert.Equal((before.FactSha256, before.AttributeSha256, before.CanonicalSha256),
            (after.FactSha256, after.AttributeSha256, after.CanonicalSha256));
        Assert.Equal(before.Rows.Select(row => (row.LineSeq, row.LineLabel)), after.Rows.Select(row => (row.LineSeq, row.LineLabel)));

        var attribute = family.ColumnsWithRole(ColumnRole.Attribute).First().CanonicalField;
        var restated = Assert.Single(Projector.Project(new(family, Store, block,
            rows.Select(row => row with { Values = new Dictionary<string, object?>(row.Values) { [attribute] = "RESTATED" } }).ToArray())).Documents);
        Assert.Equal(before.FactSha256, restated.FactSha256);
        Assert.NotEqual(before.AttributeSha256, restated.AttributeSha256);
    }

    [Fact]
    public void Fact_sha256_hashes_kept_fact_rows_with_multiplicity_in_any_order()
    {
        var rows = new[] { Row(R025, 2), Row(R025, 3), Row(R025, 4, ("product_code", "ITEM-2")) };

        var document = Assert.Single(Projector.Project(new(R025, Store, Block(3), rows)).Documents);
        var reversed = Assert.Single(Projector.Project(new(R025, Store, Block(3), Enumerable.Reverse(rows).ToArray())).Documents);
        var fewer = Assert.Single(Projector.Project(new(R025, Store, Block(2), rows.Skip(1).ToArray())).Documents);

        Assert.Equal(FactCanonicalizer.Instance.MultisetHash(document.Rows.Select(row => row.Canonical.FactRowHash)), document.FactSha256);
        Assert.Equal(document.FactSha256, reversed.FactSha256);
        Assert.NotEqual(document.FactSha256, fewer.FactSha256);
        Assert.Equal(document.Rows.Select(row => row.LineLabel), reversed.Rows.Select(row => row.LineLabel));
    }

    [Fact]
    public void Invoice_key_uses_the_financial_year_of_the_date_and_notes_a_differing_year_label()
    {
        // A 1-April return that ETP labels with the previous year (OD-1).
        var rows = new[]
        {
            Row(R022, 2, ("invoice_number", "SR-0001"), ("transaction_date", new DateOnly(2026, 4, 1)), ("invoice_year", 2026L)),
            Row(R022, 3, ("invoice_number", "INV-0002"), ("transaction_date", new DateOnly(2026, 3, 31)), ("invoice_year", 2026L))
        };

        var projection = Projector.Project(new(R022, Store, Block(2), rows));

        Assert.Equal(["2027|SR-0001", "2026|INV-0002"], projection.Documents.Select(document => document.Key.KeyText));
        var differs = Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InvoiceYearDiffers);
        Assert.Equal(ImportDiagnosticSeverity.Information, differs.Severity);
        Assert.Equal((1, 2), (differs.Occurrences, differs.RowNumber));
    }

    [Fact]
    public void Documents_follow_their_scope()
    {
        var landing = Landing();
        var day = Projector.Project(new(landing, Store, Block(3), new[]
        {
            Row(landing, 2, ("invoicedate", new DateOnly(2026, 8, 28))), Row(landing, 3), Row(landing, 4, ("invnumber", "INV-0002"))
        }));
        Assert.Equal(["2026-08-28", "2026-08-29"], day.Documents.Select(document => document.Key.KeyText));
        Assert.All(day.Documents, document => Assert.Equal(DocumentScope.Date, document.Key.Scope));
        Assert.Equal([1, 2], day.Documents.Select(document => document.RowCount));
        Assert.All(day.Documents, document => Assert.Null(document.CanonicalSha256));

        var closing = Projector.Project(new(ClosingStock, Store, Block(2), new[]
        {
            Row(ClosingStock, 2, ("snapshot_date", new DateOnly(2026, 9, 1))), Row(ClosingStock, 3, ("snapshot_date", new DateOnly(2026, 9, 2)))
        }));
        Assert.Equal(["2026-09-01|CLOSING_STOCK", "2026-09-02|CLOSING_STOCK"], closing.Documents.Select(document => document.Key.KeyText));

        var bins = Projector.Project(new(R010, Store, Block(2) with { SnapshotDate = new DateOnly(2026, 9, 7) },
            new[] { Row(R010, 2, ("lotnumber", "LOT-1")), Row(R010, 3, ("uid", "UID-1")) }));
        var snapshot = Assert.Single(bins.Documents);
        Assert.Equal(DocumentKey.ForSnapshot("R010", Store, new DateOnly(2026, 9, 7)), snapshot.Key);
        Assert.Equal(["ITEM-1|LOT-1", "ITEM-1|UID-1"], snapshot.Rows.Select(row => row.RowKey));
        Assert.Equal(new DateOnly(2026, 9, 7), snapshot.DocumentDate);

        var period = Landing(DocumentScope.Period);
        var periodic = Projector.Project(new(period, Store,
            Block(2) with { PeriodFrom = new DateOnly(2026, 8, 1), PeriodTo = new DateOnly(2026, 8, 31), PeriodBasis = PeriodBasis.Declared },
            new[] { Row(period, 2), Row(period, 3) }));
        var whole = Assert.Single(periodic.Documents);
        Assert.Equal("2026-08-01..2026-08-31", whole.Key.KeyText);
        Assert.Equal(new DateOnly(2026, 8, 31), whole.PeriodTo);
        Assert.Equal(2, whole.RowCount);
    }

    [Fact]
    public void Rows_without_a_usable_date_or_number_are_held()
    {
        var rows = new[]
        {
            Row(R025, 2), Row(R025, 3, ("transaction_date", null)), Row(R025, 4, ("invoice_number", " "))
        };

        var projection = Projector.Project(new(R025, Store, Block(3), rows));

        Assert.Equal(1, Assert.Single(projection.Documents).RowCount);
        Assert.Equal([3, 4], projection.HeldRows.Select(row => row.Source.SourceRowNumber));
        Assert.All(projection.HeldRows, row => Assert.Equal(RowDisposition.Held, row.Disposition));
        var missing = Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.RowDateMissing);
        Assert.Equal((ImportDiagnosticSeverity.Warning, 2), (missing.Severity, missing.Occurrences));

        var undated = Projector.Project(new(R010, Store, Block(1), [Row(R010, 2)]));
        Assert.Empty(undated.Documents);
        Assert.Equal(ImportCodes.SnapshotDateUnknown, Assert.Single(undated.Diagnostics).Code);
    }

    [Fact]
    public void Virtual_rows_count_as_their_own_rows_after_physical_ones()
    {
        // A delta block: one physical row and the excluded repeat of it, mapped in ETP_Excluded.
        var physical = Row(R025, 2);
        var excluded = Row(R025, new RowLocator(1, ConsolidationContractLayout.ExcludedSheet, 2, IsVirtual: true));

        var document = Assert.Single(Projector.Project(new(R025, Store, Block(1), [excluded, physical])).Documents);

        Assert.Equal([physical.Locator, excluded.Locator], document.Rows.Select(row => row.Source));
        Assert.Equal([":1", ":2"], document.Rows.Select(row => row.LineLabel![row.LineLabel!.IndexOf(':')..]));
    }

    [Fact]
    public void Snapshot_items_keep_repeats_and_number_them_by_value()
    {
        var rows = new[]
        {
            Row(ClosingStock, 2, ("source_uid", null), ("batch_number", "LOT-1"), ("quantity", 2m)),
            Row(ClosingStock, 3, ("source_uid", null), ("batch_number", "LOT-1"), ("quantity", 1m)),
            Row(ClosingStock, 4, ("source_uid", null), ("batch_number", "LOT-1"), ("quantity", 1m))
        };

        var document = Assert.Single(Projector.Project(new(ClosingStock, Store, Block(3), rows)).Documents);

        Assert.Equal(3, document.RowCount);
        Assert.Equal([(3, 1), (4, 2), (2, 3)], document.Rows.OrderBy(row => row.LineSeq).Select(row => (row.Source.SourceRowNumber, row.LineSeq)));
        Assert.All(document.Rows, row => Assert.Equal("ITEM-1|LOT-1", row.RowKey));
    }

    private static EtpReportFamily Family(string reportCode) => reportCode switch
    {
        "R025" => R025,
        "R022" => R022,
        "R003" => R003,
        "R013" => R013,
        "STOCK_LEDGER" => StockLedger,
        "CLOSING_STOCK" => ClosingStock,
        "R010" => R010,
        _ => throw new ArgumentOutOfRangeException(nameof(reportCode))
    };

    /// <summary>
    /// Planner 1's labels, written out as <c>EtpInvoiceIdentity.LineKeys</c> computes them (over every row of the
    /// file, in file order), as an independent oracle. <c>PlannerOneLabelParityTests</c> checks against the real one.
    /// </summary>
    private static IReadOnlyList<string> PlannerOneLineKeys(IReadOnlyList<SourceRow> rows)
    {
        string[] keys = ["store_code", "invoice_year", "invoice_number", "transaction_date", "product_code",
            "source_transaction_type", "source_quantity", "source_net_amount", "source_net_value", "source_tax_amount",
            "cro_number", "scheme_discount", "user_discount", "pre_discount"];
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        return rows.Select(row =>
        {
            var hash = FactCanonicalizer.Instance.Hash(row.Values.Where(pair => keys.Contains(pair.Key, StringComparer.Ordinal)));
            occurrences[hash] = occurrences.GetValueOrDefault(hash) + 1;
            return $"{hash}:{occurrences[hash]}";
        }).ToArray();
    }
}
