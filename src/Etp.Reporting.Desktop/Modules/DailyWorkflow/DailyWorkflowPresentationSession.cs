extern alias EtpApplication;

using System.Globalization;

namespace Etp.Reporting.Desktop.Modules.DailyWorkflow;

using DailyControlStatus = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyControlStatus;
using DailyManualInput = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyManualInput;
using DailyManualStockCount = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyManualStockCount;
using DailyPackSection = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyPackSection;
using DailyWorkflowScope = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyWorkflowScope;
using DailyWorkflowState = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyWorkflowState;
using DailyWorkflowStatus = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyWorkflowStatus;
using FinaliseDailyWorkflow = EtpApplication::Etp.Reporting.Application.DailyWorkflow.FinaliseDailyWorkflow;
using ReopenDailyWorkflow = EtpApplication::Etp.Reporting.Application.DailyWorkflow.ReopenDailyWorkflow;
using SaveDailyManualInput = EtpApplication::Etp.Reporting.Application.DailyWorkflow.SaveDailyManualInput;
using SaveDailyStaffTarget = EtpApplication::Etp.Reporting.Application.DailyWorkflow.SaveDailyStaffTarget;
using SaveDailyStockCount = EtpApplication::Etp.Reporting.Application.DailyWorkflow.SaveDailyStockCount;

public enum DailyWorkflowTone
{
    Critical,
    Warning,
    Healthy
}

public sealed record DailyWorkflowPresentationState(
    bool IsAvailable,
    string Status,
    DailyWorkflowTone Tone,
    string Message,
    string SourceStatus,
    string InputStatus,
    IReadOnlyList<DailyManualInput> ManualInputs,
    IReadOnlyList<DailyManualStockCount> StockCounts,
    bool CanFinalise);

/// <summary>
/// Owns the current daily-workflow snapshot and all presentation-level parsing and transitions.
/// Persistence and report calculation remain behind the Application ports.
/// </summary>
public sealed class DailyWorkflowPresentationSession
{
    private DailyWorkflowState? snapshot;

    public bool HasSnapshot => snapshot is not null;

    public DailyWorkflowScope SelectScope(string? storeCode, DateTime? businessDate)
    {
        if (businessDate is null) throw new InvalidOperationException("Select the ETP business date.");
        if (string.IsNullOrWhiteSpace(storeCode)) throw new InvalidOperationException("Select a store.");
        return new(storeCode.Trim(), DateOnly.FromDateTime(businessDate.Value));
    }

    public DailyWorkflowPresentationState Show(
        DailyWorkflowState state,
        IReadOnlyList<DailyManualStockCount> stockCounts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stockCounts);
        snapshot = state;
        return new(
            true,
            state.Status.ToString(),
            Tone(state.Status),
            state.StatusMessage,
            state.MissingReports.Count == 0
                ? $"ETP sources: complete ({string.Join(", ", state.ImportedReports)})"
                : $"Missing ETP sources: {string.Join(", ", state.MissingReports)}",
            state.MissingRequiredInputs.Count == 0
                ? "Required manual inputs: complete (zero values remain distinct from missing values)."
                : $"Missing manual inputs: {string.Join(", ", state.MissingRequiredInputs)}",
            state.ManualInputs,
            stockCounts,
            state.CanFinalise);
    }

    public DailyWorkflowPresentationState ShowUnavailable(string message)
    {
        snapshot = null;
        return new(false, "Unavailable", DailyWorkflowTone.Critical, message, string.Empty, string.Empty, [], [], false);
    }

    public SaveDailyManualInput CreateManualInput(
        DailyWorkflowScope scope,
        string? fieldCode,
        string value,
        string user,
        string reason,
        IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(fieldCode)) throw new InvalidOperationException("Select a manual-entry field.");
        decimal? numeric = null;
        string? text = null;
        if (fieldCode == "OPERATIONAL_REMARK")
            text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        else
        {
            if (!decimal.TryParse(value, NumberStyles.Number, formatProvider ?? CultureInfo.CurrentCulture, out var parsed))
                throw new InvalidOperationException("Enter a valid numeric value.");
            if (fieldCode == "WALK_INS" && (parsed < 0 || decimal.Truncate(parsed) != parsed))
                throw new InvalidOperationException("Walk-ins must be a whole number of zero or more.");
            numeric = parsed;
        }
        return new(scope, fieldCode, numeric, text, user, reason.Trim());
    }

    public SaveDailyStockCount CreateStockCount(
        DailyWorkflowScope scope,
        string inventoryGroup,
        string display,
        string backstock,
        string defective,
        string yLocation,
        string physical,
        string remarks,
        string user,
        string reason,
        IFormatProvider? formatProvider = null) =>
        new(scope, inventoryGroup, OptionalDecimal(display, formatProvider), OptionalDecimal(backstock, formatProvider),
            OptionalDecimal(defective, formatProvider), OptionalDecimal(yLocation, formatProvider), OptionalDecimal(physical, formatProvider),
            string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(), user, reason);

    public SaveDailyStaffTarget CreateStaffTarget(
        string storeCode,
        string croNumber,
        DateTime? periodStart,
        DateTime? periodEnd,
        string targetValue,
        string user,
        string reason,
        IFormatProvider? formatProvider = null)
    {
        if (periodStart is null || periodEnd is null)
            throw new InvalidOperationException("Select the target start and end dates.");
        if (!decimal.TryParse(targetValue, NumberStyles.Number, formatProvider ?? CultureInfo.CurrentCulture, out var target))
            throw new InvalidOperationException("Enter a valid target sales value.");
        return new(storeCode, croNumber, DateOnly.FromDateTime(periodStart.Value), DateOnly.FromDateTime(periodEnd.Value), target, user, reason);
    }

    public static FinaliseDailyWorkflow CreateFinalise(
        DailyWorkflowScope scope,
        string user,
        IReadOnlyList<DailyPackSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var hasBlockers = sections.Any(x => x.Status is DailyControlStatus.Blocked or DailyControlStatus.Failed);
        return new(scope, user, hasBlockers);
    }

    public static ReopenDailyWorkflow CreateReopen(
        DailyWorkflowScope scope,
        string user,
        string reason,
        bool administratorApproved) => new(scope, user, reason.Trim(), administratorApproved);

    public static string PackReady(DailyControlStatus status, string message, int generationNumber, string contentSha256) =>
        $"{status}: {message} Generation {generationNumber}, control hash {contentSha256[..12]}.";

    public static string Failed(
        string operation,
        Exception exception,
        string safeUnauthorizedMessage = "Your Windows account does not have permission for this action.") =>
        $"{operation}: {DesktopFriendlyError.Describe(exception, safeUnauthorizedMessage)}";

    private static DailyWorkflowTone Tone(DailyWorkflowStatus status) => status switch
    {
        DailyWorkflowStatus.Locked or DailyWorkflowStatus.Reconciled => DailyWorkflowTone.Healthy,
        DailyWorkflowStatus.ReadyWithWarnings or DailyWorkflowStatus.Partial => DailyWorkflowTone.Warning,
        _ => DailyWorkflowTone.Critical
    };

    private static decimal? OptionalDecimal(string value, IFormatProvider? formatProvider)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, formatProvider ?? CultureInfo.CurrentCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"'{value}' is not a valid number.");
    }
}
