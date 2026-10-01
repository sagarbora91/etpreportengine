namespace Etp.Reporting.Application.Imports;

/// <summary>
/// The import engine's diagnostic codes and SQL error numbers (import specification, Appendix B, and
/// consolidation contract section 8). Codes are stored on attempts and issues, so their text never changes.
/// Every code is a blocker unless <see cref="DefaultSeverity"/> says otherwise.
/// </summary>
public static class ImportCodes
{
    public const string ImportUpgradePending = "IMPORT_UPGRADE_PENDING";
    public const string ExportContentMismatch = "EXPORT_CONTENT_MISMATCH";
    public const string SnapshotDateUnknown = "SNAPSHOT_DATE_UNKNOWN";
    public const string SnapshotDateAmbiguous = "SNAPSHOT_DATE_AMBIGUOUS";
    public const string SnapshotDateFromFolder = "SNAPSHOT_DATE_FROM_FOLDER";
    public const string SnapshotMultipleDates = "SNAPSHOT_MULTIPLE_DATES";
    public const string InfoBlocksUnusable = "INFO_BLOCKS_UNUSABLE";
    public const string ContractUnreadable = Contract.Unreadable;
    public const string PeriodOverlapUnresolved = "PERIOD_OVERLAP_UNRESOLVED";
    public const string InSourceConflict = "IN_SOURCE_CONFLICT";
    public const string LegacyBlocksDiffer = "LEGACY_BLOCKS_DIFFER";
    public const string HeaderDateMismatch = "HEADER_DATE_MISMATCH";
    public const string RowDateMissing = "ROW_DATE_MISSING";
    public const string FamilyDerived = "FAMILY_DERIVED";
    public const string InvoiceYearDiffers = "INVOICE_YEAR_DIFFERS";
    public const string StaleCopyCollapsed = "STALE_COPY_COLLAPSED";
    public const string StockRowRepeated = "STOCK_ROW_REPEATED";
    public const string AttributeNotApplied = "ATTRIBUTE_NOT_APPLIED";
    public const string InvoiceTotalMismatch = "INVOICE_TOTAL_MISMATCH";
    public const string RestatementMatchesNothing = "RESTATEMENT_MATCHES_NOTHING";
    public const string RestatementTargetAmbiguous = "RESTATEMENT_TARGET_AMBIGUOUS";
    public const string RestatementApprovalRequired = "RESTATEMENT_APPROVAL_REQUIRED";
    public const string ImportTimeout = "IMPORT_TIMEOUT";
    public const string CommitOutcomeUnknown = "COMMIT_OUTCOME_UNKNOWN";
    public const string EvidenceNotRetained = "EVIDENCE_NOT_RETAINED";

    /// <summary>A persist procedure reported CONFLICT, or ALREADY_PRESENT for a document decided NEW (spec 9, step 12).</summary>
    public const string ImportConflict = "IMPORT_CONFLICT";

    /// <summary>The code recorded for a database error: <c>SQL_&lt;number&gt;</c> (spec 11.1).</summary>
    public static string Sql(int number) => $"SQL_{number}";

    public static ImportIssueSeverity DefaultSeverity(string code) => code switch
    {
        SnapshotDateFromFolder or InfoBlocksUnusable or RowDateMissing or StockRowRepeated or InvoiceTotalMismatch
            or EvidenceNotRetained or Contract.RowOutsidePeriod or Contract.HistoryRepeatsData or Contract.CellNotText
            => ImportIssueSeverity.Warning,
        FamilyDerived or InvoiceYearDiffers or StaleCopyCollapsed or AttributeNotApplied or Contract.KeyUnknown
            or Contract.BlocksNotInTimeOrder or Contract.LegacyBlocks
            => ImportIssueSeverity.Information,
        _ => ImportIssueSeverity.Blocker
    };

