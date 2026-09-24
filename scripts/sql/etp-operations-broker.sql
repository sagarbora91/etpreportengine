-- Installed in master by a SQL administrator. Substitutions are fixed, escaped
-- installation values, never values from an unattended request.
CREATE OR ALTER PROCEDURE dbo.[__PROCEDURE__] @operation varchar(12), @file nvarchar(260)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
 BEGIN
  DECLARE @allowed bit=0;
  EXEC sys.sp_executesql N'USE [__DATABASE_IDENTIFIER__];
   SELECT @allowed=CASE WHEN EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=SUSER_SNAME() AND is_active=1
    AND ((role_code=''OWNER'' AND IS_ROLEMEMBER(''etp_owner'')=1) OR (role_code=''STORE_MANAGER'' AND IS_ROLEMEMBER(''etp_store_manager'')=1))) THEN 1 ELSE 0 END;',
   N'@allowed bit OUTPUT',@allowed OUTPUT;
  IF @allowed<>1 THROW 51333,'The configured application role is inactive or unavailable.',1;
 END;
 IF @operation NOT IN ('BACKUP','METADATA','DRILL') OR @operation IS NULL THROW 51330,'Unknown operation.',1;
 -- P4-15, decided: the recovery drill runs as the Owner. It restores a full copy and checks
 -- every page with DBCC CHECKDB, which SQL Server allows only a sysadmin or the copy's own
 -- dbo to do - and a login that restores someone else's backup becomes its server-level
 -- owner without ever becoming the dbo inside it, and cannot fix that. No signature changes
 -- it. Backups and metadata checks stay available to the least-privileged automation
 -- account; the drill says plainly who has to run it instead of failing halfway through.
 IF @operation='DRILL' AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51334,'The recovery drill restores and integrity-checks a complete copy of the database, which only a SQL administrator can do. Run it as the Owner.',1;
 IF @file IS NULL OR @file COLLATE Latin1_General_100_BIN2 LIKE N'%[^-A-Za-z0-9_.]%'
  OR CHARINDEX(N'..',@file)>0 OR RIGHT(@file,4)<>N'.bak' OR LEFT(@file,LEN(N'__DATABASE_LITERAL__')+1)<>N'__DATABASE_LITERAL__-'
  THROW 51330,'Choose a backup belonging to the configured database.',1;
 DECLARE @path nvarchar(520)=N'__BACKUP_DIRECTORY__\'+@file;
 -- Both folders are held as values here and escaped again wherever they go into dynamic
 -- SQL. Substituting a folder straight into the dynamic statement escaped it for this
 -- procedure's literal only, so a folder with an apostrophe broke the drill's restore.
 DECLARE @restoreDirectory nvarchar(260)=N'__RESTORE_DIRECTORY__';
 DECLARE @sql nvarchar(max),@drill sysname=N'EtpRecovery_'+REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N'');
 IF @operation='BACKUP'
 BEGIN
  -- D9 revised: Express and Web cannot encrypt a backup at all, so on those editions
  -- the backup is taken unencrypted and the receipt says so. Protecting the backup
  -- folder at rest is a deferred control, not something this procedure can claim.
  DECLARE @encrypted bit = CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Express%'
                                  OR CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Web%'
                             THEN 0 ELSE 1 END;
  IF EXISTS(SELECT 1 FROM sys.dm_os_file_exists(@path) WHERE file_exists=1) THROW 51330,'An existing backup cannot be overwritten.',1;
  IF @encrypted=1
   SET @sql=N'BACKUP DATABASE [__DATABASE_IDENTIFIER__] TO DISK=N'''+REPLACE(@path,'''','''''')+N''' WITH COPY_ONLY,CHECKSUM,ENCRYPTION(ALGORITHM=AES_256,SERVER CERTIFICATE=EtpBackupCert);';
  ELSE
   SET @sql=N'BACKUP DATABASE [__DATABASE_IDENTIFIER__] TO DISK=N'''+REPLACE(@path,'''','''''')+N''' WITH COPY_ONLY,CHECKSUM;';
  EXEC sys.sp_executesql @sql;
  PRINT 'ETP_ENCRYPTION:'+CASE WHEN @encrypted=1 THEN 'AES_256' ELSE 'NONE' END;
 END;
 SET @sql=N'RESTORE VERIFYONLY FROM DISK=N'''+REPLACE(@path,'''','''''')+N''' WITH CHECKSUM;';
 EXEC sys.sp_executesql @sql;
 CREATE TABLE #files(
  LogicalName nvarchar(128),PhysicalName nvarchar(260),Type char(1),FileGroupName nvarchar(128),Size numeric(20,0),MaxSize numeric(20,0),FileId bigint,
  CreateLSN numeric(25,0),DropLSN numeric(25,0),UniqueId uniqueidentifier,ReadOnlyLSN numeric(25,0),ReadWriteLSN numeric(25,0),BackupSizeInBytes bigint,
  SourceBlockSize int,FileGroupId int,LogGroupGUID uniqueidentifier,DifferentialBaseLSN numeric(25,0),DifferentialBaseGUID uniqueidentifier,
  IsReadOnly bit,IsPresent bit,TDEThumbprint varbinary(32),SnapshotURL nvarchar(360));
 SET @sql=N'RESTORE FILELISTONLY FROM DISK=N'''+REPLACE(@path,'''','''''')+N''';';
 INSERT #files EXEC sys.sp_executesql @sql;
 IF EXISTS(SELECT 1 FROM #files WHERE Type NOT IN ('D','L') OR IsPresent<>1) THROW 51330,'Unsupported backup file layout.',1;
 IF NOT EXISTS(SELECT 1 FROM #files WHERE Type='D') OR NOT EXISTS(SELECT 1 FROM #files WHERE Type='L') THROW 51330,'Backup data and log files are required.',1;
 IF @operation='DRILL'
 BEGIN
  DECLARE @moves nvarchar(max);
  SELECT @moves=STRING_AGG(CONVERT(nvarchar(max),N'MOVE N'''+REPLACE(LogicalName,'''','''''')+N''' TO N'''+REPLACE(@restoreDirectory+N'\'+@drill+N'_'+CONVERT(nvarchar(20),FileId)+CASE Type WHEN 'L' THEN N'.ldf' ELSE N'.mdf' END,'''','''''')+N''''),N',') FROM #files;
  BEGIN TRY
   SET @sql=N'RESTORE DATABASE '+QUOTENAME(@drill)+N' FROM DISK=N'''+REPLACE(@path,'''','''''')+N''' WITH '+@moves+N',RECOVERY;';
   EXEC sys.sp_executesql @sql;
   -- P4-15. A restored copy must never trust cross-database access: a backup that carried
   -- TRUSTWORTHY or DB_CHAINING in could otherwise reach outside its own database. Both
   -- options used to be SET here, but only sysadmin may set either one, and this runs as
   -- the automation account - so the drill had never worked for anyone but a SQL
   -- administrator. RESTORE already leaves TRUSTWORTHY off. Verify both instead, and
   -- refuse the backup outright if either one came in on.
   IF EXISTS(SELECT 1 FROM sys.databases WHERE name=@drill AND (is_trustworthy_on=1 OR is_db_chaining_on=1))
    THROW 51331,'The restored copy trusts cross-database access. This backup is refused.',1;
   SET @sql=N'ALTER DATABASE '+QUOTENAME(@drill)+N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DBCC CHECKDB ('+QUOTENAME(@drill)+N') WITH NO_INFOMSGS;';
   EXEC sys.sp_executesql @sql;
   SET @sql=N'IF EXISTS(SELECT FileId,LogicalName FROM #files EXCEPT SELECT file_id,name FROM '+QUOTENAME(@drill)+N'.sys.database_files) OR EXISTS(SELECT file_id,name FROM '+QUOTENAME(@drill)+N'.sys.database_files EXCEPT SELECT FileId,LogicalName FROM #files) THROW 51331,''Restored file metadata differs from its backup.'',1;';
   EXEC sys.sp_executesql @sql;
   SET @sql=N'ALTER DATABASE '+QUOTENAME(@drill)+N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE '+QUOTENAME(@drill)+N';';
   EXEC sys.sp_executesql @sql;
  END TRY
  BEGIN CATCH
   IF DB_ID(@drill) IS NOT NULL
   BEGIN
    SET @sql=N'ALTER DATABASE '+QUOTENAME(@drill)+N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE '+QUOTENAME(@drill)+N';';
    EXEC sys.sp_executesql @sql;
   END;
   THROW;
  END CATCH;
 END;
 SELECT N'ETP_METADATA:'+ (SELECT LogicalName logicalName,Type type,FileId fileId,Size sizeBytes,UniqueId uniqueId FROM #files ORDER BY FileId FOR JSON PATH);
END;
