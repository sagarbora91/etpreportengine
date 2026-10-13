CREATE OR ALTER FUNCTION diag.is_safe_detail(@detail nvarchar(max))
RETURNS bit
AS
BEGIN
 -- A result's sub-results: an array of objects {key, outcome, findingCode?, metricValue?, fields?}; key and field
 -- values are tokens, field values may also be numbers, null or empty; nothing else is allowed.
 IF @detail IS NULL RETURN 1;
 IF ISJSON(@detail)<>1 OR LEFT(LTRIM(@detail),1)<>N'[' RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@detail) e WHERE e.[type]<>5) RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@detail) e
   WHERE JSON_VALUE(e.value,'$.key') IS NULL OR JSON_VALUE(e.value,'$.outcome') IS NULL) RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@detail) e CROSS APPLY OPENJSON(e.value) p
   WHERE NOT(
        (p.[key]=N'key' AND p.[type]=1 AND diag.is_token(p.value,100)=1)
     OR (p.[key]=N'outcome' AND p.[type]=1 AND p.value COLLATE Latin1_General_100_BIN2 IN(N'Pass',N'Warning',N'Fail',N'Known',N'Skipped'))
     OR (p.[key]=N'findingCode' AND (p.[type]=0 OR (p.[type]=1 AND diag.is_code(p.value,60)=1)))
     OR (p.[key]=N'metricValue' AND p.[type] IN(0,2))
     OR (p.[key]=N'fields' AND p.[type] IN(0,5)))) RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@detail) e CROSS APPLY OPENJSON(e.value) p
   CROSS APPLY OPENJSON(CASE WHEN p.[type]=5 THEN p.value END) f
   WHERE p.[key]=N'fields'
     AND NOT(diag.is_token(f.[key],60)=1
         AND (f.[type] IN(0,2) OR (f.[type]=1 AND (f.value=N'' OR diag.is_token(f.value,100)=1))))) RETURN 0;
 RETURN 1;
END
-- >>> GRANTS