    /// <summary>Validation codes of the consolidation contract (contract section 8).</summary>
    public static class Contract
    {
        public const string Unreadable = "CONTRACT_UNREADABLE";
        public const string VersionUnsupported = "CONTRACT_VERSION_UNSUPPORTED";
        public const string KeyMissing = "CONTRACT_KEY_MISSING";
        public const string KeyUnknown = "CONTRACT_KEY_UNKNOWN";
        public const string FamilyMismatch = "CONTRACT_FAMILY_MISMATCH";
        public const string StoreMismatch = "CONTRACT_STORE_MISMATCH";
        public const string RuleInvalid = "CONTRACT_RULE_INVALID";
        public const string SheetMissing = "CONTRACT_SHEET_MISSING";
        public const string ExtraColumns = "CONTRACT_EXTRA_COLUMNS";
        public const string RowCountMismatch = "CONTRACT_ROW_COUNT_MISMATCH";
        public const string BlockTableHeader = "CONTRACT_BLOCK_TABLE_HEADER";
        public const string BlockGap = "CONTRACT_BLOCK_GAP";
        public const string BlockOverlap = "CONTRACT_BLOCK_OVERLAP";
        public const string BlockOutsideData = "CONTRACT_BLOCK_OUTSIDE_DATA";
        public const string BlocksNotInTimeOrder = "CONTRACT_BLOCKS_NOT_IN_TIME_ORDER";
        public const string BlockArithmetic = "CONTRACT_BLOCK_ARITHMETIC";
        public const string ShaInvalid = "CONTRACT_SHA_INVALID";
        public const string ExportTimeInvalid = "CONTRACT_EXPORT_TIME_INVALID";
        public const string ExportTimeMissing = "CONTRACT_EXPORT_TIME_MISSING";
        public const string PeriodMissing = "CONTRACT_PERIOD_MISSING";
        public const string SnapshotDateMissing = "CONTRACT_SNAPSHOT_DATE_MISSING";
        public const string SnapshotDateDuplicate = "CONTRACT_SNAPSHOT_DATE_DUPLICATE";
        public const string SnapshotDateDisagrees = "CONTRACT_SNAPSHOT_DATE_DISAGREES";
        public const string CurrentShape = "CONTRACT_CURRENT_SHAPE";
        public const string ExcludedMapMissing = "CONTRACT_EXCLUDED_MAP_MISSING";
        public const string ExcludedUnresolved = "CONTRACT_EXCLUDED_UNRESOLVED";
        public const string ExcludedTwinReused = "CONTRACT_EXCLUDED_TWIN_REUSED";
        public const string BlockRebuildMismatch = "CONTRACT_BLOCK_REBUILD_MISMATCH";
        public const string DuplicateExport = "CONTRACT_DUPLICATE_EXPORT";
        public const string RowOutsidePeriod = "CONTRACT_ROW_OUTSIDE_PERIOD";
        public const string HistoryRepeatsData = "CONTRACT_HISTORY_REPEATS_DATA";
        public const string CellNotText = "CONTRACT_CELL_NOT_TEXT";
        public const string LegacyBlocks = "CONTRACT_LEGACY_BLOCKS";
    }

    /// <summary>SQL error numbers of the import engine (51700-51799). The highest number in use before it was 51561.</summary>
    public static class SqlErrors
    {
        public const int FinancialYearPrecheck = 51700;
        public const int DuplicateControlsPrecheck = 51701;
        public const int DuplicateTendersPrecheck = 51702;
        public const int DuplicateContentRowPrecheck = 51703;
        public const int SourceRowsOnSeveralSheets = 51704;
        public const int LandedRowWithoutSheet = 51705;
        public const int SourceIndexFileOrRole = 51710;
        public const int SourceIndexRowsWithoutMetadata = 51711;
        public const int ApplyContext = 51715;
        public const int ChangeSetBindingOrStatus = 51720;
        public const int BaseVersionNotCurrent = 51721;
        public const int ProposedRowsChanged = 51722;
        public const int LockedDayInChangeSet = 51723;
        public const int SharedHeaderDateConflict = 51724;
        public const int ChangeSetNotAnApprovalRequest = 51725;
        public const int FinaliseRefusedWhileItemsPending = 51740;
        public const int EvidenceHashFormat = 51750;

        public static bool IsImportEngine(int number) => number is >= 51700 and <= 51799;
    }
}

/// <summary>The outcomes <c>dbo.record_import_attempt</c> accepts. The last four arrive with migration 0038.</summary>
public static class ImportAttemptOutcomes
{
    public const string Imported = "Imported";
    public const string EmptyExport = "empty export";
    public const string Duplicate = "Duplicate";
    public const string DuplicateContent = "Duplicate content";
    public const string AlreadyPresent = "Already present";
    public const string Failed = "Failed";
    public const string UnknownLayout = "Unknown layout";
    public const string NotNeeded = "Not needed";
    public const string Cancelled = "Cancelled";
    public const string ImportedChangesPending = "Imported (changes pending)";
    public const string Held = "Held";
    public const string RestatementPending = "Restatement pending";
    public const string ReDecided = "Re-decided";

    public static IReadOnlyList<string> All { get; } =
    [
        Imported, EmptyExport, Duplicate, DuplicateContent, AlreadyPresent, Failed, UnknownLayout, NotNeeded, Cancelled,
        ImportedChangesPending, Held, RestatementPending, ReDecided
    ];
}
