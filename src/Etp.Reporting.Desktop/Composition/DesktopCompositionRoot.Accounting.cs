extern alias EtpApplication;
using System.Globalization;
using System.IO;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Infrastructure.Http;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Tally;
using Microsoft.Data.SqlClient;
using EtpApplication::Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Desktop.Composition;

// Accounting: the accounting batches workspace and, since 1.12 U3, the Tally transfer actions behind Settings > Accounting >
// Tally vouchers. The Tally screens themselves are still built by the Settings workspace; the transfer card reaches its
// actions through TallyTransferActionsProvider until SettingsWorkspaceView passes them in (integrator note, U3).
public sealed partial class DesktopCompositionRoot
{
    private static AccountingWorkspaceView CreateAccountingWorkspaceView(Func<string> connection)
    {
        // One adapter per connection string, so its HttpTallyGateway (IDisposable) is created once per endpoint and lives as long
        // as the window. The connection may change while the window is open (Settings), hence the lookup on every call.
        SqlTallyTransferActions? current = null;
        TallyTransferActionsProvider.Factory = () =>
        {
            var value = connection();
            if (current is null || !string.Equals(current.ConnectionString, value, StringComparison.Ordinal))
            {
                current?.Dispose();
                current = new SqlTallyTransferActions(value);
            }
            return current;
        };
        return new(new AccountingPresentationSession(() => new SqlServerAccountingService(connection())));
    }
}

