using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.DailyInputs;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Applies a parsed daily-inputs workbook (family D001, 1101-DAILY-INPUTS-DESIGN.md section 3) through the
/// same manual-input write as the Today screen (<see cref="DailyReportingWorkflowRepository.SaveManualInputCoreAsync"/>),
/// so the table trigger writes the history with the file name as the change reason and still refuses a
/// finalised day (51020). A file is all-or-nothing: every check runs before the first write, inside one
/// transaction; a blank cell never reaches here (the parser emits no row), and only values that differ
/// from the stored ones are written. An exact file already imported for its store and date is a Duplicate,
/// so an old file dropped again never overwrites a later screen correction.
/// </summary>
public sealed class SqlServerDailyInputsApplier : IDailyInputsApplier
{
    internal const string FamilyCode = "D001";
    internal const int ReasonLength = 500;

    // The DAILY_INPUTS_* codes this side raises (DailyInputsCodes, the shared contract). Issue messages are the catalogue's
    // value-free templates, so History keeps them as written (ImportDiagnosticCatalogue.SafeMessage).
    internal const string NoPermission = DailyInputsCodes.NoPermission;
    internal const string UnknownStore = DailyInputsCodes.UnknownStore;
    internal const string FieldInactive = DailyInputsCodes.FieldInactive;
    internal const string FieldKindMismatch = DailyInputsCodes.FieldKindMismatch;
    internal const string FieldNotForSheet = DailyInputsCodes.FieldNotForSheet;
    internal const string DayFinalised = DailyInputsCodes.DayFinalised;

    internal static string NoPermissionMessage => ImportDiagnosticCatalogue.Template(NoPermission);
    internal static string UnknownStoreMessage => ImportDiagnosticCatalogue.Template(UnknownStore);
    internal static string FieldInactiveMessage => ImportDiagnosticCatalogue.Template(FieldInactive);
    internal static string FieldKindMismatchMessage => ImportDiagnosticCatalogue.Template(FieldKindMismatch);
    internal static string FieldNotForSheetMessage => ImportDiagnosticCatalogue.Template(FieldNotForSheet);
    internal static string DayFinalisedMessage => ImportDiagnosticCatalogue.Template(DayFinalised);
    internal const string DuplicateMessage = "This daily inputs file was already imported for this store and date; nothing was changed.";
    internal const string AlreadyPresentMessage = "Every value on this daily inputs sheet is already stored; nothing was changed.";

    private const int TriggerDayLocked = 51020;
    private const int DefinitionInactive = 51022;

    private readonly string connectionString;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    public SqlServerDailyInputsApplier(string connectionString) : this(connectionString, null)
    {
    }

