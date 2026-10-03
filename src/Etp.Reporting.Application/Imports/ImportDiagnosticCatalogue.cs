using System.Text.RegularExpressions;

namespace Etp.Reporting.Application.Imports;

/// <summary>
/// The messages an attempt may store and show for each diagnostic code (spec 11.1). A message is kept only
/// when it is one the code itself writes for that code, so text built from a workbook (a header, a cell, a
/// path) can never reach <c>import_attempt_issues</c> or History. Any other message becomes the code's first
/// template, and a code missing here gets <see cref="GenericMessage"/>. Templates never contain a value.
/// </summary>
public static class ImportDiagnosticCatalogue
{
    public const string GenericMessage = "Review the indicated source column or row and correct the workbook before retrying.";

    private const string Restate = " Review and request a controlled restatement.";

    /// <summary>
    /// The message of an import whose COMMIT the check after it could not confirm (IF-014). It holds no value,
    /// so it is kept for any code and SQL error number: the database error that hid the outcome must not
    /// replace the instruction to import the file again.
    /// </summary>
    public const string CommitOutcomeUnknownMessage =
        "The database did not confirm whether this import was saved. Import the file again: if it was saved, it is reported as already imported.";

    /// <summary>
    /// The message of an import that committed but whose result could not be read back (IF-014). Like
    /// <see cref="CommitOutcomeUnknownMessage"/> it is kept for any code and SQL error number.
    /// </summary>
    public const string SavedNotReadBackMessage =
        "The import was saved, but its result could not be read back. Import the file again to see it: it is reported as already imported.";

