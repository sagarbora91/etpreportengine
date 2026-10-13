CREATE OR ALTER FUNCTION diag.is_safe_text(@value nvarchar(max),@max_length int)
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- Free text typed by the Owner (a reason) or built from a template (an explanation): not blank in the sense of
 -- dbo.is_blank_text (0044), at most @max_length characters, and no control characters (U+0000-U+001F,
 -- U+007F-U+009F, U+2028, U+2029), so a line break or an escape can never reach an export or a screen.
 IF @value IS NULL OR dbo.is_blank_text(@value)=1 OR DATALENGTH(@value)>2*@max_length RETURN 0;
 DECLARE @i int=1,@n int=DATALENGTH(@value)/2,@code int;
 WHILE @i<=@n
 BEGIN
  SET @code=UNICODE(SUBSTRING(@value,@i,1));
  IF @code<32 OR @code BETWEEN 127 AND 159 OR @code IN(8232,8233) RETURN 0;
  SET @i+=1;
 END;
 RETURN 1;
END
-- >>> GRANTS
