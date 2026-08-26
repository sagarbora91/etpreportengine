SET XACT_ABORT ON;

ALTER TABLE dbo.import_batches ADD source_row_count int NULL, failure_reason nvarchar(2000) NULL;
EXEC(N'ALTER TABLE dbo.import_batches ADD CONSTRAINT CK_import_batches_source_rows CHECK (source_row_count IS NULL OR source_row_count >= 0)');
CREATE UNIQUE INDEX UX_import_files_source_sha256 ON dbo.import_files(source_sha256);

CREATE TABLE dbo.source_lineage
(
    source_lineage_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_source_lineage PRIMARY KEY,
    import_file_id bigint NOT NULL,
    sheet_name nvarchar(128) NOT NULL,
    source_row_number int NOT NULL,
    source_record_type varchar(40) NULL,
    CONSTRAINT CK_source_lineage_row CHECK (source_row_number > 0),
    CONSTRAINT FK_source_lineage_file FOREIGN KEY(import_file_id) REFERENCES dbo.import_files(import_file_id),
    CONSTRAINT UQ_source_lineage UNIQUE(import_file_id,sheet_name,source_row_number,source_record_type)
);

CREATE TABLE dbo.sales_invoices
(
    sales_invoice_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_sales_invoices PRIMARY KEY,
    store_code varchar(30) NOT NULL, document_number nvarchar(80) NOT NULL,
    invoice_year int NOT NULL, transaction_date date NOT NULL,
    CONSTRAINT UQ_sales_invoices_natural UNIQUE(store_code,invoice_year,document_number)
);

CREATE TABLE dbo.sales_lines
(
    sales_line_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_sales_lines PRIMARY KEY,
    sales_invoice_id bigint NOT NULL, line_identifier nvarchar(80) NOT NULL,
    product_code nvarchar(80) NOT NULL, source_transaction_type nvarchar(80) NULL,
    source_quantity decimal(19,4) NOT NULL, source_gross_amount decimal(19,4) NULL,
    source_net_amount decimal(19,4) NULL, currency_code char(3) NOT NULL,
    source_lineage_id bigint NOT NULL,
    CONSTRAINT FK_sales_lines_invoice FOREIGN KEY(sales_invoice_id) REFERENCES dbo.sales_invoices(sales_invoice_id),
    CONSTRAINT FK_sales_lines_lineage FOREIGN KEY(source_lineage_id) REFERENCES dbo.source_lineage(source_lineage_id),
    CONSTRAINT UQ_sales_lines_natural UNIQUE(sales_invoice_id,line_identifier),
    CONSTRAINT UQ_sales_lines_lineage UNIQUE(source_lineage_id)
);

CREATE TABLE dbo.sales_invoice_controls
(
    sales_invoice_control_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_sales_invoice_controls PRIMARY KEY,
    sales_invoice_id bigint NOT NULL, source_transaction_type nvarchar(80) NULL,
    source_invoice_quantity decimal(19,4) NOT NULL, source_net_value decimal(19,4) NOT NULL,
    currency_code char(3) NOT NULL, source_lineage_id bigint NOT NULL,
    CONSTRAINT FK_sales_invoice_controls_invoice FOREIGN KEY(sales_invoice_id) REFERENCES dbo.sales_invoices(sales_invoice_id),
    CONSTRAINT FK_sales_invoice_controls_lineage FOREIGN KEY(source_lineage_id) REFERENCES dbo.source_lineage(source_lineage_id),
    CONSTRAINT UQ_sales_invoice_controls_lineage UNIQUE(source_lineage_id)
);

CREATE TABLE dbo.sales_tenders
(
    sales_tender_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_sales_tenders PRIMARY KEY,
    sales_invoice_id bigint NOT NULL, tender_type nvarchar(80) NOT NULL,
    source_amount decimal(19,4) NOT NULL, currency_code char(3) NOT NULL,
    is_reporting_eligible bit NOT NULL CONSTRAINT DF_sales_tenders_reporting DEFAULT(1),
    exclusion_reason nvarchar(200) NULL,
    source_lineage_id bigint NOT NULL,
    CONSTRAINT FK_sales_tenders_invoice FOREIGN KEY(sales_invoice_id) REFERENCES dbo.sales_invoices(sales_invoice_id),
    CONSTRAINT FK_sales_tenders_lineage FOREIGN KEY(source_lineage_id) REFERENCES dbo.source_lineage(source_lineage_id),
    CONSTRAINT UQ_sales_tenders_lineage UNIQUE(source_lineage_id)
    ,CONSTRAINT CK_sales_tenders_exclusion CHECK ((is_reporting_eligible=1 AND exclusion_reason IS NULL) OR (is_reporting_eligible=0 AND exclusion_reason IS NOT NULL))
);

