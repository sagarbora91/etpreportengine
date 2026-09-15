SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- A byte-identical empty workbook can be valid evidence for several report/store periods.
CREATE TABLE dbo.source_document_import_links(
 source_document_id bigint NOT NULL REFERENCES dbo.source_documents(source_document_id),
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 CONSTRAINT PK_source_document_import_links PRIMARY KEY(source_document_id,import_file_id));
INSERT dbo.source_document_import_links(source_document_id,import_file_id)
 SELECT source_document_id,import_file_id FROM dbo.source_documents WHERE import_file_id IS NOT NULL;

CREATE TABLE dbo.etp_import_content(
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_row_number int NOT NULL, content_key varchar(80) NOT NULL,
 CONSTRAINT PK_etp_import_content PRIMARY KEY(import_file_id,content_key));

CREATE TABLE dbo.[etp_landing_r001] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [trans_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [invnumber] nvarchar(max) NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL,
 [invoicequantity] decimal(19,4) NULL,
 [invoicedate] date NULL,
 [invoice_year] int NULL,
 [cash] decimal(19,4) NULL,
 [card] decimal(19,4) NULL,
 [cheque] decimal(19,4) NULL,
 [loyalty_points] decimal(19,4) NULL,
 [gv] decimal(19,4) NULL,
 [creditnote_redeem] decimal(19,4) NULL,
 [excess_gv] decimal(19,4) NULL,
 [round_off] decimal(19,4) NULL,
 [no_refund] decimal(19,4) NULL,
 [others] decimal(19,4) NULL,
 [tata_gv] decimal(19,4) NULL,
 [giftcard] decimal(19,4) NULL,
 [tatacliq] decimal(19,4) NULL,
 [gyftr] decimal(19,4) NULL,
 [paytm] decimal(19,4) NULL,
 [heliosomni] decimal(19,4) NULL,
 [advancerdeem] decimal(19,4) NULL,
 [bhimupi] decimal(19,4) NULL,
 [phonepe] decimal(19,4) NULL,
 [bharatpe] decimal(19,4) NULL,
 [bajajfin] decimal(19,4) NULL,
 [razorpay] decimal(19,4) NULL,
 [paymenttype24] decimal(19,4) NULL,
 [paymenttype25] decimal(19,4) NULL,
 [issued_creditnote] decimal(19,4) NULL,
 [cash_refund] decimal(19,4) NULL,
 [cheque_rtgs_refund] decimal(19,4) NULL,
 [netvalue] decimal(19,4) NULL,
 [encircle] nvarchar(max) NULL,
 [storetimestamp] nvarchar(max) NULL,
 [referencenumber] nvarchar(max) NULL,
 [referenceyear] int NULL
);

CREATE INDEX IX_etp_landing_r001_file ON dbo.[etp_landing_r001](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r001_locked ON dbo.[etp_landing_r001] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r002] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [trans_type] nvarchar(max) NULL,
 [storetype] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [storename] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [customernumber] nvarchar(max) NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL,
 [invnumber] nvarchar(max) NULL,
 [invdate] date NULL,
 [signet_no] nvarchar(max) NULL,
 [ulpnumber] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [itemnumber] nvarchar(max) NULL,
 [qty] decimal(19,4) NULL,
 [ucp] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [sch_discounts] decimal(19,4) NULL,
 [netgross] decimal(19,4) NULL,
 [pre_discounts] decimal(19,4) NULL,
 [othrchrgs] decimal(19,4) NULL,
 [netamount] decimal(19,4) NULL,
 [tax] decimal(19,4) NULL,
 [storetimestamp] nvarchar(max) NULL,
 [hsncode] nvarchar(max) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [remarks] nvarchar(max) NULL,
 [netvalue] decimal(19,4) NULL,
 [invrefno] nvarchar(max) NULL,
 [invrefdate] date NULL,
 [brandname] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [scheme_reference_number] nvarchar(max) NULL,
 [online] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r002_file ON dbo.[etp_landing_r002](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r002_locked ON dbo.[etp_landing_r002] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r003] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [source_transaction_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [invoice_number] nvarchar(max) NULL,
 [transaction_date] date NULL,
 [product_code] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [brand_name] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [source_quantity] decimal(19,4) NULL,
 [ucp] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [scheme_discount] decimal(19,4) NULL,
 [netgross] decimal(19,4) NULL,
 [activation_details] nvarchar(max) NULL,
 [user_discount] decimal(19,4) NULL,
 [other_charges] decimal(19,4) NULL,
 [source_net_amount] decimal(19,4) NULL,
 [user_discount_details] nvarchar(max) NULL,
 [tax] decimal(19,4) NULL,
 [source_net_value] decimal(19,4) NULL,
 [invoice_ref_no] nvarchar(max) NULL,
 [invoice_ref_date] date NULL,
 [customernumber] nvarchar(max) NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL,
 [ulp_no] nvarchar(max) NULL,
 [eastimestamp] nvarchar(max) NULL,
 [storetimestamp] nvarchar(max) NULL
);

