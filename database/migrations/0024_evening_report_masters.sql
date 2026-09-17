SET XACT_ABORT ON;
BEGIN TRANSACTION;
-- 0021-0023 are reserved for the separately audited security branch.
ALTER TABLE dbo.tender_modes DROP CONSTRAINT CK_tender_modes_mode;
ALTER TABLE dbo.tender_modes ADD CONSTRAINT CK_tender_modes_mode CHECK(mode IN('Cash','Card','UPI','CN','TC','Gift Card','Bank','Service Cash','Service Card','Service UPI'));
UPDATE dbo.tender_modes SET mode='Gift Card',modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME() WHERE source_tender_code='GIFTCARD';
UPDATE dbo.tender_modes SET mode='Bank',modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME() WHERE source_tender_code IN('CHEQUE','CHEQUE_RTGS_REFUND');

CREATE TABLE dbo.brand_rows(
 brand_row_id int IDENTITY PRIMARY KEY,
 store_code varchar(30) NOT NULL,
 row_label nvarchar(100) NOT NULL,
 sort_order int NOT NULL DEFAULT 0,
 modified_by nvarchar(100) NOT NULL DEFAULT ORIGINAL_LOGIN(),
 modified_utc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT UQ_brand_rows UNIQUE(store_code,row_label),
 CONSTRAINT CK_brand_rows_label CHECK(LEN(LTRIM(RTRIM(row_label)))>0));
CREATE TABLE dbo.brand_row_codes(
 store_code varchar(30) NOT NULL,
 source_brand nvarchar(100) NOT NULL,
 brand_row_id int NOT NULL REFERENCES dbo.brand_rows(brand_row_id),
 CONSTRAINT PK_brand_row_codes PRIMARY KEY(store_code,source_brand));
INSERT dbo.brand_rows(store_code,row_label,sort_order) VALUES
 ('WLMHW','NEBULA',10),('WLMHW','EDGE',20),('WLMHW','XYLYS',30),('WLMHW','AUTOMATIC',40),('WLMHW','RAGA',50),
 ('HEMW','SEIKO',10),('HEMW','CITIZEN',20),('HEMW','CERRUTI',30);
-- Exact labels only. Ambiguous source codes remain visible under Other / unmapped
-- until the Owner approves their assignment in Settings.
INSERT dbo.brand_row_codes(store_code,source_brand,brand_row_id) SELECT store_code,row_label,brand_row_id FROM dbo.brand_rows;
COMMIT TRANSACTION;
