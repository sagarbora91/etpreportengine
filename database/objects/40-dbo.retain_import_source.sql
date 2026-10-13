CREATE OR ALTER PROCEDURE dbo.retain_import_source
 @hash char(64),@size bigint,@content varbinary(max),@file bigint,@state varchar(16)=NULL OUTPUT
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51420,'Owner or Store Manager permission is required.',1;
 IF @@TRANCOUNT=0 THROW 51422,'Import writes require an enclosing transaction.',1;
 IF @hash IS NULL OR LEN(@hash)<>64 OR @hash COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9a-f]%'
  THROW 51750,'The source file hash must be 64 lowercase hexadecimal characters.',1;
 IF @content IS NULL OR @size IS NULL OR DATALENGTH(@content)<>@size
    OR HASHBYTES('SHA2_256',@content)<>CONVERT(binary(32),@hash,2)
  THROW 51751,'The source file bytes do not match their hash and size.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files WHERE import_file_id=@file AND source_sha256=@hash)
  THROW 51752,'Source files are kept only for an import of the same file.',1;
 -- No HOLDLOCK: a key-range probe for a new hash would lock the gap until the import commits and
  -- serialise imports for other stores. The primary key still stops a second copy of the same bytes.
 INSERT dbo.import_source_content(source_sha256,size_bytes,content,first_import_file_id)
 SELECT @hash,@size,@content,@file
 WHERE NOT EXISTS(SELECT 1 FROM dbo.import_source_content WHERE source_sha256=@hash);
 SET @state=CASE WHEN @@ROWCOUNT=1 THEN 'RETAINED' ELSE 'ALREADY_HELD' END;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.retain_import_source TO etp_viewer;
GRANT EXECUTE ON dbo.retain_import_source TO etp_owner,etp_store_manager;
