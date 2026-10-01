using System.Globalization;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Codes = Etp.Reporting.Application.Imports.ImportCodes.Contract;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// Every rule of consolidation contract section 8, one implementation for the importer (<see cref="SourceDescriptionReader"/>)
/// and <c>ImportAudit --validate-contract</c>. It judges the layout that <see cref="IConsolidationContractReader"/> read
/// against the workbook's own sheets and the family matched by header signature, reads no database, and returns the
/// blocks and the exclusion map as the importer uses them. Any blocker refuses the whole workbook. Messages carry
/// keys, block numbers, row numbers and counts, never a data value.
/// </summary>
public sealed class ConsolidationContractValidator : IConsolidationContractValidator
{
    /// <summary>The contract versions this release reads (contract 9). An unsupported version is never guessed.</summary>
    public static IReadOnlySet<int> SupportedVersions { get; } = new HashSet<int> { ConsolidationContractLayout.CurrentVersion };

    public ContractValidationResult Validate(ContractValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Workbook);
        ArgumentNullException.ThrowIfNull(request.Contract);
        ArgumentNullException.ThrowIfNull(request.MatchedFamily);
        return new Validation(request).Run();
    }

    /// <summary>
    /// The rules a family's workbook may declare (contract 6), from the catalogue identity: Document and Date scope are
    /// <c>transactional</c>, Snapshot scope is <c>snapshot</c> (and <c>current</c> for Service), Period scope is
    /// <c>period</c>. Before a family has an identity, a family dated by row is <c>transactional</c> and an undated one
    /// (or one dated by a snapshot-date column, R011) is <c>snapshot</c>. <c>empty</c> is allowed for every family.
    /// </summary>
    public static IReadOnlyList<ContractRule> AllowedRules(EtpReportFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        var service = family.BusinessUnit == BusinessUnit.Service;
        ContractRule[] rules = family.Identity?.Scope switch
        {
            DocumentScope.Document or DocumentScope.Date => [ContractRule.Transactional],
            DocumentScope.Snapshot => service ? [ContractRule.Snapshot, ContractRule.Current] : [ContractRule.Snapshot],
            DocumentScope.Period => [ContractRule.Period],
            null when SnapshotDateField(family) is not null || family.PrimaryDateHeader is null =>
                service ? [ContractRule.Snapshot, ContractRule.Current, ContractRule.Period] : [ContractRule.Snapshot],
            _ => [ContractRule.Transactional]
        };
        return [.. rules, ContractRule.Empty];
    }

    /// <summary>The canonical field that dates every row of a snapshot family (R011 <c>Date</c>); null when blocks date it.</summary>
    public static string? SnapshotDateField(EtpReportFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return family.Identity is { } identity ? identity.SnapshotDateColumn
            : family.Columns.Any(column => column.CanonicalField == "snapshot_date") ? "snapshot_date" : null;
    }

    private sealed record ParsedBlock(ContractBlockRow Row, int No, string Sheet, BlockCompleteness Completeness)
    {
        public int? First => Row.FirstRow;
        public int? Last => Row.LastRow;
        public bool HasRange => First is not null && Last is not null && Last >= First;
        public ExportTime Time => Row.ExportTime ?? ExportTime.Unknown;
        public string Label => $"Block {No.ToString(CultureInfo.InvariantCulture)}";

        public bool Holds(string sheet, int row) => HasRange && Sheet == sheet && row >= First && row <= Last;
    }

    private sealed class Validation(ContractValidationRequest request)
    {
        private const string Info = ConsolidationContractLayout.InfoSheet;
        private readonly WorkbookSnapshot workbook = request.Workbook;
        private readonly ConsolidationContract contract = request.Contract;
        private readonly ConsolidationContractHeader header = request.Contract.Header;
        private readonly EtpReportFamily family = request.MatchedFamily;
        private readonly List<ImportDiagnostic> diagnostics = [];
        private readonly List<ParsedBlock> blocks = [];
        private readonly HashSet<int> repeatedHistoryBlocks = [];
        private ContractRule? rule;
        private ContractSheetView? data;
        private ContractSheetView? history;
        private WorkbookSheet? excludedSheet;

        public ContractValidationResult Run()
        {
            if (!CheckVersion()) return new([], [], diagnostics);
            CheckCellsAreText();
            CheckKeys();
            CheckFamilyAndRule();
            FindSheets();
            if (!CheckBlockTableHeader()) return new([], [], diagnostics);
            ReadBlocks();
            CheckRowCounts();
            foreach (var block in blocks) CheckBlock(block);
            CheckPartition(data, ConsolidationContractLayout.DataSheet);
            CheckPartition(history, ConsolidationContractLayout.HistorySheet);
            CheckShape();
            CheckSnapshotDates();
            CheckDuplicateExports();
            CheckTimeOrder();
            var virtualRows = CheckExclusionMap();
            CheckColumns();
            CheckStore();
            CheckRowDates(virtualRows);
            CheckRebuild(virtualRows);
            var legacy = blocks.Count(block => block.Completeness == BlockCompleteness.Legacy);
            if (legacy > 0)
                Add(Codes.LegacyBlocks, $"{legacy} legacy block(s): imported for their rows, never used to infer that a document is missing.",
                    occurrences: legacy);
            return new(SourceBlocks(virtualRows), virtualRows, diagnostics);
        }

        private bool CheckVersion()
        {
            if (header.Version is { } version && SupportedVersions.Contains(version)) return true;
            Add(Codes.VersionUnsupported,
                $"Info!B1 holds contract version '{DiagnosticLists.Clip(header.VersionText, 12)}'; this release reads version {string.Join(", ", SupportedVersions.Order())}.",
                Info, 1, "B");
            return false;
        }

        private void CheckCellsAreText()
        {
            var rows = header.Keys.Where(key => !key.IsText).Select(key => key.RowNumber)
                .Concat(header.VersionIsText ? [] : [1])
                .Concat(contract.Blocks.Where(block => !block.AllCellsText).Select(block => block.RowNumber))
                .Distinct().Order().ToArray();
            if (rows.Length > 0)
                Add(Codes.CellNotText,
                    $"{rows.Length} Info row(s) of the machine section hold cells that are not text (rows {DiagnosticLists.Of(rows)}). Write them as text (number format @) so Excel never changes them.",
                    Info, rows[0], occurrences: rows.Length);
        }

        private void CheckKeys()
        {
            var unknown = header.Keys.Where(key => !string.IsNullOrWhiteSpace(key.Key) &&
                    !ContractKeys.Known.Contains(key.Key.Trim(), StringComparer.Ordinal))
                .ToArray();
            if (unknown.Length > 0)
                Add(Codes.KeyUnknown,
                    $"{unknown.Length} Info key(s) are not known to contract version 1 and are ignored: {DiagnosticLists.Of(unknown.Select(key => DiagnosticLists.Clip(key.Key)).ToArray())}.",
                    Info, unknown[0].RowNumber, "A", occurrences: unknown.Length);

            var required = ContractKeys.Required.ToList();
            var historyUsed = header.HistorySheet is not null || contract.Blocks.Any(block =>
                ContractSheetView.CanonicalBlockSheet(block.Sheet) == ConsolidationContractLayout.HistorySheet);
            if (historyUsed) required.AddRange([ContractKeys.HistorySheet, ContractKeys.HistoryExtraColumns, ContractKeys.HistoryRows]);
            if (header.ExcludedSheet is not null || contract.Blocks.Any(block => block.ExcludedRows > 0))
                required.AddRange([ContractKeys.ExcludedSheet, ContractKeys.ExcludedRows]);
            foreach (var key in required.Distinct().Where(key => header.Value(key) is null))
                Add(Codes.KeyMissing, $"The Info key '{key}' is missing or blank.", Info, column: "A");

            Malformed(ContractKeys.HeaderRow, header.HeaderRow == 1, "must be 1");
            Malformed(ContractKeys.DataRows, header.DataRows is not null, "must be a whole number");
            Malformed(ContractKeys.BlockCount, header.BlockCount is not null, "must be a whole number");
            Malformed(ContractKeys.HistoryRows, header.HistoryRows is not null, "must be a whole number");
            Malformed(ContractKeys.ExcludedRows, header.ExcludedRows is not null, "must be a whole number");
            Malformed(ContractKeys.BuiltAt, header.BuiltAt is not null, "must be written yyyy-MM-ddTHH:mm:ss+05:30");
            Malformed(ContractKeys.BusinessUnit, header.BusinessUnit is "RETAIL" or "SERVICE", "must be RETAIL or SERVICE");
        }

        private void Malformed(string key, bool valid, string rule)
        {
            if (header.Value(key) is null || valid) return;
            var row = header.Keys.First(candidate => string.Equals(candidate.Key.Trim(), key, StringComparison.Ordinal)).RowNumber;
            Add(Codes.Unreadable, $"The Info key '{key}' cannot be read: it {rule}.", Info, row, "B");
        }

        private void CheckFamilyAndRule()
        {
            if (header.FamilyCode is { } code && !string.Equals(code, family.FamilyCode, StringComparison.OrdinalIgnoreCase))
                Add(Codes.FamilyMismatch,
                    $"family_code '{DiagnosticLists.Clip(code, 16)}' differs from {family.FamilyCode}, the family matched by the Data header. The contract names the catalogue FamilyCode.",
                    Info, KeyRow(ContractKeys.FamilyCode), "B");
            var unit = family.BusinessUnit.ToDatabaseCode();
            if (header.BusinessUnit is "RETAIL" or "SERVICE" && header.BusinessUnit != unit)
                Add(Codes.FamilyMismatch, $"business_unit {header.BusinessUnit} differs from {unit}, the business unit of {family.FamilyCode}.",
                    Info, KeyRow(ContractKeys.BusinessUnit), "B");

            var allowed = AllowedRules(family);
            var expected = string.Join(" or ", allowed.Where(candidate => candidate != ContractRule.Empty).Select(candidate => candidate.ToContractText()));
            rule = header.Rule;
            if (header.Value(ContractKeys.Rule) is { } text && rule is null)
                Add(Codes.RuleInvalid, $"rule '{DiagnosticLists.Clip(text, 16)}' is not a contract rule; {family.FamilyCode} expects {expected}.",
                    Info, KeyRow(ContractKeys.Rule), "B");
            else if (rule is { } declared && !allowed.Contains(declared))
                Add(Codes.RuleInvalid, $"rule {declared.ToContractText()} is not allowed for {family.FamilyCode}; expected {expected}.",
                    Info, KeyRow(ContractKeys.Rule), "B");
            if (rule == ContractRule.Empty && (contract.Blocks.Count > 1 || header.DataRows is > 0 ||
                    contract.Blocks.Any(block => block.Completeness != BlockCompleteness.Empty)))
                Add(Codes.RuleInvalid, "rule empty takes at most one block, with completeness empty, and no data rows.",
                    Info, KeyRow(ContractKeys.Rule), "B");
        }

        private void FindSheets()
        {
            var dataName = header.DataSheet ?? ConsolidationContractLayout.DataSheet;
            if (!string.Equals(dataName, ConsolidationContractLayout.DataSheet, StringComparison.OrdinalIgnoreCase))
                Add(Codes.SheetMissing, $"data_sheet must name the {ConsolidationContractLayout.DataSheet} sheet.", Info, KeyRow(ContractKeys.DataSheet), "B");
            data = ContractSheetView.Find(workbook, ConsolidationContractLayout.DataSheet) is { } dataSheet ? new(dataSheet) : null;
            if (data is null)
                Add(Codes.SheetMissing, $"The workbook has no {ConsolidationContractLayout.DataSheet} sheet.");

            var historySheet = ContractSheetView.Find(workbook, ConsolidationContractLayout.HistorySheet);
            if (header.HistorySheet is { } historyName)
            {
                if (!string.Equals(historyName, ConsolidationContractLayout.HistorySheet, StringComparison.OrdinalIgnoreCase))
                    Add(Codes.SheetMissing, $"history_sheet must name the {ConsolidationContractLayout.HistorySheet} sheet.",
                        Info, KeyRow(ContractKeys.HistorySheet), "B");
                else if (historySheet is null)
                    Add(Codes.SheetMissing, $"history_sheet names {ConsolidationContractLayout.HistorySheet}, but the workbook has no such sheet.",
                        Info, KeyRow(ContractKeys.HistorySheet), "B");
            }
            history = historySheet is null ? null : new(historySheet);

            excludedSheet = ContractSheetView.Find(workbook, ConsolidationContractLayout.ExcludedSheet);
            if (header.ExcludedSheet is { } excludedName)
            {
                if (!string.Equals(excludedName, ConsolidationContractLayout.ExcludedSheet, StringComparison.OrdinalIgnoreCase))
                    Add(Codes.SheetMissing, $"excluded_sheet must name the {ConsolidationContractLayout.ExcludedSheet} sheet.",
                        Info, KeyRow(ContractKeys.ExcludedSheet), "B");
                else if (excludedSheet is null)
                    Add(Codes.SheetMissing, $"excluded_sheet names {ConsolidationContractLayout.ExcludedSheet}, but the workbook has no such sheet.",
                        Info, KeyRow(ContractKeys.ExcludedSheet), "B");
            }
        }

        private bool CheckBlockTableHeader()
        {
            var actual = contract.BlockTableHeader.Select(name => name.Trim()).ToList();
            while (actual.Count > 0 && actual[^1].Length == 0) actual.RemoveAt(actual.Count - 1);
            var expected = ConsolidationContractLayout.BlockTableColumns;
            if (actual.Count < expected.Count || !actual.Take(expected.Count).SequenceEqual(expected, StringComparer.Ordinal))
            {
                Add(Codes.BlockTableHeader,
                    $"The block-table header (Info row {contract.BlockTableHeaderRow.ToString(CultureInfo.InvariantCulture)}) must be exactly: {string.Join(" | ", expected)}.",
                    Info, contract.BlockTableHeaderRow);
                return false;
            }
            var extra = actual.Skip(expected.Count).Where(name => name.Length > 0).ToArray();
            if (extra.Length > 0)
                Add(Codes.KeyUnknown,
                    $"Block-table column(s) after disposition are not known to contract version 1 and are ignored: {DiagnosticLists.Of(extra.Select(name => DiagnosticLists.Clip(name)).ToArray())}.",
                    Info, contract.BlockTableHeaderRow, occurrences: extra.Length);
            return true;
        }

        private void ReadBlocks()
        {
            string[] numbers = ["first_row", "last_row", "row_count", "raw_rows", "excluded_rows", "superseded_rows"];
            foreach (var row in contract.Blocks)
            {
                var problems = new List<string>();
                if (row.Block is not ({ } number and > 0)) problems.Add("block is not a whole number from 1");
                var sheet = ContractSheetView.CanonicalBlockSheet(row.Sheet);
                if (sheet is null) problems.Add($"sheet must be {ConsolidationContractLayout.DataSheet} or {ConsolidationContractLayout.HistorySheet}");
                if (row.Completeness is null) problems.Add("completeness must be complete, delta, trimmed, empty or legacy");
                problems.AddRange(numbers.Where(column => row.Text(column).Length > 0 && ContractCell.Int(row.Text(column)) is null)
                    .Select(column => $"{column} is not a whole number"));
                if (row.Text("period_basis").Length > 0 && row.PeriodBasis is null) problems.Add("period_basis must be declared, observed or none");
                if (row.SourceFormat.Length > 0 && row.SourceFormat is not ("xlsx" or "csv")) problems.Add("source_format must be xlsx or csv");
                if (row.SourceFile.Length > 260) problems.Add("source_file is longer than 260 characters");
                if (problems.Count > 0)
                {
                    Add(Codes.Unreadable, $"Block-table row {row.RowNumber.ToString(CultureInfo.InvariantCulture)} cannot be read: {string.Join("; ", problems)}.",
                        Info, row.RowNumber);
                    continue;
                }
                if (blocks.Any(block => block.No == row.Block))
                {
                    Add(Codes.Unreadable, $"Block {row.Block} appears twice in the block table (row {row.RowNumber}); block numbers are unique.", Info, row.RowNumber);
                    continue;
                }
                blocks.Add(new(row, row.Block!.Value, sheet!, row.Completeness!.Value));
            }
            blocks.Sort((left, right) => left.No.CompareTo(right.No));
            if (blocks.Count > 0 && blocks[^1].No != blocks.Count)
                Add(Codes.Unreadable,
                    $"Block numbers must run 1..n in append order with none missing; the table holds {blocks.Count} block(s) numbered up to {blocks[^1].No}.",
                    Info, contract.BlockTableHeaderRow);
            if (header.BlockCount is { } count && count != contract.Blocks.Count)
                Add(Codes.RowCountMismatch, $"block_count is {count}, but the block table has {contract.Blocks.Count} row(s).",
                    Info, KeyRow(ContractKeys.BlockCount), "B");
        }

        private void CheckRowCounts()
        {
            CheckSheetCount(data, ContractKeys.DataRows, header.DataRows);
            if (history is not null) CheckSheetCount(history, ContractKeys.HistoryRows, header.HistoryRows);
            if (excludedSheet is not null && header.ExcludedRows is { } mapped && mapped != excludedSheet.Rows.Count)
                Add(Codes.RowCountMismatch, $"excluded_rows is {mapped}, but {excludedSheet.Name} has {excludedSheet.Rows.Count} map row(s).",
                    Info, KeyRow(ContractKeys.ExcludedRows), "B");
        }

        private void CheckSheetCount(ContractSheetView? sheet, string key, int? declared)
        {
            if (sheet is null) return;
            if (declared is { } rows && rows != sheet.Rows.Count)
                Add(Codes.RowCountMismatch, $"{key} is {rows}, but {sheet.Name} has {sheet.Rows.Count} data row(s).", Info, KeyRow(key), "B");
            if (!sheet.RowsAreContiguous)
                Add(Codes.RowCountMismatch, $"{sheet.Name} must have its header on row 1 and data rows from row 2 with no blank row between them.",
                    sheet.Name, sheet.Sheet.HeaderRowNumber);
            var sheetName = ContractSheetView.CanonicalBlockSheet(sheet.Name);
            var total = contract.Blocks.Where(block => ContractSheetView.CanonicalBlockSheet(block.Sheet) == sheetName)
                .Sum(block => (long)(block.RowCount ?? 0));
            if (total != sheet.Rows.Count)
                Add(Codes.RowCountMismatch, $"The {sheet.Name} blocks' row_count values add up to {total}, but {sheet.Name} has {sheet.Rows.Count} data row(s).",
                    sheet.Name);
        }

        private void CheckBlock(ParsedBlock block)
        {
            var row = block.Row;
            var legacy = block.Completeness == BlockCompleteness.Legacy;
            void Fail(string code, string message) => Add(code, $"{block.Label}: {message}", Info, row.RowNumber, block: block.No);

            if (row.RowCount is not { } count)
                Fail(Codes.RowCountMismatch, "row_count is blank.");
            else if (block.First is null || block.Last is null)
            {
                if (count != 0 || block.First is not null || block.Last is not null)
                    Fail(Codes.RowCountMismatch, "first_row and last_row are blank only when row_count is 0.");
            }
            else if (block.Last < block.First || block.Last - block.First + 1 != count)
                Fail(Codes.RowCountMismatch, $"row_count {count} is not last_row − first_row + 1 ({block.First}..{block.Last}).");

            if (row.SourceSha256.Length == 0)
            {
                if (!legacy) Fail(Codes.ShaInvalid, "source_sha256 is blank; only a legacy block may leave it blank.");
            }
            else if (!IsSha256(row.SourceSha256))
                Fail(Codes.ShaInvalid, "source_sha256 is not 64 lowercase hex characters.");

            var timeText = row.Text("export_time");
            if (timeText.Length == 0)
            {
                if (!legacy) Fail(Codes.ExportTimeMissing, "export_time is blank; only a legacy block may leave it blank.");
            }
            else if (row.ExportTime is not { } time)
                Fail(Codes.ExportTimeInvalid, "export_time is not yyyy-MM-ddTHH:mm, yyyy-MM-ddTHH:mm:ss or yyyy-MM-dd.");
            else if (Contradiction(time, ExportNameParser.Parse(row.SourceFile)) is { } contradiction)
                Fail(Codes.ExportTimeInvalid, contradiction);

            if (rule is ContractRule.Transactional or ContractRule.Period)
            {
                if (row.PeriodFrom is null || row.PeriodTo is null)
                    Fail(Codes.PeriodMissing, $"a {rule.Value.ToContractText()} block needs period_from and period_to as yyyy-MM-dd.");
                else if (row.PeriodFrom > row.PeriodTo)
                    Fail(Codes.PeriodMissing, "period_from is after period_to.");
            }
            if (rule is ContractRule.Snapshot or ContractRule.Current && row.SnapshotDate is null)
                Fail(Codes.SnapshotDateMissing, $"a {rule.Value.ToContractText()} block needs snapshot_date as yyyy-MM-dd.");

            CheckArithmetic(block, Fail);
        }

        private static void CheckArithmetic(ParsedBlock block, Action<string, string> fail)
        {
            var row = block.Row;
            var count = row.RowCount ?? 0;
            var excluded = row.ExcludedRows ?? 0;
            var superseded = row.SupersededRows ?? 0;
            if (block.Completeness == BlockCompleteness.Legacy) return;
            if (row.RawRows is not { } raw)
            {
                fail(Codes.BlockArithmetic, "raw_rows is blank; it is required unless the block is legacy.");
                return;
            }
            var (holds, formula) = block.Completeness switch
            {
                BlockCompleteness.Complete => (count == raw && excluded == 0 && superseded == 0,
                    "row_count = raw_rows, excluded_rows = 0, superseded_rows = 0"),
                BlockCompleteness.Delta => (count + excluded == raw && superseded == 0,
                    "row_count + excluded_rows = raw_rows, superseded_rows = 0"),
                BlockCompleteness.Trimmed => (count + excluded + superseded == raw && superseded > 0,
                    "row_count + excluded_rows + superseded_rows = raw_rows, superseded_rows > 0"),
                _ => (count == 0 && raw == 0 && block.First is null && block.Last is null,
                    "row_count = 0, raw_rows = 0, first_row and last_row blank")
            };
            if (!holds)
                fail(Codes.BlockArithmetic,
                    $"{block.Completeness.ToContractText()} needs {formula}; it has row_count {count}, excluded_rows {excluded}, superseded_rows {superseded}, raw_rows {raw}.");
        }

        /// <summary>
        /// Why a contract time contradicts the time in its source-file name, or null: the dates differ, the times differ
        /// at the precision both carry, or the contract adds a time to a name that carries only a date (never invent one).
        /// </summary>
        private static string? Contradiction(ExportTime contractTime, ExportTime nameTime)
        {
            if (!nameTime.IsKnown) return null;
            if (contractTime.ExportDate != nameTime.ExportDate)
                return $"export_time {contractTime.ToContractText()} contradicts the date in source_file ({nameTime.ToContractText()}).";
            if (nameTime.Basis == ExportBasis.Date && contractTime.Basis != ExportBasis.Date)
                return "export_time carries a time, but source_file carries only a date; never invent a time.";
            if (nameTime.Basis != ExportBasis.Date && contractTime.Basis != ExportBasis.Date &&
                ExportOrder.Compare(contractTime, nameTime) != ExportOrderResult.Same)
                return $"export_time {contractTime.ToContractText()} contradicts the time in source_file ({nameTime.ToContractText()}).";
            return null;
        }

        private void CheckPartition(ContractSheetView? sheet, string sheetName)
        {
            var onSheet = blocks.Where(block => block.Sheet == sheetName).ToArray();
            if (sheet is null)
            {
                if (onSheet.Length > 0)
                    Add(Codes.SheetMissing, $"{onSheet.Length} block(s) are on {sheetName}, but the workbook has no such sheet.", occurrences: onSheet.Length);
                return;
            }
            var lastData = sheet.LastDataRow;
            var ranged = onSheet.Where(block => block.HasRange).ToArray();
            foreach (var block in ranged.Where(block => block.First < 2 || block.Last > lastData))
                Add(Codes.BlockOutsideData,
                    $"{block.Label} covers {sheetName} rows {block.First}..{block.Last}; its data rows are 2..{lastData}.",
                    Info, block.Row.RowNumber, block: block.No);
            for (var index = 1; index < ranged.Length; index++)
                if (ranged[index].First <= ranged[index - 1].Last)
                    Add(Codes.BlockOverlap,
                        ranged[index].Last >= ranged[index - 1].First
                            ? $"{ranged[index].Label} ({ranged[index].First}..{ranged[index].Last}) overlaps {ranged[index - 1].Label} ({ranged[index - 1].First}..{ranged[index - 1].Last}) on {sheetName}."
                            : $"{ranged[index].Label} ({ranged[index].First}..{ranged[index].Last}) comes before {ranged[index - 1].Label} ({ranged[index - 1].First}..{ranged[index - 1].Last}) on {sheetName}; blocks appear in block-number order.",
                        Info, ranged[index].Row.RowNumber, block: ranged[index].No);

            var next = 2;
            var gaps = new List<string>();
            var gapRows = 0;
            foreach (var block in ranged.OrderBy(block => block.First))
            {
                if (block.First > next)
                {
                    var end = Math.Min(block.First!.Value - 1, lastData);
                    if (end >= next)
                    {
                        gaps.Add($"{next}..{end}");
                        gapRows += end - next + 1;
                    }
                }
                next = Math.Max(next, block.Last!.Value + 1);
            }
            if (next <= lastData)
            {
                gaps.Add($"{next}..{lastData}");
                gapRows += lastData - next + 1;
            }
            if (gaps.Count > 0)
                Add(Codes.BlockGap, $"{gapRows} {sheetName} row(s) belong to no block (rows {DiagnosticLists.Of(gaps)}).",
                    sheetName, occurrences: gapRows);
        }

        private void CheckShape()
        {
            var historyBlocks = blocks.Where(block => block.Sheet == ConsolidationContractLayout.HistorySheet).ToArray();
            if (rule == ContractRule.Current)
            {
                var dataBlocks = blocks.Count(block => block.Sheet == ConsolidationContractLayout.DataSheet);
                if (dataBlocks != 1)
                    Add(Codes.CurrentShape, $"rule current keeps exactly one block (the latest snapshot) on {ConsolidationContractLayout.DataSheet}; the table has {dataBlocks}.",
                        Info, KeyRow(ContractKeys.Rule));
                return;
            }
            if (historyBlocks.Length > 0 || history?.Rows.Count > 0)
                Add(Codes.CurrentShape, $"Only rule current keeps snapshots on {ConsolidationContractLayout.HistorySheet}.",
                    Info, KeyRow(ContractKeys.Rule));
        }

        private void CheckSnapshotDates()
        {
            if (rule is not (ContractRule.Snapshot or ContractRule.Current)) return;
            // A current workbook with other than one Data block is CONTRACT_CURRENT_SHAPE; it then has no Data snapshot to repeat.
            var dataBlocks = blocks.Where(block => block.Sheet == ConsolidationContractLayout.DataSheet && block.Row.SnapshotDate is not null).ToArray();
            var dataBlock = rule == ContractRule.Current && dataBlocks.Length == 1 ? dataBlocks[0] : null;
            foreach (var group in blocks.Where(block => block.Row.SnapshotDate is not null).GroupBy(block => block.Row.SnapshotDate!.Value))
            {
                var repeats = group.ToList();
                if (dataBlock is not null && repeats.Contains(dataBlock))
                {
                    // Contract 5: the Data snapshot repeated on Snapshot History is counted once, with a warning.
                    foreach (var repeat in repeats.Where(block => block != dataBlock && block.Sheet == ConsolidationContractLayout.HistorySheet))
                    {
                        repeatedHistoryBlocks.Add(repeat.No);
                        Add(Codes.HistoryRepeatsData,
                            $"{repeat.Label} on {ConsolidationContractLayout.HistorySheet} repeats the {ConsolidationContractLayout.DataSheet} snapshot of {group.Key:yyyy-MM-dd}; it is counted once, from {ConsolidationContractLayout.DataSheet}.",
                            Info, repeat.Row.RowNumber, block: repeat.No);
                    }
                    repeats.RemoveAll(block => repeatedHistoryBlocks.Contains(block.No));
                }
                if (repeats.Count > 1)
                    Add(Codes.SnapshotDateDuplicate,
                        $"Blocks {string.Join(", ", repeats.Select(block => block.No))} share snapshot_date {group.Key:yyyy-MM-dd}; keep only the later export of a snapshot date.",
                        Info, repeats[1].Row.RowNumber, block: repeats[1].No);
            }
        }

        private void CheckDuplicateExports()
        {
            var live = blocks.Where(block => !repeatedHistoryBlocks.Contains(block.No)).ToArray();
            foreach (var group in live.Where(block => IsSha256(block.Row.SourceSha256)).GroupBy(block => block.Row.SourceSha256).Where(group => group.Count() > 1))
                Add(Codes.DuplicateExport, $"Blocks {string.Join(", ", group.Select(block => block.No))} have the same source_sha256: one export is one block.",
                    Info, group.ElementAt(1).Row.RowNumber, block: group.ElementAt(1).No);
            foreach (var group in live.Where(block => block.Time.IsKnown && block.Row.SourceFile.Length > 0)
                         .GroupBy(block => (File: block.Row.SourceFile.ToUpperInvariant(), Time: block.Time))
                         .Where(group => group.Count() > 1))
                Add(Codes.DuplicateExport,
                    $"Blocks {string.Join(", ", group.Select(block => block.No))} have the same source_file and export_time: one export is one block.",
                    Info, group.ElementAt(1).Row.RowNumber, block: group.ElementAt(1).No);
        }

        private void CheckTimeOrder()
        {
            var known = blocks.Where(block => block.Time.IsKnown).ToArray();
            var pairs = new List<string>();
            for (var later = 1; later < known.Length; later++)
                for (var earlier = 0; earlier < later; earlier++)
                    if (ExportOrder.Compare(known[earlier].Time, known[later].Time) == ExportOrderResult.Newer)
                        pairs.Add($"block {known[later].No} is older than block {known[earlier].No}");
            if (pairs.Count > 0)
                Add(Codes.BlocksNotInTimeOrder,
                    $"Block numbers do not follow export_time ({DiagnosticLists.Of(pairs)}). This is allowed when an older export was appended later; exports are ordered by export_time, never by block number.",
                    Info, contract.BlockTableHeaderRow, occurrences: pairs.Count);
        }

        private List<VirtualRow> CheckExclusionMap()
        {
            var virtualRows = new List<VirtualRow>();
            var mapped = contract.Excluded;
            if (mapped.Count > 0 || excludedSheet?.Rows.Count > 0)
            {
                var columns = contract.ExcludedHeader.Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
                if (!columns.SequenceEqual(ConsolidationContractLayout.ExcludedColumns, StringComparer.Ordinal))
                {
                    Add(Codes.Unreadable,
                        $"Row 1 of {ConsolidationContractLayout.ExcludedSheet} must be exactly: {string.Join(" | ", ConsolidationContractLayout.ExcludedColumns)}.",
                        ConsolidationContractLayout.ExcludedSheet, 1);
                    mapped = [];
                }
            }

            var byNumber = blocks.ToDictionary(block => block.No);
            var unreadable = new List<int>();
            var unresolved = new Dictionary<int, List<int>>();
            var reused = new Dictionary<int, List<int>>();
            var twins = new Dictionary<int, HashSet<(string Sheet, int Row)>>();
            foreach (var map in mapped)
            {
                if (map.Block is not { } blockNo || ContractSheetView.CanonicalBlockSheet(map.Sheet) is not { } twinSheet || map.Row is not { } twinRow)
                {
                    unreadable.Add(map.RowNumber);
                    continue;
                }
                var twinBlock = blocks.FirstOrDefault(block => block.Holds(twinSheet, twinRow));
                var twinSheetView = twinSheet == ConsolidationContractLayout.DataSheet ? data : history;
                if (!byNumber.ContainsKey(blockNo) || twinBlock is null || twinBlock.No >= blockNo || twinSheetView?.HasRow(twinRow) != true)
                {
                    Rows(unresolved, blockNo).Add(map.RowNumber);
                    continue;
                }
                if (!(twins.TryGetValue(blockNo, out var used) ? used : twins[blockNo] = []).Add((twinSheet, twinRow)))
                {
                    Rows(reused, blockNo).Add(map.RowNumber);
                    continue;
                }
                virtualRows.Add(new(blockNo, map.RowNumber, twinSheet, twinRow));
            }
            const string Map = ConsolidationContractLayout.ExcludedSheet;
            if (unreadable.Count > 0)
                Add(Codes.Unreadable, $"{unreadable.Count} {Map} row(s) cannot be read: block and row must be whole numbers and sheet Data or Snapshot History (rows {DiagnosticLists.Of(unreadable)}).",
                    Map, unreadable[0], occurrences: unreadable.Count);
            foreach (var (blockNo, rows) in unresolved)
                Add(Codes.ExcludedUnresolved,
                    byNumber.ContainsKey(blockNo)
                        ? $"{rows.Count} {Map} row(s) of block {blockNo} point at a twin that is not a physical row of a lower-numbered block (rows {DiagnosticLists.Of(rows)})."
                        : $"{rows.Count} {Map} row(s) name block {blockNo}, which is not in the block table (rows {DiagnosticLists.Of(rows)}).",
                    Map, rows[0], block: blockNo, occurrences: rows.Count);
            foreach (var (blockNo, rows) in reused)
                Add(Codes.ExcludedTwinReused,
                    $"{rows.Count} {Map} row(s) of block {blockNo} point at a twin row another map row of the block already uses (rows {DiagnosticLists.Of(rows)}). Each omitted row needs its own distinct twin.",
                    Map, rows[0], block: blockNo, occurrences: rows.Count);

            foreach (var block in blocks)
            {
                var mapRows = mapped.Count(map => map.Block == block.No);
                var excluded = block.Row.ExcludedRows ?? 0;
                if (block.Completeness is BlockCompleteness.Delta or BlockCompleteness.Trimmed)
                {
                    if (mapRows < excluded)
                        Add(Codes.ExcludedMapMissing,
                            $"{block.Label} has excluded_rows {excluded}, but {Map} maps {mapRows} row(s) to it. Never leave rows out without mapping them.",
                            Info, block.Row.RowNumber, block: block.No);
                    else if (mapRows > excluded)
                        Add(Codes.BlockArithmetic, $"{block.Label} has excluded_rows {excluded}, but {Map} maps {mapRows} row(s) to it.",
                            Info, block.Row.RowNumber, block: block.No);
                }
                else if (mapRows > 0)
                    Add(Codes.BlockArithmetic,
                        $"{Map} maps {mapRows} row(s) to {block.Label}, which is {block.Completeness.ToContractText()}; only delta and trimmed blocks have mapped rows.",
                        Info, block.Row.RowNumber, block: block.No);
            }
            return virtualRows;
        }

        private static List<int> Rows(Dictionary<int, List<int>> rows, int blockNo) =>
            rows.TryGetValue(blockNo, out var list) ? list : rows[blockNo] = [];

        private void CheckColumns()
        {
            var expected = ContractSheetView.NormalizedHeaders(family.Headers);
            if (data is not null)
            {
                var extra = data.Headers.Where(name => !string.IsNullOrWhiteSpace(name) &&
                    !expected.Contains(Domain.Imports.ImportProfile.NormalizeHeader(name))).ToArray();
                if (extra.Length > 0)
                    Add(Codes.ExtraColumns,
                        $"{ConsolidationContractLayout.DataSheet} has column(s) that are not in the {family.FamilyCode} ETP header: {DiagnosticLists.Of(extra.Select(name => DiagnosticLists.Clip(name)).ToArray())}. A raw export cannot carry them.",
                        ConsolidationContractLayout.DataSheet, 1, extra[0]);
            }
            if (history is null) return;
            var extras = header.HistoryExtraColumns.Count > 0 ? header.HistoryExtraColumns : HistorySheetBlockReader.DefaultExtraColumns;
            var allowed = expected.Concat(ContractSheetView.NormalizedHeaders(extras)).ToHashSet(StringComparer.Ordinal);
            var unexpected = history.Headers.Where(name => !string.IsNullOrWhiteSpace(name) &&
                !allowed.Contains(Domain.Imports.ImportProfile.NormalizeHeader(name))).ToArray();
            if (unexpected.Length > 0)
                Add(Codes.ExtraColumns,
                    $"{ConsolidationContractLayout.HistorySheet} has column(s) that are neither in the ETP header nor in history_extra_columns: {DiagnosticLists.Of(unexpected.Select(name => DiagnosticLists.Clip(name)).ToArray())}.",
                    ConsolidationContractLayout.HistorySheet, 1, unexpected[0]);
        }

        private void CheckStore()
        {
            if (header.StoreCode is not { } store) return;
            var storeHeader = family.Columns.FirstOrDefault(column => column.CanonicalField == "store_code")?.SourceHeader;
            if (storeHeader is null) return;
            foreach (var sheet in new[] { data, history })
            {
                if (sheet?.Column(storeHeader) is not { } column) continue;
                var rows = sheet.Rows.Where(row => ContractSheetView.Text(sheet.Value(row, column)) is { Length: > 0 } value &&
                        !string.Equals(value, store, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.RowNumber).ToArray();
                if (rows.Length > 0)
                    Add(Codes.StoreMismatch,
                        $"store_code {DiagnosticLists.Clip(store, 12)} differs from the store of {rows.Length} {sheet.Name} row(s) (rows {DiagnosticLists.Of(rows)}).",
                        sheet.Name, rows[0], storeHeader, occurrences: rows.Length);
            }
        }

        private void CheckRowDates(IReadOnlyList<VirtualRow> virtualRows)
        {
            if (rule is ContractRule.Snapshot or ContractRule.Current) CheckSnapshotRows();
            if (rule != ContractRule.Transactional || family.PrimaryDateHeader is not { } dateHeader) return;
            foreach (var block in blocks.Where(block => block.Row.PeriodFrom is not null && block.Row.PeriodTo is not null))
            {
                var outside = new List<int>();
                foreach (var (sheetName, rowNumber) in BlockRows(block, virtualRows))
                {
                    var sheet = sheetName == ConsolidationContractLayout.DataSheet ? data : history;
                    if (sheet?.Column(dateHeader) is not { } column) continue;
                    if (ContractSheetView.Date(sheet.Value(rowNumber, column)) is { } date &&
                        (date < block.Row.PeriodFrom || date > block.Row.PeriodTo))
                        outside.Add(rowNumber);
                }
                if (outside.Count > 0)
                    Add(Codes.RowOutsidePeriod,
                        $"{block.Label}: {outside.Count} row(s) are dated outside its period {block.Row.PeriodFrom:yyyy-MM-dd}..{block.Row.PeriodTo:yyyy-MM-dd} (rows {DiagnosticLists.Of(outside)}).",
                        block.Sheet, outside[0], dateHeader, block.No, outside.Count);
            }
        }

        private void CheckSnapshotRows()
        {
            var dateField = SnapshotDateField(family);
            var dateHeader = dateField is null ? null : family.Columns.FirstOrDefault(column => column.CanonicalField == dateField)?.SourceHeader;
            var asOfHeader = HistoryColumn(HistorySheetBlockReader.SnapshotAsOfColumn);
            var fileHeader = HistoryColumn(HistorySheetBlockReader.SourceFileColumn);
            foreach (var block in blocks.Where(block => block.HasRange && block.Row.SnapshotDate is not null))
            {
                var sheet = block.Sheet == ConsolidationContractLayout.DataSheet ? data : history;
                if (sheet is null) continue;
                var snapshotDate = block.Row.SnapshotDate!.Value;
                var dateColumn = dateHeader is null ? null : sheet.Column(dateHeader);
                var asOfColumn = block.Sheet == ConsolidationContractLayout.HistorySheet && asOfHeader is not null ? sheet.Column(asOfHeader) : null;
                var fileColumn = block.Sheet == ConsolidationContractLayout.HistorySheet && fileHeader is not null ? sheet.Column(fileHeader) : null;
                var dates = new List<int>();
                var files = new List<int>();
                for (var rowNumber = block.First!.Value; rowNumber <= block.Last!.Value; rowNumber++)
                {
                    if (!sheet.HasRow(rowNumber)) continue;
                    if ((dateColumn is { } date && ContractSheetView.Date(sheet.Value(rowNumber, date)) != snapshotDate) ||
                        (asOfColumn is { } asOf && ContractSheetView.Date(sheet.Value(rowNumber, asOf)) != snapshotDate))
                        dates.Add(rowNumber);
                    if (fileColumn is { } file && !string.Equals(ContractSheetView.Text(sheet.Value(rowNumber, file)), block.Row.SourceFile, StringComparison.OrdinalIgnoreCase))
                        files.Add(rowNumber);
                }
                if (dates.Count > 0)
                    Add(Codes.SnapshotDateDisagrees,
                        $"{block.Label}: {dates.Count} row(s) are dated otherwise than its snapshot_date {snapshotDate:yyyy-MM-dd} (rows {DiagnosticLists.Of(dates)}).",
                        block.Sheet, dates[0], block.Sheet == ConsolidationContractLayout.HistorySheet ? asOfHeader : dateHeader, block.No, dates.Count);
                if (files.Count > 0)
                    Add(Codes.SnapshotDateDisagrees,
                        $"{block.Label}: {files.Count} {ConsolidationContractLayout.HistorySheet} row(s) name another SourceFile than the block's source_file (rows {DiagnosticLists.Of(files)}).",
                        block.Sheet, files[0], fileHeader, block.No, files.Count);
            }
        }

        private string? HistoryColumn(string name)
        {
            var declared = header.HistoryExtraColumns.Count > 0 ? header.HistoryExtraColumns : HistorySheetBlockReader.DefaultExtraColumns;
            return declared.FirstOrDefault(column => string.Equals(column, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The sheet rows a block's export held: its physical rows and the twin row of each of its map rows.</summary>
        private static IEnumerable<(string Sheet, int Row)> BlockRows(ParsedBlock block, IReadOnlyList<VirtualRow> virtualRows)
        {
            if (block.HasRange)
                for (var row = block.First!.Value; row <= block.Last!.Value; row++)
                    yield return (block.Sheet, row);
            foreach (var copy in virtualRows.Where(copy => copy.BlockNo == block.No))
                yield return (copy.TwinSheet, copy.TwinRow);
        }

        private void CheckRebuild(IReadOnlyList<VirtualRow> virtualRows)
        {
            if (request.RawExports.Count == 0) return;
            var checkable = blocks.Where(block => block.Completeness != BlockCompleteness.Legacy &&
                !repeatedHistoryBlocks.Contains(block.No) && IsSha256(block.Row.SourceSha256) &&
                request.RawExports.ContainsKey(block.Row.SourceSha256)).ToArray();
            if (checkable.Length == 0) return;

            var profile = family.CreateProfile();
            var stager = new ImportRowStager();
            var staged = new Dictionary<(string Sheet, int Row), string>();
            if (data is not null)
                foreach (var row in stager.Stage(data.Sheet, profile).Rows)
                    staged[(ConsolidationContractLayout.DataSheet, row.SourceRowNumber)] = RowText(row.Values);
            if (history is not null)
                foreach (var row in stager.Stage(HistorySheetBlockReader.StagingSheet(history.Sheet, header.HistoryExtraColumns), profile).Rows)
                    staged[(ConsolidationContractLayout.HistorySheet, row.SourceRowNumber)] = RowText(row.Values);

            foreach (var block in checkable)
            {
                var raw = request.RawExports[block.Row.SourceSha256];
                if (ContractSheetView.FamilySheet(raw, family) is not { } rawSheet)
                {
                    Add(Codes.BlockRebuildMismatch, $"{block.Label}: the raw export with its source_sha256 has no sheet with the {family.FamilyCode} ETP header.",
                        Info, block.Row.RowNumber, block: block.No);
                    continue;
                }
                var expected = Multiset(stager.Stage(rawSheet.Sheet, profile).Rows.Select(row => RowText(row.Values)));
                var rebuilt = Multiset(BlockRows(block, virtualRows)
                    .Select(position => staged.TryGetValue(position, out var text) ? text : null).OfType<string>());
                var missing = expected.Sum(pair => Math.Max(0, pair.Value - rebuilt.GetValueOrDefault(pair.Key)));
                var extra = rebuilt.Sum(pair => Math.Max(0, pair.Value - expected.GetValueOrDefault(pair.Key)));
                var superseded = block.Row.SupersededRows ?? 0;
                var holds = block.Completeness == BlockCompleteness.Trimmed ? extra == 0 && missing == superseded : extra == 0 && missing == 0;
                if (!holds)
                    Add(Codes.BlockRebuildMismatch,
                        $"{block.Label} rebuilt from its rows and map rows does not hold its raw export's rows: {missing} raw row(s) are missing and {extra} row(s) are not in the raw export" +
                        (block.Completeness == BlockCompleteness.Trimmed ? $" (a trimmed block may miss exactly its {superseded} superseded row(s))." : "."),
                        Info, block.Row.RowNumber, block: block.No, occurrences: missing + extra);
            }
        }

        private static Dictionary<string, int> Multiset(IEnumerable<string> rows)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows) counts[row] = counts.GetValueOrDefault(row) + 1;
            return counts;
        }

        /// <summary>A staged row as comparable text, timestamp columns left out (contract 4: they are ignored in every comparison).</summary>
        private string RowText(IReadOnlyDictionary<string, object?> values) => string.Join('\u001f', values
            .Where(pair => !IsIgnored(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={Canonical(pair.Value)}"));

        private bool IsIgnored(string field)
        {
            var column = family.Columns.FirstOrDefault(candidate => candidate.CanonicalField == field);
            return field.Contains("timestamp", StringComparison.OrdinalIgnoreCase) || column?.Role == ColumnRole.Ignored ||
                column?.SourceHeader.Contains("TIMESTAMP", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static string Canonical(object? value) => value switch
        {
            null => "",
            decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime time => time.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            string text => text.Trim(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? ""
        };

        private IReadOnlyList<SourceBlock> SourceBlocks(IReadOnlyList<VirtualRow> virtualRows) => blocks
            .Where(block => !repeatedHistoryBlocks.Contains(block.No))
            .Select(block =>
            {
                var row = block.Row;
                return new SourceBlock(block.No, block.Sheet, block.First, block.Last, row.RowCount ?? 0, block.Completeness,
                    BlockOrigin.Contract, block.Time)
                {
                    VirtualRowCount = virtualRows.Count(copy => copy.BlockNo == block.No),
                    SourceFileName = row.SourceFile.Length == 0 ? null : row.SourceFile,
                    SourceFormat = row.SourceFormat.Length == 0 ? null : row.SourceFormat,
                    SourceSha256 = IsSha256(row.SourceSha256) ? row.SourceSha256 : null,
                    PeriodFrom = row.PeriodFrom,
                    PeriodTo = row.PeriodTo,
                    PeriodBasis = row.PeriodBasis ?? PeriodBasis.None,
                    SnapshotDate = row.SnapshotDate,
                    SnapshotDateBasis = row.SnapshotDate is null ? null : SnapshotDateBasis.Contract,
                    RawRows = row.RawRows,
                    ExcludedRows = row.ExcludedRows,
                    SupersededRows = row.SupersededRows,
                    Disposition = row.Disposition.Length == 0 ? null : row.Disposition
                };
            })
            .ToArray();

        private int? KeyRow(string key) =>
            header.Keys.FirstOrDefault(candidate => string.Equals(candidate.Key.Trim(), key, StringComparison.Ordinal))?.RowNumber;

        private static bool IsSha256(string text) => text.Length == 64 && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

        private void Add(string code, string message, string? sheet = null, int? row = null, string? column = null,
            int? block = null, int occurrences = 1) =>
            diagnostics.Add(new ImportDiagnostic(code, (ImportDiagnosticSeverity)(int)ImportCodes.DefaultSeverity(code), message, sheet, row, column)
            {
                BlockNo = block,
                Occurrences = occurrences
            });
    }
}
