CREATE OR ALTER FUNCTION diag.version_key(@version varchar(20))
RETURNS bigint WITH SCHEMABINDING
AS
BEGIN
 -- 1 to 4 dot-separated parts of 1-3 digits each; NULL for anything else.
 IF @version IS NULL OR LEN(@version)=0 OR DATALENGTH(@version)<>LEN(@version)
    OR @version COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9.]%'
    OR @version LIKE '.%' OR @version LIKE '%.' OR @version LIKE '%..%'
  RETURN NULL;
 DECLARE @dots int=LEN(@version)-LEN(REPLACE(@version,'.',''));
 IF @dots>3 RETURN NULL;
 DECLARE @padded varchar(40)=@version+REPLICATE('.0',3-@dots);
 DECLARE @a varchar(40)=PARSENAME(@padded,4),@b varchar(40)=PARSENAME(@padded,3),
         @c varchar(40)=PARSENAME(@padded,2),@d varchar(40)=PARSENAME(@padded,1);
 IF LEN(@a)>3 OR LEN(@b)>3 OR LEN(@c)>3 OR LEN(@d)>3 RETURN NULL;
 RETURN CONVERT(bigint,@a)*1000000000+CONVERT(bigint,@b)*1000000+CONVERT(bigint,@c)*1000+CONVERT(bigint,@d);
END
-- >>> GRANTS
