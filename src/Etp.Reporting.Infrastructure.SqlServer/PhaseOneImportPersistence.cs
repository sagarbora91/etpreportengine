using System.Data;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class SqlServerTransactionalImportStore
{
    private sealed record PreviousFile(long Id, string Hash, DateOnly? Start, DateOnly? End, DateTime Imported,
        HashSet<string> Keys, int Version);
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
        var keys = ContentKeys(accepted);
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
                    "This period was imported before the data-truth upgrade. Re-import its original workbook first, or use Restate with a reviewed replacement. No data was changed.");
            if(!coversRange || (!isExplicit && !old.Keys.IsSubsetOf(incoming)))
                throw new Etp.Reporting.Import.Batch.ImportSourceException("IMPORT_PERIOD_ALREADY_PRESENT",
                    $"Already imported on {old.Imported:dd MMM yyyy} (hash {old.Hash[..12]}). Use Restate. {old.Keys.Except(incoming).Count():N0} conflicting or missing rows; no data was changed.");
        }
        return new(null,false,previous,keys);
    }

    private static IReadOnlyDictionary<int,string> ContentKeys(MatchedImportEnvelope accepted)
    {
        var result=new Dictionary<int,string>();
        var occurrences=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var row in accepted.Staging.Rows)
        {
            string[] stockFields=["store_code","document_number","document_date","product_code","source_transaction_type",
                "from_location","to_location","opening_quantity","transaction_quantity","closing_quantity"];
            var hash=EtpInvoiceIdentity.ContentHash(row.Values.Where(x=> accepted.ProfileIdentity.ReportCode=="STOCK_LEDGER"
                ? stockFields.Contains(x.Key,StringComparer.Ordinal)
                : !x.Key.Contains("timestamp",StringComparison.OrdinalIgnoreCase)).Select(x=>
                    x.Key.EndsWith("state_code",StringComparison.Ordinal) && int.TryParse(x.Value?.ToString(),out var state)
                        ? new KeyValuePair<string,object?>(x.Key,state.ToString("D2",System.Globalization.CultureInfo.InvariantCulture)) : x));
            occurrences.TryGetValue(hash,out var number);
            occurrences[hash]=++number;
            result[row.SourceRowNumber]=$"{hash}:{number}";
        }
        return result;
    }

    private static async Task RecordDuplicateAsync(SqlConnection c,SqlTransaction t,ImportPersistencePackage p,
        long fileId,ImportPlan plan,CancellationToken token)
    {
        await using(var q=Cmd(c,t,"UPDATE dbo.import_files SET is_superseded=1,superseded_by_import_file_id=@previous,superseded_utc=SYSUTCDATETIME(),superseded_by=@user,restatement_reason=N'Duplicate content; all rows already present.' WHERE import_file_id=@file; UPDATE dbo.import_batches SET status='Completed',source_row_count=@rows,completed_utc=SYSUTCDATETIME() WHERE import_batch_id=@batch"))
        {
            q.Parameters.AddWithValue("@previous",plan.PreviousFiles[0].Id); q.Parameters.AddWithValue("@file",fileId);
            q.Parameters.AddWithValue("@batch",p.Batch.BatchId); q.Parameters.AddWithValue("@rows",plan.Keys.Count); Add(q,"@user",p.File.ImportedBy);
            await q.ExecuteNonQueryAsync(token);
        }
        // Keep a typed, non-current source copy for each distinct workbook hash, including
        // harmless formatting/reference changes, without adding canonical reporting facts.
        await InsertFamilySourceAsync(c,t,p,fileId,plan.Keys,token,recordOutcomes:false);
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
        // Identifiers come only from the compiled, exact-header family catalogue.
        var columnNames=family.Columns.Select(x=>$"[{x.CanonicalField}]").ToArray();
        var parameters=family.Columns.Select((_,i)=>$"@v{i}").ToArray();
        var sql=$"INSERT dbo.[{table}](import_file_id,source_lineage_id,content_key,{string.Join(',',columnNames)}) VALUES(@file,@lineage,@key,{string.Join(',',parameters)})";
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
            await using(var manifest=Cmd(c,t,"INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key) VALUES(@file,@row,@key)"))
            {
                manifest.Parameters.AddWithValue("@file",fileId); manifest.Parameters.AddWithValue("@row",row.SourceRowNumber); manifest.Parameters.AddWithValue("@key",key);
                await manifest.ExecuteNonQueryAsync(token);
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
        if(count>0) throw new Etp.Reporting.Import.Batch.ImportSourceException("IMPORT_CONFLICT",
            $"{count:N0} conflicting rows. The complete file was rolled back. Review the source and use Restate.");
    }

    private static async Task InsertEnrichmentAsync(SqlConnection c,SqlTransaction t,long file,EnrichmentPersistence row,CancellationToken token)
    {
        var lineage=await Lineage(c,t,file,row.Lineage,token);
        const string sql="""
            DECLARE @matches int,@line bigint;
            SELECT @matches=COUNT(*),@line=CASE WHEN COUNT(*)=1 THEN MAX(l.sales_line_id) END
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
            WHERE i.store_code=@store AND i.document_number=@doc AND i.transaction_date=@date AND l.product_code=@product;
            IF NOT EXISTS(SELECT 1 FROM dbo.sales_line_enrichments WHERE enrichment_type=@report AND store_code=@store AND content_key=@key)
            BEGIN
              INSERT dbo.sales_line_enrichments(enrichment_type,store_code,transaction_date,document_number,product_code,
                source_transaction_type,source_quantity,source_net_value,source_gross_value,source_cro_number,staff_name,
                scheme_discount,user_discount,pre_discount,other_charges,activation_details,user_discount_details,matched_sales_line_id,match_status,source_lineage_id,content_key,invoice_year)
              VALUES(@report,@store,@date,@doc,@product,@type,@qty,@net,@gross,@cro,@name,@scheme,@userDiscount,@pre,@other,@activation,@details,@line,
                CASE @matches WHEN 0 THEN 'Missing' WHEN 1 THEN 'Matched' ELSE 'Ambiguous' END,@lineage,@key,YEAR(@date)+CASE WHEN MONTH(@date)>=4 THEN 1 ELSE 0 END);
            END;
            IF @cro IS NOT NULL
              MERGE dbo.staff WITH(HOLDLOCK) AS target USING(SELECT @store store_code,@cro staff_code) source
              ON target.store_code=source.store_code AND target.staff_code=source.staff_code
              WHEN MATCHED AND target.staff_name=target.staff_code AND NULLIF(LTRIM(RTRIM(@name)),N'') IS NOT NULL
                THEN UPDATE SET staff_name=@name,modified_utc=SYSUTCDATETIME(),modified_by=ORIGINAL_LOGIN()
              WHEN NOT MATCHED THEN INSERT(store_code,staff_code,staff_name,active) VALUES(@store,@cro,COALESCE(@name,@cro),1);
            """;
        await using var q=Cmd(c,t,sql);
        q.Parameters.AddWithValue("@report",row.ReportCode);q.Parameters.AddWithValue("@store",row.StoreCode);q.Parameters.AddWithValue("@doc",row.DocumentNumber);
        q.Parameters.AddWithValue("@date",row.TransactionDate);q.Parameters.AddWithValue("@product",row.ProductCode);q.Parameters.AddWithValue("@type",row.TransactionType);
        q.Parameters.AddWithValue("@qty",row.Quantity);q.Parameters.AddWithValue("@net",row.NetValue);q.Parameters.AddWithValue("@gross",row.GrossValue);
        Add(q,"@cro",row.CroNumber);Add(q,"@name",row.StaffName);Add(q,"@scheme",row.SchemeDiscount);Add(q,"@userDiscount",row.UserDiscount);
        Add(q,"@pre",row.PreDiscount);Add(q,"@other",row.OtherCharges);Add(q,"@activation",row.ActivationDetails);Add(q,"@details",row.UserDiscountDetails);
        q.Parameters.AddWithValue("@lineage",lineage);q.Parameters.AddWithValue("@key",row.ContentKey);await q.ExecuteNonQueryAsync(token);
        await RecordOutcomeAsync(c,t,file,lineage,row.ContentKey,"NEW",token);
    }
}
