CREATE OR ALTER FUNCTION diag.parse_date(@value nvarchar(max))
RETURNS date WITH SCHEMABINDING
AS
BEGIN
 -- yyyy-MM-dd only.
 IF @value IS NULL OR LEN(@value)<>10
    OR @value COLLATE Latin1_General_100_BIN2 NOT LIKE N'[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
  RETURN NULL;
 RETURN TRY_CONVERT(date,@value,23);
END
-- >>> GRANTS
