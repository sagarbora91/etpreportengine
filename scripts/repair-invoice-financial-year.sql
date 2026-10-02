-- Repair for migration 0041's pre-check 51700 (OD-1, IF-019): every invoice is keyed by the financial year of its
-- own date. Before release 1.9.3, R022 keyed an invoice by ETP's INVOICEYEAR label, which for a document dated
-- 1 April is the year before, while R025 keyed the same document by the financial year of its date. Such an invoice
-- either stands alone under the other year, or sits beside a second header (its "twin") that R025 keyed by the
-- financial year. The unique invoice key UQ_sales_invoices_natural(store_code,invoice_year,document_number) forbids
-- re-keying it onto the twin, so for each invoice keyed by another year this script decides one action:
--
--   RE_KEY   no twin: sets invoice_year to the financial year of the invoice date.
--   COMBINE  a twin of the same date: moves the invoice's lines, revenue control and tenders onto the twin (their
--            ids, values and lineage are kept) and deletes the emptied header. An R022 header normally holds the
--            control and tenders and its R025 twin the lines, so nothing is lost or counted twice.
--   MANUAL   anything the script must not decide. It changes nothing for that invoice and says why (below).
--
-- Procedure. Changing the live database is the Owner's step: run it as a member of db_owner, with ETP closed so that
-- no import runs (step 5 holds dbo.sales_invoices exclusively until it commits).
--   1. Back up the database and keep the .bak until the upgrade is proven.
--   2. Run scripts/check-import-upgrade.sql. Check 3 (INVOICE_YEAR_NOT_FINANCIAL_YEAR) lists every invoice keyed by
--      another year; check 18 (INVOICE_YEAR_TWIN_HEADERS) lists the ones that have a twin, with what each header holds.
--   3. Run this script as it is (@apply = 0). It changes nothing and returns the plan: one row per invoice, its action
--      and, for MANUAL, the reason.
--   4. Resolve the MANUAL rows (below). Until every one is resolved, 0041 still refuses with 51700.
--   5. Set @apply = 1 and run it again. It repairs the RE_KEY and COMBINE rows in one transaction, returns the plan
--      and the number of invoices still keyed by another year. Any error rolls the whole run back.
--   6. Run scripts/check-import-upgrade.sql again: checks 3 and 18 must be 0. Then install 1.9.3.
-- The script can be run again at any time; an invoice keyed by its financial year is never touched.
--
-- MANUAL reasons, and what each needs:
--   LOCKED_DAY         the store-day of the invoice or of its twin is finalised. Reopen the day in ETP, run step 5,
--                      then finalise the day again.
--   ACCOUNTING         an active accounting batch reserves the invoice, or a Tally voucher refers to it, under either
--                      year. Reject the unexported batch in Accounting and run again. A voucher already in Tally
--                      keeps its key there: decide with the accountant before changing it.
--   TWIN_DATE_DIFFERS  the header keyed by the financial year has another date, so they may be two documents.
--                      Compare both with the source exports; do not combine them.
--   SAME_KEY_TWICE     two headers keyed by other years would take the same financial-year key. Compare them.
--   LINES_COLLIDE      both headers hold a line with the same line identifier;
--   CONTROLS_COLLIDE   both headers hold a revenue control;
--   TENDERS_COLLIDE    both headers hold the same tender type (case ignored). For these three the script will not
--                      choose between two copies of a fact: compare them with the source export, then restate the
--                      period after the upgrade or ask for a reviewed fix.
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @apply bit = 0; -- 0: preview, changes nothing. 1: repair the RE_KEY and COMBINE invoices.

IF OBJECT_ID(N'tempdb..#fy_plan') IS NOT NULL DROP TABLE #fy_plan;
CREATE TABLE #fy_plan(
 sales_invoice_id bigint NOT NULL PRIMARY KEY,
 store_code varchar(30) COLLATE DATABASE_DEFAULT NOT NULL,
 document_number nvarchar(80) COLLATE DATABASE_DEFAULT NOT NULL,
 transaction_date date NOT NULL,
 invoice_year int NOT NULL,
 financial_year int NOT NULL,
 twin_invoice_id bigint NULL,
 twin_transaction_date date NULL,
 lines int NOT NULL,
 controls int NOT NULL,
 tenders int NOT NULL,
 action varchar(10) COLLATE DATABASE_DEFAULT NULL,
 reason nvarchar(400) COLLATE DATABASE_DEFAULT NULL);