    internal SqlServerDailyInputsApplier(string connectionString, Func<CancellationToken, Task<ApplicationAccess>>? loadAccess)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        this.connectionString = connectionString;
        this.loadAccess = loadAccess ?? new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync;
    }

    public async Task<DailyInputsApplyResult> ApplyAsync(DailyInputsDocument document, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(user)) throw new ArgumentException("A value is required.", nameof(user));
        user = user.Trim();

        // 1. Permission, as the screen asks it (SqlServerDailyWorkflowService: CanImport).
        var access = await loadAccess(cancellationToken).ConfigureAwait(false);
        if (!access.CanImport) return Failed(NoPermission, NoPermissionMessage, document);

        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 2. The exact file already imported for this store and date: nothing is read or written again.
            if (await IsDuplicateAsync(connection, transaction, document, cancellationToken).ConfigureAwait(false))
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(DailyInputsOutcomes.Duplicate, 0, document.Values.Count, null, DuplicateMessage, []);
            }

            // 3. The store must be an active ETP store; its catalogue spelling is the one written.
            var store = await ActiveStoreAsync(connection, transaction, document.StoreCode, cancellationToken).ConfigureAwait(false);
            if (store is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Failed(UnknownStore, UnknownStoreMessage, document);
            }

            // 4. Every field against its definition; any error refuses the whole file before a write.
            var definitions = await LoadDefinitionsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var errors = Validate(document, definitions);
            if (errors.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new(DailyInputsOutcomes.Failed, 0, 0, errors[0].Code, errors[0].Message, errors);
            }

            // 5. The stored values and the day's status, locked until the end of the transaction.
            var locked = await IsDayLockedAsync(connection, transaction, store, document.BusinessDate, cancellationToken).ConfigureAwait(false);
            var stored = await LoadStoredAsync(connection, transaction, store, document.BusinessDate, cancellationToken).ConfigureAwait(false);
            var changed = document.Values.Where(value => IsChanged(value, stored.GetValueOrDefault(value.FieldCode.ToUpperInvariant()))).ToArray();
            var unchanged = document.Values.Count - changed.Length;

            // 6. Nothing differs: Already present, even on a finalised day.
            if (changed.Length == 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(DailyInputsOutcomes.AlreadyPresent, 0, unchanged, null, AlreadyPresentMessage, []);
            }

            // 7. Changes to a finalised day are refused before the trigger would refuse them.
            if (locked)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Failed(DayFinalised, DayFinalisedMessage, document);
            }

            // 8. The screen's write for each changed value, the file named in the reason (and so in the history).
            var reason = Reason(document);
            await DailyReportingWorkflowRepository.EnsureDayCoreAsync(connection, transaction, store, document.BusinessDate, cancellationToken)
                .ConfigureAwait(false);
            foreach (var value in changed)
                await DailyReportingWorkflowRepository.SaveManualInputCoreAsync(connection, transaction, store, document.BusinessDate,
                    value.FieldCode.ToUpperInvariant(), value.NumericValue, value.TextValue, user, reason, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(DailyInputsOutcomes.Imported, changed.Length, unchanged, null,
                $"{changed.Length:N0} daily input(s) saved; {unchanged:N0} unchanged.", []);
        }
        catch (SqlException exception) when (exception.Number is TriggerDayLocked or DefinitionInactive)
        {
            // A day finalised, or a field retired, between the checks above and the write (a race): nothing was kept.
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            return exception.Number == TriggerDayLocked
                ? Failed(DayFinalised, DayFinalisedMessage, document)
                : Failed(FieldInactive, FieldInactiveMessage, document);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The change reason the trigger copies into the history: the file, its short hash and who filled it in.</summary>
    internal static string Reason(DailyInputsDocument document)
    {
        var hash = document.Sha256.Length > 12 ? document.Sha256[..12] : document.Sha256;
        var reason = $"Daily inputs file {document.FileName} ({hash})";
        if (!string.IsNullOrWhiteSpace(document.FilledBy)) reason += $"; filled by {document.FilledBy.Trim()}";
        return reason.Length > ReasonLength ? reason[..ReasonLength] : reason;
    }

    /// <summary>
    /// True when <paramref name="value"/> would change what is stored: no row yet, a number that differs at
    /// 4 decimal places (the column's scale), or a text that differs ordinally once trimmed.
    /// </summary>
    internal static bool IsChanged(DailyInputValue value, StoredInput? stored)
    {
        if (stored is null) return true;
        if (value.NumericValue is { } number)
            return stored.NumericValue is not { } current || decimal.Round(number, 4) != decimal.Round(current, 4) || stored.TextValue is not null;
        return stored.TextValue is null || stored.NumericValue is not null ||
            !string.Equals(value.TextValue?.Trim(), stored.TextValue.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The definition checks of section 3, step 4, one issue per bad row: the field is active, its value kind
    /// matches, and it belongs to the sheet type (Store or Cash for RETAIL, Service for SERVICE).
    /// </summary>
    internal static IReadOnlyList<DailyInputsIssue> Validate(DailyInputsDocument document, IReadOnlyDictionary<string, InputDefinition> definitions)
    {
        var errors = new List<DailyInputsIssue>();
        foreach (var value in document.Values)
        {
            var code = value.FieldCode.ToUpperInvariant();
            if (!definitions.TryGetValue(code, out var definition) || !definition.IsActive)
                errors.Add(new(value.RowNumber, code, FieldInactive, FieldInactiveMessage));
            else if (!string.Equals(definition.ValueKind, value.ValueKind.ToString(), StringComparison.OrdinalIgnoreCase)
                     || (value.ValueKind == DailyInputValueKind.Text) != (value.TextValue is not null)
                     || (value.NumericValue is null) == (value.TextValue is null))
                errors.Add(new(value.RowNumber, code, FieldKindMismatch, FieldKindMismatchMessage));
            else if (!BelongsTo(document.Kind, definition.AppliesTo))
                errors.Add(new(value.RowNumber, code, FieldNotForSheet, FieldNotForSheetMessage));
        }
        return errors;
    }

    internal static bool BelongsTo(DailyInputsKind kind, string appliesTo) => kind switch
    {
        DailyInputsKind.Retail => appliesTo is "Store" or "Cash",
        DailyInputsKind.Service => appliesTo == "Service",
        _ => false
    };

    internal sealed record InputDefinition(string FieldCode, string ValueKind, string AppliesTo, bool IsActive);

    internal sealed record StoredInput(decimal? NumericValue, string? TextValue);

    private static DailyInputsApplyResult Failed(string code, string message, DailyInputsDocument document) =>
        new(DailyInputsOutcomes.Failed, 0, 0, code, message, [new(null, null, code, message)]);

    private static async Task<bool> IsDuplicateAsync(SqlConnection connection, SqlTransaction transaction, DailyInputsDocument document,
        CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.import_attempts
              WHERE source_sha256=@sha AND report_code=@report AND store_code=@store AND period_end=@date AND outcome='Imported')
            THEN 1 ELSE 0 END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@sha", document.Sha256);
        command.Parameters.AddWithValue("@report", FamilyCode);
        command.Parameters.AddWithValue("@store", document.StoreCode.Trim());
        command.Parameters.AddWithValue("@date", document.BusinessDate);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) == 1;
    }

    private static async Task<string?> ActiveStoreAsync(SqlConnection connection, SqlTransaction transaction, string storeCode, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT store_code FROM dbo.stores WHERE store_code=@store AND is_active=1;", connection, transaction);
        command.Parameters.AddWithValue("@store", storeCode.Trim());
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static async Task<IReadOnlyDictionary<string, InputDefinition>> LoadDefinitionsAsync(SqlConnection connection, SqlTransaction transaction,
        CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT field_code,value_kind,applies_to,is_active FROM dbo.manual_input_definitions;",
            connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var definitions = new Dictionary<string, InputDefinition>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            definitions[reader.GetString(0)] = new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));
        return definitions;
    }

    private static async Task<bool> IsDayLockedAsync(SqlConnection connection, SqlTransaction transaction, string store, DateOnly date,
        CancellationToken token)
    {
        await using var command = new SqlCommand(
            "SELECT status FROM dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND business_date=@date;",
            connection, transaction);
        command.Parameters.AddWithValue("@store", store);
        command.Parameters.AddWithValue("@date", date);
        return string.Equals(await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string, "LOCKED", StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, StoredInput>> LoadStoredAsync(SqlConnection connection, SqlTransaction transaction,
        string store, DateOnly date, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT field_code,numeric_value,text_value FROM dbo.manual_operational_inputs WITH(UPDLOCK,HOLDLOCK)
            WHERE store_code=@store AND business_date=@date;
            """, connection, transaction);
        command.Parameters.AddWithValue("@store", store);
        command.Parameters.AddWithValue("@date", date);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var stored = new Dictionary<string, StoredInput>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            stored[reader.GetString(0).ToUpperInvariant()] = new(reader.IsDBNull(1) ? null : reader.GetDecimal(1), reader.IsDBNull(2) ? null : reader.GetString(2));
        return stored;
    }

    private static async Task RollbackQuietlyAsync(SqlTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidOperationException or SqlException)
        {
            // The server already ended the transaction (XACT_ABORT or a severe error); nothing is left to undo.
        }
    }
}

/// <summary>The <c>dbo.record_import_attempt</c> outcomes a daily-inputs file can have.</summary>
internal static class DailyInputsOutcomes
{
    internal const string Imported = "Imported";
    internal const string AlreadyPresent = "Already present";
    internal const string Duplicate = "Duplicate";
    internal const string Failed = "Failed";
}
