-- Customer data by role at the table level (1.11.2, lane L1 "PII permissions", 12 Oct 2026; AUDIT-2 S-01, decision 37).
--
-- 0022 grants SELECT ON SCHEMA::dbo to etp_viewer and etp_store_manager. 0056/0057 give the customer name by role in the
-- Service views, but the schema grant still let a Viewer (or a Store Manager, who may see names only) read every landing
-- table, the etp_r0xx name tables, dbo.sharing_contacts and dbo.application_users directly, for example from Excel with
-- Windows sign-in. This migration closes that at the table level:
--
--   1. etp_viewer: DENY SELECT on every dbo.etp_landing_* table (52 at 0058). Nothing in ETP reads a landing table with
--      ad hoc SQL; the views and procedures that read them are owned by dbo like the tables, so the ownership chain
--      skips the table check and every view keeps working for a Viewer (0056/0057 already hide the name there).
--   2. etp_viewer: column-level DENY SELECT on the customer columns of the dbo.etp_r0xx tables (name, phone, number,
--      address, GSTIN, e-mail, contact). The other columns stay readable.
--   3. etp_store_manager: column-level DENY SELECT on the customer contact columns (mobile, phone, e-mail, contact
--      number, address) of the landing and etp_r0xx tables. Names stay readable (decision 37). A PhonePe tender amount
--      (phonepe, tender_phonepe) is not a phone number and is not denied.
--   4. etp_viewer: DENY SELECT on dbo.sharing_contacts and dbo.application_user_history.
--   5. dbo.application_users: a row filter (row-level security). A member of etp_viewer that is not also an Owner, a
--      Store Manager or db_owner sees only its own row (windows_identity = SUSER_SNAME()), which is exactly the row
--      Phase2OperationsRepository.LoadCurrentAccessAsync reads to find the caller's role. A plain DENY would stop a Viewer
--      from signing in. Every other caller (Owner, Store Manager, automation, db_owner, diag_catalog_reader inside
--      diag.load_role_permissions) sees every row, as before.
--   6. dbo.v_invoice_customer_names: the R024 invoice customer name by 0056's role rule, for the invoice summary
--      (OperationalReportRepository.LoadInvoiceSummaryAsync reads dbo.etp_r024.customer_name with ad hoc SQL, which a
--      Viewer can no longer do; lane L5 switches it to this view).
--
-- Sections 1-3 are generated from the catalog when this migration runs, by the column name patterns below, so every
-- table that exists at 0058 is covered. A later migration that adds a landing or etp_r0xx table must deny it too:
-- CustomerDataPermissionSqlTests scans sys.columns with the same patterns and fails until it does.
--
-- Accepted risk (AUDIT-2 S-17, recorded here on Sagar's earlier acceptance, Sept audit 1.10): the Owner's login is
-- db_owner and holds server ALTER ANY LOGIN, so the Owner's day-to-day session has no SQL safety net. Kept as is. Longer
-- term: Owner = etp_owner only, with login management through a signed procedure.
--
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT and no GO.
-- 0001-0058 are not edited (checksummed). ExpectedRolePermissions.json lists the new DENY rows.
SET XACT_ABORT ON;

-- >>> L1_PII_PERMISSIONS begin
DECLARE @pii TABLE(table_name sysname NOT NULL,column_name sysname NOT NULL,is_landing bit NOT NULL,is_contact bit NOT NULL,
  PRIMARY KEY(table_name,column_name));

-- The customer columns of the landing and etp_r0xx tables. Contact = a way to reach the customer; the rest identifies them.
INSERT @pii(table_name,column_name,is_landing,is_contact)
SELECT t.name,c.name,CASE WHEN t.name LIKE N'etp[_]landing[_]%' THEN 1 ELSE 0 END,
  CASE WHEN LOWER(c.name) LIKE N'%mobile%' OR (LOWER(c.name) LIKE N'%phone%' AND LOWER(c.name) NOT LIKE N'%phonepe%')
         OR LOWER(c.name) LIKE N'%mail%' OR LOWER(c.name) LIKE N'%contact%' OR LOWER(c.name) LIKE N'%address%'
         OR LOWER(c.name) LIKE N'%landline%' THEN 1 ELSE 0 END
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
WHERE t.schema_id=SCHEMA_ID(N'dbo') AND (t.name LIKE N'etp[_]landing[_]%' OR t.name LIKE N'etp[_]r[0-9]%')
  AND (LOWER(c.name) LIKE N'%mobile%' OR (LOWER(c.name) LIKE N'%phone%' AND LOWER(c.name) NOT LIKE N'%phonepe%')
    OR LOWER(c.name) LIKE N'%mail%' OR LOWER(c.name) LIKE N'%contact%' OR LOWER(c.name) LIKE N'%address%'
    OR LOWER(c.name) LIKE N'%landline%' OR LOWER(c.name) LIKE N'%customer%' OR LOWER(c.name) LIKE N'%cust[_]name%'
    OR LOWER(c.name) LIKE N'%custname%' OR LOWER(c.name) LIKE N'%gstin%');

DECLARE @deny nvarchar(max);

-- 1. Viewer: no landing table at all.
SELECT @deny=STRING_AGG(CONVERT(nvarchar(max),N'DENY SELECT ON dbo.'+QUOTENAME(t.name)+N' TO etp_viewer;'),NCHAR(10))
  WITHIN GROUP(ORDER BY t.name)
FROM sys.tables t WHERE t.schema_id=SCHEMA_ID(N'dbo') AND t.name LIKE N'etp[_]landing[_]%';
IF @deny IS NOT NULL EXEC(@deny);

-- 2. Viewer: the customer columns of the etp_r0xx tables.
SET @deny=NULL;
SELECT @deny=STRING_AGG(CONVERT(nvarchar(max),s.statement),NCHAR(10)) WITHIN GROUP(ORDER BY s.table_name)
FROM (SELECT p.table_name,N'DENY SELECT ('+STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(p.column_name)),N',')
        WITHIN GROUP(ORDER BY p.column_name)+N') ON dbo.'+QUOTENAME(p.table_name)+N' TO etp_viewer;' statement
      FROM @pii p WHERE p.is_landing=0 GROUP BY p.table_name) s;