BEGIN TRANSACTION;

DECLARE @held int;
IF @apply = 1
 SELECT @held = COUNT(*) FROM dbo.sales_invoices WITH (TABLOCKX, HOLDLOCK);

INSERT #fy_plan(sales_invoice_id, store_code, document_number, transaction_date, invoice_year, financial_year,
  twin_invoice_id, twin_transaction_date, lines, controls, tenders)
SELECT i.sales_invoice_id, i.store_code, i.document_number, i.transaction_date, i.invoice_year,
  YEAR(i.transaction_date) + CASE WHEN MONTH(i.transaction_date) >= 4 THEN 1 ELSE 0 END,
  t.sales_invoice_id, t.transaction_date,
  (SELECT COUNT(*) FROM dbo.sales_lines x WHERE x.sales_invoice_id = i.sales_invoice_id),
  (SELECT COUNT(*) FROM dbo.sales_invoice_controls x WHERE x.sales_invoice_id = i.sales_invoice_id),
  (SELECT COUNT(*) FROM dbo.sales_tenders x WHERE x.sales_invoice_id = i.sales_invoice_id)
FROM dbo.sales_invoices i
LEFT JOIN dbo.sales_invoices t ON t.store_code = i.store_code AND t.document_number = i.document_number
 AND t.invoice_year = YEAR(i.transaction_date) + CASE WHEN MONTH(i.transaction_date) >= 4 THEN 1 ELSE 0 END
WHERE i.invoice_year <> YEAR(i.transaction_date) + CASE WHEN MONTH(i.transaction_date) >= 4 THEN 1 ELSE 0 END;

-- The first reason found is the one reported.
UPDATE p SET action = 'MANUAL', reason = N'LOCKED_DAY: the store-day of this invoice or of its twin is finalised.'
FROM #fy_plan p
WHERE EXISTS(SELECT 1 FROM dbo.daily_reporting_days d WHERE d.store_code = p.store_code AND d.status = 'LOCKED'
  AND (d.business_date = p.transaction_date OR d.business_date = p.twin_transaction_date));

-- Accounting tables exist from 0033 (batches) and 0038 (vouchers); a database without them has nothing to check.
IF OBJECT_ID(N'dbo.accounting_batch_invoices', N'U') IS NOT NULL
 EXEC sys.sp_executesql N'UPDATE p SET action = ''MANUAL'', reason = N''ACCOUNTING: an active accounting batch reserves this invoice.''
  FROM #fy_plan p
  WHERE p.action IS NULL AND EXISTS(SELECT 1 FROM dbo.accounting_batch_invoices a
    WHERE a.is_active = 1 AND a.store_code = p.store_code AND a.document_number = p.document_number
      AND a.invoice_year IN (p.invoice_year, p.financial_year));';
IF OBJECT_ID(N'dbo.accounting_vouchers', N'U') IS NOT NULL
 EXEC sys.sp_executesql N'UPDATE p SET action = ''MANUAL'', reason = N''ACCOUNTING: a Tally voucher refers to this invoice.''
  FROM #fy_plan p
  WHERE p.action IS NULL AND EXISTS(SELECT 1 FROM dbo.accounting_vouchers v
    WHERE v.sales_invoice_id = p.sales_invoice_id OR v.sales_invoice_id = p.twin_invoice_id
       OR (v.store_code = p.store_code AND v.document_number = p.document_number AND v.invoice_year IN (p.invoice_year, p.financial_year)));';

UPDATE p SET action = 'MANUAL', reason = CONCAT(N'TWIN_DATE_DIFFERS: the header keyed ', p.financial_year, N' is dated ',
  CONVERT(char(10), p.twin_transaction_date, 23), N', this one ', CONVERT(char(10), p.transaction_date, 23), N'.')
FROM #fy_plan p
WHERE p.action IS NULL AND p.twin_invoice_id IS NOT NULL AND p.twin_transaction_date <> p.transaction_date;

