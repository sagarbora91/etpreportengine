using System.Globalization;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Application.Accounting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

public sealed record TallyReadbackRecord(long Id, long BatchId, string? CompanyNameReported, bool IsComplete, string? IncompleteReason, int VoucherCount, string? TallyMessage);

public sealed record TallyComparison(long RunId, long ReadbackId, TallyReconciliationResult Result);

/// <summary>Owner-only persistence for plan tasks 7, 9 (manual file) and 10: validation findings, a Day Book file
/// the operator exported from Tally by hand, and a recorded three-way comparison. Nothing here sends anything to Tally,
/// and no batch status is changed: the batch status list widens with the file export step.</summary>
public sealed class SqlServerTallyReconciliationService(string connectionString, string? evidenceRootOverride = null)
{
    public const string ManualFileAdapterVersion = "file-7a.1";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Records validation findings for a batch; WARN and FAIL only. Voucher findings are linked by sequence.</summary>
    public async Task<int> SaveFindingsAsync(long batchId, IReadOnlyList<ValidationFinding> findings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(findings);
        await RequireOwnerAsync(cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            INSERT dbo.accounting_validation_findings(accounting_batch_id,accounting_voucher_id,rule_id,rule_version,severity,subject,observed,expected,explanation,corrective_action)
            SELECT @batch,v.accounting_voucher_id,f.rule_id,f.rule_version,f.severity,f.subject,f.observed,f.expected,f.explanation,f.corrective_action
            FROM OPENJSON(@findings) WITH(sequence int '$.VoucherSequence',rule_id varchar(30) '$.RuleId',rule_version int '$.RuleVersion',severity varchar(5) '$.Severity',
              subject nvarchar(200) '$.Subject',observed nvarchar(500) '$.Observed',expected nvarchar(500) '$.Expected',explanation nvarchar(1000) '$.Explanation',
              corrective_action nvarchar(500) '$.CorrectiveAction') f
            LEFT JOIN dbo.accounting_vouchers v ON v.accounting_batch_id=@batch AND v.voucher_sequence=f.sequence;
            DECLARE @saved int=@@ROWCOUNT;
            IF EXISTS(SELECT 1 FROM OPENJSON(@findings) WITH(sequence int '$.VoucherSequence') f
              WHERE f.sequence IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.accounting_vouchers v WHERE v.accounting_batch_id=@batch AND v.voucher_sequence=f.sequence))
              THROW 51579,'A finding names a voucher that is not in this batch.',1;
            COMMIT TRANSACTION; SELECT @saved;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@batch", batchId);
        command.Parameters.Add("@findings", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(findings.Where(f => f.Severity != ValidationSeverity.Pass));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    /// <summary>Accepts one warning with a reason (plan task 7 "Accept with reason"). Failures cannot be accepted.</summary>
    public Task AcceptWarningAsync(long findingId, string reason, CancellationToken cancellationToken = default) =>
        AcceptAsync("dbo.accounting_validation_findings", "finding_id", "waived_by", "waived_utc", "waiver_reason", findingId, reason, cancellationToken);

    /// <summary>Accepts one WARN reconciliation difference with a reason. Failures cannot be accepted.</summary>
    public Task AcceptDifferenceAsync(long differenceId, string reason, CancellationToken cancellationToken = default) =>
        AcceptAsync("dbo.tally_reconciliation_differences", "difference_id", "accepted_by", "accepted_utc", "accepted_reason", differenceId, reason, cancellationToken);

    /// <summary>Loads a Day Book XML file the operator exported from Tally by hand (plan task 9, file mode).
    /// The file is kept as evidence; the company is taken only from what the file itself says.</summary>
    public async Task<TallyReadbackRecord> LoadManualReadbackAsync(long batchId, DateOnly fromDate, DateOnly toDate, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0) throw new ArgumentException("The file is empty. Export the Day Book from Tally again.", nameof(content));
        if (fromDate > toDate) throw new ArgumentException("The first date must not be after the last.");
        await RequireOwnerAsync(cancellationToken);
        var batch = await LoadBatchAsync(batchId, cancellationToken);

        var document = TallyVoucherXmlReader.Read(content);
        var reason = document.FailureReason switch
        {
            null when document.CompanyNameReported is null => "COMPANY_NOT_REPORTED",
            null when !string.Equals(document.CompanyNameReported, batch.CompanyName.Trim(), StringComparison.Ordinal) => "WRONG_COMPANY",
            null => null,
            "TALLY_ERROR" when document.TallyMessage?.Contains("company", StringComparison.OrdinalIgnoreCase) == true => "COMPANY_NOT_OPEN",
            var failure => failure
        };

        var number = await NextNumberAsync(batchId, "READBACK_XML", cancellationToken);
        var store = new TallyEvidenceStore(connectionString, evidenceRootOverride);
        var artifact = await store.WriteAsync(batchId, "READBACK_XML", $@"{batch.Folder}\actuals\readback-{number}.xml", content, cancellationToken);

        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            INSERT dbo.tally_readbacks(tally_profile_id,accounting_batch_id,source,company_name_reported,from_date,to_date,voucher_count,is_complete,incomplete_reason,response_artifact_id,adapter_version)
            VALUES(@profile,@batch,'MANUAL_FILE',@company,@from,@to,@count,@complete,@reason,@artifact,@adapter);
            DECLARE @readback bigint=SCOPE_IDENTITY();
            INSERT dbo.tally_actual_vouchers(tally_readback_id,voucher_index,voucher_type,voucher_date,voucher_number,reference,narration,correspondence_key,tally_guid,tally_master_id,tally_alter_id,is_cancelled,is_optional,total_amount,fragment_sha256)
            SELECT @readback,voucher_index,voucher_type,voucher_date,voucher_number,reference,narration,correspondence_key,tally_guid,tally_master_id,tally_alter_id,is_cancelled,is_optional,total_amount,fragment_sha256
            FROM OPENJSON(@vouchers) WITH(voucher_index int,voucher_type nvarchar(50),voucher_date date,voucher_number nvarchar(100),reference nvarchar(200),narration nvarchar(1000),
              correspondence_key nvarchar(200),tally_guid nvarchar(100),tally_master_id bigint,tally_alter_id bigint,is_cancelled bit,is_optional bit,total_amount decimal(19,4),fragment_sha256 char(64));
            INSERT dbo.tally_actual_ledger_entries(tally_actual_id,line_number,ledger_name,amount,is_deemed_positive)
            SELECT a.tally_actual_id,l.line_number,l.ledger_name,l.amount,l.is_deemed_positive
            FROM OPENJSON(@lines) WITH(voucher_index int,line_number int,ledger_name nvarchar(200),amount decimal(19,4),is_deemed_positive bit) l
            JOIN dbo.tally_actual_vouchers a ON a.tally_readback_id=@readback AND a.voucher_index=l.voucher_index;
            EXEC dbo.record_operational_audit 'AccountingBatch','Succeeded',N'Tally read-back file recorded',N'database';
            COMMIT TRANSACTION; SELECT @readback;
            """;
        var vouchers = document.Vouchers.Select(v => new
        {
            voucher_index = v.Index, voucher_type = Cut(v.VoucherType, 50), voucher_date = v.VoucherDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            voucher_number = Cut(v.VoucherNumber, 100), reference = Cut(v.Reference, 200), narration = Cut(v.Narration, 1000),
            correspondence_key = v.Keys.Count == 1 ? v.Keys[0] : null, tally_guid = Cut(v.TallyGuid, 100), tally_master_id = v.MasterId, tally_alter_id = v.AlterId,
            is_cancelled = v.IsCancelled, is_optional = v.IsOptional,
            total_amount = v.Lines.Any(l => l.Amount is null) ? (decimal?)null : v.Lines.Where(l => l.Amount < 0).Sum(l => -l.Amount!.Value),
            fragment_sha256 = v.FragmentSha256
        });
        var lines = document.Vouchers.SelectMany(v => v.Lines.Select((l, i) => new
        {
            voucher_index = v.Index, line_number = i + 1, ledger_name = Cut(l.LedgerName, 200) ?? "", amount = l.Amount, is_deemed_positive = l.IsDeemedPositive
        }));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@profile", batch.ProfileId);
        command.Parameters.AddWithValue("@batch", batchId);
        command.Parameters.AddWithValue("@company", (object?)Cut(document.CompanyNameReported, 200) ?? DBNull.Value);
        command.Parameters.Add("@from", System.Data.SqlDbType.Date).Value = fromDate.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@to", System.Data.SqlDbType.Date).Value = toDate.ToDateTime(TimeOnly.MinValue);
        command.Parameters.AddWithValue("@count", document.Vouchers.Count);
        command.Parameters.AddWithValue("@complete", reason is null);
        command.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("@artifact", artifact.Id);
        command.Parameters.AddWithValue("@adapter", ManualFileAdapterVersion);
        command.Parameters.Add("@vouchers", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(vouchers);
        command.Parameters.Add("@lines", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return new(id, batchId, document.CompanyNameReported, reason is null, reason, document.Vouchers.Count, document.TallyMessage);
    }

    /// <summary>Compares a batch with one of its read-backs and records the run, its differences and each voucher's
    /// outcome (plan task 10). <paramref name="sourceUnchanged"/> is the caller's fresh re-hash of the source facts.
    /// Earlier runs are never touched.</summary>
    public async Task<TallyComparison> CompareAsync(long batchId, long readbackId, bool sourceUnchanged, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        var batch = await LoadBatchAsync(batchId, cancellationToken);
        var expected = await LoadExpectedAsync(batchId, cancellationToken);
        var (snapshot, actualIds) = await LoadReadbackAsync(batchId, readbackId, cancellationToken);
        var (payload, payloadUnchanged) = await LoadPayloadAsync(batchId, cancellationToken);
        var hasHttp = Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM dbo.tally_attempts WHERE accounting_batch_id=@batch AND delivery_mode='HTTP'", batchId, cancellationToken), CultureInfo.InvariantCulture) > 0;

        var result = TallyReconciliationEngine.Run(
            new ReconciliationInput(batch.CompanyName, expected.Select(e => e.Voucher).ToArray(), sourceUnchanged, payloadUnchanged, VoucherNumberVerifiable: false, hasHttp),
            payload, snapshot, ToleranceSet.None);

        var number = await NextNumberAsync(batchId, "RECONCILIATION", cancellationToken);
        var relative = $@"{batch.Folder}\reconciliation\run-{number}.json";
        var evidence = await new TallyEvidenceStore(connectionString, evidenceRootOverride)
            .WriteAsync(batchId, "RECONCILIATION", relative, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { readbackId, result }, Json)), cancellationToken);

        var voucherIds = expected.ToDictionary(e => e.Voucher.Sequence, e => e.Id);
        var differences = result.Differences.Select(d => new
        {
            voucher = d.VoucherSequence is { } s ? voucherIds[s] : (long?)null, actual = d.ActualIndex is { } i ? actualIds[i] : (long?)null,
            d.CheckLevel, d.DifferenceType, d.Severity, value_a = Cut(d.ValueA, 500), value_b = Cut(d.ValueB, 500), value_c = Cut(d.ValueC, 500), d.Delta,
            d.RuleId, rationale = Cut(d.MatchRationale, 500), action = Cut(d.RequiredAction, 500)
        });
        var outcomes = result.Vouchers.Select(v => new { id = voucherIds[v.Sequence], status = v.Status });
        var summary = JsonSerializer.Serialize(new
        {
            result.RuleSetVersion, result.BatchStatus, result.RunOutcome, vouchers = result.Vouchers.Count,
            reconciled = result.Vouchers.Count(v => v.Status == TallyVoucherStatus.Reconciled), differences = result.Differences.Count,
            failures = result.Differences.Count(d => d.Severity == "FAIL")
        });
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            INSERT dbo.tally_reconciliation_runs(accounting_batch_id,tally_readback_id,rule_set_version,outcome,summary_json,evidence_artifact_id)
            VALUES(@batch,@readback,@rules,@outcome,@summary,@evidence);
            DECLARE @run bigint=SCOPE_IDENTITY();
            INSERT dbo.tally_reconciliation_differences(run_id,accounting_voucher_id,tally_actual_id,check_level,difference_type,severity,value_a,value_b,value_c,delta,rule_id,rule_version,match_rationale,evidence_reference,required_action)
            SELECT @run,voucher,actual,CheckLevel,DifferenceType,Severity,value_a,value_b,value_c,Delta,RuleId,@ruleVersion,rationale,@path,action
            FROM OPENJSON(@differences) WITH(voucher bigint,actual bigint,CheckLevel varchar(20),DifferenceType varchar(30),Severity varchar(5),value_a nvarchar(500),value_b nvarchar(500),value_c nvarchar(500),
              Delta decimal(19,4),RuleId varchar(40),rationale nvarchar(500),action nvarchar(500));
            EXEC sys.sp_set_session_context @key=N'etp.status_reason',@value=N'Compared with a Tally read-back';
            UPDATE v SET voucher_status=o.status FROM dbo.accounting_vouchers v
            JOIN OPENJSON(@outcomes) WITH(id bigint,status varchar(30)) o ON o.id=v.accounting_voucher_id
            WHERE v.accounting_batch_id=@batch AND v.voucher_status<>o.status;
            EXEC sys.sp_set_session_context @key=N'etp.status_reason',@value=NULL;
            EXEC dbo.record_operational_audit 'AccountingBatch','Succeeded',N'Tally reconciliation run recorded',N'database';
            COMMIT TRANSACTION; SELECT @run;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@batch", batchId);
        command.Parameters.AddWithValue("@readback", readbackId);
        command.Parameters.AddWithValue("@rules", result.RuleSetVersion);
        command.Parameters.AddWithValue("@outcome", result.RunOutcome);
        command.Parameters.Add("@summary", System.Data.SqlDbType.NVarChar, -1).Value = summary;
        command.Parameters.AddWithValue("@evidence", evidence.Id);
        command.Parameters.AddWithValue("@ruleVersion", TallyReconciliationEngine.RuleVersion);
        command.Parameters.AddWithValue("@path", relative);
        command.Parameters.Add("@differences", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(differences);
        command.Parameters.Add("@outcomes", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(outcomes);
        var run = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return new(run, readbackId, result);
    }

    /// <summary>Saves the proposed recovery steps for a run as a RECOVERY_PLAN evidence file (plan task 22).
    /// The plan is a proposal: nothing is done until the Owner approves it.</summary>
    public async Task<TallyArtifact> SaveRecoveryPlanAsync(long batchId, TallyComparison comparison, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        await RequireOwnerAsync(cancellationToken);
        var batch = await LoadBatchAsync(batchId, cancellationToken);
        var plan = TallyRecoveryPlanBuilder.Build(comparison.Result, comparison.RunId);
        var number = await NextNumberAsync(batchId, "RECOVERY_PLAN", cancellationToken);
        return await new TallyEvidenceStore(connectionString, evidenceRootOverride).WriteAsync(batchId, "RECOVERY_PLAN",
            $@"{batch.Folder}\recovery-plan-{number}.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { comparison.ReadbackId, plan }, Json)), cancellationToken);
    }

    /// <summary>Completes the batch's evidence package with <c>manifest.json</c> (plan task 21): the intended company,
    /// selection, control totals, versions and the SHA-256 of every other registered file. Refused while any file is
    /// changed or missing. The manifest's own hash is stored in <c>accounting_batches.manifest_sha256</c>, not inside itself.</summary>
    public async Task<TallyArtifact> BuildManifestAsync(long batchId, string applicationVersion, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        var batch = await LoadBatchAsync(batchId, cancellationToken);
        var store = new TallyEvidenceStore(connectionString, evidenceRootOverride);
        var files = await store.VerifyAsync(batchId, cancellationToken);
        if (files.FirstOrDefault(file => file.State != TallyEvidenceState.Ok) is { } bad)
            throw new InvalidOperationException($"Evidence file {bad.Artifact.RelativePath} is {bad.State.ToString().ToUpperInvariant()}; the package cannot be completed.");
        if (files.Any(file => file.Artifact.Kind == "MANIFEST"))
            throw new InvalidOperationException("This batch's evidence package already has its manifest.");
        var expected = await LoadExpectedAsync(batchId, cancellationToken);
        var counted = expected.Where(e => e.Voucher.Status is not (TallyVoucherStatus.Excluded or TallyVoucherStatus.Blocked or TallyVoucherStatus.Cancelled)).ToArray();
        decimal Total(Func<ExpectedLedgerLine, bool> which) => counted.SelectMany(e => e.Voucher.Lines).Where(which).Sum(line => line.Debit - line.Credit);
        var schema = Convert.ToString(await ScalarAsync("SELECT MAX(migration_id) FROM dbo.schema_migrations", null, cancellationToken), CultureInfo.InvariantCulture);
        var manifest = new
        {
            intendedCompany = batch.CompanyName, environment = batch.Environment, profileCode = batch.ProfileCode,
            store = batch.StoreCode, businessDate = batch.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            deliveryMode = batch.DeliveryMode, payloadFormat = batch.PayloadFormat,
            selection = batch.SelectionJson is null ? (JsonElement?)null : JsonDocument.Parse(batch.SelectionJson).RootElement.Clone(),
            mappingVersionSet = batch.MappingVersionSetJson is null ? (JsonElement?)null : JsonDocument.Parse(batch.MappingVersionSetJson).RootElement.Clone(),
            exclusions = expected.Where(e => e.Voucher.Status is TallyVoucherStatus.Excluded or TallyVoucherStatus.Blocked)
                .Select(e => new { sequence = e.Voucher.Sequence, status = e.Voucher.Status }).ToArray(),
            voucherCount = counted.Length,
            controlTotals = new
            {
                debit = counted.SelectMany(e => e.Voucher.Lines).Sum(line => line.Debit),
                tax = -Total(line => line.BusinessEvent.StartsWith("OUTPUT_", StringComparison.Ordinal)),
                tender = Total(line => line.BusinessEvent.StartsWith("TENDER_", StringComparison.Ordinal) || line.BusinessEvent == "ROUND_OFF"),
                taxable = -Total(line => line.BusinessEvent == "SALES_REVENUE")
            },
            ruleSetVersion = TallyReconciliationEngine.RuleSetVersion,
            validationRuleVersion = AccountingValidationRules.RuleVersion,
            schemaVersion = schema, applicationVersion,
            files = files.Select(file => new { path = file.Artifact.RelativePath, kind = file.Artifact.Kind, sha256 = file.Artifact.Sha256, bytes = file.Artifact.ByteLength }).ToArray()
        };
        var artifact = await store.WriteAsync(batchId, "MANIFEST", $@"{batch.Folder}\manifest.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, Json)), cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("UPDATE dbo.accounting_batches SET manifest_sha256=@sha WHERE accounting_batch_id=@batch AND manifest_sha256 IS NULL; IF @@ROWCOUNT<>1 THROW 51579,'This batch already records a manifest.',1;", connection);
        command.Parameters.AddWithValue("@sha", artifact.Sha256); command.Parameters.AddWithValue("@batch", batchId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return artifact;
    }

    private sealed record BatchFacts(long Id, int ProfileId, string ProfileCode, string CompanyName, string StoreCode, DateOnly BusinessDate,
        string Environment, string DeliveryMode, string PayloadFormat, string? SelectionJson, string? MappingVersionSetJson)
    {
        public string Folder => TallyEvidencePaths.BatchFolder(ProfileCode, StoreCode, BusinessDate, Id);
    }

    private async Task<BatchFacts> LoadBatchAsync(long batchId, CancellationToken token)
    {
        const string sql = """
            SELECT b.accounting_batch_id,p.tally_profile_id,p.profile_code,p.company_name,b.store_code,b.business_date,
                   p.environment,p.default_delivery_mode,p.payload_format,b.selection_json,b.mapping_version_set_json
            FROM dbo.accounting_batches b JOIN dbo.tally_profiles p ON p.tally_profile_id=b.tally_profile_id WHERE b.accounting_batch_id=@batch;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@batch", batchId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("This batch is not a Tally batch, or it no longer exists.");
        return new(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), DateOnly.FromDateTime(reader.GetDateTime(5)),
            reader.GetString(6), reader.GetString(7), reader.GetString(8), Text(reader, 9), Text(reader, 10));
    }

