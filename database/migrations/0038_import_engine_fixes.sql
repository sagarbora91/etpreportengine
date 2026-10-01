-- Import engine fixes for release 1.9.3 (CONSOLIDATED-AND-RAW-IMPORT-SPEC.md section 5.1).
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT.
-- The pre-checks THROW before anything changes; every later statement is idempotent
-- (COL_LENGTH/OBJECT_ID guards, CREATE OR ALTER). A statement that uses a column added earlier in
-- this script, and every CREATE PROCEDURE, VIEW or TRIGGER, runs through EXEC(N'...').
-- New error numbers are 51700-51799.
-- Each section changes only what lies between its own begin and end markers.
SET XACT_ABORT ON;

-- >>> PRECHECK_FY begin
-- <<< PRECHECK_FY end

-- >>> PRECHECK_CONTROLS_TENDERS begin
-- <<< PRECHECK_CONTROLS_TENDERS end

-- >>> A_DIAGNOSTICS begin
-- <<< A_DIAGNOSTICS end

-- >>> B_EVIDENCE begin
-- <<< B_EVIDENCE end

-- >>> C_STOCK_MOVEMENT begin
-- <<< C_STOCK_MOVEMENT end

-- >>> C2_CONTROL_TENDER begin
-- <<< C2_CONTROL_TENDER end

-- >>> D_SNAPSHOT begin
-- <<< D_SNAPSHOT end

-- >>> E_ENRICHMENT begin
-- <<< E_ENRICHMENT end

-- >>> G_RESTATEMENT begin
-- <<< G_RESTATEMENT end

