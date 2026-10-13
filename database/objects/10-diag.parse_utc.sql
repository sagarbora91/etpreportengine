CREATE OR ALTER FUNCTION diag.parse_utc(@value nvarchar(max))
RETURNS datetime2(3) WITH SCHEMABINDING
AS
BEGIN
 -- ISO 8601 with a Z or an offset, converted to UTC; NULL for anything else (an empty string is never 1900-01-01).
 IF @value IS NULL OR LEN(@value) NOT BETWEEN 19 AND 40
    OR @value COLLATE Latin1_General_100_BIN2 NOT LIKE N'[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]%'
  RETURN NULL;
 RETURN CONVERT(datetime2(3),SWITCHOFFSET(TRY_CONVERT(datetimeoffset(3),@value,127),'+00:00'));
END
-- >>> GRANTS
