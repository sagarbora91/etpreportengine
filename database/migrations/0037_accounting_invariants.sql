-- Refuse conflicting history before adding either accounting backstop.
-- The migration runner applies this entire script in one transaction.
IF EXISTS(SELECT 1 FROM dbo.accounting_batches WITH(UPDLOCK,HOLDLOCK)
 WHERE status<>'REJECTED' GROUP BY store_code,business_date HAVING COUNT(*)>1)
 THROW 51560,'Existing accounting batches cover the same store and business date. Review the conflicting batches before updating the database; exported batches are final and cannot be replaced.',1;

IF EXISTS(SELECT 1 FROM dbo.accounting_export_receipts WITH(UPDLOCK,HOLDLOCK)
 GROUP BY accounting_batch_id HAVING COUNT(*)>1)
 THROW 51561,'Existing accounting batches have more than one export receipt. Review the duplicate receipts before updating the database; existing export history has been retained.',1;

CREATE UNIQUE INDEX UX_accounting_batches_active_day
 ON dbo.accounting_batches(store_code,business_date) WHERE status<>'REJECTED';
CREATE UNIQUE INDEX UX_accounting_export_receipts_batch
 ON dbo.accounting_export_receipts(accounting_batch_id);

EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_accounting_invoice_reservation ON dbo.accounting_batch_invoices INSTEAD OF INSERT AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource=''ETP.AccountingReservations'',@LockMode=''Exclusive'',@LockOwner=''Transaction'',@LockTimeout=15000;
 IF @lock<0 THROW 51451,''Accounting is busy. Try again.'',1;
 DECLARE @earlier bigint,@earlierStatus varchar(40),@doc nvarchar(80),@message nvarchar(2048);
 SELECT TOP(1) @earlier=old.accounting_batch_id,@earlierStatus=earlier.status,@doc=i.document_number
 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id
 JOIN dbo.accounting_batch_invoices old ON old.store_code=i.store_code AND old.invoice_year=i.invoice_year AND old.document_number=i.document_number AND old.is_active=1
 JOIN dbo.accounting_batches earlier ON earlier.accounting_batch_id=old.accounting_batch_id
 WHERE b.status<>''REJECTED'' ORDER BY old.accounting_batch_id;
 IF @earlier IS NOT NULL BEGIN
  SET @message=CASE WHEN @earlierStatus=''EXPORTED_AWAITING_IMPORT''
   THEN CONCAT(''Invoice '',@doc,'' is already in exported batch '',@earlier,''. An exported batch is final; it cannot be replaced.'')
   ELSE CONCAT(''Invoice '',@doc,'' is already in batch '',@earlier,''. Reject that unexported batch before preparing another.'') END;
  THROW 51452,@message,1;
 END;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id
 WHERE i.store_code<>b.store_code OR NOT EXISTS(SELECT 1 FROM dbo.sales_invoices s WHERE s.store_code=i.store_code AND s.invoice_year=i.invoice_year AND s.document_number=i.document_number AND s.transaction_date=b.business_date))
 THROW 51453,''The invoice does not belong to this batch store and business date.'',1;
 INSERT dbo.accounting_batch_invoices(accounting_batch_id,store_code,invoice_year,document_number,is_active)
 SELECT i.accounting_batch_id,i.store_code,i.invoice_year,i.document_number,CASE WHEN b.status=''REJECTED'' THEN 0 ELSE 1 END
 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id;
END');
