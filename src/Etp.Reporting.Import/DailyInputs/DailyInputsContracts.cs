namespace Etp.Reporting.Import.DailyInputs;

// Shared contract of the daily-inputs workbook (family D001), 1101-DAILY-INPUTS-DESIGN.md section 5.
// Lane inputs-import owns this file; lane inputs-db implements IDailyInputsApplier against it.

public enum DailyInputsKind { Retail, Service }
public enum DailyInputValueKind { Quantity, Money, Text }

public sealed record DailyInputsField(string FieldCode, string Label, DailyInputValueKind ValueKind, bool AllowNegative);

/// <summary>One non-blank Value cell. Exactly one of NumericValue/TextValue is set.</summary>
public sealed record DailyInputValue(int RowNumber, string FieldCode, DailyInputValueKind ValueKind, decimal? NumericValue, string? TextValue);

public sealed record DailyInputsDocument(
    string FileName, string Sha256, string TemplateVersion, DailyInputsKind Kind,
    string StoreCode, DateOnly BusinessDate, string? FilledBy, IReadOnlyList<DailyInputValue> Values);

public sealed record DailyInputsIssue(int? RowNumber, string? FieldCode, string Code, string Message);

public sealed record DailyInputsParseResult(DailyInputsDocument? Document, IReadOnlyList<DailyInputsIssue> Errors, IReadOnlyList<DailyInputsIssue> Notes)
{
    public bool Succeeded => Document is not null && Errors.Count == 0;
}

/// <summary>Outcome is one of "Imported", "Already present", "Duplicate", "Failed" (dbo.record_import_attempt values).</summary>
public sealed record DailyInputsApplyResult(string Outcome, int Changed, int Unchanged, string? FailureCode, string Message, IReadOnlyList<DailyInputsIssue> Errors);

public interface IDailyInputsApplier
{
    Task<DailyInputsApplyResult> ApplyAsync(DailyInputsDocument document, string user, CancellationToken cancellationToken = default);
}

/// <summary>
/// Every daily-inputs diagnostic code. The parser (inputs-import) and the applier (inputs-db) both use these
/// constants; each has a value-free template in <c>ImportDiagnosticCatalogue</c>, which is also the issue message.
/// </summary>
public static class DailyInputsCodes
{
    // Parser (no database), design section 3.
    public const string TemplateVersion = "DAILY_INPUTS_TEMPLATE_VERSION";
    public const string KindInvalid = "DAILY_INPUTS_KIND_INVALID";
    public const string StoreMissing = "DAILY_INPUTS_STORE_MISSING";
    public const string DateMissing = "DAILY_INPUTS_DATE_MISSING";
    public const string DateInvalid = "DAILY_INPUTS_DATE_INVALID";
    public const string DateInFuture = "DAILY_INPUTS_DATE_IN_FUTURE";
    public const string DateTooOld = "DAILY_INPUTS_DATE_TOO_OLD";
    public const string UnknownField = "DAILY_INPUTS_UNKNOWN_FIELD";
    public const string FieldNotForSheet = "DAILY_INPUTS_FIELD_NOT_FOR_SHEET";
    public const string DuplicateField = "DAILY_INPUTS_DUPLICATE_FIELD";
    public const string ValueInvalid = "DAILY_INPUTS_VALUE_INVALID";
    public const string ValueNegative = "DAILY_INPUTS_VALUE_NEGATIVE";
    public const string TextTooLong = "DAILY_INPUTS_TEXT_TOO_LONG";
    public const string NameMismatch = "DAILY_INPUTS_NAME_MISMATCH";
    /// <summary>Information only: the file still imports.</summary>
    public const string NameUnusual = "DAILY_INPUTS_NAME_UNUSUAL";
    public const string NoValues = "DAILY_INPUTS_NO_VALUES";
    /// <summary>Decision 43 (12 Oct 2026): the Filled by cell is empty, so nothing proves a person filled the sheet.</summary>
    public const string FilledByMissing = "DAILY_INPUTS_FILLED_BY_MISSING";

    // Applier (SQL) and the folder-import hook (inputs-db).
    public const string NoPermission = "DAILY_INPUTS_NO_PERMISSION";
    public const string UnknownStore = "DAILY_INPUTS_UNKNOWN_STORE";
    public const string FieldInactive = "DAILY_INPUTS_FIELD_INACTIVE";
    public const string FieldKindMismatch = "DAILY_INPUTS_FIELD_KIND_MISMATCH";
    public const string DayFinalised = "DAILY_INPUTS_DAY_FINALISED";
    public const string NotAvailable = "DAILY_INPUTS_NOT_AVAILABLE";

    /// <summary>Every code above, for catalogue tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        TemplateVersion, KindInvalid, StoreMissing, DateMissing, DateInvalid, DateInFuture, DateTooOld,
        UnknownField, FieldNotForSheet, DuplicateField, ValueInvalid, ValueNegative, TextTooLong,
        NameMismatch, NameUnusual, NoValues, FilledByMissing,
        NoPermission, UnknownStore, FieldInactive, FieldKindMismatch, DayFinalised, NotAvailable
    ];
}
