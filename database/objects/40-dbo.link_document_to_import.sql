CREATE OR ALTER PROCEDURE dbo.link_document_to_import @document bigint,@hash char(64),@report varchar(30),@store varchar(30),@date date
AS BEGIN

            SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51420,'Owner or Store Manager permission is required.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_documents WHERE source_document_id=@document AND source_sha256=@hash)
  THROW 51427,'The retained document hash must match its source import.',1;

            BEGIN TRANSACTION;
            INSERT dbo.source_document_import_links(source_document_id,import_file_id)
            SELECT @document,f.import_file_id FROM dbo.import_files f
            WHERE f.source_sha256=@hash AND f.data_truth_version=1 AND f.report_code=@report
              AND (@store IS NULL OR f.store_code=@store) AND (@date IS NULL OR f.period_end=@date)
              AND NOT EXISTS(SELECT 1 FROM dbo.source_document_import_links l WITH(UPDLOCK,HOLDLOCK)
                  WHERE l.source_document_id=@document AND l.import_file_id=f.import_file_id);
            UPDATE d SET import_file_id=f.import_file_id,period_start=f.period_start,period_end=f.period_end,report_code=@report,store_code=COALESCE(@store,f.store_code),business_date=COALESCE(@date,f.business_date),
              lifecycle_status='IMPORTED',last_status_by=SUSER_SNAME(),last_status_utc=SYSUTCDATETIME(),safe_message=N'ETP source validated and imported into canonical data.'
            FROM dbo.source_documents d CROSS APPLY(SELECT TOP(1) f.* FROM dbo.import_files f
              WHERE f.source_sha256=@hash AND f.data_truth_version=1 AND f.report_code=@report
                AND (@store IS NULL OR f.store_code=@store) AND (@date IS NULL OR f.period_end=@date)
              ORDER BY f.import_file_id DESC) f WHERE d.source_document_id=@document;
            COMMIT TRANSACTION;
            
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.link_document_to_import TO etp_owner,etp_store_manager;
