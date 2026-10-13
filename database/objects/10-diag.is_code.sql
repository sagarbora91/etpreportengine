CREATE OR ALTER FUNCTION diag.is_code(@value nvarchar(max),@max_length int)
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- Event and finding codes: 1 to @max_length characters of A-Z 0-9 _
 IF @value IS NULL OR LEN(@value)=0 OR DATALENGTH(@value)<>2*LEN(@value) OR LEN(@value)>@max_length RETURN 0;
 IF @value COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Z0-9_]%' RETURN 0;
 RETURN 1;
END
-- >>> GRANTS
