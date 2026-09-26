-- Phase 5 accounting foundation. Existing migrations and recorded amounts remain unchanged.
ALTER TABLE dbo.accounting_batches DROP CONSTRAINT CK_accounting_batches_status;
ALTER TABLE dbo.accounting_batches ALTER COLUMN status varchar(40) NOT NULL;
ALTER TABLE dbo.accounting_batches ADD blocking_reason nvarchar(1000) NULL;
ALTER TABLE dbo.product_settings ADD tally_company_name nvarchar(200) NULL,
 tally_environment_label varchar(10) NOT NULL CONSTRAINT DF_product_settings_tally_environment DEFAULT('TEST');

EXEC(N'UPDATE dbo.accounting_batches SET status=CASE status WHEN ''REVIEW'' THEN ''DRAFT'' WHEN ''APPROVED'' THEN ''APPROVED_READY'' WHEN ''EXPORTED'' THEN ''EXPORTED_AWAITING_IMPORT'' ELSE status END;
ALTER TABLE dbo.accounting_batches ADD CONSTRAINT CK_accounting_batches_status CHECK(status IN(''DRAFT'',''BLOCKED'',''APPROVED_READY'',''EXPORTED_AWAITING_IMPORT'',''REJECTED''));
ALTER TABLE dbo.product_settings ADD CONSTRAINT CK_product_settings_tally_environment CHECK(tally_environment_label IN(''TEST'',''PRODUCTION''));
CREATE TABLE dbo.accounting_batch_invoices(
 accounting_batch_id bigint NOT NULL REFERENCES dbo.accounting_batches(accounting_batch_id),
 store_code varchar(30) NOT NULL, invoice_year int NOT NULL, document_number nvarchar(80) NOT NULL,
 is_active bit NOT NULL,
 CONSTRAINT PK_accounting_batch_invoices PRIMARY KEY(accounting_batch_id,store_code,invoice_year,document_number));
-- Historical batches reserve their invoice identities too. Never invent a rejection to hide a conflict.
INSERT dbo.accounting_batch_invoices
 SELECT b.accounting_batch_id,i.store_code,i.invoice_year,i.document_number,CASE WHEN b.status=''REJECTED'' THEN 0 ELSE 1 END
 FROM dbo.accounting_batches b JOIN dbo.sales_invoices i ON i.store_code=b.store_code AND i.transaction_date=b.business_date;
IF EXISTS(SELECT 1 FROM dbo.accounting_batch_invoices WHERE is_active=1 GROUP BY store_code,invoice_year,document_number HAVING COUNT(*)>1)
 THROW 51450,''Existing accounting batches cover the same invoice. Review and reject duplicate unexported batches before updating the database.'',1;
CREATE UNIQUE INDEX UX_accounting_invoice_active ON dbo.accounting_batch_invoices(store_code,invoice_year,document_number) WHERE is_active=1;
CREATE TABLE dbo.accounting_export_receipts(
 accounting_export_receipt_id bigint IDENTITY PRIMARY KEY,
 accounting_batch_id bigint NOT NULL REFERENCES dbo.accounting_batches(accounting_batch_id),
 output_path nvarchar(500) NOT NULL,
 sha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(sha256 NOT LIKE ''%[^0-9a-f]%'' AND LEN(sha256)=64),
 tally_company_name nvarchar(200) NOT NULL CHECK(LEN(LTRIM(RTRIM(tally_company_name)))>0),
 environment_label varchar(10) NOT NULL CHECK(environment_label IN(''TEST'',''PRODUCTION'')),
 exported_by nvarchar(200) NOT NULL,
 exported_utc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME());');

EXEC(N'CREATE TRIGGER dbo.trg_accounting_invoice_reservation ON dbo.accounting_batch_invoices INSTEAD OF INSERT AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource=''ETP.AccountingReservations'',@LockMode=''Exclusive'',@LockOwner=''Transaction'',@LockTimeout=15000;
 IF @lock<0 THROW 51451,''Accounting is busy. Try again.'',1;
 DECLARE @earlier bigint,@doc nvarchar(80),@message nvarchar(2048);
 SELECT TOP(1) @earlier=old.accounting_batch_id,@doc=i.document_number
 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id
 JOIN dbo.accounting_batch_invoices old ON old.store_code=i.store_code AND old.invoice_year=i.invoice_year AND old.document_number=i.document_number AND old.is_active=1
 WHERE b.status<>''REJECTED'' ORDER BY old.accounting_batch_id;
 IF @earlier IS NOT NULL BEGIN SET @message=CONCAT(''Invoice '',@doc,'' is already in batch '',@earlier,''. Reject that unexported batch before preparing another.''); THROW 51452,@message,1; END;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id
 WHERE i.store_code<>b.store_code OR NOT EXISTS(SELECT 1 FROM dbo.sales_invoices s WHERE s.store_code=i.store_code AND s.invoice_year=i.invoice_year AND s.document_number=i.document_number AND s.transaction_date=b.business_date))
 THROW 51453,''The invoice does not belong to this batch store and business date.'',1;
 INSERT dbo.accounting_batch_invoices(accounting_batch_id,store_code,invoice_year,document_number,is_active)
 SELECT i.accounting_batch_id,i.store_code,i.invoice_year,i.document_number,CASE WHEN b.status=''REJECTED'' THEN 0 ELSE 1 END
 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id;
END');

EXEC(N'CREATE TRIGGER dbo.trg_accounting_batch_states ON dbo.accounting_batches AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.accounting_batch_id=d.accounting_batch_id WHERE i.accounting_batch_id IS NULL)
 THROW 51454,''Accounting history cannot be deleted. Reject an unexported batch with a reason.'',1;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.accounting_batch_id=d.accounting_batch_id
 WHERE d.status IN(''REJECTED'',''EXPORTED_AWAITING_IMPORT'') AND i.status<>d.status)
 THROW 51455,''A rejected or exported batch cannot change status.'',1;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.accounting_batch_id=d.accounting_batch_id
 WHERE i.status<>d.status AND NOT((d.status IN(''DRAFT'',''BLOCKED'') AND i.status IN(''DRAFT'',''BLOCKED'',''APPROVED_READY'',''REJECTED'')) OR (d.status=''APPROVED_READY'' AND i.status IN(''REJECTED'',''EXPORTED_AWAITING_IMPORT''))))
 THROW 51456,''This accounting status change is not allowed.'',1;
 IF EXISTS(SELECT 1 FROM inserted WHERE status=''APPROVED_READY'' AND (debit_total<>credit_total OR NULLIF(LTRIM(RTRIM(approval_reason)),'''') IS NULL OR blocking_reason IS NOT NULL))
 THROW 51457,''A balanced, unblocked batch and an approval reason are required.'',1;
 UPDATE r SET is_active=0 FROM dbo.accounting_batch_invoices r JOIN inserted i ON i.accounting_batch_id=r.accounting_batch_id WHERE i.status=''REJECTED'';
END');

EXEC(N'CREATE TRIGGER dbo.trg_accounting_invoice_immutable ON dbo.accounting_batch_invoices AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.accounting_batch_id=d.accounting_batch_id AND i.store_code=d.store_code AND i.invoice_year=d.invoice_year AND i.document_number=d.document_number
 LEFT JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id
 WHERE i.accounting_batch_id IS NULL OR i.is_active<>CASE WHEN b.status=''REJECTED'' THEN 0 ELSE 1 END)
 THROW 51458,''Invoice reservations are controlled by the accounting batch status.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_accounting_export_receipts_immutable ON dbo.accounting_export_receipts AFTER UPDATE,DELETE AS
BEGIN THROW 51459,''Accounting export receipts cannot be changed or deleted.'',1; END');

EXEC(N'ALTER TRIGGER dbo.trg_accounting_entries_approved ON dbo.accounting_entries AFTER INSERT,UPDATE,DELETE AS
BEGIN
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id WHERE b.status IN(''APPROVED_READY'',''EXPORTED_AWAITING_IMPORT'',''REJECTED''))
 OR EXISTS(SELECT 1 FROM deleted d JOIN dbo.accounting_batches b ON b.accounting_batch_id=d.accounting_batch_id WHERE b.status IN(''APPROVED_READY'',''EXPORTED_AWAITING_IMPORT'',''REJECTED''))
 THROW 51212,''Decided accounting entries are immutable.'',1;
END');

EXEC(N'ALTER PROCEDURE dbo.reject_accounting_batch @id bigint,@reason nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1 THROW 51430,''Owner permission is required to reject accounting batches.'',1;
 IF @reason IS NULL OR LEN(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))))=0 OR LEN(@reason)>1000 THROW 51431,''Enter a rejection reason of at most 1000 characters.'',1;
 BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource=''ETP.AccountingReservations'',@LockMode=''Exclusive'',@LockOwner=''Transaction'',@LockTimeout=15000;
 IF @lock<0 THROW 51451,''Accounting is busy. Try again.'',1;
 UPDATE dbo.accounting_batches SET status=''REJECTED'',rejection_reason=LTRIM(RTRIM(@reason)),rejected_by=SUSER_SNAME(),rejected_utc=SYSUTCDATETIME()
 WHERE accounting_batch_id=@id AND status IN(''DRAFT'',''BLOCKED'',''APPROVED_READY'');
 IF @@ROWCOUNT<>1 THROW 51432,''Only an unexported, unrejected batch can be rejected.'',1;
 EXEC dbo.record_operational_audit ''AccountingBatch'',''Succeeded'',N''Accounting batch rejected'',N''database'';
 COMMIT TRANSACTION;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END');

GRANT SELECT,INSERT,UPDATE ON dbo.accounting_batch_invoices TO etp_owner;
GRANT SELECT,INSERT ON dbo.accounting_export_receipts TO etp_owner;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_batch_invoices TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_export_receipts TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_batches TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_entries TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_mappings TO etp_store_manager,etp_viewer;
