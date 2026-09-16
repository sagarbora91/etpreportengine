SET XACT_ABORT ON;

-- Numeric detail is permitted only in a small aggregate-count vocabulary.
-- Customer identifiers, invoice numbers and paths remain rejected.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.record_operational_audit
 @type varchar(40), @outcome varchar(20), @detail nvarchar(200)=NULL, @version nvarchar(40)=N''database''
AS
BEGIN
 SET NOCOUNT ON;
 IF @detail LIKE N''%[:/\]%'' THROW 51310,''Audit details cannot contain paths or identifiers.'',1;
 -- Preserve the original collation-aware identifier detection (including full-width digits).
 -- Only the permitted count grammar below is deliberately ASCII and case sensitive.
 IF @detail LIKE N''%[0-9]%''
 BEGIN
   DECLARE @space int=CHARINDEX(N'' '',@detail),@count nvarchar(200),@message nvarchar(200);
   SET @count=LEFT(@detail,CASE WHEN @space>0 THEN @space-1 ELSE 0 END);
   SET @message=SUBSTRING(@detail,@space+1,200);
   IF RIGHT(@message,1)=N''.'' SET @message=LEFT(@message,LEN(@message)-1);
   IF LEN(@count) NOT BETWEEN 1 AND 9 OR @count COLLATE Latin1_General_100_BIN2 LIKE N''%[^0-9]%''
     OR @message COLLATE Latin1_General_100_BIN2 NOT IN
       (N''files imported'',N''files skipped'',N''files failed'',N''rows imported'',N''rows skipped'',N''reports generated'')
     THROW 51310,''Audit details cannot contain paths or identifiers.'',1;
 END;
 INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name)
 VALUES(@type,@outcome,@detail,@version,SUSER_SNAME());
END');