CREATE INDEX IX_etp_r003_file ON dbo.[etp_r003](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r003_locked ON dbo.[etp_r003] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r004] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [trans_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [tolocation] nvarchar(max) NULL,
 [to_region] nvarchar(max) NULL,
 [to_state] nvarchar(max) NULL,
 [to_city] nvarchar(max) NULL,
 [employeeid] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [itemnumber] nvarchar(max) NULL,
 [hsncode] nvarchar(max) NULL,
 [taxableamount] decimal(19,4) NULL,
 [cgst] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [sgst_utgst] decimal(19,4) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [igst] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [docno] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [documentdate] date NULL,
 [qty] decimal(19,4) NULL,
 [ucp] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [purinvno] nvarchar(max) NULL,
 [purinvdate] date NULL,
 [remark] nvarchar(max) NULL,
 [order_type] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r004_file ON dbo.[etp_landing_r004](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r004_locked ON dbo.[etp_landing_r004] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r005] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [trans_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [tolocation] nvarchar(max) NULL,
 [to_region] nvarchar(max) NULL,
 [to_state] nvarchar(max) NULL,
 [to_city] nvarchar(max) NULL,
 [employeeid] nvarchar(max) NULL,
 [docno] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [documentdate] date NULL,
 [qty] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [taxableamount] decimal(19,4) NULL,
 [stmvalue] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [remarks] nvarchar(max) NULL,
 [order_type] nvarchar(max) NULL,
 [couriername] nvarchar(max) NULL,
 [couriernumber] nvarchar(max) NULL,
 [courierdate] date NULL,
 [airwaybillno] nvarchar(max) NULL,
 [dispatchtype] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r005_file ON dbo.[etp_landing_r005](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r005_locked ON dbo.[etp_landing_r005] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r006] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [trans_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [fromlocation] nvarchar(max) NULL,
 [from_region] nvarchar(max) NULL,
 [from_state] nvarchar(max) NULL,
 [from_city] nvarchar(max) NULL,
 [employeeid] nvarchar(max) NULL,
 [docnumber] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [documentdate] date NULL,
 [qty] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [discount] decimal(19,4) NULL,
 [taxableamount] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [refdocnumber] nvarchar(max) NULL,
 [refdocdate] date NULL,
 [remark] nvarchar(max) NULL,
 [order_type] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r006_file ON dbo.[etp_landing_r006](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r006_locked ON dbo.[etp_landing_r006] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r007] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [trans_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [fromlocation] nvarchar(max) NULL,
 [employeeid] nvarchar(max) NULL,
 [itemnumber] nvarchar(max) NULL,
 [hsncode] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [docnumber] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [documentdate] date NULL,
 [from_region] nvarchar(max) NULL,
 [from_state] nvarchar(max) NULL,
 [from_city] nvarchar(max) NULL,
 [qty] decimal(19,4) NULL,
 [ucp_value] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [discount] decimal(19,4) NULL,
 [cgst] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [sgst_utgst] decimal(19,4) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [igst] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [netvalue] decimal(19,4) NULL,
 [refdocdate] date NULL,
 [refdocnumber] nvarchar(max) NULL,
 [purinvno] nvarchar(max) NULL,
 [purinvdate] date NULL,
 [remark] nvarchar(max) NULL,
 [order_type] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r007_file ON dbo.[etp_landing_r007](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r007_locked ON dbo.[etp_landing_r007] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r008] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [storename] nvarchar(max) NULL,
 [bankdepositnumber] nvarchar(max) NULL,
 [transactiondate] date NULL,
 [trans_type] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [invoice_number] nvarchar(max) NULL,
 [invoice_date] date NULL,
 [amount] decimal(19,4) NULL,
 [bankedon] date NULL,
 [bankedamount] decimal(19,4) NULL,
 [unbanked_amount] decimal(19,4) NULL,
 [deposit_slipno] nvarchar(max) NULL,
 [cc_chequeno] nvarchar(max) NULL,
 [deposit_date] date NULL,
 [createdate] date NULL
);

CREATE INDEX IX_etp_r008_file ON dbo.[etp_r008](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r008_locked ON dbo.[etp_r008] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r009] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [transactiondate] date NULL,
 [cash] decimal(19,4) NULL,
 [card] decimal(19,4) NULL,
 [cheque_dd] decimal(19,4) NULL,
 [total] decimal(19,4) NULL,
 [cash_deposited] decimal(19,4) NULL,
 [cc_deposited] decimal(19,4) NULL,
 [cheque_dd_deposited] decimal(19,4) NULL,
 [cash_difference] decimal(19,4) NULL,
 [card_difference] decimal(19,4) NULL,
 [cheque_dd_difference] decimal(19,4) NULL
);

CREATE INDEX IX_etp_r009_file ON dbo.[etp_r009](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r009_locked ON dbo.[etp_r009] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r010] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [itemnumber] nvarchar(max) NULL,
 [hsn_code] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [brandname] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [ean_category] nvarchar(max) NULL,
 [lotnumber] nvarchar(max) NULL,
 [uid] nvarchar(max) NULL,
 [retailbin] decimal(19,4) NULL,
 [servicebin] decimal(19,4) NULL,
 [replacementbin] decimal(19,4) NULL,
 [defectivebin] decimal(19,4) NULL,
 [instibin] decimal(19,4) NULL,
 [ecomm] decimal(19,4) NULL,
 [other1] decimal(19,4) NULL,
 [other2] decimal(19,4) NULL,
 [other3] decimal(19,4) NULL,
 [other4] decimal(19,4) NULL,
 [other5] decimal(19,4) NULL,
 [other6] decimal(19,4) NULL,
 [other7] decimal(19,4) NULL,
 [other8] decimal(19,4) NULL,
 [other9] decimal(19,4) NULL,
 [other10] decimal(19,4) NULL,
 [other11] decimal(19,4) NULL,
 [other12] decimal(19,4) NULL,
 [other13] decimal(19,4) NULL,
 [other14] decimal(19,4) NULL,
 [other15] decimal(19,4) NULL,
 [other16] decimal(19,4) NULL,
 [other17] decimal(19,4) NULL,
 [other18] decimal(19,4) NULL,
 [other19] decimal(19,4) NULL,
 [other20] decimal(19,4) NULL,
 [other21] decimal(19,4) NULL,
 [other22] decimal(19,4) NULL,
 [other23] decimal(19,4) NULL,
 [other24] decimal(19,4) NULL,
 [other25] decimal(19,4) NULL,
 [ucp] decimal(19,4) NULL,
 [closingbalance] decimal(19,4) NULL,
 [totalucp] decimal(19,4) NULL
);

CREATE INDEX IX_etp_r010_file ON dbo.[etp_r010](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r010_locked ON dbo.[etp_r010] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r011] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [snapshot_date] date NULL,
 [product_code] nvarchar(max) NULL,
 [hsn_code] nvarchar(max) NULL,
 [itemdescription] nvarchar(max) NULL,
 [ean] nvarchar(max) NULL,
 [brand_code] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [quantity] decimal(19,4) NULL,
 [unit_cost] decimal(19,4) NULL,
 [total_cost] decimal(19,4) NULL,
 [batch_number] nvarchar(max) NULL,
 [source_uid] nvarchar(max) NULL
);

