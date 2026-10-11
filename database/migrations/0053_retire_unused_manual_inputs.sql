-- 1.10.1. Retire the unread Stock manual inputs (decision 32, Sagar, 11 Oct 2026; audit RA-OPS-13).
--
-- DISPLAY_STOCK, BACKSTOCK, DEFECTIVE_STOCK, Y_LOCATION_STOCK and PHYSICAL_STOCK were seeded by 0005 but
-- no report, check or import reads them (stock is counted on the Physical count screen instead).
-- They are hidden from entry by is_active=0; the rows and any old values stay, because
-- manual_operational_inputs has a foreign key to this table and history must remain readable.
-- Entry screens and the write path only offer definitions with is_active=1 (DailyReportingWorkflowRepository).
-- The daily-inputs fields (WALK_INS, OPERATIONAL_REMARK, cash fields, SERVICE_*, WCC_*, WDC_BILLS) are untouched.

SET XACT_ABORT ON;

UPDATE dbo.manual_input_definitions
SET is_active=0
WHERE field_code IN ('DISPLAY_STOCK','BACKSTOCK','DEFECTIVE_STOCK','Y_LOCATION_STOCK','PHYSICAL_STOCK')
  AND applies_to='Stock' AND is_active=1;
