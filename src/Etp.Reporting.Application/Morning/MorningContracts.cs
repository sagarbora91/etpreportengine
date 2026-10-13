using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Application.Service;

namespace Etp.Reporting.Application.Morning;

/// <summary>
/// The tone of a Morning page state, one-to-one with the Desktop's StatusTone (ETP-DESIGN-SYSTEM.md section 8). The
/// Application layer has no WPF, so the page maps this enum; every tone is always shown with a word (ST1).
/// </summary>
public enum MorningTone { Neutral, Success, Warning, Critical, Information }

/// <summary>Where a store is on the last business day.</summary>
public enum MorningStoreStatus
{
    /// <summary>Every required file and entry is in; "Finalise all ready stores" will lock it.</summary>
    Ready,
    /// <summary>Something required is missing or was refused; the card lists why.</summary>
    Blocked,
    /// <summary>The day is finalised (LOCKED).</summary>
    Locked,
    /// <summary>Marked as a no-sale day (decision 52) and not yet finalised.</summary>
    NoSaleDay,
    /// <summary>The store's day could not be read; the card says why.</summary>
    NotChecked
}

/// <summary>One shop result file under <c>ShopResults\pending</c> or <c>\published</c> (C1), as the Morning page needs it.</summary>
/// <param name="Role">TITAN, HELIOS or SERVICE (the shop PC that sent the zip).</param>
/// <param name="FileTimeUtc">The file's time on disk; a move into <c>published</c> keeps it, so this is when ETP wrote it.</param>
/// <param name="Overall">Accepted, AcceptedWithWarning, Refused or Duplicate, as the file says.</param>
/// <param name="FileNames">The shop files the result speaks about (names only), to tie it to a store's imports.</param>
public sealed record MorningShopResult(string Role, DateOnly BusinessDate, int Sequence, bool Published, DateTimeOffset FileTimeUtc,
    string Overall, IReadOnlyList<string> FileNames);

/// <summary>Everything the Morning page reads about one active Retail store for the last business day.</summary>
/// <param name="Workflow">The Close day state; null when it could not be read (<paramref name="LoadFailure"/> says why).</param>
/// <param name="Missing">The readiness items for this store (exports, entries, targets).</param>
/// <param name="Imports">Import history entries of this store that cover the day.</param>
/// <param name="NoSale">The store-day's no-sale mark (N1), or null.</param>
public sealed record MorningStoreSource(
    string StoreCode,
    string StoreName,
    DailyWorkflowState? Workflow,
    IReadOnlyList<DailyReadinessItem> Missing,
    IReadOnlyList<ImportHistoryEntry> Imports,
    NoSaleDay? NoSale = null,
    string? LoadFailure = null);

/// <summary>The raw inputs of <see cref="MorningSummaryBuilder"/>. Every part may be missing; the page then says so.</summary>
public sealed record MorningSources(DateOnly BusinessDate, IReadOnlyList<MorningStoreSource> Stores)
{
    /// <summary>The latest nightly health run (diag.load_health_check_summary); null when none ran yet.</summary>
    public HealthCheckNightlySummary? Nightly { get; init; }

    /// <summary>The first Fail (else Warning) explanation of the latest nightly run, for the Owner.</summary>
    public string? NightlyTopFinding { get; init; }

    /// <summary>True when the nightly summary could not be read (as opposed to "no run yet").</summary>
    public bool NightlyUnreadable { get; init; }

    /// <summary>The Service freshness strip (one chip per family group); null when it could not be read.</summary>
    public IReadOnlyList<ServiceFreshnessChip>? ServiceFreshness { get; init; }

    /// <summary>Every import history entry that covers the day (all stores, Service included), for the files chip.</summary>
    public IReadOnlyList<ImportHistoryEntry> AllImports { get; init; } = [];

    /// <summary>True when the import history could not be read.</summary>
    public bool ImportsUnreadable { get; init; }