CREATE INDEX IX_etp_r011_file ON dbo.[etp_r011](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r011_locked ON dbo.[etp_r011] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r012] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [creditnotenumber] nvarchar(max) NULL,
 [creditnotedate] date NULL,
 [ref_grnno] nvarchar(max) NULL,
 [cn_refdocno] nvarchar(max) NULL,
 [expirydate] date NULL,
 [issueto] nvarchar(max) NULL,
 [creditnoteamount] decimal(19,4) NULL,
 [invoicenumber] nvarchar(max) NULL,
 [invoicedate] date NULL,
 [invoicevalue] decimal(19,4) NULL,
 [redeemed_date] date NULL,
 [redeemed_by] nvarchar(max) NULL,
 [issued_cnno] nvarchar(max) NULL,
 [issued_cnamt] decimal(19,4) NULL
);

CREATE INDEX IX_etp_r012_file ON dbo.[etp_r012](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r012_locked ON dbo.[etp_r012] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r013] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [source_transaction_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [product_code] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [brandname] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [cro_number] nvarchar(max) NULL,
 [cro_name] nvarchar(max) NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL,
 [invoice_number] nvarchar(max) NULL,
 [transaction_date] date NULL,
 [source_quantity] decimal(19,4) NULL,
 [ucp] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [scheme_discount] decimal(19,4) NULL,
 [netgross] decimal(19,4) NULL,
 [pre_discount] decimal(19,4) NULL,
 [source_net_amount] decimal(19,4) NULL,
 [source_net_value] decimal(19,4) NULL,
 [invrefno] nvarchar(max) NULL,
 [invrefdate] date NULL
);

