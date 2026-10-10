using System.Globalization;

namespace Etp.Reporting.Application.DailyReadiness;

/// <summary>Which stores an expected export is checked for.</summary>
public enum ExpectedExportScope
{
    /// <summary>Every active Retail store in the request.</summary>
    RetailStore,
    /// <summary>The Service Centre store (AW330), when the request includes it.</summary>
    ServiceCentre
}

/// <summary>
/// One expected daily export. <paramref name="Code"/> is the code the user knows (R025, R011, SERVICE_RAW);
/// <paramref name="StoredReportCodes"/> are the <c>import_files.report_code</c> values that count as received
/// (R011 lands as CLOSING_STOCK and R030 as STOCK_LEDGER; the Service pack is any landed S-family file).
/// </summary>
public sealed record ExpectedExport(
    string Code,
    string Name,
    IReadOnlyList<string> StoredReportCodes,
    ExpectedExportScope Scope);

/// <summary>A required daily manual input and the screen that enters it.</summary>
public sealed record RequiredManualInput(string FieldCode, string Name, DailyReadinessDestination Destination);

/// <summary>
/// The expected-export list and required manual inputs, as code configuration (no table; 1.9.9).
/// Checked against live <c>import_files</c> on 10 Oct 2026 (see 199-IMPORT-REMINDER-DONE.md).
/// </summary>
public static class DailyReadinessExpectations
{
    public const string ServiceCentreStoreCode = "AW330";
    public const string ServiceRawPackCode = "SERVICE_RAW";
    public const string MonthlyTargetCode = "MONTHLY_TARGET";
    public const string StaffTargetsCode = "STAFF_TARGETS";

    /// <summary>
    /// The Service families that land in an etp_landing_snnn table (ServiceInterimFamilies.Importable);
    /// any one of them for AW330 covering the date means the day's raw pack was imported.
    /// A test keeps this list equal to the importer's.
    /// </summary>
    public static IReadOnlyList<string> ServiceRawPackFamilies { get; } =
    [
        "S002", "S003", "S004",
        "S006", "S007", "S008", "S009", "S010", "S011", "S012", "S013", "S014", "S015", "S016", "S017", "S018",
        "S019", "S020", "S021", "S022", "S023", "S024", "S025", "S026",
        "S029", "S030", "S031", "S032", "S033", "S034", "S035", "S036", "S037",
        "S039", "S040", "S041"
    ];

    public static IReadOnlyList<ExpectedExport> Exports { get; } =
    [
        new("R025", "sales lines", ["R025"], ExpectedExportScope.RetailStore),
        new("R022", "invoice tenders", ["R022"], ExpectedExportScope.RetailStore),
        new("R011", "closing stock", ["CLOSING_STOCK", "R011"], ExpectedExportScope.RetailStore),
        new("R030", "stock ledger", ["STOCK_LEDGER", "R030"], ExpectedExportScope.RetailStore),
        new(ServiceRawPackCode, "Service raw pack", ServiceRawPackFamilies, ExpectedExportScope.ServiceCentre)
    ];

    public static IReadOnlyList<RequiredManualInput> ManualInputs { get; } =
    [
        new("WALK_INS", "Walk-ins", DailyReadinessDestination.TodayWalkIns),
        new("OPENING_CASH", "Opening cash", DailyReadinessDestination.TodayCashEntries),
        new("EXPENSES", "Expenses", DailyReadinessDestination.TodayCashEntries),
        new("CASH_DEPOSIT", "Cash deposit", DailyReadinessDestination.TodayCashEntries)
    ];

