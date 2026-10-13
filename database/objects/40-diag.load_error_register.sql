CREATE OR ALTER PROCEDURE diag.load_error_register @status varchar(10)=NULL,@reference char(12)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 SELECT e.error_register_id,e.fingerprint,e.fingerprint_version,e.event_code,e.sub_code,e.source,e.site,e.exception_type,e.sql_number,
   e.max_severity,e.status,e.first_seen_utc,e.last_seen_utc,e.occurrence_count,e.first_version,e.last_version,e.fixed_in_version,
   e.fixed_ticket,e.fixed_utc,e.recurred_utc,e.recurred_version,e.latest_stack,e.status_reason,e.status_changed_utc,
   e.status_changed_by,e.merged_into_id
 FROM diag.error_register e
 WHERE e.merged_into_id IS NULL
   AND (@status IS NULL OR e.status=@status)
   AND (@reference IS NULL OR EXISTS(SELECT 1 FROM diag.error_occurrences o
        WHERE o.error_register_id=e.error_register_id
          AND o.reference COLLATE Latin1_General_100_CI_AS=RTRIM(@reference) COLLATE Latin1_General_100_CI_AS))
 ORDER BY e.last_seen_utc DESC,e.error_register_id DESC;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.load_error_register TO etp_automation,etp_owner;
