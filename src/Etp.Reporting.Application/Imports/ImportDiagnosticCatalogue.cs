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
        // IF-025: the preflight's own message names the sheets; History keeps this template, which holds no sheet name.
        [ImportCodes.WorkbookEditedAfterExport] = ["This workbook was edited after export. Re-export it from ETP and import the new file; nothing from this workbook was imported."],
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
        [ImportCodes.FamilyDerived] = ["This report is derived from other reports.", "This Service report is built from the other Service reports, so it is not imported."],
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

        // Not-needed files of the folder import (IF-027; FolderImportService writes these exact texts).
        [ImportCodes.ControlWorkbookNotNeeded] = ["Consolidation control workbook; report workbooks are imported separately."],
        [ImportCodes.ReportFamilyNotNeeded] = ["This ETP report type is not needed by the reporting engine; the other workbooks are processed."],
        [ImportCodes.NotAnEtpExport] = ["This CSV file is not an ETP export; it was skipped and the other files are processed."],

        // Service Centre interim (decision 15; the folder import's ServiceRouting writes these exact texts).
        // SERVICE_SNAPSHOT_DATE_NEEDED is deliberately absent: its fix text names an example folder with a year, which a
        // template may not hold, and an importer refusal whose code the catalogue does not know keeps its own text.
        ["SERVICE_FAMILY_NOT_NEEDED"] = ["This Service report is not needed; the other files are processed."],
        ["SERVICE_FAMILY_DEFERRED"] = ["This Service report is not imported yet; it waits for the full Service import."],
        // 1.12 U2: no store code in a template (A4 makes the Service Centre data; FolderImportService writes this exact text).
        ["SERVICE_STORE_DEFAULTED"] = [ServiceStoreDefaultedMessage],
        ["SERVICE_SNAPSHOT_DATE_DIFFERS_FROM_HISTORY"] =
            ["The latest date on the Snapshot History sheet differs from the snapshot date this file is imported under. Check the folder date."],

        // Daily-inputs workbook, family D001 (1.10.1, decision 30). The codes are DailyInputsCodes in the Import project;
        // the parser and the applier write exactly these texts. DATE_TOO_OLD names DailyInputsTemplate.LookbackDays.
        ["DAILY_INPUTS_TEMPLATE_VERSION"] = ["This daily-inputs sheet is not a template version ETP knows. Use the current daily-inputs template."],
        ["DAILY_INPUTS_KIND_INVALID"] = ["The sheet type must be RETAIL or SERVICE. Use the template made for this shop PC."],
        ["DAILY_INPUTS_STORE_MISSING"] = ["The store code is missing. Use the template made for this shop PC."],
        ["DAILY_INPUTS_DATE_MISSING"] = ["The business date is missing. Enter the day the figures belong to."],
        ["DAILY_INPUTS_DATE_INVALID"] = ["The business date cannot be read. Enter it as a date, as dd-mmm-yyyy."],
        ["DAILY_INPUTS_DATE_IN_FUTURE"] = ["The business date is after today. Enter the day the figures belong to."],
        ["DAILY_INPUTS_DATE_TOO_OLD"] = ["The business date is more than 31 days ago. The Owner corrects older days on the screen."],
        ["DAILY_INPUTS_UNKNOWN_FIELD"] = ["A field code on the sheet is not one of the template's fields."],
        ["DAILY_INPUTS_FIELD_NOT_FOR_SHEET"] = ["A field does not belong on this sheet type."],
        ["DAILY_INPUTS_DUPLICATE_FIELD"] = ["A field code appears more than once on the sheet."],
        ["DAILY_INPUTS_VALUE_INVALID"] = ["A value is not a number the field accepts. Counts must be whole numbers."],
        ["DAILY_INPUTS_VALUE_NEGATIVE"] = ["A value is negative. Only the cash adjustment may be negative."],
        ["DAILY_INPUTS_TEXT_TOO_LONG"] = ["A text value is longer than the sheet allows."],
        ["DAILY_INPUTS_NAME_MISMATCH"] =
            ["The file name's store or sheet type differs from the sheet. Check the store and the sheet type, save the file under the matching name and send it again."],
        // Decision 44 #4: a warning, not a refusal. Two sentences: what happened, then what to do (the shop result splits at the first).
        ["DAILY_INPUTS_NAME_DATE_DIFFERS"] =
            ["The date in the file name differs from the date on the sheet, so the sheet was imported under its own date. Next time save the file under a name with the sheet's date."],
        ["DAILY_INPUTS_NAME_UNUSUAL"] = ["The file name does not follow the daily-inputs naming. The sheet was imported under its own store and date."],
        ["DAILY_INPUTS_NO_VALUES"] = ["The sheet has no values. Fill in the figures, save it and send it again."],
        // Decision 43 (1.11.2): the sheet's own zeros prove nothing, so the person who filled it must give a name.
        ["DAILY_INPUTS_FILLED_BY_MISSING"] =
            ["Filled by is empty. Type the name of the person who filled in the sheet, save it and send it again."],
        ["DAILY_INPUTS_NO_PERMISSION"] = ["The account importing this file may not change manual entries. It must be an active Owner or Store Manager."],
        ["DAILY_INPUTS_UNKNOWN_STORE"] = ["The store on the sheet is not an active ETP store."],
        ["DAILY_INPUTS_FIELD_INACTIVE"] = ["A field on the sheet is not an active manual entry."],
        ["DAILY_INPUTS_FIELD_KIND_MISMATCH"] = ["A field's value type differs from its manual entry definition."],
        ["DAILY_INPUTS_DAY_FINALISED"] =
            ["This business day is finalised, so its manual entries cannot change. Reopen the day, then put the file in the watch folder again."],
        ["DAILY_INPUTS_NOT_AVAILABLE"] = ["Daily-inputs files cannot be applied by this import. Nothing was changed."],

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

    /// <summary>The SERVICE_STORE_DEFAULTED text (1.12: names no store code; the Service Centre is data from 0067 on).
    /// Integration (U2 x A4): one text, A4's, so the folder import's note and the catalogue cannot drift.</summary>
    public const string ServiceStoreDefaultedMessage = Etp.Reporting.Application.Stores.ServiceCentreRules.StoreDefaultedMessage;

    // ----- Shop-facing text (1.12 U2, review m-2) -----
    // A shop result (ShopResultBuilder) shows the shop what happened and what to do. "What happened" is the first sentence of
    // the code's template. "What to do" is never an office instruction (no watch folder, no reopening, no Settings): the
    // shop can correct its own sheet, send a file again, or tell the office. Every other code says "Tell the office."

    /// <summary>What a shop does about a code nobody on the shop floor can fix.</summary>
    public const string TellTheOffice = "Tell the office.";

    /// <summary>"What happened" for a code the catalogue does not know (an importer refusal with a code of its own).</summary>
    public const string ShopUnknownMessage = "ETP could not import this file.";

    private const string UseTheSheet = "Use the daily-inputs sheet made for this shop PC, fill it in, save it and send it again.";
    private const string CorrectAndSend = ", save, send again.";

    private static readonly Dictionary<string, string> ShopWhatToDo = new(StringComparer.Ordinal)
    {
        ["DUPLICATE_FILE"] = "Nothing to do: ETP already has this file.",
        ["FILE_EMPTY"] = "Export the file again and send it. If it is refused again, tell the office.",
        ["FILE_HASH_INVALID"] = "Export the file again and send it. If it is refused again, tell the office.",
        ["WORKBOOK_NO_SHEETS"] = "Export the file again and send it. If it is refused again, tell the office.",
        [ImportCodes.WorkbookEditedAfterExport] = "Export it again and send the new file without opening or changing it.",
        ["EMPTY_EXPORT"] = "Nothing to do if the shop had nothing to export that day. Otherwise export it again and send it.",
        ["WORKBOOK_MULTIPLE_STORES"] = "Export one file per store and send them again.",

        ["DAILY_INPUTS_TEMPLATE_VERSION"] = UseTheSheet + " If this PC has no current sheet, tell the office.",
        ["DAILY_INPUTS_KIND_INVALID"] = UseTheSheet,
        ["DAILY_INPUTS_STORE_MISSING"] = UseTheSheet,
        ["DAILY_INPUTS_DATE_MISSING"] = "Open the sheet, type the day the figures belong to in the date box" + CorrectAndSend,
        ["DAILY_INPUTS_DATE_INVALID"] = "Open the sheet, type the date in the date box as dd-mmm-yyyy" + CorrectAndSend,
        ["DAILY_INPUTS_DATE_IN_FUTURE"] = "Open the sheet, correct the date in the box" + CorrectAndSend,
        ["DAILY_INPUTS_DATE_TOO_OLD"] = "Tell the office: a day this old is corrected in ETP by the office, not by a sheet.",
        ["DAILY_INPUTS_UNKNOWN_FIELD"] = "Use the sheet made for this shop PC without adding or renaming rows, and send it again.",
        ["DAILY_INPUTS_FIELD_NOT_FOR_SHEET"] = "Use the sheet made for this shop PC without adding or renaming rows, and send it again.",
        ["DAILY_INPUTS_DUPLICATE_FIELD"] = "Use the sheet made for this shop PC without adding or renaming rows, and send it again.",
        ["DAILY_INPUTS_VALUE_INVALID"] = "Open the sheet, correct the figure (counts are whole numbers)" + CorrectAndSend,
        ["DAILY_INPUTS_VALUE_NEGATIVE"] = "Open the sheet, correct the figure (only the cash adjustment may be below zero)" + CorrectAndSend,
        ["DAILY_INPUTS_TEXT_TOO_LONG"] = "Open the sheet, shorten the text" + CorrectAndSend,
        ["DAILY_INPUTS_NAME_MISMATCH"] = "Check the store and the sheet type, save the file under the matching name and send it again.",
        ["DAILY_INPUTS_NAME_DATE_DIFFERS"] = "Next time save the file under a name with the sheet's date.",
        ["DAILY_INPUTS_NAME_UNUSUAL"] = "Nothing to do now. Next time save the file under the name the sheet suggests.",
        ["DAILY_INPUTS_NO_VALUES"] = "Fill in the figures, save it and send it again.",
        ["DAILY_INPUTS_FILLED_BY_MISSING"] = "Type the name of the person who filled in the sheet, save it and send it again.",
        // The office takes the sheet in from where ETP keeps it (Fix buttons, 1.12): the shop does not send it again.
        ["DAILY_INPUTS_DAY_FINALISED"] = "Tell the office. This day is already closed in ETP; the office takes the sheet in from here, so do not send it again.",
    };

    /// <summary>"What happened", for the shop: the first sentence of the code's template, or <see cref="ShopUnknownMessage"/>.</summary>
    public static string MessageForShop(string? code) =>
        code is not null && Templates.TryGetValue(code, out var messages) ? FirstSentence(messages[0]).First : ShopUnknownMessage;

    /// <summary>"What to do", for the shop (review m-2): never an office instruction; <see cref="TellTheOffice"/> by default.</summary>
    public static string WhatToDoForShop(string? code) =>
        code is not null && ShopWhatToDo.TryGetValue(code, out var text) ? text : TellTheOffice;

    /// <summary>The one sentence "Tell the shop" copies: what happened, then what to do.</summary>
    public static string ShopSentence(string? code) => $"{MessageForShop(code)} {WhatToDoForShop(code)}";

    /// <summary>First sentence = what happened; the rest = the office's instruction. A one-sentence text has no rest.</summary>
    public static (string First, string Remainder) FirstSentence(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var index = template.IndexOf(". ", StringComparison.Ordinal);
        return index < 0 ? (template, string.Empty) : (template[..(index + 1)], template[(index + 2)..]);
    }

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
