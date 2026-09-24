-- A handoff and SMTP acceptance are observable outcomes, never delivery claims.
ALTER TABLE dbo.share_attempts DROP CONSTRAINT CK_share_attempts_outcome;
ALTER TABLE dbo.share_attempts ADD CONSTRAINT CK_share_attempts_outcome
    CHECK(outcome IN('INITIATED','SUCCEEDED','FAILED','CANCELLED','HANDOFF_READY','SMTP_ACCEPTED','UNKNOWN'));
ALTER TABLE dbo.share_attempts ADD attempt_key uniqueidentifier NULL;
CREATE INDEX IX_share_attempts_attempt ON dbo.share_attempts(attempt_key,share_attempt_id);
GRANT SELECT,INSERT ON dbo.share_attempts TO etp_viewer;
GRANT INSERT ON dbo.report_packages TO etp_viewer;
-- Windows Task Scheduler owns cadence; this unused database value was misleading.
DECLARE @constraint sysname;
SELECT @constraint = dc.name FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
WHERE dc.parent_object_id=OBJECT_ID(N'dbo.watch_folder_settings') AND c.name=N'poll_minutes';
IF @constraint IS NOT NULL
BEGIN
    DECLARE @dropDefault nvarchar(max)=N'ALTER TABLE dbo.watch_folder_settings DROP CONSTRAINT '+QUOTENAME(@constraint);
    EXEC(@dropDefault);
END;
DECLARE @checks nvarchar(max)=N'';
SELECT @checks=@checks+N'ALTER TABLE dbo.watch_folder_settings DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.watch_folder_settings') AND definition LIKE N'%poll_minutes%';
IF LEN(@checks)>0 EXEC(@checks);
ALTER TABLE dbo.watch_folder_settings DROP COLUMN poll_minutes;
