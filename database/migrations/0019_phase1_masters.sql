SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.tender_modes
(
 source_tender_code nvarchar(80) NOT NULL CONSTRAINT PK_tender_modes PRIMARY KEY,
 agency_name nvarchar(200) NULL,
 mode varchar(30) NOT NULL,
 active bit NOT NULL CONSTRAINT DF_tender_modes_active DEFAULT(1),
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_tender_modes_modified DEFAULT SYSUTCDATETIME(),
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_tender_modes_actor DEFAULT ORIGINAL_LOGIN(),
 CONSTRAINT CK_tender_modes_mode CHECK(mode IN('Cash','Card','UPI','CN','TC','Service Cash','Service Card','Service UPI'))
);
INSERT dbo.tender_modes(source_tender_code,agency_name,mode) VALUES
 (N'CASH',NULL,'Cash'),(N'CARD',N'CC01, CC02, CC05, CC08, CC10','Card'),
 (N'CHEQUE',NULL,'TC'),(N'LOYALTY_POINTS',NULL,'TC'),(N'GV',NULL,'TC'),
 (N'CREDITNOTE_REDEEM',N'WLMHW, HEMW','CN'),(N'EXCESS_GV',NULL,'TC'),
 (N'ROUND_OFF',NULL,'Cash'),(N'NO_REFUND',NULL,'Cash'),(N'OTHERS',NULL,'TC'),
 (N'TATA_GV',NULL,'TC'),(N'GIFTCARD',N'GIFT CARD','TC'),(N'TATACLIQ',NULL,'TC'),
 (N'GYFTR',NULL,'TC'),(N'PAYTM',N'PAYTM','UPI'),(N'HELIOSOMNI',NULL,'TC'),
 (N'ADVANCERDEEM',NULL,'TC'),(N'BHIMUPI',NULL,'UPI'),(N'PHONEPE',N'PHONEPE','UPI'),
 (N'BHARATPE',NULL,'UPI'),(N'BAJAJFIN',NULL,'TC'),(N'RAZORPAY',NULL,'UPI'),
 (N'PAYMENTTYPE24',NULL,'TC'),(N'PAYMENTTYPE25',N'AIRPAY','UPI'),
 (N'ISSUED_CREDITNOTE',N'WLMHW, HEMW','CN'),(N'CASH_REFUND',NULL,'Cash'),(N'CHEQUE_RTGS_REFUND',NULL,'TC'),
 (N'PAYMENTTYPE15',NULL,'TC'),(N'PAYMENTTYPE19',NULL,'TC'),(N'PAYMENTTYPE20',N'PHONEPE','UPI'),
 (N'PAYMENTTYPE21',NULL,'UPI'),(N'PAYMENTTYPE22',NULL,'TC'),(N'PAYMENTTYPE23',NULL,'UPI'),
 (N'SERVICE_CASH',NULL,'Service Cash'),(N'SERVICE_CARD',NULL,'Service Card'),(N'SERVICE_UPI',NULL,'Service UPI');

CREATE TABLE dbo.staff
(
 store_code varchar(30) NOT NULL,
 staff_code nvarchar(80) NOT NULL,
 staff_name nvarchar(200) NOT NULL,
 active bit NOT NULL CONSTRAINT DF_staff_active DEFAULT(1),
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_staff_modified DEFAULT SYSUTCDATETIME(),
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_staff_actor DEFAULT ORIGINAL_LOGIN(),
 CONSTRAINT PK_staff PRIMARY KEY(store_code,staff_code),
 CONSTRAINT CK_staff_code CHECK(LEN(LTRIM(RTRIM(staff_code)))>0),
 CONSTRAINT CK_staff_name CHECK(LEN(LTRIM(RTRIM(staff_name)))>0)
);
INSERT dbo.staff(store_code,staff_code,staff_name)
 SELECT store_code,source_cro_number,COALESCE(MAX(NULLIF(staff_name,'')),source_cro_number)
 FROM dbo.sales_line_enrichments WHERE enrichment_type='R013' AND NULLIF(source_cro_number,'') IS NOT NULL
 GROUP BY store_code,source_cro_number;

INSERT dbo.manual_input_definitions(field_code,display_name,value_kind,is_required_for_finalisation,applies_to) VALUES
 ('SERVICE_WDC',N'Service WDC','Money',0,'Service'),
 ('WCC_WALKIN',N'WCC walk-ins','Quantity',0,'Service'),
 ('WCC_SALES',N'WCC sales','Money',0,'Service'),
 ('WDC_BILLS',N'WDC bills','Quantity',0,'Service');

CREATE TABLE dbo.monthly_targets
(
 store_code varchar(30) NOT NULL,
 target_month date NOT NULL,
 target_sales decimal(19,4) NOT NULL,
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_monthly_targets_modified DEFAULT SYSUTCDATETIME(),
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_monthly_targets_actor DEFAULT ORIGINAL_LOGIN(),
 CONSTRAINT PK_monthly_targets PRIMARY KEY(store_code,target_month),
 CONSTRAINT CK_monthly_targets_month CHECK(DAY(target_month)=1),
 CONSTRAINT CK_monthly_targets_amount CHECK(target_sales>=0)
);
;WITH latest AS
(
 SELECT store_code,DATEFROMPARTS(YEAR(business_date),MONTH(business_date),1) target_month,numeric_value,
 ROW_NUMBER() OVER(PARTITION BY store_code,YEAR(business_date),MONTH(business_date) ORDER BY business_date DESC) rn
 FROM dbo.manual_operational_inputs WHERE field_code='SALES_TARGET' AND numeric_value>=0
)
INSERT dbo.monthly_targets(store_code,target_month,target_sales)
 SELECT store_code,target_month,numeric_value FROM latest WHERE rn=1;
UPDATE dbo.manual_input_definitions SET is_active=0 WHERE field_code='SALES_TARGET';

ALTER TABLE dbo.staff_sales_targets ADD target_month AS DATEFROMPARTS(YEAR(period_start),MONTH(period_start),1) PERSISTED;
IF EXISTS(SELECT 1 FROM dbo.staff_sales_targets GROUP BY store_code,cro_number,YEAR(period_start),MONTH(period_start) HAVING COUNT(*)>1)
 THROW 51260,'More than one staff target starts in the same month. Resolve the overlapping targets before updating.',1;
CREATE UNIQUE INDEX UQ_staff_sales_targets_month ON dbo.staff_sales_targets(store_code,cro_number,target_month);

EXEC(N'CREATE OR ALTER VIEW dbo.reporting_sales_tenders AS
 SELECT sales_tender_id,sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id
 FROM dbo.sales_tenders WHERE is_reporting_eligible=1');

COMMIT TRANSACTION;
