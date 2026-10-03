using System.Data;
using System.Globalization;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class SqlServerTransactionalImportStore
{
    internal sealed record PreviousFile(long Id, string Hash, DateOnly? Start, DateOnly? End, DateTime Imported,
        HashSet<string> Keys, int Version)
    {
        /// <summary>
        /// The snapshot dates an undated family's rows were read for, from their content keys; null when the file has
        /// no keys or any key carries no date (a dated family, or a snapshot imported before keys carried their date).
        /// </summary>
        public IReadOnlySet<DateOnly>? SnapshotDates
        {
            get
            {
                if (Keys.Count == 0) return null;
                var dates = new HashSet<DateOnly>();
                foreach (var key in Keys)
                {
                    if (SnapshotDateOfKey(key) is not { } date) return null;
                    dates.Add(date);
                }
                return dates;
            }
        }
    }
    private sealed record ImportPlan(long? ExistingHashFileId, bool DuplicateContent,
        IReadOnlyList<PreviousFile> PreviousFiles, IReadOnlyDictionary<int, string> Keys);

    private static async Task<ImportPlan> PlanImportAsync(SqlConnection c, SqlTransaction t,
        ImportPersistencePackage package, CancellationToken token)
    {
        var file = package.File;
        await using (var gate = Cmd(c, t, "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000; IF @result<0 THROW 51244,'Another import is using this store and report. Retry shortly.',1;"))
        {
            gate.Parameters.AddWithValue("@resource", $"ETP_IMPORT:{file.StoreCode}:{file.Profile.ReportCode}");
            await gate.ExecuteNonQueryAsync(token);
        }
        await using (var exact = Cmd(c, t, """
            SELECT import_file_id FROM dbo.import_files WHERE source_sha256=@hash AND data_truth_version=1
              AND report_code=@report AND (store_code=@store OR (store_code IS NULL AND @store IS NULL))
              AND (period_start=@start OR (period_start IS NULL AND @start IS NULL))
              AND (period_end=@end OR (period_end IS NULL AND @end IS NULL))
            """))
        {
            exact.Parameters.AddWithValue("@hash", SqlServerImportFileRepository.NormalizeHash(file.SourceSha256));
            exact.Parameters.AddWithValue("@report", PersistenceValidation.ResolveReportCode(file));
            Add(exact,"@store",file.StoreCode);
            Add(exact,"@start",file.PeriodStart ?? file.BusinessDate);
            Add(exact,"@end",file.PeriodEnd ?? file.BusinessDate);
            if (await exact.ExecuteScalarAsync(token) is long id) return new(id, true, [], new Dictionary<int,string>());
        }
        if (package.AcceptedImport is not { } accepted) return new(null, false, [], new Dictionary<int,string>());
        var keys = ContentKeys(accepted, file.BusinessDate);
        var previous = new List<PreviousFile>();
        const string sql = """
            SELECT f.import_file_id,f.source_sha256,f.period_start,f.period_end,b.started_utc,k.content_key,f.data_truth_version
            FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
            LEFT JOIN dbo.etp_import_content k ON k.import_file_id=f.import_file_id
            WHERE f.store_code=@store AND f.report_code=@report AND f.is_superseded=0
              AND COALESCE(f.period_start,f.business_date)<=@end AND COALESCE(f.period_end,f.business_date)>=@start
            ORDER BY f.import_file_id;
            """;
        await using (var query = Cmd(c, t, sql))
        {
            Add(query,"@store",file.StoreCode); query.Parameters.AddWithValue("@report",file.Profile.ReportCode);
            Add(query,"@start",file.PeriodStart ?? file.BusinessDate); Add(query,"@end",file.PeriodEnd ?? file.BusinessDate);
            await using var reader = await query.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))
            {
                var id=reader.GetInt64(0);
                var row=previous.LastOrDefault(x=>x.Id==id);
                if(row is null)
                {
                    row=new(id,reader.GetString(1),reader.IsDBNull(2)?null:reader.GetFieldValue<DateOnly>(2),
                        reader.IsDBNull(3)?null:reader.GetFieldValue<DateOnly>(3),reader.GetDateTime(4),new(StringComparer.Ordinal),Convert.ToInt32(reader.GetValue(6)));
                    previous.Add(row);
                }
                if(!reader.IsDBNull(5)) row.Keys.Add(reader.GetString(5));
            }
        }
        previous=SharingSnapshotDates(previous,keys.Values,package.Restatement?.PreviousImportFileId);
        // ImportAudit holds a copy of this decision in PlannerOnePlanRules.Decide; change both (PlannerOnePlanRulesAgreementTests).
        var incoming=keys.Values.ToHashSet(StringComparer.Ordinal);
        var allExisting=previous.SelectMany(x=>x.Keys).ToHashSet(StringComparer.Ordinal);
        if(previous.Count>0 && incoming.IsSubsetOf(allExisting) && package.Restatement is null)
            return new(null,true,previous,keys);
        foreach(var old in previous)
        {
            var coversRange=(file.PeriodStart??file.BusinessDate)<=old.Start && (file.PeriodEnd??file.BusinessDate)>=old.End;
            var isExplicit=package.Restatement?.PreviousImportFileId==old.Id;
            if(old.Version==0 && old.Hash!=file.SourceSha256 && !isExplicit)
                throw new Etp.Reporting.Import.Batch.ImportSourceException("IMPORT_LEGACY_RESTATEMENT_REQUIRED",
                    "This period was imported before the data-truth upgrade. Re-import its original workbook first, or use Restate with a reviewed replacement. No data was changed.")
                    { Stage = Etp.Reporting.Application.Imports.FailureStage.Plan };
            // During a restatement "Use Restate" sends the Owner in a circle: say which other import blocks it, and why.
            if(package.Restatement is { } restating && !isExplicit && !coversRange)
                throw new Etp.Reporting.Import.Batch.ImportSourceException(Etp.Reporting.Application.Imports.ImportCodes.RestatementTargetNotCovered,
                    $"This restatement replaces import {restating.PreviousImportFileId}, but its period only partly overlaps current import {old.Id}, so it cannot replace that one too. Import a file whose period covers it fully. No data was changed.")
                    { Stage = Etp.Reporting.Application.Imports.FailureStage.Plan };
            if(package.Restatement is { } replacing && !isExplicit && !old.Keys.IsSubsetOf(incoming))
                throw new Etp.Reporting.Import.Batch.ImportSourceException(Etp.Reporting.Application.Imports.ImportCodes.RestatementOtherImportChanged,
                    $"This restatement replaces import {replacing.PreviousImportFileId}, but its period also covers current import {old.Id}, and it changes or drops {old.Keys.Except(incoming).Count():N0} of that import's rows. A run restates only one import: restate each import with a corrected file for its own period. No data was changed.")
                    { Stage = Etp.Reporting.Application.Imports.FailureStage.Plan };
            if(!coversRange || (!isExplicit && !old.Keys.IsSubsetOf(incoming)))
                throw new Etp.Reporting.Import.Batch.ImportSourceException("IMPORT_PERIOD_ALREADY_PRESENT",
                    $"Already imported on {old.Imported:dd MMM yyyy} (hash {old.Hash[..12]}). Use Restate. {old.Keys.Except(incoming).Count():N0} conflicting or missing rows; no data was changed.")
                    { Stage = Etp.Reporting.Application.Imports.FailureStage.Plan };
        }
        return new(null,false,previous,keys);
    }

    /// <summary>
    /// Each row's content key, <c>{hash}:{n}</c>, n counting identical rows. A row of an undated family (R010, R023,
    /// SOR_AGEING) is a reading of its snapshot date, which no column holds, so its key is <c>{hash}@{yyyyMMdd}:{n}</c>:
    /// an unchanged unit in the 2 Jul and 7 Aug blocks of a stacked R010 is two rows, each matched only by a row of
    /// its own date when the plan looks for duplicates and promotes a covered file.
    /// </summary>
    internal static IReadOnlyDictionary<int,string> ContentKeys(MatchedImportEnvelope accepted,DateOnly? businessDate)
    {
        var result=new Dictionary<int,string>();
        var occurrences=new Dictionary<string,int>(StringComparer.Ordinal);
        var undated=ImportScope.IsUndatedFamily(accepted.ProfileIdentity.ReportCode);
        foreach(var row in accepted.Staging.Rows)
        {
            // Field selection and the two-digit state code rule live in the shared canonicaliser (spec 7.1).
            var hash=Etp.Reporting.Import.Identity.FactCanonicalizer.Instance.ContentKeyHash(accepted.ProfileIdentity.ReportCode,row.Values);
            if(undated && accepted.Scope.SnapshotDateOf(accepted.MatchedSheet.Name,row.SourceRowNumber,businessDate) is { } date)
                hash=$"{hash}{SnapshotDateMark}{date.ToString(SnapshotDateFormat,CultureInfo.InvariantCulture)}";
            occurrences.TryGetValue(hash,out var number);
            occurrences[hash]=++number;
            result[row.SourceRowNumber]=$"{hash}:{number}";
        }
        return result;
    }

    private const char SnapshotDateMark='@';
    private const string SnapshotDateFormat="yyyyMMdd";

    /// <summary>The snapshot date a content key carries (<see cref="ContentKeys"/>), or null for a key without one.</summary>
    internal static DateOnly? SnapshotDateOfKey(string key)=>
        key.Length>=64+1+8 && key[64]==SnapshotDateMark &&
        DateOnly.TryParseExact(key.AsSpan(65,8),SnapshotDateFormat,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
            ? date : null;

    /// <summary>
    /// For an undated family, the current files that hold one of the incoming snapshot dates. A stacked R010's period is
    /// min..max of its blocks, but it holds only those dates: a single-date file inside that range is another snapshot,
    /// never a duplicate of it, and a stacked file overlaps a single-date file only on its date. A file whose keys carry
    /// no date is matched by its period, as before; an explicit restatement target is always kept.
    /// </summary>
    internal static List<PreviousFile> SharingSnapshotDates(IReadOnlyList<PreviousFile> previous,IEnumerable<string> incomingKeys,long? restatementTarget)
    {
        var dates=incomingKeys.Select(SnapshotDateOfKey).OfType<DateOnly>().ToHashSet();
        if(dates.Count==0) return previous.ToList();
        return previous.Where(old=>old.Id==restatementTarget || (old.SnapshotDates is { } held
            ? held.Overlaps(dates)
            : dates.Any(date=>(old.Start is null || old.Start<=date) && (old.End is null || date<=old.End)))).ToList();
    }

    private static async Task RecordDuplicateAsync(SqlConnection c,SqlTransaction t,ImportPersistencePackage p,
        long fileId,ImportPlan plan,CancellationToken token)
    {
        // Stage retained source rows before SQL validates their content against current imports.
        await InsertFamilySourceAsync(c,t,p,fileId,plan.Keys,token,recordOutcomes:false);
        await using(var q=Cmd(c,t,"EXEC dbo.complete_duplicate_import @file,@previous"))
        {
            q.Parameters.AddWithValue("@previous",plan.PreviousFiles[0].Id);
            q.Parameters.AddWithValue("@file",fileId);
            await q.ExecuteNonQueryAsync(token);
        }
        foreach(var row in plan.Keys)
        {
            var lineage=await Lineage(c,t,fileId,new(p.AcceptedImport!.MatchedSheet.Name,row.Key,"DUPLICATE_SOURCE"),token);
            await RecordOutcomeAsync(c,t,fileId,lineage,row.Value,"ALREADY_PRESENT",token);
        }
    }

    private static async Task InsertFamilySourceAsync(SqlConnection c,SqlTransaction t,ImportPersistencePackage package,
        long fileId,IReadOnlyDictionary<int,string> keys,CancellationToken token,bool recordOutcomes=true)
    {
        if(package.AcceptedImport is not { } accepted) return;
        var family=EtpReportFamilyRegistry.Families.Single(x=>x.ReportCode==accepted.ProfileIdentity.ReportCode);
        var table=family.TableName;
        // Each catalogue family has a static, typed SQL append procedure.
        var parameters=family.Columns.Select((_,i)=>$"@v{i}").ToArray();
        var sql=$"EXEC dbo.[append_{table}] @file,@lineage,@key,{string.Join(',',parameters)}";
        foreach(var row in accepted.Staging.Rows)
        {
            var key=keys[row.SourceRowNumber];
            var lineage=await Lineage(c,t,fileId,new(accepted.MatchedSheet.Name,row.SourceRowNumber,$"{family.FamilyCode}_SOURCE"),token);
            await using(var insert=Cmd(c,t,sql))
            {
                insert.Parameters.AddWithValue("@file",fileId); insert.Parameters.AddWithValue("@lineage",lineage); insert.Parameters.AddWithValue("@key",key);
                for(var i=0;i<family.Columns.Count;i++) Add(insert,$"@v{i}",row.Values.GetValueOrDefault(family.Columns[i].CanonicalField));
                await insert.ExecuteNonQueryAsync(token);
            }
            if(recordOutcomes && package.SalesLines.Count+package.InvoiceControls.Count+package.Enrichments.Count+package.StockMovements.Count+package.StockSnapshots.Count==0)
                await RecordOutcomeAsync(c,t,fileId,lineage,key,"NEW",token);
        }
    }

    private static async Task RecordOutcomeAsync(SqlConnection c,SqlTransaction t,long file,long lineage,string key,string outcome,CancellationToken token)
    {
        await using var q=Cmd(c,t,"INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@key,@outcome,@hash,N'ETP source row processed.')");
        q.Parameters.AddWithValue("@file",file);q.Parameters.AddWithValue("@lineage",lineage);q.Parameters.AddWithValue("@key",key);
        q.Parameters.AddWithValue("@outcome",outcome);q.Parameters.AddWithValue("@hash",key[..64]);await q.ExecuteNonQueryAsync(token);
    }

    private static async Task ThrowOnConflictsAsync(SqlConnection c,SqlTransaction t,long file,CancellationToken token)
    {
        await using var q=Cmd(c,t,"SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id=@file AND outcome='CONFLICT'");
        q.Parameters.AddWithValue("@file",file); var count=Convert.ToInt32(await q.ExecuteScalarAsync(token));
        if(count>0) throw await ConflictAsync(c,t,file,count,token);
    }

    private static async Task InsertEnrichmentAsync(SqlConnection c,SqlTransaction t,long file,EnrichmentPersistence row,CancellationToken token)
    {
        var lineage=await Lineage(c,t,file,row.Lineage,token);
        const string sql="EXEC dbo.persist_phase_one_enrichment @file,@report,@store,@doc,@date,@product,@type,@qty,@net,@gross,@cro,@name,@scheme,@userDiscount,@pre,@other,@activation,@details,@lineage,@key,@outcome OUTPUT";
        await using var q=Cmd(c,t,sql);
        // NEW, or ALREADY_PRESENT when the content key is already stored (migration 0041, section E).
        var outcome=q.Parameters.Add("@outcome",SqlDbType.VarChar,16);outcome.Direction=ParameterDirection.Output;
        q.Parameters.AddWithValue("@file",file);
        q.Parameters.AddWithValue("@report",row.ReportCode);q.Parameters.AddWithValue("@store",row.StoreCode);q.Parameters.AddWithValue("@doc",row.DocumentNumber);
        q.Parameters.AddWithValue("@date",row.TransactionDate);q.Parameters.AddWithValue("@product",row.ProductCode);q.Parameters.AddWithValue("@type",row.TransactionType);
        q.Parameters.AddWithValue("@qty",row.Quantity);q.Parameters.AddWithValue("@net",row.NetValue);q.Parameters.AddWithValue("@gross",row.GrossValue);
        Add(q,"@cro",row.CroNumber);Add(q,"@name",row.StaffName);Add(q,"@scheme",row.SchemeDiscount);Add(q,"@userDiscount",row.UserDiscount);
        Add(q,"@pre",row.PreDiscount);Add(q,"@other",row.OtherCharges);Add(q,"@activation",row.ActivationDetails);Add(q,"@details",row.UserDiscountDetails);
        q.Parameters.AddWithValue("@lineage",lineage);q.Parameters.AddWithValue("@key",row.ContentKey);await q.ExecuteNonQueryAsync(token);
        await RecordOutcomeAsync(c,t,file,lineage,row.ContentKey,outcome.Value as string
            ?? throw new InvalidOperationException("The enrichment procedure did not report an outcome."),token);
    }
}
