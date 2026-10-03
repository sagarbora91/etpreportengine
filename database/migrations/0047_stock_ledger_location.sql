SET XACT_ABORT ON;

-- 1.9.4, WLMHW report audit FIX-14 (R-WLMHW-14, 3 October 2026): the stock ledger's bin.
--
-- R030 (STOCK_LEDGER) carries LOCATION, the bin a movement belongs to (RETAILBIN, DEFECTIVEBIN, ...). The typed
-- landing table dbo.etp_r030 has stored it since 0018, but dbo.stock_movements never did, so every bin's running
-- balance looked like one chain and the Stock Variance opening could be the opening of a defective-bin chain.
-- This migration stores the bin on the movement. It is NOT part of the movement identity: the unique identity index,
-- the identity text, the content hash and line_seq stay exactly as 0041 made them, so no stored row changes identity
-- and a re-import of an old export is still ALREADY_PRESENT, never a conflict.
--
-- Existing rows: the bin is copied from the typed R030 row of the same source row where one is held (first the
-- movement's own source row, then any R030 row with the same store, document, date, item, type, locations and
-- quantities when all such rows name one bin). A movement no R030 row describes keeps a NULL location until a
-- later ledger export that covers its day is imported: persist_stock_movement then fills a missing bin on the
-- ALREADY_PRESENT row (never on a finalised day, and never replacing a bin already stored).
--
-- The migration runner applies this script in one transaction. Every step is idempotent: the column is added once
-- and the backfill only writes rows whose location is still NULL.

IF COL_LENGTH(N'dbo.stock_movements',N'location') IS NULL
 ALTER TABLE dbo.stock_movements ADD location nvarchar(80) NULL;