CREATE INDEX IX_etp_r013_file ON dbo.[etp_r013](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r013_locked ON dbo.[etp_r013] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r014] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [invoice_date] date NULL,
 [cash] decimal(19,4) NULL,
 [creditcard] decimal(19,4) NULL,
 [cn_utilised] decimal(19,4) NULL,
 [giftcard] decimal(19,4) NULL,
 [cheque] decimal(19,4) NULL,
 [round_off] decimal(19,4) NULL,
 [no_refund] decimal(19,4) NULL,
 [tata_gv] decimal(19,4) NULL,
 [loyalty] decimal(19,4) NULL,
 [cn_issued] decimal(19,4) NULL,
 [cash_refund] decimal(19,4) NULL,
 [tatacliq] decimal(19,4) NULL,
 [gyftr] decimal(19,4) NULL,
 [paytm] decimal(19,4) NULL,
 [heliosomni] decimal(19,4) NULL,
 [paymenttype19] decimal(19,4) NULL,
 [paymenttype20] decimal(19,4) NULL,
 [paymenttype21] decimal(19,4) NULL,
 [paymenttype22] decimal(19,4) NULL,
 [paymenttype23] decimal(19,4) NULL,
 [paymenttype24] decimal(19,4) NULL,
 [paymenttype25] decimal(19,4) NULL,
 [paymenttype26] decimal(19,4) NULL,
 [omni] decimal(19,4) NULL,
 [total_revenue] decimal(19,4) NULL
);

CREATE INDEX IX_etp_r014_file ON dbo.[etp_r014](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r014_locked ON dbo.[etp_r014] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r015] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [encircle_no] nvarchar(max) NULL,
 [encircle_enrol_date] date NULL,
 [invoicenumber] nvarchar(max) NULL,
 [invoicedate] date NULL,
 [invoicequantity] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [sch_discounts] decimal(19,4) NULL,
 [netgross] decimal(19,4) NULL,
 [pre_discounts] decimal(19,4) NULL,
 [netamount] decimal(19,4) NULL,
 [customernumber] nvarchar(max) NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r015_file ON dbo.[etp_landing_r015](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r015_locked ON dbo.[etp_landing_r015] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r016] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [encircle_number] nvarchar(max) NULL,
 [docnumber] nvarchar(max) NULL,
 [customer_name] nvarchar(max) NULL,
 [transaction_date] date NULL,
 [invoice_amount] decimal(19,4) NULL,
 [loyality_points] decimal(19,4) NULL,
 [approval_number] nvarchar(max) NULL,
 [otp_number] nvarchar(max) NULL,
 [refinvoicenumber] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r016_file ON dbo.[etp_landing_r016](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r016_locked ON dbo.[etp_landing_r016] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r017] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [invnumber] nvarchar(max) NULL,
 [invdate] date NULL,
 [qty] decimal(19,4) NULL,
 [totalbillvalue] decimal(19,4) NULL,
 [totalgcamtredeemd] decimal(19,4) NULL,
 [giftcardno] nvarchar(max) NULL,
 [approvalnumber] nvarchar(max) NULL,
 [gccount] decimal(19,4) NULL
);

