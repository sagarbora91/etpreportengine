CREATE OR ALTER PROCEDURE dbo.record_recovery_drill_result
 @outcome varchar(20), @backup_sha256 varchar(128), @row_counts_status varchar(20), @not_recorded_reason varchar(40)=NULL,
 @sales_invoices_receipt bigint=NULL, @sales_invoices_restored bigint=NULL,
 @sales_lines_receipt bigint=NULL, @sales_lines_restored bigint=NULL,
 @import_files_receipt bigint=NULL, @import_files_restored bigint=NULL,
 @daily_reporting_days_receipt bigint=NULL, @daily_reporting_days_restored bigint=NULL
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_automation'),0)<>1
    AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51341,'The dedicated operations account or an Owner is required.',1;
 IF @backup_sha256 IS NULL OR DATALENGTH(@backup_sha256)<>64
    OR @backup_sha256 COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9A-Fa-f]%'
  THROW 51342,'A complete backup verification hash is required.',1;
 -- The table's constraints decide which combinations are valid; this turns a refusal into
 -- one message instead of a constraint name.
 BEGIN TRY
  INSERT dbo.recovery_drill_results(outcome,backup_sha256,row_counts_status,not_recorded_reason,
    sales_invoices_receipt,sales_invoices_restored,sales_lines_receipt,sales_lines_restored,
    import_files_receipt,import_files_restored,daily_reporting_days_receipt,daily_reporting_days_restored,recorded_by)
  VALUES(@outcome,UPPER(@backup_sha256),@row_counts_status,@not_recorded_reason,
    @sales_invoices_receipt,@sales_invoices_restored,@sales_lines_receipt,@sales_lines_restored,
    @import_files_receipt,@import_files_restored,@daily_reporting_days_receipt,@daily_reporting_days_restored,SUSER_SNAME());
 END TRY
 BEGIN CATCH
  IF ERROR_NUMBER()=547 THROW 51344,'The recovery drill result is not consistent.',1;
  THROW;
 END CATCH;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.record_recovery_drill_result TO etp_automation,etp_owner;
