-- Import engine fixes for release 1.9.3 (CONSOLIDATED-AND-RAW-IMPORT-SPEC.md section 5.1).
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT.
-- The pre-checks THROW before anything changes; every later statement is idempotent
-- (COL_LENGTH/OBJECT_ID guards, CREATE OR ALTER). A statement that uses a column added earlier in
-- this script, and every CREATE PROCEDURE, VIEW or TRIGGER, runs through EXEC(N'...').
-- New error numbers are 51700-51799.
-- Each section changes only what lies between its own begin and end markers.
SET XACT_ABORT ON;

-- >>> PRECHECK_FY begin
-- <<< PRECHECK_FY end

-- >>> PRECHECK_CONTROLS_TENDERS begin
-- <<< PRECHECK_CONTROLS_TENDERS end

-- >>> A_DIAGNOSTICS begin
-- <<< A_DIAGNOSTICS end

-- >>> B_EVIDENCE begin
-- B. Evidence inside the database (IF-023, Owner decision OD-2). The import transaction stores the
-- bytes the reader parsed, once per SHA-256. A file's evidence is present when this table holds its own
-- import_files.source_sha256, so no link table is needed. Viewers and store managers cannot read the
-- bytes; v_import_source_evidence shows every role what is held, without them.
IF OBJECT_ID(N'dbo.import_source_content',N'U') IS NULL
CREATE TABLE dbo.import_source_content(
 source_sha256 char(64) NOT NULL CONSTRAINT PK_import_source_content PRIMARY KEY,
 size_bytes bigint NOT NULL,
 content varbinary(max) NOT NULL,
 first_import_file_id bigint NULL CONSTRAINT FK_import_source_content_file REFERENCES dbo.import_files(import_file_id),
 retained_utc datetime2(3) NOT NULL CONSTRAINT DF_import_source_content_utc DEFAULT SYSUTCDATETIME(),
 retained_by nvarchar(200) NOT NULL CONSTRAINT DF_import_source_content_by DEFAULT SUSER_SNAME(),
 CONSTRAINT CK_import_source_content_hash CHECK(source_sha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
 CONSTRAINT CK_import_source_content_size CHECK(size_bytes>=0 AND DATALENGTH(content)=size_bytes)
);
DENY SELECT ON dbo.import_source_content TO etp_viewer,etp_store_manager;
DENY INSERT,UPDATE,DELETE ON dbo.import_source_content TO etp_viewer,etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER VIEW dbo.v_import_source_evidence AS
SELECT source_sha256,size_bytes,first_import_file_id,retained_utc FROM dbo.import_source_content');
GRANT SELECT ON dbo.v_import_source_evidence TO etp_viewer,etp_store_manager,etp_owner;

-- @state is RETAINED when this call stored the bytes and ALREADY_HELD when they were held already.
-- The bytes must hash to @hash and belong to an imported file, so nothing else can be stored here.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.retain_import_source
 @hash char(64),@size bigint,@content varbinary(max),@file bigint,@state varchar(16)=NULL OUTPUT
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF @hash IS NULL OR LEN(@hash)<>64 OR @hash COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9a-f]%''
  THROW 51750,''The source file hash must be 64 lowercase hexadecimal characters.'',1;
 IF @content IS NULL OR @size IS NULL OR DATALENGTH(@content)<>@size
    OR HASHBYTES(''SHA2_256'',@content)<>CONVERT(binary(32),@hash,2)
  THROW 51751,''The source file bytes do not match their hash and size.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files WHERE import_file_id=@file AND source_sha256=@hash)
  THROW 51752,''Source files are kept only for an import of the same file.'',1;
 IF EXISTS(SELECT 1 FROM dbo.import_source_content WITH(UPDLOCK,HOLDLOCK) WHERE source_sha256=@hash)
  SET @state=''ALREADY_HELD'';
 ELSE
 BEGIN
  INSERT dbo.import_source_content(source_sha256,size_bytes,content,first_import_file_id)
  VALUES(@hash,@size,@content,@file);
  SET @state=''RETAINED'';
 END
END');
GRANT EXECUTE ON dbo.retain_import_source TO etp_store_manager,etp_owner;
DENY EXECUTE ON dbo.retain_import_source TO etp_viewer;
-- <<< B_EVIDENCE end

-- >>> C_STOCK_MOVEMENT begin
-- <<< C_STOCK_MOVEMENT end

-- >>> C2_CONTROL_TENDER begin
-- <<< C2_CONTROL_TENDER end

-- >>> D_SNAPSHOT begin
-- <<< D_SNAPSHOT end

-- >>> E_ENRICHMENT begin
-- <<< E_ENRICHMENT end

-- >>> G_RESTATEMENT begin
-- <<< G_RESTATEMENT end