CREATE INDEX IX_etp_landing_r017_file ON dbo.[etp_landing_r017](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r017_locked ON dbo.[etp_landing_r017] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r018] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [document_number] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [transaction_type] nvarchar(max) NULL,
 [doc_invoice_no] nvarchar(max) NULL,
 [doc_invoice_date] date NULL,
 [invoice_year] int NULL,
 [reference_doc] decimal(19,4) NULL,
 [reference_date] date NULL,
 [issue_state_name] nvarchar(max) NULL,
 [issue_gstn_no] nvarchar(max) NULL,
 [issue_state_code] nvarchar(max) NULL,
 [recipient_state_name] nvarchar(max) NULL,
 [recipient_gstn_no] nvarchar(max) NULL,
 [recipient_state_code] nvarchar(max) NULL,
 [item_number] nvarchar(max) NULL,
 [hsn_code] nvarchar(max) NULL,
 [qty] decimal(19,4) NULL,
 [uom] nvarchar(max) NULL,
 [ucp] decimal(19,4) NULL,
 [gross_ucp] decimal(19,4) NULL,
 [discounts] decimal(19,4) NULL,
 [net_amount] decimal(19,4) NULL,
 [taxable_value] decimal(19,4) NULL,
 [cgst_rate] decimal(19,4) NULL,
 [cgst_amount] decimal(19,4) NULL,
 [sgst_utgst_rate] decimal(19,4) NULL,
 [sgst_utgst_amount] decimal(19,4) NULL,
 [igst_rate] decimal(19,4) NULL,
 [igst_amount] decimal(19,4) NULL,
 [cess_rate] decimal(19,4) NULL,
 [cess_amount] decimal(19,4) NULL,
 [customer_name] nvarchar(max) NULL,
 [customernumber] nvarchar(max) NULL
);

CREATE INDEX IX_etp_r018_file ON dbo.[etp_r018](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r018_locked ON dbo.[etp_r018] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r019] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [transaction_type] nvarchar(max) NULL,
 [doc_invoice_no] nvarchar(max) NULL,
 [doc_invoice_date] date NULL,
 [reference_doc] decimal(19,4) NULL,
 [reference_date] date NULL,
 [issue_state_name] nvarchar(max) NULL,
 [issue_gstn_no] nvarchar(max) NULL,
 [issue_state_code] nvarchar(max) NULL,
 [recipient_state_name] nvarchar(max) NULL,
 [recipient_gstn_no] nvarchar(max) NULL,
 [recipient_state_code] nvarchar(max) NULL,
 [item_number] nvarchar(max) NULL,
 [hsn_code] nvarchar(max) NULL,
 [qty] decimal(19,4) NULL,
 [uom] nvarchar(max) NULL,
 [ucp] decimal(19,4) NULL,
 [gross_ucp] decimal(19,4) NULL,
 [discounts] decimal(19,4) NULL,
 [net_amount] decimal(19,4) NULL,
 [taxable_value] decimal(19,4) NULL,
 [cgst_rate] decimal(19,4) NULL,
 [cgst_amount] decimal(19,4) NULL,
 [sgst_rate] decimal(19,4) NULL,
 [sgst_amount] decimal(19,4) NULL,
 [igst_rate] decimal(19,4) NULL,
 [igst_amount] decimal(19,4) NULL
);

CREATE INDEX IX_etp_r019_file ON dbo.[etp_r019](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r019_locked ON dbo.[etp_r019] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r020] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [channel] nvarchar(max) NULL,
 [type] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [storename] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [invnumber] nvarchar(max) NULL,
 [invdate] date NULL,
 [invoice_amount] decimal(19,4) NULL,
 [doc_type_no] nvarchar(max) NULL,
 [agencyname] nvarchar(max) NULL,
 [creditcardno] nvarchar(max) NULL,
 [approvalnumber] nvarchar(max) NULL,
 [cashamount] decimal(19,4) NULL,
 [cardamount] decimal(19,4) NULL,
 [chequeamount] decimal(19,4) NULL,
 [gvamount] decimal(19,4) NULL,
 [gcamount] decimal(19,4) NULL,
 [creditnote] decimal(19,4) NULL,
 [loyaltypoints] decimal(19,4) NULL,
 [round_off] decimal(19,4) NULL,
 [no_refund] decimal(19,4) NULL,
 [tata_gv] decimal(19,4) NULL,
 [refund] decimal(19,4) NULL,
 [paymenttype15] decimal(19,4) NULL,
 [gyftr] decimal(19,4) NULL,
 [paytm] decimal(19,4) NULL,
 [heliosomni] decimal(19,4) NULL,
 [paymenttype19] decimal(19,4) NULL,
 [paymenttype20] decimal(19,4) NULL,
 [paymenttype21] decimal(19,4) NULL,
 [paymenttype22] decimal(19,4) NULL,
 [paymenttype23] decimal(19,4) NULL,
 [paymenttype24] decimal(19,4) NULL,
 [paymenttype25] decimal(19,4) NULL
);

