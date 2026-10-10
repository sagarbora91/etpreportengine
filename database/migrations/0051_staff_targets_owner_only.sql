-- 1.10.0. Staff targets are Owner-only (decision 27: Sagar, 10 Oct 2026, extends D22).
--
-- 1.9.9 made the screen and SqlServerDailyWorkflowService.SaveStaffTargetAsync Owner-only, but
-- 0022 still grants INSERT,UPDATE ON dbo.staff_sales_targets to etp_store_manager, so a Store
-- Manager could write a target outside ETP. This migration takes that grant back and blocks
-- writes in the same way 0022 blocks dbo.approval_requests and dbo.controlled_adjustments.
--
-- Who still writes. The Owner is a member of db_owner and etp_owner, never of etp_store_manager
-- or etp_viewer (dbo.configure_application_role gives each account exactly one ETP role), so
-- neither the REVOKE nor the DENY reaches the Owner. ETP writes staff targets with one MERGE
-- in OperationalCompletionRepository.SaveStaffTargetAsync (no stored procedure). The history
-- rows are written by trg_staff_sales_targets_audit_lock (0010), which is owned by dbo like
-- dbo.staff_sales_target_history, so the ownership chain covers that INSERT and nothing
-- changes on the history table. The automation account (etp_automation, also a member of
-- etp_store_manager on Workpc) runs backup and the recovery drill only; it never writes staff
-- targets. Reading is unchanged: SELECT comes from the schema grant to both roles.
--
-- Checked read-only on the live database (Workpc, 10 Oct 2026, at 0048): the only permissions on
-- dbo.staff_sales_targets were GRANT INSERT and GRANT UPDATE to etp_store_manager; none on
-- dbo.staff_sales_target_history; no DELETE was ever granted; the only module that names the
-- table is the 0010 trigger.
--
-- 0022 is untouched (the migration runner is checksum fail-closed).

SET XACT_ABORT ON;

REVOKE INSERT,UPDATE ON dbo.staff_sales_targets FROM etp_store_manager;
DENY INSERT,UPDATE,DELETE ON dbo.staff_sales_targets TO etp_store_manager,etp_viewer;
