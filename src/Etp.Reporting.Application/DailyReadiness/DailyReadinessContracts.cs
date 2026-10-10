namespace Etp.Reporting.Application.DailyReadiness;

/// <summary>What kind of thing is missing for the business date.</summary>
public enum DailyReadinessItemKind
{
    /// <summary>An expected export (R025, R022, R011, R030 per Retail store; the Service raw pack for AW330) has no successful import covering the date.</summary>
    MissingExport,
    /// <summary>A required manual input (WALK_INS, OPENING_CASH, EXPENSES, CASH_DEPOSIT) has no value for the store and date.</summary>
    MissingManualInput,
    /// <summary>The store has no monthly sales target for the month of the date.</summary>
    MissingMonthlyTarget,
    /// <summary>The store has no staff sales target starting in the month of the date.</summary>
    MissingStaffTargets
}

/// <summary>The screen that fixes the item. The Today checklist maps each value to a navigation target.</summary>
public enum DailyReadinessDestination
{
    /// <summary>Import > Intake.</summary>
    ImportIntake,
    /// <summary>Today > Walk-ins.</summary>
    TodayWalkIns,
    /// <summary>Today > Cash > Cash and service entries.</summary>
    TodayCashEntries,
    /// <summary>Settings > Brands and targets > Monthly targets.</summary>
    SettingsMonthlyTargets,
    /// <summary>Settings > Staff targets.</summary>
    SettingsStaffTargets
}

/// <summary>
/// One missing item, one line on the Today checklist.
/// <para><see cref="Code"/> is the report code (R025, R022, R011, R030, SERVICE_RAW), the manual
/// input field code (WALK_INS, OPENING_CASH, EXPENSES, CASH_DEPOSIT), MONTHLY_TARGET or STAFF_TARGETS.</para>
/// <para><see cref="Message"/> is a plain sentence for the user, for example
/// "R025 Sales register for AX123 on 09 Oct 2026 has not been imported." It never holds customer data.</para>
/// </summary>
public sealed record DailyReadinessItem(
    DailyReadinessItemKind Kind,
    string StoreCode,
    string Code,
    string Message,
    DailyReadinessDestination Destination);

/// <summary>
/// The request: one business date and a store set.
/// <para><see cref="StoreCodes"/> empty means every active Retail store ("All stores").
/// Codes that are not active Retail stores are ignored for the Retail checks.</para>
/// <para><see cref="IncludeServiceCentre"/> adds the Service raw pack check for AW330.</para>
/// </summary>
public sealed record DailyReadinessRequest(
    DateOnly BusinessDate,
    IReadOnlyList<string> StoreCodes,
    bool IncludeServiceCentre = true);

/// <summary>
/// The answer. <see cref="Missing"/> is ordered by store, then exports, manual inputs, monthly
/// target, staff targets. <see cref="IsComplete"/> is true when nothing is missing (the panel hides).
/// <see cref="StoreCodes"/> are the Retail stores actually checked.
/// </summary>
public sealed record DailyReadinessResult(
    DateOnly BusinessDate,
    IReadOnlyList<string> StoreCodes,
    IReadOnlyList<DailyReadinessItem> Missing)
{
    public bool IsComplete => Missing.Count == 0;
}

/// <summary>
/// Daily readiness: for a business date and store set, the expected exports that have not been
/// imported, the required manual inputs not entered, and the month's targets not set. Read-only.
/// </summary>
public interface IDailyReadinessQuery
{
    Task<DailyReadinessResult> LoadAsync(DailyReadinessRequest request, CancellationToken cancellationToken = default);
}
