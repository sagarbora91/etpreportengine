using System.Globalization;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Stock;

namespace Etp.Reporting.Import.Documents;

/// <summary>
/// Turns one block's physical and virtual rows into documents and observations (spec 7, 9 step 5). Each row is
/// first put in staged form (<see cref="StagedValues"/>), so landed rows re-project exactly like the staged rows they
/// came from, then canonicalised under the family's roles, placed in its document by the family's scope, and the
/// family's row rule (spec 7.2) decides which rows the observation keeps:
/// <list type="bullet">
/// <item><c>Multiset</c>: rows equal on Key and Fact fields are partitioned by their Descriptive values; the largest
/// partition is kept (on a tie, the one holding the latest row) and the rest are stale copies (<c>C</c>,
/// <c>STALE_COPY_COLLAPSED</c>). Ignored timestamps never split a partition, so genuine repeats keep their count.</item>
/// <item><c>SingleRowPerDocument</c>: copies with equal Key and Fact fields collapse to the latest; copies that differ
/// in a fact hold the document (<c>IN_SOURCE_CONFLICT</c>).</item>
/// <item><c>StockUnitChain</c>: every row is kept, <c>line_seq</c> from <see cref="StockUnitSequencer"/>, so the
/// chain does not depend on row order and exact repeats stay (<c>STOCK_ROW_REPEATED</c>).</item>
/// <item><c>SnapshotItems</c>: every row is kept, <c>line_seq</c> from <see cref="SnapshotItemSequencer"/> for stock
/// snapshots, and by row key and fact content otherwise.</item>
/// </list>
/// Rows are taken in sheet order, physical rows before virtual ones, whatever order they arrive in. Sales lines and
/// enrichments are labelled as planner 1 labels them, over the kept rows (spec 7.1). A row with no usable date or
/// document number belongs to no document and is held (<c>ROW_DATE_MISSING</c>). Pure: no SQL, no clock.
/// </summary>
public sealed class DocumentProjector(IFactCanonicalizer canonicalizer) : IDocumentProjector
{
    private const int ListedRows = 10;
    private const string InvoiceYearField = "invoice_year";

    public DocumentProjector() : this(FactCanonicalizer.Instance)
    {
    }

