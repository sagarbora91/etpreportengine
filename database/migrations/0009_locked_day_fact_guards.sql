SET XACT_ABORT ON;
BEGIN TRANSACTION;

EXEC(N'CREATE TRIGGER dbo.trg_sales_lines_protect_locked
ON dbo.sales_lines AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM
   (SELECT sales_invoice_id FROM inserted UNION SELECT sales_invoice_id FROM deleted) x
   JOIN dbo.sales_invoices i ON i.sales_invoice_id=x.sales_invoice_id
   JOIN dbo.daily_reporting_days d ON d.store_code=i.store_code AND d.business_date=i.transaction_date
   WHERE d.status=''LOCKED''
 ) THROW 51030,''The business date is finalised. Reopen it before changing sales facts.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_sales_controls_protect_locked
ON dbo.sales_invoice_controls AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM
   (SELECT sales_invoice_id FROM inserted UNION SELECT sales_invoice_id FROM deleted) x
   JOIN dbo.sales_invoices i ON i.sales_invoice_id=x.sales_invoice_id
   JOIN dbo.daily_reporting_days d ON d.store_code=i.store_code AND d.business_date=i.transaction_date
   WHERE d.status=''LOCKED''
 ) THROW 51031,''The business date is finalised. Reopen it before changing invoice controls.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_sales_tenders_protect_locked
ON dbo.sales_tenders AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM
   (SELECT sales_invoice_id FROM inserted UNION SELECT sales_invoice_id FROM deleted) x
   JOIN dbo.sales_invoices i ON i.sales_invoice_id=x.sales_invoice_id
   JOIN dbo.daily_reporting_days d ON d.store_code=i.store_code AND d.business_date=i.transaction_date
   WHERE d.status=''LOCKED''
 ) THROW 51032,''The business date is finalised. Reopen it before changing tender facts.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_stock_movements_protect_locked
ON dbo.stock_movements AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM
   (SELECT store_code,document_date FROM inserted UNION SELECT store_code,document_date FROM deleted) x
   JOIN dbo.daily_reporting_days d ON d.store_code=x.store_code AND d.business_date=x.document_date
   WHERE d.status=''LOCKED''
 ) THROW 51033,''The business date is finalised. Reopen it before changing stock movements.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_stock_snapshots_protect_locked
ON dbo.stock_snapshots AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM
   (SELECT store_code,snapshot_date FROM inserted UNION SELECT store_code,snapshot_date FROM deleted) x
   JOIN dbo.daily_reporting_days d ON d.store_code=x.store_code AND d.business_date=x.snapshot_date
   WHERE d.status=''LOCKED''
 ) THROW 51034,''The business date is finalised. Reopen it before changing stock snapshots.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_sales_enrichments_protect_locked
ON dbo.sales_line_enrichments AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM
   (SELECT store_code,transaction_date FROM inserted UNION SELECT store_code,transaction_date FROM deleted) x
   JOIN dbo.daily_reporting_days d ON d.store_code=x.store_code AND d.business_date=x.transaction_date
   WHERE d.status=''LOCKED''
 ) THROW 51035,''The business date is finalised. Reopen it before changing sales enrichments.'',1;
END');

COMMIT TRANSACTION;