CREATE INDEX IX_etp_r020_file ON dbo.[etp_r020](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r020_locked ON dbo.[etp_r020] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r021] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [location] nvarchar(max) NULL,
 [from_region] nvarchar(max) NULL,
 [from_state] nvarchar(max) NULL,
 [from_city] nvarchar(max) NULL,
 [doc_number] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [totalqty] decimal(19,4) NULL,
 [totalvalue] decimal(19,4) NULL,
 [discount] decimal(19,4) NULL,
 [netvalue] decimal(19,4) NULL,
 [tax] decimal(19,4) NULL,
 [remark] nvarchar(max) NULL,
 [ref_document_number] nvarchar(max) NULL,
 [ref_document_date] date NULL,
 [physc_recv_date] date NULL,
 [tcschargepercentage] decimal(19,4) NULL,
 [tcsamount] decimal(19,4) NULL,
 [total_final_value] decimal(19,4) NULL
);

CREATE INDEX IX_etp_landing_r021_file ON dbo.[etp_landing_r021](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r021_locked ON dbo.[etp_landing_r021] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r022] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [source_transaction_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [invoice_number] nvarchar(max) NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL,
 [source_invoice_quantity] decimal(19,4) NULL,
 [transaction_date] date NULL,
 [invoice_year] int NULL,
 [tender_cash] decimal(19,4) NULL,
 [tender_card] decimal(19,4) NULL,
 [tender_cheque] decimal(19,4) NULL,
 [tender_loyalty_points] decimal(19,4) NULL,
 [tender_gift_voucher] decimal(19,4) NULL,
 [tender_credit_note_redeemed] decimal(19,4) NULL,
 [tender_excess_gv] decimal(19,4) NULL,
 [tender_round_off] decimal(19,4) NULL,
 [tender_no_refund] decimal(19,4) NULL,
 [tender_others] decimal(19,4) NULL,
 [tender_tata_gv] decimal(19,4) NULL,
 [tender_gift_card] decimal(19,4) NULL,
 [tender_tatacliq] decimal(19,4) NULL,
 [tender_gyftr] decimal(19,4) NULL,
 [tender_paytm] decimal(19,4) NULL,
 [tender_helios_omni] decimal(19,4) NULL,
 [tender_advance_redeem] decimal(19,4) NULL,
 [tender_bhim_upi] decimal(19,4) NULL,
 [tender_phonepe] decimal(19,4) NULL,
 [tender_bharatpe] decimal(19,4) NULL,
 [tender_bajaj_finance] decimal(19,4) NULL,
 [tender_razorpay] decimal(19,4) NULL,
 [tender_payment_type24] decimal(19,4) NULL,
 [tender_payment_type25] decimal(19,4) NULL,
 [tender_issued_credit_note] decimal(19,4) NULL,
 [tender_cash_refund] decimal(19,4) NULL,
 [tender_cheque_rtgs_refund] decimal(19,4) NULL,
 [source_net_value] decimal(19,4) NULL,
 [encircle] nvarchar(max) NULL,
 [source_store_timestamp] nvarchar(max) NULL,
 [reference_invoice_number] nvarchar(max) NULL,
 [referenceyear] int NULL
);

CREATE INDEX IX_etp_r022_file ON dbo.[etp_r022](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r022_locked ON dbo.[etp_r022] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r023] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [businessruleid] nvarchar(max) NULL,
 [businessrulename] nvarchar(max) NULL,
 [versionid] nvarchar(max) NULL,
 [strategy_id] nvarchar(max) NULL,
 [brand_code] nvarchar(max) NULL,
 [brand_name] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [discount_amt] decimal(19,4) NULL,
 [discount_percentage] decimal(19,4) NULL,
 [start_date] date NULL,
 [end_date] date NULL,
 [strategyname] nvarchar(max) NULL,
 [itemnumber] nvarchar(max) NULL,
 [ucp] decimal(19,4) NULL
);

CREATE INDEX IX_etp_landing_r023_file ON dbo.[etp_landing_r023](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r023_locked ON dbo.[etp_landing_r023] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r024] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [trans_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [storename] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [inv_number] nvarchar(max) NULL,
 [inv_date] date NULL,
 [qty] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [sch_discounts] decimal(19,4) NULL,
 [netgross] decimal(19,4) NULL,
 [pre_discounts] decimal(19,4) NULL,
 [netamount] decimal(19,4) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [cess_value] decimal(19,4) NULL,
 [tax] decimal(19,4) NULL,
 [tax_inc] decimal(19,4) NULL,
 [tax_exc] decimal(19,4) NULL,
 [netvalue] decimal(19,4) NULL,
 [invrefno] nvarchar(max) NULL,
 [invrefdate] date NULL,
 [customer_no] nvarchar(max) NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL,
 [ulp_number] nvarchar(max) NULL,
 [customer_gstin_no] nvarchar(max) NULL,
 [customer_address] nvarchar(max) NULL,
 [store_timestamp] nvarchar(max) NULL,
 [eas_timestamp] nvarchar(max) NULL
);

