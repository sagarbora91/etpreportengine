namespace Etp.Reporting.Desktop.Modules.Accounting;

// 1.12 wave 2, lane U3. What the Tally transfer panel (TallyTransferView) needs from the services behind it, in plain
// types the screen can show. The composition root (DesktopCompositionRoot.Accounting.cs) maps the D1 services onto this;
// tests give the panel a fake. Nothing here knows a connection string, and nothing opens a socket.

/// <summary>One Tally voucher batch of a company, as the transfer panel lists it.</summary>
public sealed record TallyTransferBatch(long Id, string StoreCode, DateOnly BusinessDate, string Status);

/// <summary>What "Write masters" produced. <paramref name="MappingsToApprove"/> are ETP's proposed ledger names the Owner still
/// has to approve in Settings, one plain line each.</summary>
public sealed record TallyMastersSummary(
    string XmlPath,
    string ReviewPath,
    int GroupCount,
    int CostCentreCount,
    int LedgerCount,
    IReadOnlyList<string> MappingsToApprove);

/// <summary>What "Write file" produced for one batch.</summary>
public sealed record TallyFileSummary(string RelativePath, string Sha256, int VoucherCount, decimal VoucherTotal);

/// <summary>A recorded read-back of Tally's Day Book. Only Compare says whether Tally holds what ETP wrote.</summary>
public sealed record TallyReadbackSummary(long ReadbackId, bool IsComplete, string? IncompleteReason, int VoucherCount, string? TallyMessage);

/// <summary>Tally's answer to a send. <paramref name="Message"/> never claims the vouchers are in Tally.</summary>
public sealed record TallySendSummary(long AttemptId, string AttemptStatus, int VoucherCount, bool AllCreated, string Message);

/// <summary>A finished Compare.</summary>
public sealed record TallyCompareSummary(string BatchStatus, string RunOutcome, int VoucherCount, int ReconciledCount, int DifferenceCount, int FailureCount);

/// <summary>Every step of the Tally transfer, for one company at a time. All are Owner only; the services refuse anyone else.</summary>
public interface ITallyTransferActions
{
    /// <summary>The name of this PC (normally <see cref="Environment.MachineName"/>), compared with the profile's test PC name.</summary>
    string ThisPc { get; }

    Task<IReadOnlyList<TallyTransferBatch>> LoadBatchesAsync(int profileId, CancellationToken cancellationToken = default);

    /// <summary>Writes the masters file and the accountant's list into <paramref name="folder"/>.</summary>
    Task<TallyMastersSummary> WriteMastersAsync(int profileId, string folder, CancellationToken cancellationToken = default);

    Task<TallyFileSummary> WriteFileAsync(long batchId, CancellationToken cancellationToken = default);

    /// <summary>Reads Tally's Day Book over HTTP (the test Tally on this PC) and records it.</summary>
    Task<TallyReadbackSummary> ReadBackAsync(long batchId, CancellationToken cancellationToken = default);

    /// <summary>Records a Day Book file exported from Tally by hand.</summary>
    Task<TallyReadbackSummary> ReadBackFileAsync(long batchId, DateOnly from, DateOnly to, string path, CancellationToken cancellationToken = default);

    /// <summary>Sends the written file to the test Tally. Refused, before anything is recorded, on any PC but the profile's test PC.</summary>
    Task<TallySendSummary> SendAsync(long batchId, CancellationToken cancellationToken = default);

    Task<TallyCompareSummary> CompareAsync(long batchId, long readbackId, CancellationToken cancellationToken = default);

    /// <summary>The full path of the batch's evidence folder, or null when none can be named.</summary>
    Task<string?> EvidenceFolderAsync(long batchId, CancellationToken cancellationToken = default);
}

/// <summary>Where the Settings Tally screens get their transfer actions until SettingsWorkspaceView passes them in itself.
/// The composition root sets <see cref="Factory"/> when it builds the Accounting workspace.</summary>
public static class TallyTransferActionsProvider
{
    public static Func<ITallyTransferActions>? Factory { get; set; }
}
