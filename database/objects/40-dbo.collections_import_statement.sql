-- 1.12 wave 1, lane D2 (112-WAVE-PLAN D-b; migration 0063; master plan 8a tasks 3-4). The only way a bank or provider
-- statement file reaches the database. SqlServerCollectionsImportService reads the file with the Owner's layout, computes
-- each line's identity (Etp.Reporting.Import.Collections.LineIdentity) and passes the lines here as JSON; this procedure
-- decides, in one transaction under an application lock per account / provider:
--   ALREADY_IMPORTED     the same file (SHA-256) was imported before: nothing is written.
--   DUPLICATE_CONTENT    every readable line is already present for the account / provider: nothing is written.
--   POSSIBLE_DUPLICATES  @overlap_choice STOP and a new line has the date, amount and direction of a current line with another
--                        identity (an export that changed a reference, or two lines the bank really booked): nothing is
--                        written; the Owner or Store Manager chooses NEW (keep both) or RESTATE (this file replaces the
--                        earlier ones for its dates), with a reason.
--   IMPORTED             the file, its lineage and row outcomes are recorded and only the lines with a new identity are added
--                        (existing line ids survive). An earlier current file whose every line is in this one is marked
--                        superseded ('Later statement export'); with RESTATE every current file overlapping this file's dates
--                        is superseded and a line in those dates missing from this file stops being current.
-- import_files: report_code BANK_STATEMENT / PROVIDER_STATEMENT, store_code NULL, business_date NULL, the line dates as the
-- period. Its lock trigger joins on store_code, so a statement covering a finalised day imports and supersedes.
-- Row outcomes: business_identity = '<report>:<account id or provider key>:<identity>' and content_sha256 = the line identity,
-- so the supersede test sees every line a file contained (also those already present when it came).
-- Each line JSON: row, identity, error (a safe parse message, never a cell value) and the mapped fields; raw is an object.
-- THROW numbers: lane block 51950-51959 (51951-51954 here).
CREATE OR ALTER PROCEDURE dbo.collections_import_statement
 @layout_id int,
 @original_file_name nvarchar(260),
 @source_sha256 char(64),
 @size_bytes bigint,
 @sheet_name nvarchar(128),
 @lines nvarchar(max),
 @overlap_choice varchar(10)='STOP',
 @reason nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF @overlap_choice IS NULL OR @overlap_choice NOT IN('STOP','NEW','RESTATE')
  THROW 51951,'Choose how to treat lines that look like ones already imported.',1;
 IF @overlap_choice<>'STOP' AND LEN(LTRIM(RTRIM(COALESCE(@reason,N''))))=0
  THROW 51951,'Give a reason for importing lines that look like ones already imported.',1;
 IF @source_sha256 IS NULL OR LEN(@source_sha256)<>64 OR @source_sha256 COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9a-f]%'
   OR @size_bytes IS NULL OR @size_bytes<0 OR LEN(LTRIM(RTRIM(COALESCE(@original_file_name,N''))))=0
   OR LEN(LTRIM(RTRIM(COALESCE(@sheet_name,N''))))=0 OR @lines IS NULL OR ISJSON(@lines)<>1
  THROW 51952,'The statement file could not be passed to the database. Import it again.',1;

 DECLARE @kind varchar(10),@account int,@provider varchar(30),@report varchar(30),@scope nvarchar(100),@record varchar(40);
 SELECT @kind=l.layout_kind,@account=l.bank_account_id,@provider=l.provider_key
 FROM dbo.statement_layouts l
 LEFT JOIN dbo.bank_accounts a ON a.bank_account_id=l.bank_account_id
 LEFT JOIN dbo.collection_providers p ON p.provider_key=l.provider_key
 WHERE l.layout_id=@layout_id AND l.active=1 AND COALESCE(a.active,p.active)=1;
 IF @kind IS NULL
  THROW 51953,'This file layout, its bank account or its provider is not set up or is switched off. Ask the owner to check Collections setup.',1;
 SET @report=CASE @kind WHEN 'BANK' THEN 'BANK_STATEMENT' ELSE 'PROVIDER_STATEMENT' END;
 SET @record=CASE @kind WHEN 'BANK' THEN 'BANK_LINE' ELSE 'PROVIDER_LINE' END;
 SET @scope=CONCAT(@report,':',COALESCE(CONVERT(varchar(30),@account),@provider),':');

 DECLARE @l TABLE(
  source_row int NOT NULL PRIMARY KEY,
  line_identity char(64) NOT NULL,
  parse_error nvarchar(300) NULL,
  value_date date NULL, posting_date date NULL, narration nvarchar(500) NULL, bank_reference nvarchar(100) NULL,
  debit_amount decimal(19,4) NULL, credit_amount decimal(19,4) NULL, running_balance decimal(19,4) NULL,
  merchant_id nvarchar(50) NULL, terminal_id nvarchar(50) NULL, provider_txn_reference nvarchar(100) NULL,
  invoice_reference nvarchar(80) NULL, approval_code nvarchar(30) NULL, txn_datetime datetime2(0) NULL,
  txn_business_date date NULL, settlement_date date NULL, settlement_id nvarchar(100) NULL, provider_status nvarchar(30) NULL,
  is_failed bit NULL, line_kind varchar(15) NULL, gross_amount decimal(19,4) NULL, fee_amount decimal(19,4) NULL,
  tax_on_fee_amount decimal(19,4) NULL, net_amount decimal(19,4) NULL, payer_name nvarchar(200) NULL, payer_vpa nvarchar(200) NULL,
  raw_json nvarchar(max) NOT NULL,
  line_date date NULL,
  is_new bit NOT NULL DEFAULT(0),
  source_lineage_id bigint NULL);
 INSERT @l(source_row,line_identity,parse_error,value_date,posting_date,narration,bank_reference,debit_amount,credit_amount,running_balance,
   merchant_id,terminal_id,provider_txn_reference,invoice_reference,approval_code,txn_datetime,txn_business_date,settlement_date,settlement_id,
   provider_status,is_failed,line_kind,gross_amount,fee_amount,tax_on_fee_amount,net_amount,payer_name,payer_vpa,raw_json)
 SELECT j.source_row,LOWER(j.line_identity),NULLIF(LTRIM(RTRIM(j.parse_error)),N''),j.value_date,j.posting_date,j.narration,j.bank_reference,
   j.debit_amount,j.credit_amount,j.running_balance,j.merchant_id,j.terminal_id,j.provider_txn_reference,j.invoice_reference,j.approval_code,
   j.txn_datetime,j.txn_business_date,j.settlement_date,j.settlement_id,j.provider_status,COALESCE(j.is_failed,0),
   COALESCE(j.line_kind,'UNKNOWN'),j.gross_amount,j.fee_amount,j.tax_on_fee_amount,j.net_amount,j.payer_name,j.payer_vpa,COALESCE(j.raw_json,N'{}')
 FROM OPENJSON(@lines) WITH(
   source_row int '$.row', line_identity char(64) '$.identity', parse_error nvarchar(300) '$.error',
   value_date date '$.value_date', posting_date date '$.posting_date', narration nvarchar(500) '$.narration',
   bank_reference nvarchar(100) '$.bank_reference', debit_amount decimal(19,4) '$.debit', credit_amount decimal(19,4) '$.credit',
   running_balance decimal(19,4) '$.running_balance', merchant_id nvarchar(50) '$.merchant_id', terminal_id nvarchar(50) '$.terminal_id',
   provider_txn_reference nvarchar(100) '$.txn_reference', invoice_reference nvarchar(80) '$.invoice_reference',
   approval_code nvarchar(30) '$.approval_code', txn_datetime datetime2(0) '$.txn_datetime', txn_business_date date '$.txn_business_date',
   settlement_date date '$.settlement_date', settlement_id nvarchar(100) '$.settlement_id', provider_status nvarchar(30) '$.status',
   is_failed bit '$.is_failed', line_kind varchar(15) '$.line_kind', gross_amount decimal(19,4) '$.gross', fee_amount decimal(19,4) '$.fee',
   tax_on_fee_amount decimal(19,4) '$.tax_on_fee', net_amount decimal(19,4) '$.net', payer_name nvarchar(200) '$.payer_name',
   payer_vpa nvarchar(200) '$.payer_vpa', raw_json nvarchar(max) '$.raw' AS JSON) j;

 IF NOT EXISTS(SELECT 1 FROM @l) THROW 51952,'The statement file has no lines.',1;
 IF EXISTS(SELECT 1 FROM @l WHERE source_row<1 OR LEN(line_identity)<>64 OR line_identity COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9a-f]%')
   OR EXISTS(SELECT line_identity FROM @l GROUP BY line_identity HAVING COUNT(*)>1)
  THROW 51952,'The statement file could not be passed to the database. Import it again.',1;
 UPDATE @l SET line_date=CASE @kind WHEN 'BANK' THEN COALESCE(value_date,posting_date) ELSE COALESCE(txn_business_date,settlement_date) END
 WHERE parse_error IS NULL;
 IF EXISTS(SELECT 1 FROM @l WHERE parse_error IS NULL AND line_date IS NULL)
  THROW 51952,'The statement file could not be passed to the database. Import it again.',1;

 BEGIN TRANSACTION;
 DECLARE @lock nvarchar(255)=CONCAT(N'etp-collections-',@scope),@locked int;
 EXEC @locked=sys.sp_getapplock @Resource=@lock,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
 IF @locked<0 THROW 51954,'Another statement for this account or provider is being imported. Try again in a minute.',1;

 DECLARE @outcome varchar(20),@new int,@present int,@invalid int,@collisions int=0,@superseded int=0,@file bigint;
 IF EXISTS(SELECT 1 FROM dbo.import_files WHERE source_sha256=@source_sha256 AND report_code=@report)
 BEGIN
   SET @outcome='ALREADY_IMPORTED';
 END
 ELSE
 BEGIN
   IF @kind='BANK'
     UPDATE l SET is_new=1 FROM @l l
     WHERE l.parse_error IS NULL AND NOT EXISTS(SELECT 1 FROM dbo.bank_statement_lines b WHERE b.bank_account_id=@account AND b.line_identity=l.line_identity);
   ELSE
     UPDATE l SET is_new=1 FROM @l l
     WHERE l.parse_error IS NULL AND NOT EXISTS(SELECT 1 FROM dbo.provider_settlement_lines p WHERE p.provider_key=@provider AND p.line_identity=l.line_identity);
   SELECT @new=SUM(CASE WHEN is_new=1 THEN 1 ELSE 0 END),
          @present=SUM(CASE WHEN parse_error IS NULL AND is_new=0 THEN 1 ELSE 0 END),
          @invalid=SUM(CASE WHEN parse_error IS NOT NULL THEN 1 ELSE 0 END)
   FROM @l;

   IF @new=0 SET @outcome='DUPLICATE_CONTENT';
   ELSE
   BEGIN
     IF @kind='BANK'
       SELECT @collisions=COUNT(*) FROM @l l
       WHERE l.is_new=1 AND EXISTS(SELECT 1 FROM dbo.bank_statement_lines b
         WHERE b.bank_account_id=@account AND b.is_current=1 AND b.line_date=l.line_date
           AND ((l.credit_amount IS NOT NULL AND b.credit_amount=l.credit_amount) OR (l.debit_amount IS NOT NULL AND b.debit_amount=l.debit_amount)));
     ELSE
       SELECT @collisions=COUNT(*) FROM @l l
       WHERE l.is_new=1 AND EXISTS(SELECT 1 FROM dbo.provider_settlement_lines p
         WHERE p.provider_key=@provider AND p.is_current=1 AND COALESCE(p.txn_business_date,p.settlement_date)=l.line_date
           AND p.line_kind=l.line_kind AND (p.gross_amount=l.gross_amount OR (p.gross_amount IS NULL AND l.gross_amount IS NULL AND p.net_amount=l.net_amount)));
     SET @outcome=CASE WHEN @collisions>0 AND @overlap_choice='STOP' THEN 'POSSIBLE_DUPLICATES' ELSE 'IMPORTED' END;
   END;
 END;

 IF @outcome<>'IMPORTED'
 BEGIN
   COMMIT TRANSACTION;
   SELECT @outcome AS outcome,CONVERT(bigint,NULL) AS import_file_id,0 AS new_lines,COALESCE(@present,0) AS present_lines,
          COALESCE(@invalid,0) AS invalid_lines,@collisions AS possible_duplicates,0 AS superseded_files;
   RETURN;
 END;

 DECLARE @batch uniqueidentifier=NEWID(),@now datetime2(3)=SYSUTCDATETIME(),@from date,@to date,@rows int=(SELECT COUNT(*) FROM @l);
 SELECT @from=MIN(line_date),@to=MAX(line_date) FROM @l WHERE parse_error IS NULL;
 INSERT dbo.import_batches(import_batch_id,status,store_id,period_start,period_end,started_utc,completed_utc,source_row_count)
 VALUES(@batch,'Completed',NULL,@from,@to,@now,@now,@rows);
 INSERT dbo.import_files(import_batch_id,import_profile_id,original_file_name,source_sha256,size_bytes,source_row_count,
   report_code,store_code,business_date,source_report_date,imported_by,period_start,period_end,data_truth_version)
 VALUES(@batch,NULL,@original_file_name,@source_sha256,@size_bytes,@rows,@report,NULL,NULL,NULL,ORIGINAL_LOGIN(),@from,@to,1);
 SET @file=CONVERT(bigint,SCOPE_IDENTITY());

 INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
 SELECT @file,@sheet_name,source_row,@record FROM @l;
 UPDATE l SET source_lineage_id=s.source_lineage_id FROM @l l
 JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name=@sheet_name AND s.source_row_number=l.source_row AND s.source_record_type=@record;

 IF @kind='BANK'
   INSERT dbo.bank_statement_lines(bank_account_id,layout_id,import_file_id,source_lineage_id,value_date,posting_date,narration,
     bank_reference,debit_amount,credit_amount,running_balance,raw_json,line_identity)
   SELECT @account,@layout_id,@file,source_lineage_id,value_date,posting_date,narration,bank_reference,debit_amount,credit_amount,
     running_balance,raw_json,line_identity
   FROM @l WHERE is_new=1;
 ELSE
   INSERT dbo.provider_settlement_lines(provider_key,layout_id,import_file_id,source_lineage_id,store_code,merchant_id,terminal_id,
     provider_txn_reference,invoice_reference,approval_code,txn_datetime,txn_business_date,settlement_date,settlement_id,bank_reference,
     provider_status,is_failed,line_kind,gross_amount,fee_amount,tax_on_fee_amount,net_amount,payer_name,payer_vpa,raw_json,line_identity)
   SELECT @provider,@layout_id,@file,l.source_lineage_id,
     (SELECT TOP(1) m.store_code FROM dbo.provider_merchant_ids m
      WHERE m.provider_key=@provider AND m.active=1 AND m.merchant_id=l.merchant_id AND (m.terminal_id=l.terminal_id OR m.terminal_id IS NULL)
      ORDER BY CASE WHEN m.terminal_id IS NULL THEN 1 ELSE 0 END),
     l.merchant_id,l.terminal_id,l.provider_txn_reference,l.invoice_reference,l.approval_code,l.txn_datetime,l.txn_business_date,
     l.settlement_date,l.settlement_id,l.bank_reference,l.provider_status,l.is_failed,l.line_kind,l.gross_amount,l.fee_amount,
     l.tax_on_fee_amount,l.net_amount,l.payer_name,l.payer_vpa,l.raw_json,l.line_identity
   FROM @l l WHERE l.is_new=1;

 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message)
 SELECT @file,source_lineage_id,CONCAT(@scope,line_identity),
   CASE WHEN parse_error IS NOT NULL THEN 'INVALID' WHEN is_new=1 THEN 'NEW' ELSE 'ALREADY_PRESENT' END,
   line_identity,
   COALESCE(parse_error,CASE WHEN is_new=1 THEN N'New statement line.' ELSE N'This line was imported before.' END)
 FROM @l;

 -- A line of this file that had stopped being current is current again.
 IF @kind='BANK'
   UPDATE b SET is_current=1 FROM dbo.bank_statement_lines b JOIN @l l ON l.line_identity=b.line_identity
   WHERE b.bank_account_id=@account AND b.is_current=0 AND l.parse_error IS NULL;
 ELSE
   UPDATE p SET is_current=1 FROM dbo.provider_settlement_lines p JOIN @l l ON l.line_identity=p.line_identity
   WHERE p.provider_key=@provider AND p.is_current=0 AND l.parse_error IS NULL;

 DECLARE @replaced TABLE(import_file_id bigint NOT NULL PRIMARY KEY);
 INSERT @replaced(import_file_id)
 SELECT f.import_file_id FROM dbo.import_files f
 WHERE f.report_code=@report AND f.is_superseded=0 AND f.import_file_id<>@file
   AND EXISTS(SELECT 1 FROM dbo.import_row_outcomes o WHERE o.import_file_id=f.import_file_id AND LEFT(o.business_identity,LEN(@scope))=@scope)
   AND (
     NOT EXISTS(SELECT 1 FROM dbo.import_row_outcomes o
                WHERE o.import_file_id=f.import_file_id AND o.outcome IN('NEW','ALREADY_PRESENT')
                  AND NOT EXISTS(SELECT 1 FROM @l l WHERE l.parse_error IS NULL AND l.line_identity=o.content_sha256))
     OR (@overlap_choice='RESTATE' AND f.period_start<=@to AND f.period_end>=@from));

 IF @overlap_choice='RESTATE'
 BEGIN
   IF @kind='BANK'
     UPDATE b SET is_current=0 FROM dbo.bank_statement_lines b
     WHERE b.bank_account_id=@account AND b.is_current=1 AND b.line_date BETWEEN @from AND @to
       AND NOT EXISTS(SELECT 1 FROM @l l WHERE l.line_identity=b.line_identity);
   ELSE
     UPDATE p SET is_current=0 FROM dbo.provider_settlement_lines p
     WHERE p.provider_key=@provider AND p.is_current=1 AND COALESCE(p.txn_business_date,p.settlement_date) BETWEEN @from AND @to
       AND NOT EXISTS(SELECT 1 FROM @l l WHERE l.line_identity=p.line_identity);
 END;

 UPDATE f SET is_superseded=1,superseded_by_import_file_id=@file,superseded_utc=@now,superseded_by=ORIGINAL_LOGIN(),
   restatement_reason=CASE WHEN @overlap_choice='RESTATE' THEN LEFT(CONCAT(N'Restated: ',LTRIM(RTRIM(@reason))),500) ELSE N'Later statement export' END
 FROM dbo.import_files f JOIN @replaced r ON r.import_file_id=f.import_file_id;
 SET @superseded=@@ROWCOUNT;

 DECLARE @detail nvarchar(200)=CONCAT(@new,N' rows imported');
 EXEC dbo.record_operational_audit 'CollectionsImport','Succeeded',@detail,N'database';
 COMMIT TRANSACTION;

 SELECT @outcome AS outcome,@file AS import_file_id,@new AS new_lines,@present AS present_lines,@invalid AS invalid_lines,
        @collisions AS possible_duplicates,@superseded AS superseded_files;
END;
-- >>> GRANTS
GRANT EXECUTE ON dbo.collections_import_statement TO etp_store_manager,etp_owner;
