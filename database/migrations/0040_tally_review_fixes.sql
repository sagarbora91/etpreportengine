-- Phase 7 fixes found by review of 0038 and 0039 (those files are never edited).
-- 1. The finding and difference guards treated a zero-row UPDATE (accepting a failure, or a warning twice) as a delete,
--    so the caller saw 51573 "cannot be deleted" instead of its own 51579. They now return when nothing changed.
-- 2. A voucher of a decided batch could go from BLOCKED back to PLANNED by a status-only update, after its reservation
--    had been released. That is refused now (51212, like every other change to a decided voucher).
-- 3. A read-back whose own period does not contain the dates entered for it is recorded incomplete (PERIOD_MISMATCH).

EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_accounting_validation_findings_guard ON dbo.accounting_validation_findings AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted)
 BEGIN
  IF EXISTS(SELECT 1 FROM deleted) THROW 51573,''Validation findings cannot be deleted.'',1;
  RETURN;
 END;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.finding_id=d.finding_id
  WHERE d.waived_by IS NOT NULL OR i.waived_by IS NULL
   OR i.accounting_batch_id<>d.accounting_batch_id OR ISNULL(i.accounting_voucher_id,-1)<>ISNULL(d.accounting_voucher_id,-1)
   OR i.rule_id<>d.rule_id OR i.rule_version<>d.rule_version OR i.severity<>d.severity
   OR ISNULL(i.subject,N'''')<>ISNULL(d.subject,N'''') OR ISNULL(i.observed,N'''')<>ISNULL(d.observed,N'''') OR ISNULL(i.expected,N'''')<>ISNULL(d.expected,N'''')
   OR i.explanation<>d.explanation OR ISNULL(i.corrective_action,N'''')<>ISNULL(d.corrective_action,N'''') OR i.created_utc<>d.created_utc)
  THROW 51573,''A validation finding can only be accepted, and only once.'',1;
END');

EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_tally_reconciliation_differences_guard ON dbo.tally_reconciliation_differences AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted)
 BEGIN
  IF EXISTS(SELECT 1 FROM deleted) THROW 51573,''Reconciliation differences cannot be deleted.'',1;
  RETURN;
 END;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.difference_id=d.difference_id
  WHERE d.accepted_by IS NOT NULL OR i.accepted_by IS NULL
   OR i.run_id<>d.run_id OR ISNULL(i.accounting_voucher_id,-1)<>ISNULL(d.accounting_voucher_id,-1) OR ISNULL(i.tally_actual_id,-1)<>ISNULL(d.tally_actual_id,-1)
   OR i.check_level<>d.check_level OR i.difference_type<>d.difference_type OR i.severity<>d.severity
   OR ISNULL(i.value_a,N'''')<>ISNULL(d.value_a,N'''') OR ISNULL(i.value_b,N'''')<>ISNULL(d.value_b,N'''') OR ISNULL(i.value_c,N'''')<>ISNULL(d.value_c,N'''')
   OR ISNULL(i.delta,0)<>ISNULL(d.delta,0) OR (i.delta IS NULL AND d.delta IS NOT NULL) OR (i.delta IS NOT NULL AND d.delta IS NULL)
   OR i.rule_id<>d.rule_id OR i.rule_version<>d.rule_version OR i.match_rationale<>d.match_rationale
   OR i.evidence_reference<>d.evidence_reference OR i.required_action<>d.required_action)
  THROW 51573,''A reconciliation difference can only be accepted, and only once.'',1;
END');

EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_accounting_vouchers_approved ON dbo.accounting_vouchers AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted)
  AND NOT(UPDATE(accounting_batch_id) OR UPDATE(voucher_sequence) OR UPDATE(component_role) OR UPDATE(voucher_type)
   OR UPDATE(store_code) OR UPDATE(invoice_year) OR UPDATE(document_number) OR UPDATE(revision) OR UPDATE(sales_invoice_id)
   OR UPDATE(voucher_date) OR UPDATE(expected_total) OR UPDATE(correspondence_key) OR UPDATE(source_sha256) OR UPDATE(plan_sha256))
 BEGIN
  -- Only the status or its reason changed. In a decided batch a voucher never goes back to PLANNED, BLOCKED or EXCLUDED,
  -- and a BLOCKED or EXCLUDED voucher stays so: its reservation is already released, so it must never become sendable.
  IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.accounting_voucher_id=i.accounting_voucher_id
   JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id
   WHERE b.status NOT IN(''DRAFT'',''BLOCKED'') AND i.voucher_status<>d.voucher_status
    AND (i.voucher_status IN(''PLANNED'',''BLOCKED'',''EXCLUDED'') OR d.voucher_status IN(''BLOCKED'',''EXCLUDED'')))
   THROW 51212,''A voucher of a decided batch cannot go back to planned, blocked or excluded.'',1;
  RETURN;
 END;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id WHERE b.status NOT IN(''DRAFT'',''BLOCKED''))
 OR EXISTS(SELECT 1 FROM deleted d JOIN dbo.accounting_batches b ON b.accounting_batch_id=d.accounting_batch_id WHERE b.status NOT IN(''DRAFT'',''BLOCKED''))
  THROW 51212,''Decided accounting vouchers are immutable.'',1;
END');

EXEC(N'ALTER TABLE dbo.tally_readbacks DROP CONSTRAINT CK_tally_readbacks_reason;
ALTER TABLE dbo.tally_readbacks WITH CHECK ADD CONSTRAINT CK_tally_readbacks_reason
 CHECK(incomplete_reason IN(''WRONG_COMPANY'',''COMPANY_NOT_REPORTED'',''COMPANY_NOT_OPEN'',''TALLY_ERROR'',''TRUNCATED'',''NOT_XML'',''PERIOD_MISMATCH''));');