    /// <summary>Every stored report code the query must read.</summary>
    public static IReadOnlyList<string> AllStoredReportCodes { get; } =
        Exports.SelectMany(x => x.StoredReportCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

/// <summary>
/// What the database holds for one business date: the active Retail stores, the stored report codes
/// received per store (a completed, current import file whose period covers the date), the manual
/// inputs with a value, and the stores with a monthly target / a staff target for the month.
/// Store and code comparisons ignore case.
/// </summary>
public sealed record DailyReadinessFacts(
    IReadOnlyList<string> ActiveRetailStores,
    IReadOnlyCollection<(string StoreCode, string ReportCode)> ReceivedExports,
    IReadOnlyCollection<(string StoreCode, string FieldCode)> EnteredManualInputs,
    IReadOnlyCollection<string> StoresWithMonthlyTarget,
    IReadOnlyCollection<string> StoresWithStaffTargets);

/// <summary>
/// The application service: turns the request and the facts into the missing-item list. Pure, so the
/// rules are tested without SQL.
/// </summary>
public static class DailyReadinessEvaluator
{
    private static readonly StringComparer Codes = StringComparer.OrdinalIgnoreCase;

    /// <summary>The Retail stores to check: the requested codes that are active Retail stores, or all of them when none is requested.</summary>
    public static IReadOnlyList<string> RetailStores(DailyReadinessRequest request, IReadOnlyList<string> activeRetailStores)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(activeRetailStores);
        var requested = (request.StoreCodes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        return requested.Length == 0
            ? activeRetailStores.Distinct(Codes).Order(Codes).ToArray()
            : activeRetailStores.Where(x => requested.Contains(x, Codes)).Distinct(Codes).Order(Codes).ToArray();
    }

    public static DailyReadinessResult Evaluate(DailyReadinessRequest request, DailyReadinessFacts facts)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(facts);
        var stores = RetailStores(request, facts.ActiveRetailStores);
        var received = facts.ReceivedExports.Select(x => Key(x.StoreCode, x.ReportCode)).ToHashSet(Codes);
        var entered = facts.EnteredManualInputs.Select(x => Key(x.StoreCode, x.FieldCode)).ToHashSet(Codes);
        var monthly = facts.StoresWithMonthlyTarget.ToHashSet(Codes);
        var staff = facts.StoresWithStaffTargets.ToHashSet(Codes);
        var date = request.BusinessDate;
        var day = date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        var month = date.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        var missing = new List<DailyReadinessItem>();

        foreach (var store in stores)
        {
            foreach (var export in DailyReadinessExpectations.Exports.Where(x => x.Scope == ExpectedExportScope.RetailStore))
                if (!export.StoredReportCodes.Any(code => received.Contains(Key(store, code))))
                    missing.Add(new(DailyReadinessItemKind.MissingExport, store, export.Code,
                        $"{export.Code} {export.Name} for {store} on {day} has not been imported.", DailyReadinessDestination.ImportIntake));
            foreach (var input in DailyReadinessExpectations.ManualInputs)
                if (!entered.Contains(Key(store, input.FieldCode)))
                    missing.Add(new(DailyReadinessItemKind.MissingManualInput, store, input.FieldCode,
                        $"{input.Name} for {store} on {day} has not been entered.", input.Destination));
            if (!monthly.Contains(store))
                missing.Add(new(DailyReadinessItemKind.MissingMonthlyTarget, store, DailyReadinessExpectations.MonthlyTargetCode,
                    $"The monthly sales target for {store} for {month} has not been set.", DailyReadinessDestination.SettingsMonthlyTargets));
            if (!staff.Contains(store))
                missing.Add(new(DailyReadinessItemKind.MissingStaffTargets, store, DailyReadinessExpectations.StaffTargetsCode,
                    $"Staff targets for {store} for {month} have not been set.", DailyReadinessDestination.SettingsStaffTargets));
        }

        if (request.IncludeServiceCentre)
        {
            const string service = DailyReadinessExpectations.ServiceCentreStoreCode;
            foreach (var export in DailyReadinessExpectations.Exports.Where(x => x.Scope == ExpectedExportScope.ServiceCentre))
                if (!export.StoredReportCodes.Any(code => received.Contains(Key(service, code))))
                    missing.Add(new(DailyReadinessItemKind.MissingExport, service, export.Code,
                        $"The {export.Name} for Service Centre ({service}) on {day} has not been imported.", DailyReadinessDestination.ImportIntake));
        }

        return new(date, stores, missing);
    }

    private static string Key(string store, string code) => store.Trim() + "|" + code.Trim();
}
