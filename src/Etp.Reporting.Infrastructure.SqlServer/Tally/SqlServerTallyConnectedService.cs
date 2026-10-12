using System.Data;
using System.Globalization;
using System.Text.Json;
using Etp.Reporting.Application.Accounting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>The outcome of "Send to the test Tally". <paramref name="Result"/> is Tally's own answer when one came back; it never
/// proves the vouchers are in Tally. Only a read-back and Compare do.</summary>
public sealed record TallySendOutcome(long AttemptId, string AttemptStatus, int VoucherCount, TallyImportResult? Result, long ReadbackBeforeSendId, string Message);

/// <summary>1.12 D1, decision 49: talks to the test Tally in the acceptance VM over HTTP. Read back pulls the Day Book and records
/// it as a read-back (source HTTP) for the existing comparison. Send posts a batch's already written and verified Tally file, and
/// only after a fresh read-back proves none of its vouchers is in Tally yet, so nothing is ever posted twice. Both are refused
/// unless the batch's company is the TEST company 'Saagar Traders Titan' with an address on this PC (default: none, so off).
/// Owner only. Every request and answer is kept as evidence.</summary>
public sealed class SqlServerTallyConnectedService(
    string connectionString,
    Func<Uri, ITallyReadbackGateway> readbackGateway,
    Func<Uri, ITallyImportGateway>? importGateway = null,
    string? evidenceRootOverride = null)
{
    public const string HttpAdapterVersion = "http-12.1";

    /// <summary>Reads the Day Book of the batch's company for the dates (default: the batch's day) and records it. Tally not
    /// answering, or the company not open, records nothing and says so plainly.</summary>
    public async Task<TallyReadbackRecord> ReadBackAsync(long batchId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        var (batch, profile, endpoint) = await LoadAsync(batchId, cancellationToken);
        var from = fromDate ?? batch.BusinessDate;
        var to = toDate ?? batch.BusinessDate;
        if (from > to) throw new ArgumentException("The first date must not be after the last.");
        return await ReadBackAsync(batchId, profile, endpoint, from, to, cancellationToken);
    }

    /// <summary>Sends the batch's written Tally file to the test Tally. Refused when the file changed, when Tally already holds any
    /// of its vouchers, or when the read-back cannot prove it does not. Tally's answer is recorded with the attempt.</summary>
    public async Task<TallySendOutcome> SendAsync(long batchId, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        var (batch, profile, endpoint) = await LoadAsync(batchId, cancellationToken);
        if (profile.DefaultDeliveryMode != "HTTP")
            throw new InvalidOperationException("Sending straight to Tally is off for this company: its delivery is 'file'. Change it in Settings → Tally companies, or import the file in Tally by hand.");
        if (importGateway is null) throw new InvalidOperationException("Sending to Tally is not available in this build.");
        if (batch.Status != TallyBatchStatus.ExportedAwaitingImport)
            throw new InvalidOperationException("Write the Tally file first. Only a written and checked file is sent.");

        var evidence = new TallyEvidenceStore(connectionString, evidenceRootOverride);
        var payload = (await evidence.VerifyAsync(batchId, cancellationToken)).Where(check => check.Artifact.Kind == "PAYLOAD_XML")
            .OrderByDescending(check => check.Artifact.Id).FirstOrDefault()
            ?? throw new InvalidOperationException("No Tally file is recorded for this batch. Nothing was sent.");
        var content = payload.State == TallyEvidenceState.Ok ? await evidence.ReadVerifiedAsync(payload.Artifact, cancellationToken) : null;
        if (content is null) throw new InvalidOperationException("The batch's Tally file changed or is missing since it was written. Nothing was sent.");
        var document = TallyVoucherXmlReader.Read(content);
        if (document.FailureReason is not null || document.Vouchers.Count == 0)
            throw new InvalidOperationException("The batch's Tally file cannot be read. Nothing was sent.");
        if (!string.Equals(document.CompanyNameReported, profile.CompanyName.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException($"The file is for '{document.CompanyNameReported}', not '{profile.CompanyName}'. Nothing was sent.");
        var keys = document.Vouchers.SelectMany(voucher => voucher.Keys).ToHashSet(StringComparer.Ordinal);
        var dates = document.Vouchers.Select(voucher => voucher.VoucherDate ?? batch.BusinessDate).ToArray();
        if (TallyEducationalCalendar.AppliesTo(profile) && TallyEducationalCalendar.Default.Refusal(dates) is { } educational)
            throw new InvalidOperationException(educational + " Nothing was sent.");

        // Duplicate guard: what does Tally hold right now for these days?
        var before = await ReadBackAsync(batchId, profile, endpoint, dates.Min(), dates.Max(), cancellationToken);
        if (!before.IsComplete)
            throw new InvalidOperationException($"The read-back before sending is incomplete ({before.IncompleteReason}), so ETP cannot prove Tally does not hold these vouchers yet. Nothing was sent.");
        var present = await CountKeysInReadbackAsync(before.Id, keys, cancellationToken);
        if (present > 0)
            throw new InvalidOperationException($"Tally already holds {present} of these {keys.Count} voucher(s). Sending again would post them twice. Nothing was sent; use Compare instead.");

        var attemptId = await OpenAttemptAsync(batchId, profile.Id!.Value, payload.Artifact.Id, cancellationToken);
        var folder = TallyEvidencePaths.BatchFolder(profile.ProfileCode, batch.StoreCode, batch.BusinessDate, batchId);
        await evidence.WriteAsync(batchId, "HTTP_REQUEST", $@"{folder}\attempts\attempt-{attemptId}-request.xml", content, cancellationToken);
        await MarkSentAsync(attemptId, cancellationToken);

        // If this is cancelled the request may still have reached Tally: the attempt stays SENT and the next send reads back first.
        var exchange = await importGateway(endpoint).ImportAsync(profile.CompanyName.Trim(), content, cancellationToken);

        TallyArtifact? response = null;
        if (exchange.Response is { Length: > 0 } body)
            response = await evidence.WriteAsync(batchId, "HTTP_RESPONSE", $@"{folder}\attempts\attempt-{attemptId}-response.xml", body, cancellationToken);
        var result = exchange.HttpStatus is not null && exchange.Response is { Length: > 0 } ? TallyImportResponseParser.Parse(exchange.Response) : null;
        var status = exchange.HttpStatus is not null ? "RESPONDED"
            : exchange.Failure == TallyGatewayFailure.TimedOut ? "TIMED_OUT" : "TRANSPORT_FAILED";
        var diagnostics = JsonSerializer.Serialize(new
        {
            failure = exchange.Failure, message = exchange.Message, sent = document.Vouchers.Count,
            created = result?.Created, altered = result?.Altered, ignored = result?.Ignored, errors = result?.Errors, exceptions = result?.Exceptions,
            cancelled = result?.Cancelled, lastVoucherId = result?.LastVoucherId, duplicate = result?.ReportsDuplicate ?? false,
            lineErrors = result?.LineErrors.Take(50).Select(error => error.Length <= 500 ? error : error[..500]).ToArray() ?? [],
            readbackBeforeSend = before.Id
        });
        await CloseAttemptAsync(attemptId, status, exchange.HttpStatus, response?.Id, diagnostics,
            status == "RESPONDED" && result is not null && result.AllCreated(document.Vouchers.Count));

        var message = status switch
        {
            "RESPONDED" when result is not null => result.Describe(document.Vouchers.Count),
            "RESPONDED" => "Tally answered, but not with an import result. Read back from Tally before doing anything else.",
            "TIMED_OUT" => "Tally did not answer in time. The vouchers may or may not be in Tally: read back from Tally before sending again.",
            _ => (exchange.Message ?? "Tally could not be reached.") + " Nothing reached Tally; you can send again."
        };
        return new(attemptId, status, document.Vouchers.Count, result, before.Id, message);
    }

    private async Task<TallyReadbackRecord> ReadBackAsync(long batchId, TallyProfile profile, Uri endpoint, DateOnly from, DateOnly to, CancellationToken token)
    {
        var gateway = readbackGateway(endpoint);
        var company = profile.CompanyName.Trim();
        var open = await gateway.FetchOpenCompaniesAsync(token);
        if (!open.Exchange.Succeeded)
            throw new InvalidOperationException(open.Exchange.Message ?? "Tally is not answering on this PC.");
        if (!open.Names.Contains(company, StringComparer.Ordinal))
            throw new InvalidOperationException($"Tally is running but the company '{company}' is not open. Open it in Tally, then try again.");

        var exchange = await gateway.FetchVouchersAsync(new TallyReadbackRequest(company, from, to, [TallySalesVoucherComposer.VoucherType]), token);
        if (exchange.Response is not { Length: > 0 } body)
            throw new InvalidOperationException(exchange.Message ?? "Tally sent no answer. Nothing was recorded.");
        return await new SqlServerTallyReconciliationService(connectionString, evidenceRootOverride)
            .RecordReadbackAsync(batchId, "HTTP", from, to, body, exchange.Request, company, exchange.Failure, HttpAdapterVersion, token);
    }

    private sealed record BatchHead(string Status, string StoreCode, DateOnly BusinessDate, int ProfileId);

    private async Task<(BatchHead Batch, TallyProfile Profile, Uri Endpoint)> LoadAsync(long batchId, CancellationToken token)
    {
        if (batchId <= 0) throw new ArgumentOutOfRangeException(nameof(batchId));
        BatchHead? batch;
        await using (var connection = await OpenAsync(token))
        await using (var command = new SqlCommand(
            "SELECT status,store_code,business_date,tally_profile_id FROM dbo.accounting_batches WHERE accounting_batch_id=@batch AND batch_kind='SALES_VOUCHERS'", connection))
        {
            command.Parameters.AddWithValue("@batch", batchId);
            await using var reader = await command.ExecuteReaderAsync(token);
            batch = await reader.ReadAsync(token) && !reader.IsDBNull(3)
                ? new(reader.GetString(0), reader.GetString(1), DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetInt32(3))
                : null;
        }
        if (batch is null) throw new InvalidOperationException("This batch is not a Tally voucher batch, or it no longer exists.");
        var profile = (await new SqlServerTallyProfileService(connectionString).LoadAsync(token)).FirstOrDefault(item => item.Id == batch.ProfileId)
            ?? throw new InvalidOperationException("This batch's Tally company no longer exists.");
        if (TallyConnectedMode.Refusal(profile) is { } refusal) throw new InvalidOperationException(refusal);
        if (!TallyConnectedMode.IsAllowedEndpoint(profile.EndpointUrl!.Trim(), out var endpoint) || endpoint is null)
            throw new InvalidOperationException(TallyConnectedMode.OnlyThisPc);
        return (batch, profile, endpoint);
    }

    private async Task<int> CountKeysInReadbackAsync(long readbackId, IReadOnlySet<string> keys, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT COUNT(DISTINCT a.correspondence_key) FROM dbo.tally_actual_vouchers a
            JOIN OPENJSON(@keys) k ON CONVERT(nvarchar(200),k.value) COLLATE DATABASE_DEFAULT=a.correspondence_key
            WHERE a.tally_readback_id=@readback;
            """, connection);
        command.Parameters.AddWithValue("@readback", readbackId);
        command.Parameters.Add("@keys", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(keys.ToArray());
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    /// <summary>Opens a RECORDED HTTP attempt. Under the accounting lock it first refuses a send already in progress (an attempt
    /// opened in the last five minutes) and closes older open attempts: RECORDED ones as cancelled, SENT ones as timed out.
    /// The read-back just taken proved none of their vouchers reached Tally.</summary>
    private async Task<long> OpenAttemptAsync(long batchId, int profileId, long payloadArtifactId, CancellationToken token)
    {
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource='ETP.AccountingReservations',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @lock<0 THROW 51451,'Accounting is busy. Try again.',1;
            IF EXISTS(SELECT 1 FROM dbo.tally_attempts WITH(UPDLOCK,HOLDLOCK) WHERE accounting_batch_id=@batch AND delivery_mode='HTTP'
                AND attempt_status IN('RECORDED','SENT') AND started_utc>DATEADD(MINUTE,-5,SYSUTCDATETIME()))
              THROW 51943,'This batch is being sent to Tally right now. Wait for that send to finish, then read back.',1;
            UPDATE dbo.tally_attempts SET attempt_status=CASE attempt_status WHEN 'RECORDED' THEN 'CANCELLED' ELSE 'TIMED_OUT' END,completed_utc=SYSUTCDATETIME()
              WHERE accounting_batch_id=@batch AND delivery_mode='HTTP' AND attempt_status IN('RECORDED','SENT');
            INSERT dbo.tally_attempts(accounting_batch_id,tally_profile_id,payload_artifact_id,delivery_mode,payload_format,attempt_status)
            VALUES(@batch,@profile,@payload,'HTTP','XML','RECORDED');
            DECLARE @id bigint=SCOPE_IDENTITY();
            COMMIT TRANSACTION; SELECT @id;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@batch", batchId);
        command.Parameters.AddWithValue("@profile", profileId);
        command.Parameters.AddWithValue("@payload", payloadArtifactId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private async Task MarkSentAsync(long attemptId, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("""
            UPDATE dbo.tally_attempts SET attempt_status='SENT' WHERE tally_attempt_id=@id AND attempt_status='RECORDED';
            IF @@ROWCOUNT<>1 THROW 51944,'This send was closed by another send. Nothing was sent.',1;
            """, connection);
        command.Parameters.AddWithValue("@id", attemptId);
        await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>Recorded even when the caller has given up: an unrecorded answer would hide what Tally did.</summary>
    private async Task CloseAttemptAsync(long attemptId, string status, int? httpStatus, long? responseArtifactId, string diagnostics, bool allCreated)
    {
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            UPDATE dbo.tally_attempts SET attempt_status=@status,http_status=@http,response_artifact_id=@response,tally_diagnostics_json=@diagnostics,completed_utc=SYSUTCDATETIME()
              WHERE tally_attempt_id=@id AND attempt_status='SENT';
            IF @@ROWCOUNT<>1 THROW 51944,'This send was closed by another send. Read back from Tally before anything else.',1;
            EXEC dbo.record_operational_audit 'AccountingExport',@outcome,N'Tally file sent to the test Tally over HTTP; read back to prove the result',N'database';
            COMMIT TRANSACTION;
            """;
        await using var connection = await OpenAsync(CancellationToken.None);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", attemptId);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@http", (object?)httpStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("@response", (object?)responseArtifactId ?? DBNull.Value);
        command.Parameters.Add("@diagnostics", SqlDbType.NVarChar, -1).Value = diagnostics;
        command.Parameters.AddWithValue("@outcome", allCreated ? "Succeeded" : "Failed");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private async Task RequireOwnerAsync(CancellationToken token)
    {
        if (!(await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(token)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString)));
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
}