CREATE TABLE dbo.stock_movements
(
    stock_movement_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_stock_movements PRIMARY KEY,
    store_code varchar(30) NOT NULL, document_number nvarchar(80) NOT NULL,
    invoice_year int NOT NULL, document_date date NOT NULL, product_code nvarchar(80) NOT NULL,
    source_transaction_type nvarchar(80) NOT NULL, from_location nvarchar(80) NULL, to_location nvarchar(80) NULL,
    opening_quantity decimal(19,4) NOT NULL, transaction_quantity decimal(19,4) NOT NULL, closing_quantity decimal(19,4) NOT NULL,
    source_lineage_id bigint NOT NULL,
    CONSTRAINT CK_stock_movements_balance CHECK (closing_quantity = opening_quantity + transaction_quantity),
    CONSTRAINT FK_stock_movements_lineage FOREIGN KEY(source_lineage_id) REFERENCES dbo.source_lineage(source_lineage_id),
    CONSTRAINT UQ_stock_movements_lineage UNIQUE(source_lineage_id)
);

CREATE TABLE dbo.stock_snapshots
(
    stock_snapshot_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_stock_snapshots PRIMARY KEY,
    store_code varchar(30) NOT NULL, snapshot_date date NOT NULL, product_code nvarchar(80) NOT NULL,
    ean nvarchar(80) NULL, brand_code nvarchar(80) NULL, brand_name nvarchar(200) NULL,
    cluster nvarchar(100) NULL, gender nvarchar(50) NULL, batch_number nvarchar(80) NULL, source_uid nvarchar(100) NULL,
    quantity decimal(19,4) NOT NULL, unit_cost decimal(19,4) NULL, total_cost decimal(19,4) NULL,
    source_lineage_id bigint NOT NULL,
    CONSTRAINT FK_stock_snapshots_lineage FOREIGN KEY(source_lineage_id) REFERENCES dbo.source_lineage(source_lineage_id),
    CONSTRAINT UQ_stock_snapshots_lineage UNIQUE(source_lineage_id)
);
CREATE INDEX IX_stock_snapshots_asof ON dbo.stock_snapshots(snapshot_date,store_code,product_code) INCLUDE(quantity,total_cost);
CREATE INDEX IX_stock_movements_date ON dbo.stock_movements(document_date,store_code,product_code) INCLUDE(transaction_quantity,closing_quantity,source_transaction_type);
CREATE INDEX IX_stock_movements_document ON dbo.stock_movements(store_code,invoice_year,document_number,document_date,product_code,source_transaction_type);
CREATE INDEX IX_sales_lines_report ON dbo.sales_lines(product_code,sales_invoice_id) INCLUDE(source_quantity,source_gross_amount,source_net_amount);

EXEC(N'CREATE PROCEDURE dbo.persist_sales_line
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@line nvarchar(80),@product nvarchar(80),@type nvarchar(80)=NULL,@qty decimal(19,4),@gross decimal(19,4)=NULL,@net decimal(19,4)=NULL,@currency char(3),@lineage bigint
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint;
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date) THROW 51001,''Invoice date conflicts with the existing natural key.'',1;
 INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,currency_code,source_lineage_id) VALUES(@invoice,@line,@product,@type,@qty,@gross,@net,@currency,@lineage);
END');

EXEC(N'CREATE PROCEDURE dbo.persist_sales_tender
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@type nvarchar(80),@amount decimal(19,4),@currency char(3),@lineage bigint,@eligible bit=1,@reason nvarchar(200)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint;
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date) THROW 51001,''Invoice date conflicts with the existing natural key.'',1;
 IF UPPER(@type)=''PAYMENTTYPE25'' BEGIN SET @eligible=0; SET @reason=COALESCE(@reason,''UNRESOLVED_PAYMENTTYPE25''); END
 INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id,is_reporting_eligible,exclusion_reason) VALUES(@invoice,@type,@amount,@currency,@lineage,@eligible,@reason);
END');

EXEC(N'CREATE VIEW dbo.reporting_sales_tenders AS
 SELECT sales_tender_id,sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id
 FROM dbo.sales_tenders WHERE is_reporting_eligible=1');

EXEC(N'CREATE PROCEDURE dbo.persist_sales_invoice_control
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@type nvarchar(80)=NULL,@qty decimal(19,4),@net decimal(19,4),@currency char(3),@lineage bigint
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint;
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date) THROW 51001,''Invoice date conflicts with the existing natural key.'',1;
 INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id) VALUES(@invoice,@type,@qty,@net,@currency,@lineage);
END');