IF OBJECT_ID(N'dbo.etp_r030',N'U') IS NOT NULL
BEGIN
 -- The backfill must also work for finalised days (as 0041's line_seq backfill does); the guard is off for these
 -- two statements only, and DDL is transactional, so a failure rolls the trigger state back as well.
 DISABLE TRIGGER dbo.trg_stock_movements_protect_locked ON dbo.stock_movements;

 -- 1. The R030 row of the movement's own source row (same file, sheet and row), when its values agree.
 EXEC(N'UPDATE m SET location=CONVERT(nvarchar(80),LTRIM(RTRIM(r.location)))
 FROM dbo.stock_movements m
 JOIN dbo.source_lineage ml ON ml.source_lineage_id=m.source_lineage_id
 JOIN dbo.source_lineage rl ON rl.import_file_id=ml.import_file_id AND rl.sheet_name=ml.sheet_name
  AND rl.source_row_number=ml.source_row_number AND rl.source_lineage_id<>ml.source_lineage_id
 JOIN dbo.etp_r030 r ON r.source_lineage_id=rl.source_lineage_id
 WHERE m.location IS NULL AND NULLIF(LTRIM(RTRIM(r.location)),N'''') IS NOT NULL
  AND r.store_code=m.store_code AND r.document_number=m.document_number AND r.document_date=m.document_date
  AND r.product_code=m.product_code AND r.source_transaction_type=m.source_transaction_type
  AND ISNULL(r.from_location,N'''')=m.from_key AND ISNULL(r.to_location,N'''')=m.to_key
  AND r.opening_quantity=m.opening_quantity AND r.transaction_quantity=m.transaction_quantity
  AND r.closing_quantity=m.closing_quantity');

 -- 2. Rows a later superset export re-linked to its own source rows, or whose own row is not held: any R030 row
 --    with the same values, when every such row names the same bin. An ambiguous movement stays NULL.
 EXEC(N'WITH k AS
 (
  SELECT CONVERT(nvarchar(30),r.store_code) store_code,CONVERT(nvarchar(80),r.document_number) document_number,
         r.document_date,CONVERT(nvarchar(80),r.product_code) product_code,
         CONVERT(nvarchar(80),r.source_transaction_type) source_transaction_type,
         CONVERT(nvarchar(80),ISNULL(r.from_location,N'''')) from_key,CONVERT(nvarchar(80),ISNULL(r.to_location,N'''')) to_key,
         r.opening_quantity,r.transaction_quantity,r.closing_quantity,CONVERT(nvarchar(80),LTRIM(RTRIM(r.location))) location
  FROM dbo.etp_r030 r
  WHERE NULLIF(LTRIM(RTRIM(r.location)),N'''') IS NOT NULL
 ),
 bins AS
 (
  SELECT store_code,document_number,document_date,product_code,source_transaction_type,from_key,to_key,
         opening_quantity,transaction_quantity,closing_quantity,MIN(location) low_location,MAX(location) high_location
  FROM k
  GROUP BY store_code,document_number,document_date,product_code,source_transaction_type,from_key,to_key,
           opening_quantity,transaction_quantity,closing_quantity
 )
 UPDATE m SET location=b.low_location
 FROM dbo.stock_movements m
 JOIN bins b ON b.store_code=m.store_code AND b.document_number=m.document_number AND b.document_date=m.document_date
  AND b.product_code=m.product_code AND b.source_transaction_type=m.source_transaction_type
  AND b.from_key=m.from_key AND b.to_key=m.to_key AND b.opening_quantity=m.opening_quantity
  AND b.transaction_quantity=m.transaction_quantity AND b.closing_quantity=m.closing_quantity
 WHERE m.location IS NULL AND b.low_location=b.high_location');

 ENABLE TRIGGER dbo.trg_stock_movements_protect_locked ON dbo.stock_movements;
END

-- persist_stock_movement as 0041 section C left it, plus @location (last, default NULL, so a caller that does not
-- pass it behaves as before). The identity lookup, identity text, content hash, outcomes and conflicts are
-- unchanged. A NEW row stores the bin; an identical stored row with no bin gets this one unless its day is
-- finalised. A blank bin is stored as NULL. The grants of 0022 stay with the procedure (CREATE OR ALTER).
EXEC(N'CREATE OR ALTER PROCEDURE dbo.persist_stock_movement
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@product nvarchar(80),@type nvarchar(80),@from nvarchar(80)=NULL,@to nvarchar(80)=NULL,@opening decimal(19,4),@transaction decimal(19,4),@closing decimal(19,4),@lineage bigint,@line_seq int=1,@location nvarchar(80)=NULL
AS
BEGIN
 SET NOCOUNT ON; DECLARE @existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SET @location=NULLIF(LTRIM(RTRIM(@location)),N'''');
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/'',@date,N''/'',@product,N''/'',UPPER(@type),N''/'',ISNULL(@from,N''''),N''/'',ISNULL(@to,N''''),N''/#'',@line_seq);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@opening,N''|'',@transaction,N''|'',@closing)),2));
 SELECT TOP(1) @existing=stock_movement_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(opening_quantity,N''|'',transaction_quantity,N''|'',closing_quantity)),2)) FROM dbo.stock_movements WHERE store_code=@store AND invoice_year=@year AND document_number=@doc AND document_date=@date AND product_code=@product AND source_transaction_type=@type AND from_key=ISNULL(@from,N'''') AND to_key=ISNULL(@to,N'''') AND line_seq=@line_seq ORDER BY stock_movement_id;
 IF @existing IS NOT NULL
 BEGIN
  IF @current=@incoming
  BEGIN
   IF @location IS NOT NULL
    UPDATE dbo.stock_movements SET location=@location WHERE stock_movement_id=@existing AND location IS NULL
     AND NOT EXISTS(SELECT 1 FROM dbo.daily_reporting_days d WHERE d.store_code=@store AND d.business_date=@date AND d.status=''LOCKED'');
   INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical stock movement already exists.'');
  END
  ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Stock movement identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''STOCK_LEDGER'',@identity,@current,@incoming,N''Stock movement values differ. Review and request a controlled restatement.''); END
  RETURN;
 END
 INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,from_location,to_location,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id,line_seq,location) VALUES(@store,@doc,@year,@date,@product,@type,@from,@to,@opening,@transaction,@closing,@lineage,@line_seq,@location);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New stock movement imported.'');
END');
