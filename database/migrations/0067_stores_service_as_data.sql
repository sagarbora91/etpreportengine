-- 1.12 wave 2, lane A4 (112-W2-PLAN.md section 3 A4, wave plan section 2 A4): stores, brands and the Service Centre are data.
--
-- business_units.unit_kind: RETAIL (the default) or SERVICE. The unit seeded by 0048 (business_unit_code SERVICE) becomes
--    SERVICE; every other unit stays RETAIL. ETP finds its Service Centres by unit_kind, never by a store code.
-- stores.service_job_prefix: the start of a Service Centre's job order numbers (2 to 20 capital letters A-Z or digits). Every
--    store of a SERVICE unit gets JO followed by its store code, which is the live numbering. A Retail store has none.
--
-- From this migration on, no migration inserts stores, business units, brand rows or brand row codes: they are added in
-- Settings (text test ServiceCentreCatalogMigrationTextTests).
--
-- THROW numbers (lane A4 block 51970-51979):
--   51970 a job-number prefix on a store that is not a Service Centre store
--   51971 two Service Centres whose job-number prefixes overlap
--   51972 an active store in a SERVICE unit (0048's 51900 covers the unit coded SERVICE; this covers every SERVICE unit)
--   51973 a business unit that has stores changing between RETAIL and SERVICE
--
-- No grant changes: both tables are read through SELECT ON SCHEMA::dbo (0022) and written by the Owner only.
-- The migration runner applies this script in one transaction, so it has no BEGIN or COMMIT.
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.business_units', N'unit_kind') IS NULL
  ALTER TABLE dbo.business_units ADD unit_kind varchar(10) NOT NULL
    CONSTRAINT DF_business_units_unit_kind DEFAULT('RETAIL');

IF COL_LENGTH(N'dbo.stores', N'service_job_prefix') IS NULL
  ALTER TABLE dbo.stores ADD service_job_prefix varchar(20) NULL;

-- Later statements reference the new columns, so they compile in their own batches.
EXEC(N'IF OBJECT_ID(N''dbo.CK_business_units_unit_kind'',N''C'') IS NULL
  ALTER TABLE dbo.business_units ADD CONSTRAINT CK_business_units_unit_kind CHECK(unit_kind IN(''RETAIL'',''SERVICE''));');

EXEC(N'IF OBJECT_ID(N''dbo.CK_stores_service_job_prefix'',N''C'') IS NULL
  ALTER TABLE dbo.stores ADD CONSTRAINT CK_stores_service_job_prefix CHECK(service_job_prefix IS NULL OR
    (DATALENGTH(service_job_prefix) BETWEEN 2 AND 20 AND service_job_prefix COLLATE Latin1_General_BIN2 NOT LIKE ''%[^A-Z0-9]%''));');

-- The 0048 Service unit is a Service Centre unit.
EXEC(N'UPDATE dbo.business_units SET unit_kind=''SERVICE'' WHERE business_unit_code=''SERVICE'' AND unit_kind<>''SERVICE'';');

-- Each store of a SERVICE unit that has no prefix yet gets JO and its code, when that is a valid prefix. Only the prefix
-- column changes; the store's name, state and audit columns stay as they are.
EXEC(N'UPDATE s SET service_job_prefix=''JO''+s.store_code
FROM dbo.stores s JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
WHERE u.unit_kind=''SERVICE'' AND s.service_job_prefix IS NULL
  AND LEN(s.store_code) BETWEEN 1 AND 18 AND s.store_code COLLATE Latin1_General_BIN2 NOT LIKE ''%[^A-Z0-9]%'';');

-- 51970-51972: a prefix belongs to a Service Centre store only; prefixes never overlap; a store of any SERVICE unit stays
-- inactive (it is not a shop store).
EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_stores_service_job_prefix ON dbo.stores
AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN dbo.business_units u ON u.business_unit_id=i.business_unit_id
   WHERE i.service_job_prefix IS NOT NULL AND COALESCE(u.unit_kind,''RETAIL'')<>''SERVICE'')
  THROW 51970,''A job-number prefix is for a Service Centre store only.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.stores s ON s.store_id<>i.store_id
   WHERE i.service_job_prefix IS NOT NULL AND s.service_job_prefix IS NOT NULL
     AND (LEFT(s.service_job_prefix,LEN(i.service_job_prefix))=i.service_job_prefix
       OR LEFT(i.service_job_prefix,LEN(s.service_job_prefix))=s.service_job_prefix))
  THROW 51971,''Each Service Centre needs its own job-number prefix, and one prefix cannot begin with another.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.business_units u ON u.business_unit_id=i.business_unit_id
   WHERE u.unit_kind=''SERVICE'' AND i.is_active=1)
  THROW 51972,''A Service Centre store is not a shop store and cannot be made active.'',1;
END');

-- 51973: a unit with stores keeps its kind, so a Retail shop never becomes a Service Centre by a unit edit (or back).
EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_business_units_unit_kind ON dbo.business_units
AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.business_unit_id=i.business_unit_id
   WHERE i.unit_kind<>d.unit_kind AND EXISTS(SELECT 1 FROM dbo.stores s WHERE s.business_unit_id=i.business_unit_id))
  THROW 51973,''A business unit that has stores cannot change between Retail and Service.'',1;
END');