    public BlockProjection Project(ProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Family);
        ArgumentNullException.ThrowIfNull(request.Block);
        ArgumentNullException.ThrowIfNull(request.Rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StoreCode);
        var identity = request.Family.Identity ?? throw new ArgumentException(
            $"The catalogue gives {request.Family.ReportCode} no identity, so its rows cannot be projected.", nameof(request));
        return new BlockRun(canonicalizer, request, identity).Project();
    }

    // One row in sheet order; compared by reference. Date is set once the row is placed in a document.
    private sealed class Placed(RowLocator locator, IReadOnlyDictionary<string, object?> values, CanonicalRow canonical, int position)
    {
        public RowLocator Locator { get; } = locator;
        public IReadOnlyDictionary<string, object?> Values { get; } = values;
        public CanonicalRow Canonical { get; } = canonical;
        public int Position { get; } = position;
        public DateOnly? Date { get; set; }
    }

    private sealed class Document(DocumentKey key, DateOnly? periodTo, int firstPosition)
    {
        public DocumentKey Key { get; } = key;
        public DateOnly? PeriodTo { get; } = periodTo;
        public int FirstPosition { get; } = firstPosition;
        public List<Placed> Rows { get; } = [];
        public List<Placed> Kept { get; } = [];
        public List<Placed> SetAside { get; } = [];
        public string? HoldCode { get; set; }
        public DateOnly? Date => Rows.Min(row => row.Date);
    }

    private sealed class BlockRun(IFactCanonicalizer canonicalizer, ProjectionRequest request, EtpFamilyIdentity identity)
    {
        private readonly EtpReportFamily family = request.Family;
        private readonly SourceBlock block = request.Block;
        private readonly List<ImportDiagnostic> diagnostics = [];
        private readonly Dictionary<Placed, int> lineSeq = [];
        private readonly Dictionary<Placed, string> labels = [];
        private string? primaryDateField;

        public BlockProjection Project()
        {
            primaryDateField = PrimaryDateField();
            var rows = Place(request.Rows);
            var documents = new Dictionary<string, Document>(StringComparer.Ordinal);
            var held = new List<Placed>();
            foreach (var row in rows)
            {
                if (Locate(row) is not { } location)
                {
                    held.Add(row);
                    continue;
                }
                if (!documents.TryGetValue(location.Key.Hash, out var document))
                    documents.Add(location.Key.Hash, document = new(location.Key, location.PeriodTo, row.Position));
                row.Date = location.Date;
                document.Rows.Add(row);
            }

            var ordered = documents.Values.OrderBy(document => document.FirstPosition).ToArray();
            foreach (var document in ordered) ApplyRowRule(document);
            Sequence(ordered);
            Label(ordered);
            InvoiceYears(ordered);
            Held(held);
            return new(block.BlockNo, ordered.Select(Observe).ToArray(),
                held.Select(row => new FactRow(row.Locator, row.Canonical) { Disposition = RowDisposition.Held }).ToArray(),
                diagnostics);
        }

        private List<Placed> Place(IReadOnlyList<SourceRow> rows) => rows
            .Select(row => (row.Locator, Values: StagedValues.Normalize(family, row.Values)))
            .OrderBy(row => row.Locator.IsVirtual)
            .ThenBy(row => row.Locator.SheetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Locator.SourceRowNumber)
            .Select((row, position) => new Placed(row.Locator, row.Values, canonicalizer.Canonicalize(family, row.Values), position))
            .ToList();

        // Finds the row's document by the family's scope (spec 7.1 document key text); null holds the row.
        private (DocumentKey Key, DateOnly Date, DateOnly? PeriodTo)? Locate(Placed row)
        {
            var report = family.ReportCode;
            var store = request.StoreCode;
            switch (identity.Scope)
            {
                case DocumentScope.Document:
                {
                    if (PrimaryDate(row) is not { } date) return null;
                    var parts = identity.DocumentKey.Select(field => canonicalizer.Format(row.Values.GetValueOrDefault(field))).ToArray();
                    if (parts.Length == 0)
                        throw new InvalidOperationException($"The catalogue gives {report} a document scope without a document key.");
                    if (parts.Any(string.IsNullOrWhiteSpace)) return null;
                    var number = string.Join('|', parts);
                    return (identity.YearRule == YearRule.FinancialYearOfPrimaryDate
                        ? DocumentKey.ForDocument(report, store, date, number)
                        : new DocumentKey(report, store, DocumentScope.Document, number.ToUpperInvariant()), date, null);
                }
                case DocumentScope.Date:
                    return PrimaryDate(row) is { } day ? (DocumentKey.ForDate(report, store, day), day, null) : null;
                case DocumentScope.Snapshot:
                    return SnapshotDate(row) is { } snapshot ? (DocumentKey.ForSnapshot(report, store, snapshot), snapshot, null) : null;
                case DocumentScope.Period:
                    if (block is { PeriodFrom: { } from, PeriodTo: { } to })
                        return (DocumentKey.ForPeriod(report, store, from, to), from, to);
                    // Without a declared period (a legacy whole file) a period family is one snapshot of the block (spec 7.5).
                    return block.SnapshotDate is { } asOf ? (DocumentKey.ForSnapshot(report, store, asOf), asOf, null) : null;
                default:
                    throw new InvalidOperationException($"Unknown document scope {identity.Scope}.");
            }
        }

        private DateOnly? PrimaryDate(Placed row) => primaryDateField is { } field && row.Values.GetValueOrDefault(field) is DateOnly date
            ? date : null;

        private DateOnly? SnapshotDate(Placed row) => identity.SnapshotDateColumn is { } column
            ? row.Values.GetValueOrDefault(column) as DateOnly?
            : block.SnapshotDate;

        private string? PrimaryDateField()
        {
            var needsDate = identity.Scope is DocumentScope.Document or DocumentScope.Date;
            var field = family.PrimaryDateHeader is { } header
                ? family.Columns.FirstOrDefault(column => string.Equals(column.SourceHeader, header, StringComparison.OrdinalIgnoreCase))?.CanonicalField
                : null;
            if (needsDate && field is null)
                throw new InvalidOperationException($"The catalogue gives {family.ReportCode} no primary date column for its {identity.Scope} scope.");
            return field;
        }

        private void ApplyRowRule(Document document)
        {
            switch (identity.RowRule)
            {
                case RowRule.Multiset:
                    foreach (var copies in document.Rows.GroupBy(row => row.Canonical.FactRowHash, StringComparer.Ordinal))
                    {
                        // Customer fields are invoice-level, so one export never carries two of them in one document:
                        // the largest partition is the export's own count, the others are stale copies.
                        var partitions = copies.GroupBy(row => row.Canonical.DescriptiveHash, StringComparer.Ordinal)
                            .OrderByDescending(partition => partition.Count())
                            .ThenByDescending(partition => partition.Max(row => row.Position))
                            .ToArray();
                        document.Kept.AddRange(partitions[0]);
                        document.SetAside.AddRange(partitions.Skip(1).SelectMany(partition => partition));
                    }
                    break;
                case RowRule.SingleRowPerDocument:
                    foreach (var copies in document.Rows.GroupBy(row => row.Canonical.FactRowHash, StringComparer.Ordinal))
                    {
                        var latest = copies.MaxBy(row => row.Position)!;
                        document.Kept.Add(latest);
                        document.SetAside.AddRange(copies.Where(row => row != latest));
                    }
                    if (document.Kept.Count > 1)
                    {
                        document.HoldCode = ImportCodes.InSourceConflict;
                        Report(ImportCodes.InSourceConflict, document, document.Kept,
                            $"{document.Kept.Count} rows of this document in one export differ in a fact (rows {RowList(document.Kept)}). The document is held for review and nothing from it is applied.");
                    }
                    break;
                default:
                    document.Kept.AddRange(document.Rows);
                    break;
            }
            document.Kept.Sort((a, b) => a.Position.CompareTo(b.Position));
            document.SetAside.Sort((a, b) => a.Position.CompareTo(b.Position));
            if (document.SetAside.Count > 0)
                Report(ImportCodes.StaleCopyCollapsed, document, document.SetAside,
                    $"{document.SetAside.Count} {(document.SetAside.Count == 1 ? "row" : "rows")} of this document (rows {RowList(document.SetAside)}) repeat kept rows apart from customer, store or time details and were set aside as stale copies.");

            // An invoice header holds one date (spec 7.3): lines of one export that disagree on it are never merged.
            if (document.HoldCode is null && identity.Route is (FamilyRoute.Sales or FamilyRoute.Revenue) &&
                document.Kept.Select(row => row.Date).Distinct().Count() > 1)
            {
                document.HoldCode = ImportCodes.InSourceConflict;
                Report(ImportCodes.InSourceConflict, document, document.Kept,
                    $"The rows of this invoice in one export carry more than one date (rows {RowList(document.Kept)}). The document is held for review and nothing from it is applied.");
            }
        }

        private void Sequence(IReadOnlyList<Document> documents)
        {
            var rows = documents.SelectMany(document => document.Kept.Select(row => (Row: row, document.Date))).ToArray();
            switch (identity.RowRule)
            {
                case RowRule.StockUnitChain:
                {
                    var sequence = StockUnitSequencer.Assign(rows, row => StockUnit(row.Row));
                    for (var i = 0; i < rows.Length; i++) lineSeq[rows[i].Row] = sequence.LineSeq[i];
                    diagnostics.AddRange(sequence.Diagnostics.Select(diagnostic => diagnostic with { BlockNo = block.BlockNo }));
                    break;
                }
                case RowRule.SnapshotItems when identity.Route == FamilyRoute.StockSnapshot &&
                    StockSnapshotFields.For(family.ReportCode) is { } fields:
                {
                    var sequence = SnapshotItemSequencer.Assign(rows, row => SnapshotItem(row.Row, row.Date!.Value, fields));
                    for (var i = 0; i < rows.Length; i++) lineSeq[rows[i].Row] = sequence.LineSeq[i];
                    break;
                }
                case RowRule.SnapshotItems:
                    // Landing snapshots: rows paired by row key, numbered by fact content, then sheet order.
                    foreach (var group in rows.GroupBy(row => (row.Date, RowKey(row.Row))))
                    {
                        var position = 0;
                        foreach (var row in group.OrderBy(row => row.Row.Canonical.FactRowHash, StringComparer.Ordinal).ThenBy(row => row.Row.Position))
                            lineSeq[row.Row] = ++position;
                    }
                    break;
            }
        }

        private StockUnitRow StockUnit(Placed row)
        {
            var date = row.Date!.Value;
            return new(Text(row, "store_code") ?? request.StoreCode, DocumentKey.FinancialYearEnd(date), Text(row, "document_number") ?? "",
                date, Text(row, "product_code") ?? "", Text(row, "source_transaction_type") ?? "", Text(row, "from_location"),
                Text(row, "to_location"), Number(row, "opening_quantity") ?? 0m, Number(row, "transaction_quantity") ?? 0m,
                Number(row, "closing_quantity") ?? 0m, row.Locator.SheetName, row.Locator.SourceRowNumber)
            {
                RefDocumentNumber = Text(row, "ref_documentnumber"),
                RefDocumentDate = row.Values.GetValueOrDefault("ref_documentdate") as DateOnly?
            };
        }

        private SnapshotItemRow SnapshotItem(Placed row, DateOnly snapshotDate, StockSnapshotFields fields) => new(
            fields.RowStore ? Text(row, "store_code") ?? request.StoreCode : request.StoreCode, snapshotDate,
            fields.SourceReportCode, Text(row, fields.Product) ?? "", Text(row, fields.Uid), Text(row, fields.Batch),
            fields.Ean is null ? null : Text(row, fields.Ean), Number(row, fields.Quantity) ?? 0m, Number(row, fields.UnitCost),
            Number(row, fields.TotalCost), row.Locator.SheetName, row.Locator.SourceRowNumber);

        private void Label(IReadOnlyList<Document> documents)
        {
            if (identity.Route is not (FamilyRoute.Sales or FamilyRoute.Enrichment)) return;
            foreach (var document in documents)
            {
                var keys = PlannerOneLabels.LineKeys(canonicalizer, family, document.Kept.Select(row => row.Values));
                for (var i = 0; i < document.Kept.Count; i++) labels[document.Kept[i]] = keys[i];
            }
        }

        // OD-1: the key's year is the financial year of the date; an ETP year label that disagrees is information only.
        private void InvoiceYears(IReadOnlyList<Document> documents)
        {
            if (identity is not { Scope: DocumentScope.Document, YearRule: YearRule.FinancialYearOfPrimaryDate } ||
                !family.ColumnsWithRole(ColumnRole.Label).Any(column => column.CanonicalField == InvoiceYearField)) return;
            var differing = documents.SelectMany(document => document.Rows)
                .Where(row => row.Values.GetValueOrDefault(InvoiceYearField) is { } label &&
                    long.TryParse(Convert.ToString(label, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) &&
                    year != DocumentKey.FinancialYearEnd(row.Date!.Value))
                .OrderBy(row => row.Position).ToArray();
            if (differing.Length > 0)
                Add(ImportCodes.InvoiceYearDiffers, differing,
                    $"{differing.Length} {(differing.Length == 1 ? "row carries" : "rows carry")} an invoice year that differs from the financial year of the date (rows {RowList(differing)}). The financial year of the date is used.");
        }

        private void Held(IReadOnlyList<Placed> held)
        {
            if (held.Count == 0) return;
            var undated = identity.Scope is (DocumentScope.Snapshot or DocumentScope.Period) &&
                identity.SnapshotDateColumn is null;
            if (undated)
                Add(ImportCodes.SnapshotDateUnknown, held,
                    $"This export has no snapshot date, so its {held.Count} {(held.Count == 1 ? "row belongs" : "rows belong")} to no snapshot and {(held.Count == 1 ? "is" : "are")} held.");
            else
                Add(ImportCodes.RowDateMissing, held,
                    $"{held.Count} {(held.Count == 1 ? "row has" : "rows have")} no usable date or document number (rows {RowList(held)}). {(held.Count == 1 ? "It belongs" : "They belong")} to no document and {(held.Count == 1 ? "is" : "are")} held.");
        }

        private DocumentObservation Observe(Document document)
        {
            var kept = document.Kept.Select(row => new FactRow(row.Locator, row.Canonical)
            {
                LineSeq = lineSeq.GetValueOrDefault(row, 1),
                RowKey = identity.RowRule == RowRule.SnapshotItems ? RowKey(row) : null,
                LineLabel = labels.GetValueOrDefault(row)
            }).ToArray();
            var setAside = document.SetAside.Select(row => new FactRow(row.Locator, row.Canonical) { Disposition = RowDisposition.Collapsed }).ToArray();
            var snapshotDate = document.Key.Scope == DocumentScope.Snapshot ? document.Date : null;
            return new(document.Key, block.BlockNo, block.ExportTime, document.Date, kept,
                canonicalizer.MultisetHash(document.Kept.Select(row => row.Canonical.FactRowHash)),
                canonicalizer.MultisetHash(document.Kept.Select(row => row.Canonical.AttributeHash)))
            {
                PeriodTo = document.PeriodTo,
                CanonicalSha256 = identity.HasTypedFacts
                    ? canonicalizer.MultisetHash(document.Kept.SelectMany(row =>
                        CanonicalFactProjection.Rows(family, request.StoreCode, snapshotDate, row.Values)).Select(canonicalizer.Hash))
                    : null,
                SetAside = setAside,
                HoldCode = document.HoldCode
            };
        }

        // The fields that pair rows between two readings of a snapshot, as the database's case-insensitive collation
        // compares them; COALESCE(a,b,...) takes the first non-blank. Without a row key every fact pairs (spec 7.5).
        private string RowKey(Placed row) => identity.RowKey.Count == 0
            ? row.Canonical.FactRowHash
            : string.Join('|', identity.RowKey.Select(entry => RowKeyPart(row, entry))).ToUpperInvariant();

        private string RowKeyPart(Placed row, string entry)
        {
            var text = entry.Trim();
            if (!text.StartsWith("COALESCE(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
                return canonicalizer.Format(row.Values.GetValueOrDefault(text));
            return text["COALESCE(".Length..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(field => canonicalizer.Format(row.Values.GetValueOrDefault(field)))
                .FirstOrDefault(value => value.Length > 0) ?? "";
        }

        private void Report(string code, Document document, IReadOnlyList<Placed> rows, string message) =>
            diagnostics.Add(Diagnostic(code, rows, message) with { DocumentRef = DocumentRef(document) });

        private void Add(string code, IReadOnlyList<Placed> rows, string message) => diagnostics.Add(Diagnostic(code, rows, message));

        private ImportDiagnostic Diagnostic(string code, IReadOnlyList<Placed> rows, string message) =>
            new(code, (ImportDiagnosticSeverity)(int)ImportCodes.DefaultSeverity(code), message, rows[0].Locator.SheetName, rows[0].Locator.SourceRowNumber)
            {
                BlockNo = block.BlockNo,
                Occurrences = rows.Count
            };

        // Document number and date only, never a customer value (spec 11.1).
        private string DocumentRef(Document document) =>
            document.Key.Scope == DocumentScope.Document && document.Date is { } date
                ? $"{(identity.YearRule == YearRule.FinancialYearOfPrimaryDate ? document.Key.KeyText[(document.Key.KeyText.IndexOf('|') + 1)..] : document.Key.KeyText)} {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                : document.Key.KeyText;

        private static string RowList(IReadOnlyList<Placed> rows)
        {
            var numbers = rows.Select(row => row.Locator.IsVirtual
                ? $"{row.Locator.SheetName} {row.Locator.SourceRowNumber}"
                : row.Locator.SourceRowNumber.ToString(CultureInfo.InvariantCulture)).ToArray();
            return numbers.Length <= ListedRows
                ? string.Join(", ", numbers)
                : $"{string.Join(", ", numbers.Take(ListedRows))} and {numbers.Length - ListedRows} more";
        }

        private static string? Text(Placed row, string field) => row.Values.GetValueOrDefault(field) switch
        {
            null => null,
            string text => text,
            var value => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

        private static decimal? Number(Placed row, string field) => row.Values.GetValueOrDefault(field) as decimal?;
    }
}
