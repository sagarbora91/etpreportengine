namespace Etp.Reporting.Application.Service;

// TEMPORARY COPY for lane L5 (svc5-screens): written from SERVICE-LANES.md 4.1 step 5 so the
// screens compile before lane L0 lands. L0 owns this file; on the pull merge L0's version wins.

public enum ServiceJobEventKind { FirstSeen, StillListed, LeftList, Reappeared }

public sealed record ServiceRefresh(string ReportCode, DateOnly SnapshotDate, int Rows, long ImportFileId, DateTime ImportedAtUtc);
public sealed record ServiceJobRow(string JobOrderNumber, string StatusView, string StatusLabel, DateOnly? JobDate, DateOnly? Edd,
    string? Brand, string? Model, string? ProductCategory, string? CustomerName, decimal? SpareValue, decimal? LabourCharge,
    int Lines, DateOnly SnapshotDate, int InOtherLists);
public sealed record ServicePendingRow(string List, string JobOrderNumber, DateOnly? JobDate, int? AgeDays, string? Brand,
    string? Model, string? CustomerName, string? PendingStore, DateOnly SnapshotDate);
public sealed record ServiceJobEvent(string JobOrderNumber, string ReportCode, string ListLabel, ServiceJobEventKind EventKind,
    DateOnly SnapshotDate, DateOnly? PreviousSnapshotDate);
public sealed record ServiceMoneyDay(DateOnly BusinessDate, string Tender, decimal? S004Amount, decimal? ManualAmount,
    decimal? Difference, IReadOnlyList<string> ManualStores);
public sealed record ServiceMoneyChange(DateOnly BusinessDate, string ReportCode, DateOnly? PreviousSnapshotDate,
    decimal? PreviousAmount, DateOnly CurrentSnapshotDate, decimal? CurrentAmount);

public interface IServiceReportQuery
{
    Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default);
}