CREATE INDEX IX_etp_r024_file ON dbo.[etp_r024](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r024_locked ON dbo.[etp_r024] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r025] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [source_transaction_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [storename] nvarchar(max) NULL,
 [storetype] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [product_code] nvarchar(max) NULL,
 [hsn_code] nvarchar(max) NULL,
 [source_brand_code] nvarchar(max) NULL,
 [source_brand_name] nvarchar(max) NULL,
 [brand_segment_code] nvarchar(max) NULL,
 [gender_code] nvarchar(max) NULL,
 [invoice_number] nvarchar(max) NULL,
 [transaction_date] date NULL,
 [source_quantity] decimal(19,4) NULL,
 [source_ucp] decimal(19,4) NULL,
 [source_gross_ucp] decimal(19,4) NULL,
 [scheme_discount] decimal(19,4) NULL,
 [user_discount] decimal(19,4) NULL,
 [helios_creditnote] decimal(19,4) NULL,
 [promo_gc] decimal(19,4) NULL,
 [netgross] decimal(19,4) NULL,
 [pre_discount] decimal(19,4) NULL,
 [source_net_amount] decimal(19,4) NULL,
 [sgst_utgst] decimal(19,4) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [csgt] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [igst] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [cess] decimal(19,4) NULL,
 [cess_value] decimal(19,4) NULL,
 [source_tax_amount] decimal(19,4) NULL,
 [source_net_value] decimal(19,4) NULL,
 [reference_invoice_number] nvarchar(max) NULL,
 [reference_invoice_date] date NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL,
 [ulpnumber] nvarchar(max) NULL,
 [source_store_timestamp] nvarchar(max) NULL
);

CREATE INDEX IX_etp_r025_file ON dbo.[etp_r025](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r025_locked ON dbo.[etp_r025] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r026] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [transaction_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [store_sap_code] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [prp_doc_no] nvarchar(max) NULL,
 [prp_doc_date] date NULL,
 [prp_amount] decimal(19,4) NULL,
 [jo_no] nvarchar(max) NULL,
 [jo_date] date NULL,
 [prp_variant_no] nvarchar(max) NULL,
 [prp_scrap_item_code] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r026_file ON dbo.[etp_landing_r026](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r026_locked ON dbo.[etp_landing_r026] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r027] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [warehouse] nvarchar(max) NULL,
 [store_sap_code] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [prp_doc_no] nvarchar(max) NULL,
 [prp_doc_date] date NULL,
 [prp_variant_no] nvarchar(max) NULL,
 [prp_amount] decimal(19,4) NULL,
 [etp_invoice_no] nvarchar(max) NULL,
 [etp_invoice_date] date NULL,
 [prp_amount_redeemed] decimal(19,4) NULL,
 [stock_reciept_doc_no] nvarchar(max) NULL,
 [stock_reciept_doc_date] date NULL,
 [reciept_scrap_item_code] nvarchar(max) NULL,
 [receipt_scrap_item_value] decimal(19,4) NULL,
 [stm_purchase_return_doc_no] nvarchar(max) NULL,
 [stm_purchase_return_doc_date] date NULL,
 [stm_scrap_item_code] nvarchar(max) NULL,
 [stm_scrap_item_value] decimal(19,4) NULL,
 [stm_courier_doc_no] nvarchar(max) NULL,
 [stm_courier_doc_date] date NULL
);

CREATE INDEX IX_etp_landing_r027_file ON dbo.[etp_landing_r027](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r027_locked ON dbo.[etp_landing_r027] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r028] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [store_type] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [itemnumber] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [brandname] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [invnumber] nvarchar(max) NULL,
 [invdate] date NULL,
 [qty] decimal(19,4) NULL,
 [ucp] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [sch_discount] decimal(19,4) NULL,
 [netgross] decimal(19,4) NULL,
 [pre_discount] decimal(19,4) NULL,
 [netamount] decimal(19,4) NULL,
 [hsn_code] nvarchar(max) NULL,
 [tax] decimal(19,4) NULL,
 [csgt] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [sgst_utgst] decimal(19,4) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [igst] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [netvalue] decimal(19,4) NULL,
 [purinvno] nvarchar(max) NULL,
 [purinvdate] date NULL,
 [purprice] decimal(19,4) NULL,
 [invoicereferencenumber] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r028_file ON dbo.[etp_landing_r028](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r028_locked ON dbo.[etp_landing_r028] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r029] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [bankdepositno] nvarchar(max) NULL,
 [invoice_year] int NULL,
 [trans_type] nvarchar(max) NULL,
 [invoicenumber] nvarchar(max) NULL,
 [trans_date] date NULL,
 [bankedamount] decimal(19,4) NULL,
 [bankeddate] date NULL
);