    private static readonly Dictionary<string, string[]> Templates = new(StringComparer.Ordinal)
    {
        // Reading and matching (ImportPreflight, WorkbookLayoutNormalizer, MatchedImportEnvelopeFactory).
        ["FILE_EMPTY"] = ["The source file is empty."],
        ["FILE_HASH_INVALID"] = ["The source file does not have a valid SHA-256 identity."],
        ["DUPLICATE_FILE"] = ["This exact source file has already been imported."],
        ["WORKBOOK_NO_SHEETS"] = ["The workbook contains no readable sheets."],
        ["SHEET_NAME_MISSING"] = ["A worksheet has no name."],
        ["HEADER_INVALID"] = ["A complete, non-empty header row is required."],
        ["HEADER_DUPLICATE"] = ["Duplicate normalized headers are not allowed."],
        ["LAYOUT_UNKNOWN"] = ["No import profile exactly matches a worksheet header signature."],
        ["LAYOUT_AMBIGUOUS"] = ["More than one worksheet matches an import profile."],
        ["EMPTY_EXPORT"] = ["Empty export; no rows to import."],
        ["REQUIRED_COLUMN_MISSING"] = ["A column required by the closest approved layout is missing."],
        ["UNEXPECTED_COLUMN"] = ["A column is not part of the closest approved layout."],
        ["ROW_EXTRA_COLUMNS"] =
        [
            "This row contains data beyond the approved header columns.",
            "This row contains data beyond the repeated header columns."
        ],
        ["REPEATED_LAYOUT_MISMATCH"] = ["The worksheet repeats its header layout horizontally, but the corresponding row values differ."],
        ["REPEATED_LAYOUT_COLLAPSED"] = ["An exact duplicated horizontal layout was validated and collapsed."],
        ["HEADER_BELOW_TITLE_ROWS"] = ["The header row was found below title rows; the title rows are not imported."],
        ["WORKBOOK_MULTIPLE_STORES"] = ["This workbook contains more than one store. Export one workbook per store."],
        ["IMPORT_LAYOUT_BLOCKED"] = ["Workbook validation was blocked."],

        // Staging (ImportRowStager, TypedCellConverter, StockWorkbookParser).
        ["UNKNOWN_SALES_TRANSACTION_TYPE"] = ["Unrecognised transaction type; this row was skipped."],
        ["UNKNOWN_STOCK_TRANSACTION_TYPE"] =
        [
            "Unrecognised transaction type; this row was skipped.",
            "Unrecognised stock transaction type; this row was skipped."
        ],
        ["WORKBOOK_SHEET_COUNT"] = ["A stock workbook must contain exactly one data worksheet."],
        ["UNKNOWN_STOCK_LAYOUT"] = ["The stock layout is not an approved exact-header profile."],
        ["STOCK_BALANCE_MISMATCH"] = ["Closing quantity does not equal opening plus source transaction quantity."],
        ["VALUE_REQUIRED"] = ["A required value is missing.", "A required date is missing.", "An identifier cannot be empty."],
        ["VALUE_INVALID"] =
        [
            "A value cannot be converted to the type its column requires.",
            .. new[] { "Text", "Identifier", "Decimal", "Integer", "Date", "Boolean" }.Select(type => $"Value cannot be converted to {type}.")
        ],
        ["TYPE_UNSUPPORTED"] = ["The approved layout uses an unsupported column type."],

        // Persistence. The alternatives are the differences the persist procedures write to import_conflicts.
        [ImportCodes.ImportConflict] =
        [
            "Stored facts differ from this source row." + Restate,
            "Invoice date differs." + Restate,
            "One or more protected transaction values differ." + Restate,
            "Revenue control values differ." + Restate,
            "Tender values differ." + Restate,
            "Stock movement values differ." + Restate,
            "Closing-stock values differ." + Restate
        ],

        // Import engine codes (spec Appendix B).
        [ImportCodes.ImportUpgradePending] = ["Import history must be upgraded before this report can import."],
        [ImportCodes.ExportContentMismatch] = ["Two copies of one export hold different content."],
        [ImportCodes.SnapshotDateUnknown] = ["The snapshot date could not be determined."],
        [ImportCodes.SnapshotDateAmbiguous] = ["More than one snapshot date is possible for these rows."],
        [ImportCodes.SnapshotDateRepeated] = ["Two blocks of the data sheet have the same snapshot date. Keep only the later export of that date."],
        [ImportCodes.SnapshotDateFromFolder] = ["The snapshot date was taken from the folder name."],
        [ImportCodes.SnapshotDateFromSiblings] = ["The snapshot date was taken from the other exports in this folder."],
        [ImportCodes.SnapshotDateFromOverride] = ["The snapshot date was taken from the date override; the file states no date of its own."],
        [ImportCodes.SnapshotMultipleDates] = ["The workbook holds more than one snapshot date. Import one snapshot date per workbook."],
        [ImportCodes.InfoBlocksUnusable] = ["The Info sheet's block list could not be used."],
        [ImportCodes.PeriodOverlapUnresolved] = ["Two exports cover overlapping periods that could not be resolved."],
        [ImportCodes.InSourceConflict] = ["Exports inside this workbook disagree about a document; the document is held."],
        [ImportCodes.LegacyBlocksDiffer] = ["Blocks of a legacy consolidated workbook differ for a document; the document is held."],
        [ImportCodes.HeaderDateMismatch] = ["The document's date differs from its invoice header; the document is held."],
        [ImportCodes.RowDateMissing] =
        [
            "The row has no usable date or document number; it is held and belongs to no document.",
            "The row has no usable date; it is held and belongs to no document."
        ],
        [ImportCodes.RowFactValueMissing] = ["The row lacks a value its fact table needs; it is held, and so is any document it belongs to."],
        [ImportCodes.FamilyDerived] = ["This report is derived from other reports."],
        [ImportCodes.InvoiceYearDiffers] = ["ETP's invoice year differs from the financial year of the invoice date; the financial year of the date is used."],
        [ImportCodes.StaleCopyCollapsed] = ["An older copy of a row was collapsed into the newer export's row."],
        [ImportCodes.StockRowRepeated] = ["A stock ledger row repeats an earlier row exactly; both are kept, each with its own line."],
        [ImportCodes.AttributeNotApplied] = ["A newer master-data or status value was not applied."],
        [ImportCodes.InvoiceTotalMismatch] = ["The invoice lines do not add up to the invoice total."],
        [ImportCodes.RestatementMatchesNothing] = ["The restatement matches no current import for this report, store and period."],
        [ImportCodes.RestatementTargetAmbiguous] = ["The restatement overlaps more than one current import. Pick the one to restate."],
        [ImportCodes.RestatementTargetNotCovered] = ["This file only partly overlaps a current import, so it cannot replace it. Import a file whose period covers it fully."],
        [ImportCodes.RestatementOtherImportChanged] = ["This file also changes another current import its period covers. A run restates only one import: restate each import with a corrected file for its own period."],
        [ImportCodes.RestatementApprovalRequired] = ["This replacement and reason need unused Owner approval before import."],
        [ImportCodes.ImportTimeout] = ["The import timed out and can be retried."],
        [ImportCodes.CommitOutcomeUnknown] = [CommitOutcomeUnknownMessage],
        ["ISSUES_TRUNCATED"] = ["More issue codes than an attempt keeps; their occurrences are counted together."],
        [ImportCodes.EvidenceNotRetained] = ["Data is present; the source file could not be kept as evidence."],

        // Consolidation contract (contract section 8).
        [ImportCodes.Contract.Unreadable] = ["The consolidation contract in the Info sheet could not be read."],
        [ImportCodes.Contract.VersionUnsupported] = ["The consolidation contract version is not supported."],
        [ImportCodes.Contract.KeyMissing] = ["A required consolidation contract key is missing or blank."],
        [ImportCodes.Contract.KeyUnknown] = ["A consolidation contract key is not recognised and was ignored."],
        [ImportCodes.Contract.FamilyMismatch] = ["The contract's family differs from the report the workbook matches."],
        [ImportCodes.Contract.StoreMismatch] = ["The contract's store differs from the store in the data."],
        [ImportCodes.Contract.RuleInvalid] = ["The contract's consolidation rule is not allowed for this report."],
        [ImportCodes.Contract.SheetMissing] = ["A sheet the contract names was not found."],
        [ImportCodes.Contract.ExtraColumns] = ["The Data sheet has a column that is not in the ETP header."],
        [ImportCodes.Contract.RowCountMismatch] = ["A row count in the contract differs from the sheet."],
        [ImportCodes.Contract.BlockTableHeader] = ["The contract's block table header is not as specified."],
        [ImportCodes.Contract.BlockGap] = ["The contract's blocks leave rows of a sheet uncovered."],
        [ImportCodes.Contract.BlockOverlap] = ["Two of the contract's blocks overlap."],
        [ImportCodes.Contract.BlockOutsideData] = ["A contract block lies outside its sheet's rows."],
        [ImportCodes.Contract.BlocksNotInTimeOrder] = ["Block numbers do not follow export time."],
        [ImportCodes.Contract.BlockArithmetic] = ["A block's row counts do not add up."],
        [ImportCodes.Contract.ShaInvalid] = ["A block's source SHA-256 is missing or not 64 lowercase hex characters."],
        [ImportCodes.Contract.ExportTimeInvalid] = ["A block's export time cannot be read or contradicts its file name."],
        [ImportCodes.Contract.ExportTimeMissing] = ["A block has no export time."],
        [ImportCodes.Contract.PeriodMissing] = ["A block has no period."],
        [ImportCodes.Contract.SnapshotDateMissing] = ["A snapshot block has no snapshot date."],
        [ImportCodes.Contract.SnapshotDateDuplicate] = ["Two blocks have the same snapshot date."],
        [ImportCodes.Contract.SnapshotDateDisagrees] = ["The data's date differs from the block's snapshot date."],
        [ImportCodes.Contract.CurrentShape] = ["A current-state workbook must have exactly one Data block."],
        [ImportCodes.Contract.ExcludedMapMissing] = ["A block's excluded rows are not all mapped."],
        [ImportCodes.Contract.ExcludedUnresolved] = ["An excluded-row map entry names a block or twin row that does not exist."],
        [ImportCodes.Contract.ExcludedTwinReused] = ["Two excluded rows of one block point at the same twin row."],
        [ImportCodes.Contract.BlockRebuildMismatch] = ["A rebuilt block does not match its raw export."],
        [ImportCodes.Contract.DuplicateExport] = ["Two blocks come from the same export."],
        [ImportCodes.Contract.RowOutsidePeriod] = ["A row is dated outside its block's period."],
        [ImportCodes.Contract.HistoryRepeatsData] = ["The Data snapshot also appears on Snapshot History."],
        [ImportCodes.Contract.CellNotText] = ["A contract cell is not text."],
        [ImportCodes.Contract.LegacyBlocks] = ["The workbook has legacy blocks; they are imported but never used to infer missing documents."]
    };

