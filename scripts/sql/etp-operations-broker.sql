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
 IF @file IS NULL OR @file COLLATE Latin1_General_100_BIN2 LIKE N'%[^-A-Za-z0-9_.]%'
  OR CHARINDEX(N'..',@file)>0 OR RIGHT(@file,4)<>N'.bak' OR LEFT(@file,LEN(N'__DATABASE_LITERAL__')+1)<>N'__DATABASE_LITERAL__-'
  THROW 51330,'Choose a backup belonging to the configured database.',1;
 DECLARE @path nvarchar(520)=N'__BACKUP_DIRECTORY__\'+@file;
 DECLARE @sql nvarchar(max),@drill sysname=N'EtpRecovery_'+REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N'');
 IF @operation='BACKUP'
 BEGIN
  IF CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Express%' OR CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Web%'
   THROW 51320,'Native encrypted backups require a supported SQL Server edition.',1;
  IF EXISTS(SELECT 1 FROM sys.dm_os_file_exists(@path) WHERE file_exists=1) THROW 51330,'An existing backup cannot be overwritten.',1;
  SET @sql=N'BACKUP DATABASE [__DATABASE_IDENTIFIER__] TO DISK=N'''+REPLACE(@path,'''','''''')+N''' WITH COPY_ONLY,CHECKSUM,ENCRYPTION(ALGORITHM=AES_256,SERVER CERTIFICATE=EtpBackupCert);';
  EXEC sys.sp_executesql @sql;
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
  SELECT @moves=STRING_AGG(CONVERT(nvarchar(max),N'MOVE N'''+REPLACE(LogicalName,'''','''''')+N''' TO N''__RESTORE_DIRECTORY__\'+@drill+N'_'+CONVERT(nvarchar(20),FileId)+CASE Type WHEN 'L' THEN N'.ldf' ELSE N'.mdf' END+N''''),N',') FROM #files;
  BEGIN TRY
   SET @sql=N'RESTORE DATABASE '+QUOTENAME(@drill)+N' FROM DISK=N'''+REPLACE(@path,'''','''''')+N''' WITH '+@moves+N',RECOVERY;';
   EXEC sys.sp_executesql @sql;
   -- Always disable trust and cross-database ownership chaining on the restored copy.
   SET @sql=N'ALTER DATABASE '+QUOTENAME(@drill)+N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ALTER DATABASE '+QUOTENAME(@drill)+N' SET TRUSTWORTHY OFF; ALTER DATABASE '+QUOTENAME(@drill)+N' SET DB_CHAINING OFF; DBCC CHECKDB ('+QUOTENAME(@drill)+N') WITH NO_INFOMSGS;';
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
