using System.Data;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerImportBatchRepository(string connectionString) : IImportBatchRepository
{
    public async Task CreateAsync(ImportBatchRegistration batch, CancellationToken cancellationToken = default)
    {
        const string sql = "INSERT dbo.import_batches(import_batch_id,status,store_id,period_start,period_end,started_utc) VALUES(@id,'Processing',@store,@start,@end,@utc)";
        await ExecuteAsync(sql, c => { c.Parameters.AddWithValue("@id", batch.BatchId); Add(c,"@store",batch.StoreId); Add(c,"@start",batch.PeriodStart); Add(c,"@end",batch.PeriodEnd); c.Parameters.AddWithValue("@utc",batch.StartedUtc.UtcDateTime); }, cancellationToken);
    }

    public Task CompleteAsync(Guid batchId, int sourceRowCount, CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE dbo.import_batches SET status='Completed',source_row_count=@rows,completed_utc=SYSUTCDATETIME(),failure_reason=NULL WHERE import_batch_id=@id AND status='Processing'", c => { c.Parameters.AddWithValue("@id",batchId); c.Parameters.AddWithValue("@rows",sourceRowCount); }, cancellationToken, requireOne:true);

    public Task FailAsync(Guid batchId, string reason, CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE dbo.import_batches SET status='Failed',completed_utc=SYSUTCDATETIME(),failure_reason=@reason WHERE import_batch_id=@id AND status IN ('Pending','Processing')", c => { c.Parameters.AddWithValue("@id",batchId); c.Parameters.AddWithValue("@reason",reason); }, cancellationToken, requireOne:true);

    private async Task ExecuteAsync(string sql, Action<SqlCommand> bind, CancellationToken token, bool requireOne=false)
    {
        await using var connection=new SqlConnection(connectionString); await connection.OpenAsync(token);
        await using var command=new SqlCommand(sql,connection); bind(command);
        var affected=await command.ExecuteNonQueryAsync(token);
        if(requireOne && affected!=1) throw new DBConcurrencyException("The import batch is missing or is not in an allowed state.");
    }
    internal static void Add(SqlCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}

public sealed class SqlServerImportFileRepository(string connectionString) : IImportFileRepository
{
    public async Task<bool> ExistsByHashAsync(string sourceSha256, CancellationToken cancellationToken=default)
    {
        await using var connection=new SqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var command=new SqlCommand("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.import_files WHERE source_sha256=@hash) THEN 1 ELSE 0 END)",connection);
        command.Parameters.AddWithValue("@hash",NormalizeHash(sourceSha256)); return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
    public async Task<long> RegisterAsync(ImportFileRegistration file,CancellationToken cancellationToken=default)
    {
        var reportCode=PersistenceValidation.ResolveReportCode(file);
        await using var connection=new SqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var profileId=await SqlServerImportProfileResolver.ResolveOrRegisterAsync(connection,transaction,file.Profile,cancellationToken);
            var fileId=await InsertFileAsync(connection,transaction,file,profileId,reportCode,cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return fileId;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }
    public async Task<Etp.Reporting.Import.Batch.WorkbookImportOutcome> LoadOutcomeByHashAsync(string sourceSha256,CancellationToken cancellationToken=default)
    {
        const string sql="""
            SELECT COALESCE(b.source_row_count,0),
                   COALESCE(SUM(CASE WHEN o.outcome='NEW' THEN 1 ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN o.outcome='ALREADY_PRESENT' THEN 1 ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN o.outcome='CONFLICT' THEN 1 ELSE 0 END),0)
            FROM dbo.import_files f
            JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
            LEFT JOIN dbo.import_row_outcomes o ON o.import_file_id=f.import_file_id
            WHERE f.source_sha256=@hash
            GROUP BY b.source_row_count;
            """;
        await using var connection=new SqlConnection(connectionString);await connection.OpenAsync(cancellationToken);
        await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@hash",NormalizeHash(sourceSha256));
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken)) return Etp.Reporting.Import.Batch.WorkbookImportOutcome.Imported;
        return new(Convert.ToInt32(reader.GetValue(0)),Convert.ToInt32(reader.GetValue(1)),Convert.ToInt32(reader.GetValue(2)),Convert.ToInt32(reader.GetValue(3)));
    }
    internal static string NormalizeHash(string value)
    {
        var normalized=value?.Trim().ToLowerInvariant()??string.Empty;
        if(normalized.Length!=64 || normalized.Any(c=>!Uri.IsHexDigit(c))) throw new ArgumentException("A 64-character SHA-256 value is required.",nameof(value));
        return normalized;
    }

    private static async Task<long> InsertFileAsync(SqlConnection connection,SqlTransaction transaction,ImportFileRegistration file,int profileId,string reportCode,CancellationToken token)
    {
        await using var command=new SqlCommand("INSERT dbo.import_files(import_batch_id,import_profile_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,source_report_date,imported_by) VALUES(@batch,@profile,@name,@hash,@size,@report,@store,@business,@sourceDate,@user); SELECT CONVERT(bigint,SCOPE_IDENTITY());",connection,transaction);
        command.Parameters.AddWithValue("@batch",file.BatchId); command.Parameters.AddWithValue("@profile",profileId); command.Parameters.AddWithValue("@name",file.OriginalFileName); command.Parameters.AddWithValue("@hash",NormalizeHash(file.SourceSha256)); command.Parameters.AddWithValue("@size",file.SizeBytes); command.Parameters.AddWithValue("@report",reportCode); SqlServerImportBatchRepository.Add(command,"@store",file.StoreCode); SqlServerImportBatchRepository.Add(command,"@business",file.BusinessDate); SqlServerImportBatchRepository.Add(command,"@sourceDate",file.SourceReportDate); SqlServerImportBatchRepository.Add(command,"@user",file.ImportedBy);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }
}

public sealed class SqlServerTransactionalImportStore(string connectionString) : ITransactionalImportStore
{
    public async Task<long> PersistAsync(ImportPersistencePackage package,CancellationToken cancellationToken=default)
    {
        PersistenceValidation.Validate(package);
        var expectedRows=package.InvoiceControls.Count+package.SalesLines.Count+package.Tenders.Count+package.StockMovements.Count+package.StockSnapshots.Count;
        await using var connection=new SqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await InsertBatch(connection,transaction,package.Batch,cancellationToken);
            var profileId=await SqlServerImportProfileResolver.ResolveOrRegisterAsync(connection,transaction,package.File.Profile,cancellationToken);
            var fileId=await InsertFile(connection,transaction,package.File,profileId,cancellationToken);
            if(package.Restatement is { } restatement)
                await PrepareRestatement(connection,transaction,restatement,fileId,cancellationToken);
            foreach(var row in package.InvoiceControls) await InsertInvoiceControl(connection,transaction,fileId,row,cancellationToken);
            foreach(var row in package.SalesLines) await InsertSales(connection,transaction,fileId,row,cancellationToken);
            foreach(var row in package.Tenders) await InsertTender(connection,transaction,fileId,row,cancellationToken);
            foreach(var row in package.StockMovements) await InsertMovement(connection,transaction,fileId,row,cancellationToken);
            foreach(var row in package.StockSnapshots) await InsertSnapshot(connection,transaction,fileId,row,cancellationToken);
            if(package.SalesLines.Count>0) await RefreshEnrichmentMatches(connection,transaction,cancellationToken);
            await using var complete=Cmd(connection,transaction,"UPDATE dbo.import_batches SET status='Completed',source_row_count=@rows,completed_utc=SYSUTCDATETIME() WHERE import_batch_id=@id AND status='Processing'"); complete.Parameters.AddWithValue("@rows",expectedRows);complete.Parameters.AddWithValue("@id",package.Batch.BatchId);if(await complete.ExecuteNonQueryAsync(cancellationToken)!=1)throw new DBConcurrencyException("Import batch completion failed.");
            await transaction.CommitAsync(cancellationToken); return fileId;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private static async Task InsertBatch(SqlConnection c,SqlTransaction t,ImportBatchRegistration x,CancellationToken token){await using var q=Cmd(c,t,"INSERT dbo.import_batches(import_batch_id,status,store_id,period_start,period_end,started_utc) VALUES(@id,'Processing',@store,@start,@end,@utc)");q.Parameters.AddWithValue("@id",x.BatchId);Add(q,"@store",x.StoreId);Add(q,"@start",x.PeriodStart);Add(q,"@end",x.PeriodEnd);q.Parameters.AddWithValue("@utc",x.StartedUtc.UtcDateTime);await q.ExecuteNonQueryAsync(token);}
    private static async Task<long> InsertFile(SqlConnection c,SqlTransaction t,ImportFileRegistration x,int profileId,CancellationToken token){var reportCode=PersistenceValidation.ResolveReportCode(x);await using var q=Cmd(c,t,"INSERT dbo.import_files(import_batch_id,import_profile_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,source_report_date,imported_by) VALUES(@batch,@profile,@name,@hash,@size,@report,@store,@business,@sourceDate,@user); SELECT CONVERT(bigint,SCOPE_IDENTITY());");q.Parameters.AddWithValue("@batch",x.BatchId);q.Parameters.AddWithValue("@profile",profileId);q.Parameters.AddWithValue("@name",x.OriginalFileName);q.Parameters.AddWithValue("@hash",SqlServerImportFileRepository.NormalizeHash(x.SourceSha256));q.Parameters.AddWithValue("@size",x.SizeBytes);q.Parameters.AddWithValue("@report",reportCode);Add(q,"@store",x.StoreCode);Add(q,"@business",x.BusinessDate);Add(q,"@sourceDate",x.SourceReportDate);Add(q,"@user",x.ImportedBy);return Convert.ToInt64(await q.ExecuteScalarAsync(token));}
    private static async Task PrepareRestatement(SqlConnection c,SqlTransaction t,ImportRestatementRequest x,long replacementFileId,CancellationToken token)
    {
        await using var q=Cmd(c,t,"EXEC dbo.prepare_import_restatement @previous,@replacement,@user,@reason");
        q.Parameters.AddWithValue("@previous",x.PreviousImportFileId);q.Parameters.AddWithValue("@replacement",replacementFileId);
        q.Parameters.AddWithValue("@user",x.RequestedBy.Trim());q.Parameters.AddWithValue("@reason",x.Reason.Trim());
        await q.ExecuteNonQueryAsync(token);
    }
    private static async Task<long> Lineage(SqlConnection c,SqlTransaction t,long fileId,SourceRowRegistration x,CancellationToken token){await using var q=Cmd(c,t,"INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) OUTPUT INSERTED.source_lineage_id VALUES(@file,@sheet,@row,@type)");q.Parameters.AddWithValue("@file",fileId);q.Parameters.AddWithValue("@sheet",x.SheetName);q.Parameters.AddWithValue("@row",x.SourceRowNumber);Add(q,"@type",x.SourceRecordType);return Convert.ToInt64(await q.ExecuteScalarAsync(token));}
    private static async Task InsertSales(SqlConnection c,SqlTransaction t,long f,SalesLinePersistence x,CancellationToken token){var l=await Lineage(c,t,f,x.Lineage,token);await using var q=Cmd(c,t,"EXEC dbo.persist_sales_line @store,@doc,@year,@date,@line,@product,@type,@qty,@gross,@net,@brandcode,@brandname,@segment,@currency,@lineage");Bind(q,x.StoreCode,x.DocumentNumber,x.InvoiceYear,x.TransactionDate,x.LineIdentifier,x.ProductCode,x.SourceTransactionType,x.SourceQuantity,x.SourceGrossAmount,x.SourceNetAmount,x.SourceBrandCode,x.SourceBrandName,x.BrandSegment,x.CurrencyCode,l);await q.ExecuteNonQueryAsync(token);}
    private static async Task InsertInvoiceControl(SqlConnection c,SqlTransaction t,long f,SalesInvoiceControlPersistence x,CancellationToken token){var l=await Lineage(c,t,f,x.Lineage,token);await using var q=Cmd(c,t,"EXEC dbo.persist_sales_invoice_control @store,@doc,@year,@date,@type,@qty,@net,@currency,@lineage");q.Parameters.AddWithValue("@store",x.StoreCode);q.Parameters.AddWithValue("@doc",x.DocumentNumber);q.Parameters.AddWithValue("@year",x.InvoiceYear);q.Parameters.AddWithValue("@date",x.TransactionDate);Add(q,"@type",x.SourceTransactionType);q.Parameters.AddWithValue("@qty",x.SourceInvoiceQuantity);q.Parameters.AddWithValue("@net",x.SourceNetValue);q.Parameters.AddWithValue("@currency",x.CurrencyCode);q.Parameters.AddWithValue("@lineage",l);await q.ExecuteNonQueryAsync(token);}
    private static async Task InsertTender(SqlConnection c,SqlTransaction t,long f,TenderPersistence x,CancellationToken token){var l=await Lineage(c,t,f,x.Lineage,token);await using var q=Cmd(c,t,"EXEC dbo.persist_sales_tender @store,@doc,@year,@date,@type,@amount,@currency,@lineage,@eligible,@reason");q.Parameters.AddWithValue("@store",x.StoreCode);q.Parameters.AddWithValue("@doc",x.DocumentNumber);q.Parameters.AddWithValue("@year",x.InvoiceYear);q.Parameters.AddWithValue("@date",x.TransactionDate);q.Parameters.AddWithValue("@type",x.TenderType);q.Parameters.AddWithValue("@amount",x.SourceAmount);q.Parameters.AddWithValue("@currency",x.CurrencyCode);q.Parameters.AddWithValue("@lineage",l);q.Parameters.AddWithValue("@eligible",x.IsReportingEligible);Add(q,"@reason",x.ExclusionReason);await q.ExecuteNonQueryAsync(token);}
    private static async Task InsertMovement(SqlConnection c,SqlTransaction t,long f,StockMovementPersistence x,CancellationToken token){var l=await Lineage(c,t,f,x.Lineage,token);await using var q=Cmd(c,t,"EXEC dbo.persist_stock_movement @store,@doc,@year,@date,@product,@type,@from,@to,@opening,@transaction,@closing,@lineage");q.Parameters.AddWithValue("@store",x.StoreCode);q.Parameters.AddWithValue("@doc",x.DocumentNumber);q.Parameters.AddWithValue("@year",x.InvoiceYear);q.Parameters.AddWithValue("@date",x.DocumentDate);q.Parameters.AddWithValue("@product",x.ProductCode);q.Parameters.AddWithValue("@type",x.SourceTransactionType);Add(q,"@from",x.FromLocation);Add(q,"@to",x.ToLocation);q.Parameters.AddWithValue("@opening",x.OpeningQuantity);q.Parameters.AddWithValue("@transaction",x.TransactionQuantity);q.Parameters.AddWithValue("@closing",x.ClosingQuantity);q.Parameters.AddWithValue("@lineage",l);await q.ExecuteNonQueryAsync(token);}
    private static async Task InsertSnapshot(SqlConnection c,SqlTransaction t,long f,StockSnapshotPersistence x,CancellationToken token){var l=await Lineage(c,t,f,x.Lineage,token);await using var q=Cmd(c,t,"EXEC dbo.persist_stock_snapshot @store,@date,@product,@ean,@brand,@brandname,@cluster,@gender,@batch,@uid,@qty,@unit,@total,@lineage");q.Parameters.AddWithValue("@store",x.StoreCode);q.Parameters.AddWithValue("@date",x.SnapshotDate);q.Parameters.AddWithValue("@product",x.ProductCode);Add(q,"@ean",x.Ean);Add(q,"@brand",x.BrandCode);Add(q,"@brandname",x.BrandName);Add(q,"@cluster",x.Cluster);Add(q,"@gender",x.Gender);Add(q,"@batch",x.BatchNumber);Add(q,"@uid",x.SourceUid);q.Parameters.AddWithValue("@qty",x.Quantity);Add(q,"@unit",x.UnitCost);Add(q,"@total",x.TotalCost);q.Parameters.AddWithValue("@lineage",l);await q.ExecuteNonQueryAsync(token);}
    private static async Task RefreshEnrichmentMatches(SqlConnection c,SqlTransaction t,CancellationToken token)
    {
        const string sql="""
            IF OBJECT_ID(N'dbo.sales_line_enrichments',N'U') IS NOT NULL
            UPDATE e SET matched_sales_line_id=matches.sales_line_id,match_status=matches.match_status
            FROM dbo.sales_line_enrichments e
            CROSS APPLY
            (
              SELECT CASE WHEN COUNT_BIG(*)=1 THEN MAX(l.sales_line_id) END sales_line_id,
                     CASE COUNT_BIG(*) WHEN 0 THEN 'Missing' WHEN 1 THEN 'Matched' ELSE 'Ambiguous' END match_status
              FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
              WHERE i.store_code=e.store_code AND i.transaction_date=e.transaction_date
                AND i.document_number=e.document_number AND l.product_code=e.product_code
            ) matches
            WHERE e.match_status<>'Matched';
            """;
        await using var q=Cmd(c,t,sql);await q.ExecuteNonQueryAsync(token);
    }
    private static SqlCommand Cmd(SqlConnection c,SqlTransaction t,string sql)=>new(sql,c,t){CommandTimeout=0};
    private static void Add(SqlCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
    private static void Bind(SqlCommand q,string store,string doc,int year,DateOnly date,string line,string product,string? type,decimal qty,decimal? gross,decimal? net,string? brandCode,string? brandName,string? segment,string currency,long lineage){q.Parameters.AddWithValue("@store",store);q.Parameters.AddWithValue("@doc",doc);q.Parameters.AddWithValue("@year",year);q.Parameters.AddWithValue("@date",date);q.Parameters.AddWithValue("@line",line);q.Parameters.AddWithValue("@product",product);Add(q,"@type",type);q.Parameters.AddWithValue("@qty",qty);Add(q,"@gross",gross);Add(q,"@net",net);Add(q,"@brandcode",brandCode);Add(q,"@brandname",brandName);Add(q,"@segment",segment);q.Parameters.AddWithValue("@currency",currency);q.Parameters.AddWithValue("@lineage",lineage);}
}