    public static IReadOnlyCollection<string> Codes => Templates.Keys;

    public static bool IsKnown(string? code) => code is not null && Templates.ContainsKey(code);

    /// <summary>The code's own message, or <see cref="GenericMessage"/> for a code the catalogue does not know.</summary>
    public static string Template(string? code) =>
        code is not null && Templates.TryGetValue(code, out var messages) ? messages[0] : GenericMessage;

    /// <summary>The message of an <c>IMPORT_CONFLICT</c> refusal: the count, never a row.</summary>
    public static string ConflictCountMessage(int count) =>
        $"{count:N0} conflicting rows. The complete file was rolled back. Review the source and use Restate.";

    /// <summary>
    /// The failure message an attempt may store and show (<c>import_attempts.failure_message</c>). The two
    /// commit-outcome messages are kept whatever the code and SQL number; our own SQL THROWs (50000-59999) keep
    /// their text; any other SQL error keeps only the "Database error" form; a code the catalogue knows keeps
    /// only its own text or the conflict count; an importer refusal with a code of its own keeps the text its
    /// code wrote.
    /// </summary>
    public static string SafeFailureMessage(string? code, string? message, int? sqlNumber)
    {
        if (string.IsNullOrWhiteSpace(message)) return Template(code);
        if (message is CommitOutcomeUnknownMessage or SavedNotReadBackMessage) return message;
        if (sqlNumber is >= 50000 and <= 59999) return message;
        if (sqlNumber is not null)
            return DatabaseErrorPattern.IsMatch(message) ? message : "The import failed with a database error.";
        if (code == ImportCodes.ImportConflict && ConflictCountPattern.IsMatch(message)) return message;
        return IsKnown(code) ? SafeMessage(code, message) : message;
    }

    private static readonly Regex DatabaseErrorPattern =
        new(@"^(The import timed out and can be retried\.|Database error -?\d+( in [A-Za-z0-9_.\[\]]+)?, line \d+\.)$");

    private static readonly Regex ConflictCountPattern =
        new(@"^\d[\d,]* conflicting rows\. The complete file was rolled back\. Review the source and use Restate\.$");

    /// <summary><paramref name="message"/> when the code writes exactly that text, otherwise <see cref="Template"/>.</summary>
    public static string SafeMessage(string? code, string? message) =>
        code is not null && Templates.TryGetValue(code, out var messages) && message is not null && messages.Contains(message, StringComparer.Ordinal)
            ? message
            : Template(code);
}
