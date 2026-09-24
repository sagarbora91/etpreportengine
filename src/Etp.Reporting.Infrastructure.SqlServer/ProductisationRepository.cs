using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class ProductisationRepository(string connectionString)
{
    internal const string SaveSharingContactSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @result int;
        IF @id=0
        BEGIN
          INSERT dbo.sharing_contacts(display_name,contact_role,email_address,phone_e164,default_subscriptions,is_active,modified_by,change_reason)
          VALUES(@name,@role,@email,@phone,@subscriptions,@active,SUSER_SNAME(),@reason);
          SET @result=CONVERT(int,SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
          UPDATE dbo.sharing_contacts SET display_name=@name,contact_role=@role,email_address=@email,phone_e164=@phone,default_subscriptions=@subscriptions,
            is_active=@active,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
          WHERE sharing_contact_id=@id;
          IF @@ROWCOUNT<>1 THROW 51227,'The sharing contact was not found.',1;
          SET @result=@id;
        END
        EXEC dbo.record_operational_audit 'SharingContactChange','Succeeded',N'Sharing contact changed',N'database';
        COMMIT TRANSACTION;
        SELECT @result;
        """;

    public async Task<ProductSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT document_repository_path,share_folder_path,smtp_host,smtp_port,
                   smtp_use_tls,smtp_from_address,maximum_attachment_mb,modified_utc,modified_by
            FROM dbo.product_settings WHERE product_setting_id=1;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Product settings are not initialized.");
        return new(reader.GetString(0), reader.GetString(1), OptionalString(reader, 2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetBoolean(4),
            OptionalString(reader, 5), reader.GetInt32(6), reader.GetDateTime(7), reader.GetString(8));
    }

    public async Task SaveSettingsAsync(ProductSettings settings, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOwnerAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter a reason for the settings change.", nameof(reason));
        _ = Path.GetFullPath(settings.DocumentRepositoryPath); _ = Path.GetFullPath(settings.ShareFolderPath);
        const string sql = """
            UPDATE dbo.product_settings SET document_repository_path=@documents,share_folder_path=@share,
              smtp_host=@smtp,smtp_port=@port,smtp_use_tls=@tls,
              smtp_from_address=@from,maximum_attachment_mb=@maximum,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
            WHERE product_setting_id=1;
            EXEC dbo.record_operational_audit 'ConfigurationChange','Succeeded',N'Product integration settings changed',N'database';
            """;
        await using var connection = await OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection);
        Add(command, "@documents", Path.GetFullPath(settings.DocumentRepositoryPath)); Add(command, "@share", Path.GetFullPath(settings.ShareFolderPath));
        Add(command, "@smtp", Clean(settings.SmtpHost)); Add(command, "@port", settings.SmtpPort); command.Parameters.AddWithValue("@tls", settings.SmtpUseTls);
        Add(command, "@from", Clean(settings.SmtpFromAddress)); command.Parameters.AddWithValue("@maximum", settings.MaximumAttachmentMb);
        command.Parameters.AddWithValue("@reason", reason.Trim()); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SourceDocumentRow?> FindDocumentByHashAsync(string sha256, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(DocumentSelect + " WHERE source_sha256=@hash", connection);
        command.Parameters.AddWithValue("@hash", SqlServerImportFileRepository.NormalizeHash(sha256));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    public async Task<SourceDocumentRow> RegisterDocumentAsync(string originalName, string managedPath, string sha256, long size,
        string sourceType, string? documentType, string? storeCode, DateOnly? businessDate, string lifecycleStatus,
        string? safeMessage, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @id bigint;
            SELECT @id=source_document_id FROM dbo.source_documents WITH(UPDLOCK,HOLDLOCK) WHERE source_sha256=@hash;
            IF @id IS NULL
            BEGIN
              INSERT dbo.source_documents(original_file_name,managed_file_path,source_sha256,size_bytes,source_type,document_type,store_code,business_date,lifecycle_status,received_by,last_status_by,safe_message)
              VALUES(@name,@path,@hash,@size,@sourceType,@documentType,@store,@date,@status,SUSER_SNAME(),SUSER_SNAME(),@message);
              SET @id=SCOPE_IDENTITY();
              EXEC dbo.record_operational_audit 'DocumentIntake','Succeeded',N'Source document received',N'database';
            END
            SELECT @id; COMMIT TRANSACTION;
            """;
        await using var connection = await OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", Path.GetFileName(originalName)); command.Parameters.AddWithValue("@path", Path.GetFullPath(managedPath));
        command.Parameters.AddWithValue("@hash", SqlServerImportFileRepository.NormalizeHash(sha256)); command.Parameters.AddWithValue("@size", size);
        command.Parameters.AddWithValue("@sourceType", sourceType); Add(command, "@documentType", Clean(documentType)); Add(command, "@store", Clean(storeCode)?.ToUpperInvariant());
        Add(command, "@date", businessDate); command.Parameters.AddWithValue("@status", lifecycleStatus); Add(command, "@message", Clean(safeMessage));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return await LoadDocumentAsync(id, cancellationToken);
    }

    public async Task<SourceDocumentRow> LoadDocumentAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(DocumentSelect + " WHERE source_document_id=@id", connection); command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : throw new KeyNotFoundException("The source document was not found.");
    }

    public async Task<IReadOnlyList<SourceDocumentRow>> LoadSourceInboxAsync(string? status = null, int limit = 500, CancellationToken cancellationToken = default)
    {
        var sql = DocumentSelect + " WHERE (@status IS NULL OR lifecycle_status=@status) ORDER BY received_utc DESC OFFSET 0 ROWS FETCH NEXT @limit ROWS ONLY";
        await using var connection = await OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection);
        Add(command, "@status", Clean(status)?.ToUpperInvariant()); command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 2000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var rows = new List<SourceDocumentRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadDocument(reader)); return rows;
    }

    public async Task<IReadOnlyList<SharingContactRow>> LoadSharingContactsAsync(CancellationToken cancellationToken=default)
    {
        const string sql="SELECT sharing_contact_id,display_name,contact_role,email_address,phone_e164,default_subscriptions,is_active,modified_by,modified_utc FROM dbo.sharing_contacts ORDER BY is_active DESC,display_name";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<SharingContactRow>();
        while(await reader.ReadAsync(cancellationToken))rows.Add(new(reader.GetInt32(0),reader.GetString(1),OptionalString(reader,2),OptionalString(reader,3),OptionalString(reader,4),OptionalString(reader,5),reader.GetBoolean(6),reader.GetString(7),reader.GetDateTime(8)));return rows;
    }

    public async Task<int> SaveSharingContactAsync(SharingContactRow contact,string reason,CancellationToken cancellationToken=default)
    {
        await EnsureOwnerAsync(cancellationToken);if(string.IsNullOrWhiteSpace(contact.DisplayName))throw new ArgumentException("Enter a contact name.");if(string.IsNullOrWhiteSpace(contact.EmailAddress)&&string.IsNullOrWhiteSpace(contact.PhoneE164))throw new ArgumentException("Enter an email address or phone number.");if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Enter a reason for the contact change.");
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(SaveSharingContactSql,connection);command.Parameters.AddWithValue("@id",contact.Id);command.Parameters.AddWithValue("@name",contact.DisplayName.Trim());Add(command,"@role",Clean(contact.ContactRole));Add(command,"@email",Clean(contact.EmailAddress));Add(command,"@phone",Clean(contact.PhoneE164));Add(command,"@subscriptions",Clean(contact.DefaultSubscriptions));command.Parameters.AddWithValue("@active",contact.IsActive);command.Parameters.AddWithValue("@reason",reason.Trim());return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task LinkDocumentToImportAsync(long documentId,string sourceSha256,string reportCode,string? storeCode,DateOnly? businessDate,CancellationToken cancellationToken=default)
    {
        const string sql="EXEC dbo.link_document_to_import @document,@hash,@report,@store,@date";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@document",documentId);command.Parameters.AddWithValue("@hash",SqlServerImportFileRepository.NormalizeHash(sourceSha256));command.Parameters.AddWithValue("@report",reportCode);Add(command,"@store",Clean(storeCode)?.ToUpperInvariant());Add(command,"@date",businessDate);await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> SaveRegisterEntryAsync(RegisterEntryRow entry, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter a reason for the register entry.", nameof(reason));
        const string sql = "EXEC dbo.save_register_entry @type,@document,@store,@date,@number,@documentDate,@counterparty,@quantity,@amount,@reference,@received,@verification,@remarks,@reason";
        await using var connection = await OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@type", entry.RegisterType.ToUpperInvariant()); Add(command, "@document", entry.SourceDocumentId);
        command.Parameters.AddWithValue("@store", entry.StoreCode.Trim().ToUpperInvariant()); command.Parameters.AddWithValue("@date", entry.BusinessDate);
        command.Parameters.AddWithValue("@number", entry.DocumentNumber.Trim()); Add(command, "@documentDate", entry.DocumentDate);
        Add(command, "@counterparty", Clean(entry.Counterparty)); Add(command, "@quantity", entry.Quantity); Add(command, "@amount", entry.Amount);
        Add(command, "@reference", Clean(entry.Reference)); Add(command, "@received", Clean(entry.ReceivedBy)); command.Parameters.AddWithValue("@verification", entry.VerificationStatus.ToUpperInvariant());
        Add(command, "@remarks", Clean(entry.Remarks)); command.Parameters.AddWithValue("@reason", reason.Trim());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<RegisterEntryRow>> LoadRegisterEntriesAsync(string? search = null, int limit = 500, CancellationToken cancellationToken = default, string? storeCode = null, DateOnly? businessDate = null)
    {
        const string sql = """
            SELECT TOP(@limit) register_entry_id,register_type,source_document_id,store_code,business_date,document_number,document_date,counterparty,quantity,amount,
              reference,received_by,verification_status,remarks,modified_by,modified_utc
            FROM dbo.register_entries
            WHERE (@search IS NULL OR document_number LIKE @pattern OR counterparty LIKE @pattern OR reference LIKE @pattern OR store_code LIKE @pattern)
              AND (@store IS NULL OR store_code=@store) AND (@date IS NULL OR business_date=@date)
            ORDER BY business_date DESC,register_entry_id DESC;
            """;
        await using var connection = await OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection);
        Add(command,"@store",Clean(storeCode)); Add(command,"@date",businessDate);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 2000)); Add(command, "@search", Clean(search)); Add(command, "@pattern", Clean(search) is null ? null : $"%{EscapeLike(search!.Trim())}%");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var rows = new List<RegisterEntryRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetInt64(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetInt64(2),reader.GetString(3),DateOnly.FromDateTime(reader.GetDateTime(4)),reader.GetString(5),reader.IsDBNull(6)?null:DateOnly.FromDateTime(reader.GetDateTime(6)),OptionalString(reader,7),reader.IsDBNull(8)?null:reader.GetDecimal(8),reader.IsDBNull(9)?null:reader.GetDecimal(9),OptionalString(reader,10),OptionalString(reader,11),reader.GetString(12),OptionalString(reader,13),reader.GetString(14),reader.GetDateTime(15)));
        return rows;
    }

    public async Task<IReadOnlyList<ImportConflictRow>> LoadImportConflictsAsync(string? status = null, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT TOP(500) import_conflict_id,store_code,business_date,report_code,business_identity,status,safe_difference,created_utc FROM dbo.import_conflicts WHERE @status IS NULL OR status=@status ORDER BY created_utc DESC";
        await using var connection = await OpenAsync(cancellationToken); await using var command = new SqlCommand(sql, connection); Add(command,"@status",Clean(status)?.ToUpperInvariant());
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<ImportConflictRow>();
        while(await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetInt64(0),OptionalString(reader,1),reader.IsDBNull(2)?null:DateOnly.FromDateTime(reader.GetDateTime(2)),OptionalString(reader,3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetDateTime(7)));return rows;
    }

    public async Task<IReadOnlyList<KpiCatalogueRow>> LoadKpiCatalogueAsync(CancellationToken cancellationToken = default)
    {
        const string sql="SELECT kpi_code,business_name,definition,formula,data_source,effective_date,version,approval_status,approved_by,is_active FROM dbo.kpi_catalogue ORDER BY business_name";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<KpiCatalogueRow>();
        while(await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),DateOnly.FromDateTime(reader.GetDateTime(5)),reader.GetInt32(6),reader.GetString(7),OptionalString(reader,8),reader.GetBoolean(9)));return rows;
    }

    public async Task<long> CreateApprovalAsync(string approvalType,string subjectType,string subjectId,object payload,string? storeCode=null,DateOnly? businessDate=null,CancellationToken cancellationToken=default)
    {
        const string sql="EXEC dbo.submit_approval_request @type,@subjectType,@subject,@store,@date,@payload;";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@type",approvalType.ToUpperInvariant());command.Parameters.AddWithValue("@subjectType",subjectType.Trim());command.Parameters.AddWithValue("@subject",subjectId.Trim());Add(command,"@store",Clean(storeCode)?.ToUpperInvariant());Add(command,"@date",businessDate);command.Parameters.AddWithValue("@payload",JsonSerializer.Serialize(payload));return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<ApprovalRequestRow>> LoadApprovalsAsync(string? status="PENDING",CancellationToken cancellationToken=default)
    {
        const string sql="SELECT TOP(500) approval_request_id,approval_type,subject_type,subject_id,store_code,business_date,requested_by,requested_utc,status,decided_by,decided_utc,decision_reason,JSON_VALUE(request_payload_json,'$.reason'),JSON_VALUE(request_payload_json,'$.replacementSha256') FROM dbo.approval_requests WHERE @status IS NULL OR status=@status ORDER BY CASE WHEN status='PENDING' THEN 0 ELSE 1 END,requested_utc DESC";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);Add(command,"@status",Clean(status)?.ToUpperInvariant());await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<ApprovalRequestRow>();while(await reader.ReadAsync(cancellationToken))rows.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),OptionalString(reader,4),reader.IsDBNull(5)?null:DateOnly.FromDateTime(reader.GetDateTime(5)),reader.GetString(6),reader.GetDateTime(7),reader.GetString(8),OptionalString(reader,9),reader.IsDBNull(10)?null:reader.GetDateTime(10),OptionalString(reader,11),OptionalString(reader,12),OptionalString(reader,13)));return rows;
    }

    public async Task DecideApprovalAsync(long id,bool approve,string reason,CancellationToken cancellationToken=default)
    {
        await EnsureOwnerAsync(cancellationToken);if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Enter an approval decision reason.",nameof(reason));
        const string sql="EXEC dbo.decide_approval_request @id,@approve,@reason;";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@id",id);command.Parameters.AddWithValue("@approve",approve);command.Parameters.AddWithValue("@reason",reason.Trim());await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> CreateAdjustmentRequestAsync(string storeCode,DateOnly businessDate,string adjustmentType,decimal amount,string reason,long? sourceDocumentId=null,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Enter the adjustment reason.",nameof(reason));
        const string sql="EXEC dbo.submit_controlled_adjustment @store,@date,@type,@amount,@reason,@document;";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@store",storeCode.Trim().ToUpperInvariant());command.Parameters.AddWithValue("@date",businessDate);command.Parameters.AddWithValue("@type",adjustmentType.Trim().ToUpperInvariant());command.Parameters.AddWithValue("@amount",amount);command.Parameters.AddWithValue("@reason",reason.Trim());Add(command,"@document",sourceDocumentId);return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<InvestigationResult>> SearchAsync(string term,int limit=200,CancellationToken cancellationToken=default)
    {
        var value=term?.Trim()??string.Empty;if(value.Length<2)throw new ArgumentException("Enter at least two characters to search.",nameof(term));
        const string sql="""
            DECLARE @pattern nvarchar(210)=N'%'+REPLACE(REPLACE(REPLACE(@term,N'~',N'~~'),N'%',N'~%'),N'_',N'~_')+N'%';
            SELECT TOP(@limit) result_type,primary_reference,scope,business_date,summary,navigation_hint,target_task,target_id,target_store FROM
            (
              SELECT N'Invoice' result_type,i.document_number primary_reference,i.store_code scope,i.transaction_date business_date,N'Canonical sales invoice' summary,N'Reports > Invoice Source Drill-down' navigation_hint,N'invoice-lineage' target_task,i.sales_invoice_id target_id,i.store_code target_store FROM dbo.sales_invoices i WHERE i.document_number LIKE @pattern ESCAPE N'~'
              UNION ALL SELECT N'Product',l.product_code,CONCAT(i.store_code,N' / ',i.document_number),i.transaction_date,N'Canonical sales line',N'Reports > Item-wise sales',N'sales-item',l.sales_line_id,i.store_code FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE l.product_code LIKE @pattern ESCAPE N'~'
              UNION ALL SELECT N'Source file',f.original_file_name,COALESCE(f.store_code,N'Unassigned'),f.business_date,CONCAT(N'ETP ',COALESCE(f.report_code,N'unknown'),N' source'),N'Import > History',N'import-history',f.import_file_id,f.store_code FROM dbo.import_files f WHERE f.original_file_name LIKE @pattern ESCAPE N'~' OR f.source_sha256 LIKE @pattern ESCAPE N'~'
              UNION ALL SELECT N'Report generation',CONCAT(N'Generation ',g.generation_number),g.store_code,g.business_date,CASE WHEN g.is_final=1 THEN N'Final immutable report' ELSE N'Draft immutable report' END,N'Reports > Archive',N'generations',g.daily_report_generation_id,g.store_code FROM dbo.daily_report_generations g WHERE CONVERT(nvarchar(30),g.daily_report_generation_id) LIKE @pattern ESCAPE N'~' OR g.content_sha256 LIKE @pattern ESCAPE N'~'
              UNION ALL SELECT N'Register',r.document_number,CONCAT(r.store_code,N' / ',r.register_type),r.business_date,COALESCE(r.counterparty,N'Register entry'),N'Registers',CASE r.register_type WHEN 'INWARD' THEN 'register-inward' WHEN 'OUTWARD' THEN 'register-outward' WHEN 'CREDIT_NOTE' THEN 'register-credit' WHEN 'SERVICE_RECEIPT' THEN 'register-service' WHEN 'COURIER' THEN 'register-courier' WHEN 'STOCK_TRANSFER' THEN 'register-transfer' WHEN 'EXPENSE' THEN 'register-expense' WHEN 'VENDOR_INVOICE' THEN 'register-vendor' END,r.register_entry_id,r.store_code FROM dbo.register_entries r WHERE r.document_number LIKE @pattern ESCAPE N'~' OR r.counterparty LIKE @pattern ESCAPE N'~' OR r.reference LIKE @pattern ESCAPE N'~'
              UNION ALL SELECT N'Document',d.original_file_name,COALESCE(d.store_code,N'Unassigned'),d.business_date,CONCAT(COALESCE(d.document_type,d.source_type),N' / ',d.lifecycle_status),N'Import > Received files',N'received-files',d.source_document_id,d.store_code FROM dbo.source_documents d WHERE d.original_file_name LIKE @pattern ESCAPE N'~' OR d.source_sha256 LIKE @pattern ESCAPE N'~'
            ) results ORDER BY business_date DESC,primary_reference;
            """;
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@term",value);command.Parameters.AddWithValue("@limit",Math.Clamp(limit,1,500));await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<InvestigationResult>();while(await reader.ReadAsync(cancellationToken))rows.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:DateOnly.FromDateTime(reader.GetDateTime(3)),reader.GetString(4),reader.GetString(5)) { TargetTaskId=OptionalString(reader,6),TargetId=reader.IsDBNull(7)?null:reader.GetInt64(7),StoreCode=OptionalString(reader,8) });return rows;
    }

    public async Task RecordPackageAsync(long generationId,string packageType,string path,string manifestJson,string sha256,bool isFinal,CancellationToken cancellationToken=default)
    {
        const string sql="IF NOT EXISTS(SELECT 1 FROM dbo.report_packages WITH(UPDLOCK,HOLDLOCK) WHERE package_sha256=@hash) INSERT dbo.report_packages(daily_report_generation_id,package_type,package_path,manifest_json,package_sha256,package_status,created_by) VALUES(@generation,@type,@path,@manifest,@hash,@status,SUSER_SNAME()); EXEC dbo.record_operational_audit 'ReportPackage','Succeeded',N'Immutable ZIP report package created or re-exported',N'database';";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@generation",generationId);command.Parameters.AddWithValue("@type",packageType);command.Parameters.AddWithValue("@path",Path.GetFullPath(path));command.Parameters.AddWithValue("@manifest",manifestJson);command.Parameters.AddWithValue("@hash",SqlServerImportFileRepository.NormalizeHash(sha256));command.Parameters.AddWithValue("@status",isFinal?"FINAL":"DRAFT");await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordShareAttemptAsync(long generationId,long? packageId,string channel,string? destinationSafe,string attachmentName,string outcome,string message,CancellationToken cancellationToken=default)
    {
        const string sql="INSERT dbo.share_attempts(daily_report_generation_id,report_package_id,channel,destination_safe,attachment_file_name,outcome,safe_message,initiated_by) VALUES(@generation,@package,@channel,@destination,@attachment,@outcome,@message,SUSER_SNAME()); EXEC dbo.record_operational_audit 'ShareInitiated',@auditOutcome,N'Report share action initiated',N'database';";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@generation",generationId);Add(command,"@package",packageId);command.Parameters.AddWithValue("@channel",channel);Add(command,"@destination",Clean(destinationSafe));command.Parameters.AddWithValue("@attachment",Path.GetFileName(attachmentName));command.Parameters.AddWithValue("@outcome",outcome);command.Parameters.AddWithValue("@message",message);command.Parameters.AddWithValue("@auditOutcome",outcome=="FAILED"?"Failed":"Succeeded");await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingMapping>> LoadApprovedAccountingMappingsAsync(string storeCode,DateOnly businessDate,CancellationToken cancellationToken=default)
    {
        const string sql="""
            SELECT business_event,debit_ledger,credit_ledger,narration_template,cost_centre
            FROM dbo.accounting_mappings m JOIN dbo.approval_requests a ON a.approval_request_id=m.approval_request_id AND a.status='APPROVED'
            WHERE m.is_active=1 AND (m.store_code IS NULL OR m.store_code=@store) AND m.effective_from<=@date AND (m.effective_to IS NULL OR m.effective_to>=@date)
            ORDER BY CASE WHEN m.store_code=@store THEN 0 ELSE 1 END,m.version DESC;
            """;
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@store",storeCode.Trim().ToUpperInvariant());command.Parameters.AddWithValue("@date",businessDate);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<AccountingMapping>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);while(await reader.ReadAsync(cancellationToken)){var code=reader.GetString(0);if(seen.Add(code))rows.Add(new(code,reader.GetString(1),reader.GetString(2),reader.GetString(3),OptionalString(reader,4)));}return rows;
    }

    private const string SaveAccountingMappingSql="""
            IF NOT EXISTS(SELECT 1 FROM dbo.approval_requests WHERE approval_request_id=@approval AND approval_type='ACCOUNTING_MAPPING' AND status='APPROVED')
              THROW 51224,'An approved accounting-mapping request is required.',1;
            DECLARE @version int=ISNULL((SELECT MAX(version) FROM dbo.accounting_mappings WITH(UPDLOCK,HOLDLOCK) WHERE business_event=@event AND ISNULL(store_code,'')=ISNULL(@store,'')),0)+1;
            UPDATE dbo.accounting_mappings SET is_active=0,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME() WHERE business_event=@event AND ISNULL(store_code,'')=ISNULL(@store,'') AND is_active=1;
            INSERT dbo.accounting_mappings(business_event,store_code,debit_ledger,credit_ledger,narration_template,effective_from,version,approval_request_id,modified_by)
            VALUES(@event,@store,@debit,@credit,@narration,@effective,@version,@approval,SUSER_SNAME());
            EXEC dbo.record_operational_audit 'MasterDataChange','Succeeded',N'Approved accounting mapping version created',N'database';
            """;

    public async Task ApproveAccountingMappingAsync(Etp.Reporting.Application.Accounting.ApproveAccountingMapping value, CancellationToken token=default)
    {
        await EnsureOwnerAsync(token);
        if(new[]{value.BusinessEvent,value.DebitLedger,value.CreditLedger,value.NarrationTemplate,value.Reason}.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Mapping fields and an approval reason are required.");
        await using var connection=await OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(token);
        var eventCode=value.BusinessEvent.Trim().ToUpperInvariant();
        await using var create=new SqlCommand("EXEC dbo.submit_approval_request 'ACCOUNTING_MAPPING','AccountingMapping',@event,@store,@date,@payload;",connection,transaction);
        create.Parameters.AddWithValue("@event",eventCode);
        create.Parameters.AddWithValue("@store",value.Scope.StoreCode.Trim().ToUpperInvariant());
        create.Parameters.AddWithValue("@date",value.Scope.BusinessDate);
        create.Parameters.AddWithValue("@payload",JsonSerializer.Serialize(new { Event=eventCode,Debit=value.DebitLedger,Credit=value.CreditLedger,Narration=value.NarrationTemplate,Store=value.Scope.StoreCode }));
        var id=Convert.ToInt64(await create.ExecuteScalarAsync(token));
        await using var decide=new SqlCommand("EXEC dbo.decide_approval_request @id,1,@reason;",connection,transaction);
        decide.Parameters.AddWithValue("@id",id);decide.Parameters.AddWithValue("@reason",value.Reason.Trim());
        await decide.ExecuteNonQueryAsync(token);
        await using var save=new SqlCommand(SaveAccountingMappingSql,connection,transaction);
        save.Parameters.AddWithValue("@approval",id);save.Parameters.AddWithValue("@event",eventCode);
        save.Parameters.AddWithValue("@store",value.Scope.StoreCode.Trim().ToUpperInvariant());
        save.Parameters.AddWithValue("@debit",value.DebitLedger.Trim());save.Parameters.AddWithValue("@credit",value.CreditLedger.Trim());
        save.Parameters.AddWithValue("@narration",value.NarrationTemplate.Trim());save.Parameters.AddWithValue("@effective",value.Scope.BusinessDate);
        await save.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task SaveAccountingMappingAsync(long approvedRequestId,string businessEvent,string debitLedger,string creditLedger,string narration,string? storeCode,DateOnly effectiveFrom,CancellationToken cancellationToken=default)
    {
        await EnsureOwnerAsync(cancellationToken);
        if(new[]{businessEvent,debitLedger,creditLedger,narration}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Business event, debit ledger, credit ledger and narration are required.");
        const string sql=SaveAccountingMappingSql;
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@approval",approvedRequestId);command.Parameters.AddWithValue("@event",businessEvent.Trim().ToUpperInvariant());Add(command,"@store",Clean(storeCode)?.ToUpperInvariant());command.Parameters.AddWithValue("@debit",debitLedger.Trim());command.Parameters.AddWithValue("@credit",creditLedger.Trim());command.Parameters.AddWithValue("@narration",narration.Trim());command.Parameters.AddWithValue("@effective",effectiveFrom);await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(long GenerationId,IReadOnlyList<AccountingBusinessEvent> Events)> LoadAccountingSourceAsync(string storeCode,DateOnly businessDate,CancellationToken cancellationToken=default)
    {
        const string sql="""
            DECLARE @generation bigint=(SELECT TOP(1) daily_report_generation_id FROM dbo.daily_report_generations WHERE store_code=@store AND business_date=@date AND is_final=1 ORDER BY generation_number DESC);
            IF @generation IS NULL THROW 51220,'Finalise the report generation before preparing accounting.',1;
            SELECT @generation;
            SELECT event_code,amount,source_reference,description FROM
            (
              SELECT 'NET_SALES' event_code,COALESCE(SUM(l.source_gross_amount),0) amount,CONCAT(@store,'/',CONVERT(varchar(10),@date,23)) source_reference,'ETP net sales including GST' description
              FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE i.store_code=@store AND i.transaction_date=@date
              UNION ALL
              SELECT 'TENDER_TOTAL',COALESCE(SUM(t.source_amount),0),CONCAT(@store,'/',CONVERT(varchar(10),@date,23)),'Eligible ETP tender total'
              FROM dbo.reporting_sales_tenders t JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id WHERE i.store_code=@store AND i.transaction_date=@date
              UNION ALL
              SELECT 'SERVICE_SALES',COALESCE(SUM(CASE WHEN field_code IN('SERVICE_CASH','SERVICE_CARD','SERVICE_UPI') THEN numeric_value ELSE 0 END),0),CONCAT(@store,'/',CONVERT(varchar(10),@date,23)),'Approved service collections'
              FROM dbo.manual_operational_inputs WHERE store_code=@store AND business_date=@date
              UNION ALL
              SELECT 'ADJUSTMENT',COALESCE(SUM(amount),0),CONCAT(@store,'/',CONVERT(varchar(10),@date,23)),'Owner-approved controlled adjustments'
              FROM dbo.controlled_adjustments WHERE store_code=@store AND business_date=@date AND status='APPROVED'
            ) source WHERE amount<>0;
            """;
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@store",storeCode.Trim().ToUpperInvariant());command.Parameters.AddWithValue("@date",businessDate);await using var reader=await command.ExecuteReaderAsync(cancellationToken);if(!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("A final report generation was not found.");var generation=reader.GetInt64(0);await reader.NextResultAsync(cancellationToken);var events=new List<AccountingBusinessEvent>();while(await reader.ReadAsync(cancellationToken))events.Add(new(reader.GetString(0),reader.GetDecimal(1),reader.GetString(2),reader.GetString(3)));return(generation,events);
    }

    public async Task UpdateIssueWorkflowAsync(long issueId,string status,string reason,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Enter an issue workflow reason.",nameof(reason));
        const string sql="UPDATE dbo.data_quality_issues SET workflow_status=@status,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),resolution_reason=@reason WHERE data_quality_issue_id=@id; IF @@ROWCOUNT<>1 THROW 51223,'The issue was not found.',1; EXEC dbo.record_operational_audit 'IssueWorkflow','Succeeded',N'Data-quality issue workflow updated; technical control status retained',N'database';";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@id",issueId);command.Parameters.AddWithValue("@status",status.ToUpperInvariant());command.Parameters.AddWithValue("@reason",reason.Trim());await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SyncDataQualityIssuesAsync(IReadOnlyList<DataQualitySummaryRow> findings,CancellationToken cancellationToken=default)
    {
        var payload=findings.Where(x=>x.Count>0).Select(x=>new{IssueKey=$"COMPUTED/{x.Area}/{x.Code}",x.Area,x.Code,Severity=x.Severity.ToUpperInvariant() switch { "FAIL" => "CRITICAL", "INFORMATION" => "INFO", "INFO" => "INFO", "WARNING" => "WARNING", "CRITICAL" => "CRITICAL", _ => throw new ArgumentException("Unknown data-quality severity.", nameof(findings)) },x.Count,x.Message}).ToArray();
        const string sql="""
            DECLARE @now datetime2(3)=SYSUTCDATETIME();
            MERGE dbo.data_quality_issues WITH(HOLDLOCK) target
            USING(SELECT IssueKey,Area,Code,Severity,[Count],[Message] FROM OPENJSON(@json)
              WITH(IssueKey nvarchar(300),Area varchar(50),Code varchar(50),Severity varchar(10),[Count] bigint,[Message] nvarchar(500))) source
              ON target.issue_key=source.IssueKey
            WHEN MATCHED THEN UPDATE SET category=source.Code,severity=UPPER(source.Severity),technical_control_status=CASE UPPER(source.Severity) WHEN 'CRITICAL' THEN 'FAIL' ELSE 'WARNING' END,
              safe_summary=CONCAT(source.[Message],N' (',source.[Count],N' current)'),modified_by=SUSER_SNAME(),modified_utc=@now
            WHEN NOT MATCHED THEN INSERT(issue_key,category,severity,technical_control_status,workflow_status,safe_summary,modified_by)
              VALUES(source.IssueKey,source.Code,UPPER(source.Severity),CASE UPPER(source.Severity) WHEN 'CRITICAL' THEN 'FAIL' ELSE 'WARNING' END,'OPEN',CONCAT(source.[Message],N' (',source.[Count],N' current)'),SUSER_SNAME());
            UPDATE dbo.data_quality_issues SET technical_control_status='PASS',modified_by=SUSER_SNAME(),modified_utc=@now
            WHERE issue_key LIKE N'COMPUTED/%' AND NOT EXISTS(SELECT 1 FROM OPENJSON(@json) WITH(IssueKey nvarchar(300)) currentRows WHERE currentRows.IssueKey=dbo.data_quality_issues.issue_key);
            """;
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@json",JsonSerializer.Serialize(payload));await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DataQualityIssueRow>> LoadDataQualityIssuesAsync(CancellationToken cancellationToken=default)
    {
        const string sql="SELECT TOP(500) data_quality_issue_id,category,severity,store_code,business_date,technical_control_status,workflow_status,safe_summary,assigned_to,modified_utc,resolution_reason FROM dbo.data_quality_issues ORDER BY CASE severity WHEN 'CRITICAL' THEN 0 WHEN 'WARNING' THEN 1 ELSE 2 END,modified_utc DESC";
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(sql,connection);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<DataQualityIssueRow>();while(await reader.ReadAsync(cancellationToken))rows.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),OptionalString(reader,3),reader.IsDBNull(4)?null:DateOnly.FromDateTime(reader.GetDateTime(4)),reader.GetString(5),reader.GetString(6),reader.GetString(7),OptionalString(reader,8),reader.GetDateTime(9),OptionalString(reader,10)));return rows;
    }

    public async Task<IReadOnlyList<ProductHealthItem>> LoadProductHealthAsync(CancellationToken cancellationToken=default)
    {
        var settings=await LoadSettingsAsync(cancellationToken);var items=new List<ProductHealthItem>();
        items.Add(PathHealth("Document repository",settings.DocumentRepositoryPath));items.Add(PathHealth("Share folder",settings.ShareFolderPath));
        items.Add(string.IsNullOrWhiteSpace(settings.SmtpHost)?new("Email","Warning","Direct SMTP is not configured; use safe email drafts instead."):new("Email","Healthy","SMTP metadata is configured; credentials remain outside the database."));
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand("SELECT (SELECT COUNT_BIG(*) FROM dbo.import_conflicts WHERE status IN('OPEN','ACKNOWLEDGED')),(SELECT COUNT_BIG(*) FROM dbo.approval_requests WHERE status='PENDING')",connection);await using var reader=await command.ExecuteReaderAsync(cancellationToken);if(await reader.ReadAsync(cancellationToken)){items.Add(QueueHealth("Import conflicts",reader.GetInt64(0)));items.Add(QueueHealth("Pending approvals",reader.GetInt64(1)));}return items;
    }

    private async Task EnsureOwnerAsync(CancellationToken token)
    {
        var access=await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(token);if(!access.CanAdminister)throw new UnauthorizedAccessException("Owner permission is required.");
    }
    private async Task<SqlConnection> OpenAsync(CancellationToken token){var connection=new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));await connection.OpenAsync(token);return connection;}
    private static void Add(SqlCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string? OptionalString(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static string EscapeLike(string value)=>value.Replace("~","~~",StringComparison.Ordinal).Replace("%","~%",StringComparison.Ordinal).Replace("_","~_",StringComparison.Ordinal);
    private static ProductHealthItem PathHealth(string component,string path){try{var full=Path.GetFullPath(path);return Directory.Exists(full)?new(component,"Healthy",full):new(component,"Warning",$"Folder will be created when first used: {full}");}catch(Exception ex)when(ex is ArgumentException or NotSupportedException or PathTooLongException){return new(component,"Critical","The configured local path is invalid.");}}
    private static ProductHealthItem QueueHealth(string component,long count)=>count==0?new(component,"Healthy","No pending items."):new(component,"Warning",$"{count:N0} item(s) require review.");
    private static SourceDocumentRow ReadDocument(SqlDataReader r)=>new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4),r.GetString(5),OptionalString(r,6),OptionalString(r,7),r.IsDBNull(8)?null:DateOnly.FromDateTime(r.GetDateTime(8)),r.GetString(9),OptionalString(r,10),r.IsDBNull(11)?null:r.GetInt64(11),r.IsDBNull(12)?null:r.GetInt64(12),r.GetString(13),r.GetDateTime(14),OptionalString(r,15));
    private const string DocumentSelect="SELECT source_document_id,original_file_name,managed_file_path,source_sha256,size_bytes,source_type,document_type,store_code,business_date,lifecycle_status,report_code,import_file_id,report_generation_id,received_by,received_utc,safe_message FROM dbo.source_documents";
}
