ALTER TABLE dbo.accounting_batches ADD approval_reason nvarchar(1000) NULL;
EXEC(N'ALTER TABLE dbo.accounting_batches ADD CONSTRAINT CK_accounting_batches_approval_reason
 CHECK (approval_reason IS NULL OR LEN(LTRIM(RTRIM(approval_reason)))>0);');