/// <summary>Maps the D1 Tally services onto <see cref="ITallyTransferActions"/>. Owner only: every service call checks that itself.
/// Gateways to the test Tally are cached per endpoint and disposed with this object. Nothing here contacts any Tally unless the
/// Owner presses Read back or Send, and the gateway only ever accepts http://127.0.0.1:9000/ or http://localhost:9000/.</summary>
internal sealed class SqlTallyTransferActions(
    string connectionString,
    Func<string>? machineName = null,
    string? evidenceRootOverride = null,
    Func<Uri, ITallyReadbackGateway>? readbackGateway = null,
    Func<Uri, ITallyImportGateway>? importGateway = null) : ITallyTransferActions, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, HttpTallyGateway> gateways = new(StringComparer.Ordinal);
    private bool disposed;

    public string ConnectionString => connectionString;

    public string ThisPc => (machineName ?? (() => Environment.MachineName))();

    private HttpTallyGateway Gateway(Uri endpoint)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var key = endpoint.AbsoluteUri;
            if (!gateways.TryGetValue(key, out var found)) gateways[key] = found = new HttpTallyGateway(endpoint);
            return found;
        }
    }

    private SqlServerTallyConnectedService Connected() =>
        new(connectionString, readbackGateway ?? (uri => Gateway(uri)), importGateway ?? (uri => Gateway(uri)), evidenceRootOverride, machineName);

    public async Task<IReadOnlyList<TallyTransferBatch>> LoadBatchesAsync(int profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP(100) accounting_batch_id,store_code,business_date,status FROM dbo.accounting_batches
            WHERE batch_kind='SALES_VOUCHERS' AND tally_profile_id=@profile ORDER BY business_date DESC,accounting_batch_id DESC;
            """, connection);
        command.Parameters.AddWithValue("@profile", profileId);
        var result = new List<TallyTransferBatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetInt64(0), reader.GetString(1), DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetString(3)));
        return result;
    }

    public async Task<TallyMastersSummary> WriteMastersAsync(int profileId, string folder, CancellationToken cancellationToken = default)
    {
        var service = new SqlServerTallyMastersService(connectionString);
        var plan = await service.PlanAsync(profileId, cancellationToken);
        var receipt = await service.ExportAsync(profileId, folder, cancellationToken);
        return new(receipt.XmlPath, receipt.ReviewPath, receipt.GroupCount, receipt.CostCentreCount, receipt.LedgerCount,
            plan.MappingsToApprove.Select(mapping => $"{mapping.StoreCode} {mapping.BusinessEvent}: {mapping.LedgerName}").ToArray());
    }

    public async Task<TallyFileSummary> WriteFileAsync(long batchId, CancellationToken cancellationToken = default)
    {
        var receipt = await new SqlServerTallySalesBatchService(connectionString, evidenceRootOverride: evidenceRootOverride).ExportAsync(batchId, cancellationToken);
        return new(receipt.RelativePath, receipt.Sha256, receipt.VoucherCount, receipt.VoucherTotal);
    }

    public async Task<TallyReadbackSummary> ReadBackAsync(long batchId, CancellationToken cancellationToken = default) =>
        Summarise(await Connected().ReadBackAsync(batchId, cancellationToken: cancellationToken));

    public async Task<TallyReadbackSummary> ReadBackFileAsync(long batchId, DateOnly from, DateOnly to, string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose the Day Book file exported from Tally.", nameof(path));
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The Day Book file is not there any more. Choose it again.");
        if (info.Length > TallyXml.MaxBytes) throw new InvalidOperationException("That file is too large to be a Tally Day Book (over 50 MB).");
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        return Summarise(await new SqlServerTallyReconciliationService(connectionString, evidenceRootOverride)
            .LoadManualReadbackAsync(batchId, from, to, content, cancellationToken));
    }

    public async Task<TallySendSummary> SendAsync(long batchId, CancellationToken cancellationToken = default)
    {
        var outcome = await Connected().SendAsync(batchId, cancellationToken);
        return new(outcome.AttemptId, outcome.AttemptStatus, outcome.VoucherCount,
            outcome.AttemptStatus == "RESPONDED" && outcome.Result is { } result && result.AllCreated(outcome.VoucherCount), outcome.Message);
    }

    public async Task<TallyCompareSummary> CompareAsync(long batchId, long readbackId, CancellationToken cancellationToken = default)
    {
        // The batch's file is re-hashed inside CompareAsync (payloadUnchanged); the source-facts re-hash is the caller's, and no
        // screen has a way to recompute it yet, so the unchanged-source claim is the one the service already holds. Integrator note.
        var comparison = await new SqlServerTallyReconciliationService(connectionString, evidenceRootOverride)
            .CompareAsync(batchId, readbackId, sourceUnchanged: true, cancellationToken);
        var result = comparison.Result;
        return new(result.BatchStatus, result.RunOutcome, result.Vouchers.Count,
            result.Vouchers.Count(voucher => voucher.Status == TallyVoucherStatus.Reconciled),
            result.Differences.Count, result.Differences.Count(difference => difference.Severity == "FAIL"));
    }

    public async Task<string?> EvidenceFolderAsync(long batchId, CancellationToken cancellationToken = default)
    {
        string? root = evidenceRootOverride;
        string profileCode, store;
        DateOnly day;
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("""
                SELECT p.profile_code,b.store_code,b.business_date,(SELECT TOP(1) tally_evidence_root FROM dbo.product_settings ORDER BY product_setting_id)
                FROM dbo.accounting_batches b JOIN dbo.tally_profiles p ON p.tally_profile_id=b.tally_profile_id
                WHERE b.accounting_batch_id=@batch AND b.batch_kind='SALES_VOUCHERS';
                """, connection);
            command.Parameters.AddWithValue("@batch", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            profileCode = reader.GetString(0); store = reader.GetString(1); day = DateOnly.FromDateTime(reader.GetDateTime(2));
            root ??= reader.IsDBNull(3) ? null : reader.GetString(3);
        }
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) return null;
        var folder = Path.Combine(root, TallyEvidencePaths.BatchFolder(profileCode, store, day, batchId));
        return Directory.Exists(folder) ? folder : null;
    }

    private static TallyReadbackSummary Summarise(TallyReadbackRecord record) =>
        new(record.Id, record.IsComplete, record.IncompleteReason, record.VoucherCount, record.TallyMessage);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var found in gateways.Values) found.Dispose();
            gateways.Clear();
        }
    }
}
