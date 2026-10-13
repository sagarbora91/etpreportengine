CREATE OR ALTER FUNCTION diag.is_check_id(@value nvarchar(max))
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- DB-01, SEC-03, RPT-22, DATA-05 ...: 2-4 capital letters, a dash, two digits.
 IF @value IS NULL OR LEN(@value) NOT BETWEEN 5 AND 7 OR DATALENGTH(@value)<>2*LEN(@value) RETURN 0;
 IF @value COLLATE Latin1_General_100_BIN2 NOT LIKE N'[A-Z][A-Z]%-[0-9][0-9]'
    OR @value COLLATE Latin1_General_100_BIN2 LIKE N'%[^-A-Z0-9]%'
    OR @value COLLATE Latin1_General_100_BIN2 LIKE N'%[0-9]%-%'
    OR LEN(@value)-LEN(REPLACE(@value,N'-',N''))<>1 RETURN 0;
 RETURN 1;
END
-- >>> GRANTS