    private async Task<IReadOnlyList<(long Id, ExpectedVoucher Voucher)>> LoadExpectedAsync(long batchId, CancellationToken token)
    {
        const string sql = """
            SELECT v.accounting_voucher_id,v.voucher_sequence,v.correspondence_key,v.voucher_type,v.voucher_date,v.document_number,v.voucher_status,
                   e.business_event,e.ledger_name,e.debit_amount,e.credit_amount
            FROM dbo.accounting_vouchers v LEFT JOIN dbo.accounting_entries e ON e.accounting_voucher_id=v.accounting_voucher_id
            WHERE v.accounting_batch_id=@batch ORDER BY v.voucher_sequence,e.line_number;
            """;
        var heads = new List<(long Id, int Sequence, string Key, string Type, DateOnly Date, string Number, string Status)>();
        var lines = new Dictionary<long, List<ExpectedLedgerLine>>();
        await using (var connection = await OpenAsync(token))
        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@batch", batchId);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var id = reader.GetInt64(0);
                if (!lines.ContainsKey(id))
                {
                    lines[id] = [];
                    heads.Add((id, reader.GetInt32(1), reader.GetString(2), reader.GetString(3), DateOnly.FromDateTime(reader.GetDateTime(4)), reader.GetString(5), reader.GetString(6)));
                }
                if (!reader.IsDBNull(7))
                    lines[id].Add(new(reader.GetString(7), reader.GetString(8), reader.GetDecimal(9), reader.GetDecimal(10)));
            }
        }
        return heads.Select(h => (h.Id, new ExpectedVoucher(h.Sequence, h.Key, h.Type, h.Date, h.Number, h.Status, lines[h.Id]))).ToArray();
    }

    private async Task<(ReadbackSnapshot Snapshot, IReadOnlyDictionary<int, long> ActualIds)> LoadReadbackAsync(long batchId, long readbackId, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        ReadbackSnapshot? head;
        await using (var command = new SqlCommand("SELECT company_name_reported,is_complete,incomplete_reason,from_date,to_date FROM dbo.tally_readbacks WHERE tally_readback_id=@id AND accounting_batch_id=@batch", connection))
        {
            command.Parameters.AddWithValue("@id", readbackId); command.Parameters.AddWithValue("@batch", batchId);
            await using var reader = await command.ExecuteReaderAsync(token);
            head = await reader.ReadAsync(token)
                ? new(readbackId, reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateOnly.FromDateTime(reader.GetDateTime(3)), DateOnly.FromDateTime(reader.GetDateTime(4)), [])
                : null;
        }
        if (head is null) throw new InvalidOperationException("This read-back does not belong to the batch.");

        const string sql = """
            SELECT a.tally_actual_id,a.voucher_index,a.voucher_type,a.voucher_date,a.voucher_number,a.reference,a.narration,a.is_cancelled,a.is_optional,
                   a.tally_guid,a.tally_master_id,a.tally_alter_id,a.fragment_sha256,l.ledger_name,l.amount,l.is_deemed_positive
            FROM dbo.tally_actual_vouchers a LEFT JOIN dbo.tally_actual_ledger_entries l ON l.tally_actual_id=a.tally_actual_id
            WHERE a.tally_readback_id=@id ORDER BY a.voucher_index,l.line_number;
            """;
        var ids = new Dictionary<int, long>();
        var heads = new List<ParsedVoucher>();
        var lines = new Dictionary<int, List<ParsedLedgerLine>>();
        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@id", readbackId);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var index = reader.GetInt32(1);
                if (!ids.ContainsKey(index))
                {
                    ids[index] = reader.GetInt64(0);
                    lines[index] = [];
                    heads.Add(new(index, Text(reader, 2), reader.IsDBNull(3) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(3)), Text(reader, 4), Text(reader, 5), Text(reader, 6),
                        reader.IsDBNull(7) ? (bool?)null : reader.GetBoolean(7), reader.IsDBNull(8) ? (bool?)null : reader.GetBoolean(8), Text(reader, 9),
                        reader.IsDBNull(10) ? (long?)null : reader.GetInt64(10), reader.IsDBNull(11) ? (long?)null : reader.GetInt64(11), reader.GetString(12), []));
                }
                if (!reader.IsDBNull(13))
                    lines[index].Add(new(reader.GetString(13), reader.IsDBNull(14) ? (decimal?)null : reader.GetDecimal(14), reader.IsDBNull(15) ? (bool?)null : reader.GetBoolean(15)));
            }
        }
        return (head with { Vouchers = heads.Select(h => h with { Lines = lines[h.Index] }).ToArray() }, ids);
    }

    private async Task<(IReadOnlyList<ParsedVoucher>? Payload, bool Unchanged)> LoadPayloadAsync(long batchId, CancellationToken token)
    {
        var checks = await new TallyEvidenceStore(connectionString, evidenceRootOverride).VerifyAsync(batchId, token);
        var payload = checks.Where(check => check.Artifact.Kind == "PAYLOAD_XML").OrderByDescending(check => check.Artifact.Id).FirstOrDefault();
        if (payload is null) return (null, true);
        if (payload.State != TallyEvidenceState.Ok) return (null, false);
        var root = evidenceRootOverride ?? Convert.ToString(await ScalarAsync("SELECT TOP(1) tally_evidence_root FROM dbo.product_settings ORDER BY product_setting_id", null, token), CultureInfo.InvariantCulture)!;
        var document = TallyVoucherXmlReader.Read(await File.ReadAllBytesAsync(Path.Combine(root, payload.Artifact.RelativePath.Replace('\\', Path.DirectorySeparatorChar)), token));
        return document.FailureReason is null ? (document.Vouchers, true) : (null, false);
    }

    private async Task<int> NextNumberAsync(long batchId, string kind, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("SELECT COUNT(*)+1 FROM dbo.tally_artifacts WHERE accounting_batch_id=@batch AND artifact_kind=@kind", connection);
        command.Parameters.AddWithValue("@batch", batchId); command.Parameters.AddWithValue("@kind", kind);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private async Task AcceptAsync(string table, string key, string byColumn, string utcColumn, string reasonColumn, long id, string reason, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000) throw new ArgumentException("Enter a reason of at most 1000 characters.");
        await RequireOwnerAsync(token);
        // Table and column names come from the two fixed callers above, never from input.
        var sql = $"""
            UPDATE {table} SET {byColumn}=ORIGINAL_LOGIN(),{utcColumn}=SYSUTCDATETIME(),{reasonColumn}=@reason
            WHERE {key}=@id AND severity='WARN' AND {byColumn} IS NULL;
            IF @@ROWCOUNT<>1 THROW 51579,'Only a warning that has not been accepted yet can be accepted.',1;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@reason", reason.Trim());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<object?> ScalarAsync(string sql, long? batchId, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        if (batchId is { } id) command.Parameters.AddWithValue("@batch", id);
        return await command.ExecuteScalarAsync(token);
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

    private static string? Text(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string? Cut(string? value, int length) => value is null ? null : value.Length <= length ? value : value[..length];
}
