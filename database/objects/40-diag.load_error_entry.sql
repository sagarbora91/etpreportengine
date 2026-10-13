CREATE OR ALTER PROCEDURE diag.load_error_entry @error_register_id int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51801,'Only an Owner can open an error register entry.',1;
 SELECT e.error_register_id,e.fingerprint,e.fingerprint_version,e.event_code,e.sub_code,e.source,e.site,e.exception_type,e.sql_number,
   e.max_severity,e.status,e.first_seen_utc,e.last_seen_utc,e.occurrence_count,e.first_version,e.last_version,e.fixed_in_version,
   e.fixed_ticket,e.fixed_utc,e.recurred_utc,e.recurred_version,e.latest_stack,e.status_reason,e.status_changed_utc,
   e.status_changed_by,e.merged_into_id
 FROM diag.error_register e WHERE e.error_register_id=@error_register_id;
 SELECT v.app_version,v.occurrence_count,v.first_seen_utc,v.last_seen_utc
 FROM diag.error_register_versions v WHERE v.error_register_id=@error_register_id
 ORDER BY v.app_version_key DESC,v.app_version DESC;
 SELECT TOP(20) o.error_occurrence_id,o.occurred_utc,o.reference,o.app_version,o.severity,o.report_code,o.store_code,
   o.business_date,o.date_from,o.operation,o.reporter_role,o.ingested_utc
 FROM diag.error_occurrences o WHERE o.error_register_id=@error_register_id
 ORDER BY o.occurred_utc DESC,o.error_occurrence_id DESC;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.load_error_entry TO etp_owner;