IF @deny IS NOT NULL EXEC(@deny);

-- 3. Store Manager: the customer contact columns of the landing and etp_r0xx tables.
SET @deny=NULL;
SELECT @deny=STRING_AGG(CONVERT(nvarchar(max),s.statement),NCHAR(10)) WITHIN GROUP(ORDER BY s.table_name)
FROM (SELECT p.table_name,N'DENY SELECT ('+STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(p.column_name)),N',')
        WITHIN GROUP(ORDER BY p.column_name)+N') ON dbo.'+QUOTENAME(p.table_name)+N' TO etp_store_manager;' statement
      FROM @pii p WHERE p.is_contact=1 GROUP BY p.table_name) s;
IF @deny IS NOT NULL EXEC(@deny);

-- 4. Viewer: the sharing contacts (e-mail and phone of the people packs go to) and the user history.
DENY SELECT ON dbo.sharing_contacts TO etp_viewer;
DENY SELECT ON dbo.application_user_history TO etp_viewer;

-- 5. dbo.application_users: a Viewer sees its own row only.
IF OBJECT_ID(N'dbo.application_users_visible_row',N'IF') IS NULL
 EXEC(N'CREATE FUNCTION dbo.application_users_visible_row(@windows_identity nvarchar(200))
RETURNS TABLE WITH SCHEMABINDING AS
RETURN SELECT 1 AS is_visible
WHERE COALESCE(IS_MEMBER(N''etp_viewer''),0)=0
   OR COALESCE(IS_MEMBER(N''db_owner''),0)=1
   OR COALESCE(IS_MEMBER(N''etp_owner''),0)=1
   OR COALESCE(IS_MEMBER(N''etp_store_manager''),0)=1
   OR @windows_identity=SUSER_SNAME()');
IF NOT EXISTS(SELECT 1 FROM sys.security_policies WHERE name=N'application_users_viewer_filter' AND schema_id=SCHEMA_ID(N'dbo'))
 EXEC(N'CREATE SECURITY POLICY dbo.application_users_viewer_filter
ADD FILTER PREDICATE dbo.application_users_visible_row(windows_identity) ON dbo.application_users
WITH (STATE=ON,SCHEMABINDING=ON)');

-- 6. The R024 invoice customer name by role, for the invoice summary (lane L5).
EXEC(N'CREATE OR ALTER VIEW dbo.v_invoice_customer_names AS
SELECT d.etp_row_id,d.import_file_id,d.store_code,d.inv_number,d.inv_date,
  CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN d.customer_name END customer_name
FROM dbo.etp_r024 d');
GRANT SELECT ON dbo.v_invoice_customer_names TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_invoice_customer_names TO etp_store_manager,etp_viewer;
-- <<< L1_PII_PERMISSIONS end
