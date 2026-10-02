using Etp.Reporting.Application.Imports;
using Etp.Reporting.Domain.Imports;
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
        // A document hold holds the document, never the file.
        Assert.Equal(ImportDiagnosticSeverity.Warning, conflict.Severity);
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

        var projection = Projector.Project(new(R022, Store, Block(2), rows));
        var document = Assert.Single(projection.Documents);

        Assert.Null(document.HoldCode);
        Assert.Equal(3, Assert.Single(document.Rows).Source.SourceRowNumber);
        Assert.Equal("REF-2", document.Rows[0].Canonical.Attributes["reference_invoice_number"]);
        var collapsed = Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.StaleCopyCollapsed);
        Assert.Contains("reference", collapsed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("REF-", collapsed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invoice_whose_lines_carry_two_dates_is_held_for_sales_but_not_for_enrichments()
    {
        // One invoice number in one export with lines dated 29 and 30 Aug: a sales header holds one date.
        SourceRow[] Lines(EtpReportFamily family) =>
        [
            Row(family, 2, ("invoice_number", "INV-X"), ("transaction_date", SaleDate)),
            Row(family, 3, ("invoice_number", "INV-X"), ("transaction_date", new DateOnly(2026, 8, 30)), ("product_code", "ITEM-2")),
            Row(family, 4, ("invoice_number", "INV-Y"))
        ];

        var sales = Projector.Project(new(R025, Store, Block(3), Lines(R025)));
        var held = Assert.Single(sales.Documents, document => document.Key.KeyText == "2027|INV-X");
        Assert.Equal(ImportCodes.InSourceConflict, held.HoldCode);
        Assert.Equal(2, held.RowCount);
        Assert.Null(Assert.Single(sales.Documents, document => document.Key.KeyText == "2027|INV-Y").HoldCode);
        var conflict = Assert.Single(sales.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InSourceConflict);
        Assert.Equal((ImportDiagnosticSeverity.Warning, 2), (conflict.Severity, conflict.Occurrences));

        var enrichments = Projector.Project(new(R013, Store, Block(3), Lines(R013)));
        Assert.All(enrichments.Documents, document => Assert.Null(document.HoldCode));
        Assert.DoesNotContain(enrichments.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InSourceConflict);
    }

    [Fact]
    public void Stock_chain_of_an_issue_runs_down_and_breaks_ties_on_the_reference()
    {
        // An STM Issue (transaction -1): openings 4/3/2/1 run downwards whatever the file order; two rows equal but for
        // REF_DOCUMENTNUMBER take their order from it.
        SourceRow Issue(int sheetRow, decimal opening, string product = "ITEM-1", string? reference = null) => Row(StockLedger, sheetRow,
            ("document_number", "STM-0830"), ("document_date", SaleDate), ("source_transaction_type", "STM Issue"), ("product_code", product),
            ("opening_quantity", opening), ("transaction_quantity", -1m), ("closing_quantity", opening - 1m), ("ref_documentnumber", reference));
        SourceRow[] Rows(decimal[] openings, string[] references) =>
        [
            .. openings.Select((opening, i) => Issue(i + 2, opening)),
            Issue(openings.Length + 2, 5m, "ITEM-9", references[0]), Issue(openings.Length + 3, 5m, "ITEM-9", references[1])
        ];

        foreach (var rows in new[] { Rows([2m, 4m, 1m, 3m], ["REF-B", "REF-A"]), Rows([1m, 3m, 4m, 2m], ["REF-A", "REF-B"]) })
        {
            var document = Assert.Single(Projector.Project(new(StockLedger, Store, Block(rows.Length), rows)).Documents);
            var byRow = rows.ToDictionary(row => row.Locator, row => (Product: (string)row.Values["product_code"]!,
                Opening: (decimal)row.Values["opening_quantity"]!, Reference: row.Values["ref_documentnumber"] as string));

            Assert.Equal([(4m, 1), (3m, 2), (2m, 3), (1m, 4)], document.Rows.Where(row => byRow[row.Source].Product == "ITEM-1")
                .OrderBy(row => row.LineSeq).Select(row => (byRow[row.Source].Opening, row.LineSeq)));
            Assert.Equal([("REF-A", 1), ("REF-B", 2)], document.Rows.Where(row => byRow[row.Source].Product == "ITEM-9")
                .OrderBy(row => row.LineSeq).Select(row => (byRow[row.Source].Reference, row.LineSeq)));
        }
    }

    [Fact]
    public void A_revenue_row_its_fact_tables_cannot_store_is_held_and_the_block_goes_ahead()
    {
        var rows = new[]
        {
            Row(R022, 2, ("invoice_number", "INV-0900")),
            Row(R022, 3, ("invoice_number", "INV-0901"), ("source_net_value", null))
        };

        var projection = Projector.Project(new(R022, Store, Block(2), rows));

        Assert.Equal("2027|INV-0900", Assert.Single(projection.Documents).Key.KeyText);
        var held = Assert.Single(projection.HeldRows);
        Assert.Equal((3, RowDisposition.Held), (held.Source.SourceRowNumber, held.Disposition));
        // Its own code: the row has a date, so ROW_DATE_MISSING ("no usable date") would tell the Owner the wrong reason.
        Assert.DoesNotContain(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.RowDateMissing);
        var missing = Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.RowFactValueMissing);
        Assert.Equal((ImportDiagnosticSeverity.Warning, 1, 3), (missing.Severity, missing.Occurrences, missing.RowNumber));
        Assert.Equal(ImportIssueSeverity.Warning, ImportCodes.DefaultSeverity(ImportCodes.RowFactValueMissing));
        Assert.DoesNotContain("date", ImportDiagnosticCatalogue.Template(ImportCodes.RowFactValueMissing), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_document_with_a_row_its_fact_tables_cannot_store_is_held_not_applied_without_it()
    {
        // INV-0900's second row differs in a fact and lacks its invoice quantity. Projected without it, the first row
        // would pass as the whole invoice and no conflict would be raised.
        var rows = new[]
        {
            Row(R022, 2, ("invoice_number", "INV-0900")),
            Row(R022, 3, ("invoice_number", "INV-0900"), ("tender_cash", 0m), ("tender_card", 1000m), ("source_invoice_quantity", null)),
            Row(R022, 4, ("invoice_number", "INV-0901"))
        };

        var projection = Projector.Project(new(R022, Store, Block(3), rows));

        var held = Assert.Single(projection.Documents, document => document.Key.KeyText == "2027|INV-0900");
        Assert.Equal(ImportCodes.InSourceConflict, held.HoldCode);
        Assert.Equal([2], held.Rows.Select(row => row.Source.SourceRowNumber));
        Assert.Null(Assert.Single(projection.Documents, document => document.Key.KeyText == "2027|INV-0901").HoldCode);
        Assert.Equal([3], projection.HeldRows.Select(row => row.Source.SourceRowNumber));
        var conflict = Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InSourceConflict);
        Assert.Equal((ImportDiagnosticSeverity.Warning, "INV-0900 2026-08-29", 1, 3),
            (conflict.Severity, conflict.DocumentRef, conflict.Occurrences, conflict.RowNumber));
        Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.RowFactValueMissing);
    }

    [Fact]
    public void A_virtual_row_never_wins_a_tie_over_a_physical_row()
    {
        // One physical row and one virtual row, equal on facts but with different contacts: a tie the physical row wins,
        // whatever the map row's number.
        var physical = Row(R025, 2, ("customer_phone", "PHONE-A"));
        var excluded = Row(R025, new RowLocator(1, ConsolidationContractLayout.ExcludedSheet, 1, IsVirtual: true), ("customer_phone", "PHONE-B"));

        var document = Assert.Single(Projector.Project(new(R025, Store, Block(1), [excluded, physical])).Documents);
        Assert.Equal([physical.Locator], document.Rows.Select(row => row.Source));
        Assert.Equal([excluded.Locator], document.SetAside.Select(row => row.Source));

        var revenue = Row(R022, 2, ("reference_invoice_number", "REF-1"));
        var revenueCopy = Row(R022, new RowLocator(1, ConsolidationContractLayout.ExcludedSheet, 9, IsVirtual: true), ("reference_invoice_number", "REF-2"));
        var invoice = Assert.Single(Projector.Project(new(R022, Store, Block(1), [revenueCopy, revenue])).Documents);
        Assert.Equal(revenue.Locator, Assert.Single(invoice.Rows).Source);
    }

    [Fact]
    public void Day_scope_copies_that_differ_only_in_a_descriptive_value_collapse_whatever_the_field_names()
    {
        // Spec 7.2: every Multiset family partitions by its Descriptive values. Which families collapse is the
        // catalogue's decision (roles, under RulesetVersion), never a guess from field names: R009, R014, R026 and R027
        // hold no invoice number among their facts, and a store renamed between two exports is still a stale copy.
        var r009 = EtpReportFamilyRegistry.Resolve("R009");
        foreach (var (family, date) in new[] { (Landing(numberIsFact: false), "invoicedate"), (Landing(), "invoicedate"), (r009, "transactiondate") })
        {
            var rows = new[]
            {
                Row(family, 2, (date, SaleDate), ("store_name", "Old store name")),
                Row(family, 3, (date, SaleDate), ("store_name", "New store name"))
            };

            var projection = Projector.Project(new(family, Store, Block(2), rows));

            var day = Assert.Single(projection.Documents);
            Assert.Equal([3], day.Rows.Select(row => row.Source.SourceRowNumber));
            Assert.Equal([2], day.SetAside.Select(row => row.Source.SourceRowNumber));
            Assert.Single(projection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.StaleCopyCollapsed);
        }
    }

    [Fact]
    public void Snapshot_row_keys_are_the_catalogue_row_key_text()
    {
        // One implementation (EtpFamilyIdentityFields.RowKeyText) for incoming and stored rows, or rule 14 never pairs them.
        var rows = new[]
        {
            Row(ClosingStock, 2, ("batch_number", "lot-1")),
            Row(ClosingStock, 3, ("source_uid", "uid-9"), ("batch_number", "lot-1")),
            Row(ClosingStock, 4, ("ean", "EAN-1"))
        };

        var document = Assert.Single(Projector.Project(new(ClosingStock, Store, Block(3), rows)).Documents);

        Assert.Equal(["ITEM-1|LOT-1", "ITEM-1|UID-9", "ITEM-1|EAN-1"],
            document.Rows.OrderBy(row => row.Source.SourceRowNumber).Select(row => row.RowKey));
        Assert.All(document.Rows, row => Assert.Equal(ClosingStock.Identity!.RowKeyText(row.Canonical.Facts), row.RowKey));

        // The COALESCE spelling is read the same way everywhere, in any case.
        Assert.Equal(["source_uid", "batch_number", "ean"], EtpFamilyIdentityFields.RowKeyAlternatives("coalesce(source_uid,batch_number,ean)"));
        var lower = ClosingStock with { Identity = ClosingStock.Identity! with { RowKey = ["product_code", "coalesce(source_uid,batch_number,ean)"] } };
        Assert.Equal(document.Rows.Select(row => row.RowKey),
            Assert.Single(Projector.Project(new(lower, Store, Block(3), rows)).Documents).Rows.Select(row => row.RowKey));
    }

    public static TheoryData<string> ShippedRetailFamilies
    {
        get
        {
            var codes = new TheoryData<string>();
            foreach (var family in EtpReportFamilyRegistry.Families.Where(family => family.BusinessUnit == BusinessUnit.Retail))
                codes.Add(family.ReportCode);
            return codes;
        }
    }

    [Theory]
    [MemberData(nameof(ShippedRetailFamilies))]
    public void Every_shipped_retail_family_projects_with_its_own_roles_and_identity(string reportCode)
    {
        // The catalogue as shipped, not ProjectorTestCatalogue's overrides: its scopes, keys and dates must project.
        var family = EtpReportFamilyRegistry.Resolve(reportCode);
        var date = new DateOnly(2026, 8, 29);
        var block = Block(2) with { SnapshotDate = date, PeriodFrom = date, PeriodTo = date, PeriodBasis = PeriodBasis.Declared };
        SourceRow[] Rows(Func<EtpSourceColumn, object?> value) => [.. Enumerable.Range(2, 2).Select(sheetRow => new SourceRow(
            new RowLocator(1, Sheet, sheetRow), family.Columns.ToDictionary(column => column.CanonicalField, value, StringComparer.Ordinal)))];
        object? Synthetic(EtpSourceColumn column, bool changed) => column.DataType switch
        {
            CanonicalDataType.Decimal => changed ? 2m : 1m,
            CanonicalDataType.Date => changed ? date.AddDays(-3) : date,
            CanonicalDataType.Integer => changed ? 2031L : 2027L,
            CanonicalDataType.Boolean => !changed,
            _ => column.CanonicalField == "store_code" ? Store : changed ? "CHANGED" : "X"
        };
        bool Unseen(EtpSourceColumn column) => column.Role is ColumnRole.Descriptive or ColumnRole.Ignored;

        var projection = Projector.Project(new(family, Store, block, Rows(column => Synthetic(column, false))));
        var changed = Projector.Project(new(family, Store, block, Rows(column => Synthetic(column, Unseen(column)))));

        Assert.Empty(projection.HeldRows);
        var document = Assert.Single(projection.Documents);
        Assert.Equal(family.Identity!.Scope, document.Key.Scope);
        Assert.Null(document.HoldCode);
        Assert.Equal(2, document.RowCount + document.SetAside.Count);
        Assert.Equal(family.Identity.HasTypedFacts, document.CanonicalSha256 is not null);
        var same = Assert.Single(changed.Documents);
        Assert.Equal((document.Key, document.FactSha256, document.CanonicalSha256), (same.Key, same.FactSha256, same.CanonicalSha256));
    }

    [Fact]
    public void Landing_snapshot_items_are_numbered_by_their_fact_values_in_any_order()
    {
        var family = Landing(DocumentScope.Snapshot, "Block", rowKey: ["invnumber"]);
        var block = Block(3) with { SnapshotDate = new DateOnly(2026, 9, 7) };
        SourceRow[] Rows(decimal[] values) => values.Select((value, i) => Row(family, i + 2, ("netvalue", value))).ToArray();

        foreach (var rows in new[] { Rows([30m, 10m, 20m]), Rows([20m, 30m, 10m]) })
        {
            var document = Assert.Single(Projector.Project(new(family, Store, block, rows)).Documents);
            var values = rows.ToDictionary(row => row.Locator, row => (decimal)row.Values["netvalue"]!);
            Assert.Equal([(10m, 1), (20m, 2), (30m, 3)], document.Rows.OrderBy(row => row.LineSeq).Select(row => (values[row.Source], row.LineSeq)));
            Assert.All(document.Rows, row => Assert.Equal("INV-0001", row.RowKey));
        }
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