    /// <summary>The shop result files for the day (pending and published).</summary>
    public IReadOnlyList<MorningShopResult> ShopResults { get; init; } = [];

    /// <summary>Target items of the current month (readiness of today), read on days 1-7 only; null otherwise.</summary>
    public IReadOnlyList<DailyReadinessItem>? MonthStartMissing { get; init; }

    /// <summary>The month the <see cref="MonthStartMissing"/> items are about.</summary>
    public DateOnly? MonthStart { get; init; }

    /// <summary>Hours since the newest off-PC copy (OPS-10's rule); null when this PC makes none or it cannot be read.</summary>
    public double? OffPcBackupAgeHours { get; init; }

    /// <summary>True when this PC copies backups to OneDrive (an ETP-Backups folder exists), even if no copy is there yet.</summary>
    public bool OffPcBackupConfigured { get; init; }

    /// <summary>Hours since the newest copy on the backup disk (G1, decision 54); null until that copy is set up.</summary>
    public double? DiskCopyAgeHours { get; init; }
}

/// <summary>A chip in the state row: System check, Shop files, Service.</summary>
/// <param name="Label">What the chip is about ("System check").</param>
/// <param name="Word">The state word the chip carries ("OK", "2 refused").</param>
/// <param name="Detail">One plain sentence beside the chip.</param>
/// <param name="OpenTaskId">The screen the chip opens, or null.</param>
public sealed record MorningChip(string Label, string Word, MorningTone Tone, string Detail, string? OpenTaskId);

/// <summary>One line of a store card: Files, Entries, Checks or Sent back.</summary>
public sealed record MorningCardLine(string Label, string Word, MorningTone Tone, string Detail);

/// <summary>A button on a store card. <paramref name="Argument"/> is a route argument (an import history key), never a path.</summary>
public sealed record MorningAction(string Label, string TaskId, string? Argument = null);

/// <summary>One card per active Retail store.</summary>
public sealed record MorningStoreCard(
    string StoreCode,
    string StoreName,
    MorningStoreStatus Status,
    string StatusWord,
    MorningTone Tone,
    IReadOnlyList<MorningCardLine> Lines,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<MorningAction> Actions,
    NoSaleDay? NoSale,
    bool CanMarkNoSale)
{
    public bool IsReady => Status is MorningStoreStatus.Ready || Status is MorningStoreStatus.NoSaleDay && Blockers.Count == 0;
}

/// <summary>The month-start card (days 1-7 of a month when a target of that month is missing).</summary>
public sealed record MorningMonthStartCard(string Title, string Message, IReadOnlyList<string> StoreCodes, string OpenTaskId);

/// <summary>The Morning page (template D): verdict, chips, store cards, month card, quiet footer.</summary>
public sealed record MorningSummary(
    DateOnly BusinessDate,
    string Verdict,
    string VerdictDetail,
    MorningTone VerdictTone,
    IReadOnlyList<string> ReadyStoreCodes,
    IReadOnlyList<MorningChip> Chips,
    IReadOnlyList<MorningStoreCard> Stores,
    MorningMonthStartCard? MonthStart,
    IReadOnlyList<MorningCardLine> Footer,
    DateTimeOffset BuiltUtc)
{
    /// <summary>"Finalise 2 ready stores", or null when nothing is ready (the primary button is then hidden, B1).</summary>
    public string? FinaliseLabel => ReadyStoreCodes.Count switch
    {
        0 => null,
        1 => "Finalise 1 ready store",
        var n => $"Finalise {n} ready stores"
    };
}

/// <summary>Builds the Owner's Morning page for the last business day. Read-only.</summary>
public interface IMorningSummaryQuery
{
    Task<MorningSummary> LoadAsync(DateOnly lastBusinessDay, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of finalising one store from the Morning page.</summary>
public sealed record MorningFinaliseResult(string StoreCode, bool Locked, string Message);