UPDATE p SET action = 'MANUAL', reason = N'SAME_KEY_TWICE: another header keyed by another year would take the same financial-year key.'
FROM #fy_plan p
WHERE p.action IS NULL AND p.twin_invoice_id IS NULL AND EXISTS(SELECT 1 FROM #fy_plan q
  WHERE q.sales_invoice_id <> p.sales_invoice_id AND q.store_code = p.store_code
    AND q.document_number = p.document_number AND q.financial_year = p.financial_year);

UPDATE p SET action = 'MANUAL', reason = N'LINES_COLLIDE: both headers hold a line with the same line identifier.'
FROM #fy_plan p
WHERE p.action IS NULL AND EXISTS(SELECT 1 FROM dbo.sales_lines a JOIN dbo.sales_lines b ON b.line_identifier = a.line_identifier
  WHERE a.sales_invoice_id = p.sales_invoice_id AND b.sales_invoice_id = p.twin_invoice_id);

UPDATE p SET action = 'MANUAL', reason = N'CONTROLS_COLLIDE: both headers hold a revenue control.'
FROM #fy_plan p
WHERE p.action IS NULL AND p.controls > 0
  AND EXISTS(SELECT 1 FROM dbo.sales_invoice_controls c WHERE c.sales_invoice_id = p.twin_invoice_id);

UPDATE p SET action = 'MANUAL', reason = N'TENDERS_COLLIDE: both headers hold the same tender type.'
FROM #fy_plan p
WHERE p.action IS NULL AND EXISTS(SELECT 1 FROM dbo.sales_tenders a JOIN dbo.sales_tenders b ON UPPER(b.tender_type) = UPPER(a.tender_type)
  WHERE a.sales_invoice_id = p.sales_invoice_id AND b.sales_invoice_id = p.twin_invoice_id);

UPDATE #fy_plan SET action = CASE WHEN twin_invoice_id IS NULL THEN 'RE_KEY' ELSE 'COMBINE' END WHERE action IS NULL;

IF @apply = 1
BEGIN
 UPDATE i SET invoice_year = p.financial_year
 FROM dbo.sales_invoices i JOIN #fy_plan p ON p.sales_invoice_id = i.sales_invoice_id
 WHERE p.action = 'RE_KEY';

 UPDATE x SET sales_invoice_id = p.twin_invoice_id
 FROM dbo.sales_lines x JOIN #fy_plan p ON p.sales_invoice_id = x.sales_invoice_id
 WHERE p.action = 'COMBINE';
 UPDATE x SET sales_invoice_id = p.twin_invoice_id
 FROM dbo.sales_invoice_controls x JOIN #fy_plan p ON p.sales_invoice_id = x.sales_invoice_id
 WHERE p.action = 'COMBINE';
 UPDATE x SET sales_invoice_id = p.twin_invoice_id
 FROM dbo.sales_tenders x JOIN #fy_plan p ON p.sales_invoice_id = x.sales_invoice_id
 WHERE p.action = 'COMBINE';

 DELETE i
 FROM dbo.sales_invoices i JOIN #fy_plan p ON p.sales_invoice_id = i.sales_invoice_id
 WHERE p.action = 'COMBINE';
END;

-- 1. The plan, one row per invoice keyed by another year when the run started.
SELECT CASE WHEN @apply = 1 AND action <> 'MANUAL' THEN 'REPAIRED' WHEN @apply = 1 THEN 'NOT_CHANGED' ELSE 'PREVIEW' END AS run,
  action, reason, sales_invoice_id, store_code, document_number, transaction_date, invoice_year, financial_year,
  twin_invoice_id, twin_transaction_date, lines, controls, tenders
FROM #fy_plan
ORDER BY CASE action WHEN 'MANUAL' THEN 0 ELSE 1 END, store_code, transaction_date, document_number;

-- 2. What 0041's pre-check 51700 would still find.
SELECT COUNT_BIG(*) AS still_keyed_by_another_year
FROM dbo.sales_invoices
WHERE invoice_year <> YEAR(transaction_date) + CASE WHEN MONTH(transaction_date) >= 4 THEN 1 ELSE 0 END;

IF @apply = 1
 BEGIN COMMIT TRANSACTION; END
ELSE
 BEGIN ROLLBACK TRANSACTION; END;
DROP TABLE #fy_plan;