CREATE INDEX IX_etp_r029_file ON dbo.[etp_r029](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r029_locked ON dbo.[etp_r029] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_r030] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [source_transaction_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [product_code] nvarchar(max) NULL,
 [hsn_code] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [brandname] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [document_number] nvarchar(max) NULL,
 [document_date] date NULL,
 [from_location] nvarchar(max) NULL,
 [to_location] nvarchar(max) NULL,
 [ref_documentnumber] nvarchar(max) NULL,
 [ref_documentdate] date NULL,
 [opening_quantity] decimal(19,4) NULL,
 [transaction_quantity] decimal(19,4) NULL,
 [closing_quantity] decimal(19,4) NULL,
 [city] nvarchar(max) NULL,
 [state] nvarchar(max) NULL,
 [location] nvarchar(max) NULL
);

CREATE INDEX IX_etp_r030_file ON dbo.[etp_r030](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_r030_locked ON dbo.[etp_r030] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_r031] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [trans_type] nvarchar(max) NULL,
 [store_code] nvarchar(max) NULL,
 [storename] nvarchar(max) NULL,
 [storetype] nvarchar(max) NULL,
 [channel] nvarchar(max) NULL,
 [region] nvarchar(max) NULL,
 [city] nvarchar(max) NULL,
 [itemnumber] nvarchar(max) NULL,
 [hsncode] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [brandname] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [invnumber] nvarchar(max) NULL,
 [invdate] date NULL,
 [qty] decimal(19,4) NULL,
 [ucp] decimal(19,4) NULL,
 [grossucp] decimal(19,4) NULL,
 [sch_discounts] decimal(19,4) NULL,
 [netgross] decimal(19,4) NULL,
 [pre_discounts] decimal(19,4) NULL,
 [netamount] decimal(19,4) NULL,
 [sgst_utgst] decimal(19,4) NULL,
 [sgst_utgst_value] decimal(19,4) NULL,
 [csgt] decimal(19,4) NULL,
 [csgt_value] decimal(19,4) NULL,
 [igst] decimal(19,4) NULL,
 [igst_value] decimal(19,4) NULL,
 [tax] decimal(19,4) NULL,
 [netvalue] decimal(19,4) NULL,
 [invrefno] nvarchar(max) NULL,
 [invrefdate] date NULL,
 [customer_name] nvarchar(max) NULL,
 [customer_phone] nvarchar(max) NULL,
 [ulpnumber] nvarchar(max) NULL,
 [storetimestamp] nvarchar(max) NULL,
 [etpadvorder] nvarchar(max) NULL,
 [etpadvdate] date NULL,
 [ordertype] nvarchar(max) NULL,
 [ocshipmentno] nvarchar(max) NULL
);

CREATE INDEX IX_etp_landing_r031_file ON dbo.[etp_landing_r031](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_r031_locked ON dbo.[etp_landing_r031] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

CREATE TABLE dbo.[etp_landing_sor_ageing] (
 etp_row_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
 content_key varchar(80) NOT NULL,
 [store_code] nvarchar(max) NULL,
 [store_name] nvarchar(max) NULL,
 [itemnumber] nvarchar(max) NULL,
 [hsn_code] nvarchar(max) NULL,
 [brand] nvarchar(max) NULL,
 [brandname] nvarchar(max) NULL,
 [cluster] nvarchar(max) NULL,
 [gender] nvarchar(max) NULL,
 [gr_number] nvarchar(max) NULL,
 [purchaseinvno] nvarchar(max) NULL,
 [purchaseinvdt] date NULL,
 [available_stk] decimal(19,4) NULL,
 [numberofdays] decimal(19,4) NULL
);

CREATE INDEX IX_etp_landing_sor_ageing_file ON dbo.[etp_landing_sor_ageing](import_file_id);

EXEC(N'CREATE TRIGGER dbo.trg_etp_landing_sor_ageing_locked ON dbo.[etp_landing_sor_ageing] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');

COMMIT TRANSACTION;
