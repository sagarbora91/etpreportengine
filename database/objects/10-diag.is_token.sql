CREATE OR ALTER FUNCTION diag.is_token(@value nvarchar(max),@max_length int)
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- The SafeToken allow-list: 1 to @max_length characters of A-Z a-z 0-9 . _ : < > + -
 -- (hyphen first in the class: see the header; the CHECKs on the diag tables use the same class)
 IF @value IS NULL OR LEN(@value)=0 OR DATALENGTH(@value)<>2*LEN(@value) OR LEN(@value)>@max_length RETURN 0;
 IF @value COLLATE Latin1_General_100_BIN2 LIKE N'%[^-A-Za-z0-9._:<>+]%' RETURN 0;
 RETURN 1;
END
-- >>> GRANTS
