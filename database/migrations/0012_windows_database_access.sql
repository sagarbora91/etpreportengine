SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL
    EXEC(N'CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS');

IF NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE sid=SUSER_SID(N'NT AUTHORITY\SYSTEM'))
    EXEC(N'CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM]');

IF IS_ROLEMEMBER(N'db_datareader',N'NT AUTHORITY\SYSTEM')<>1
    ALTER ROLE db_datareader ADD MEMBER [NT AUTHORITY\SYSTEM];
IF IS_ROLEMEMBER(N'db_datawriter',N'NT AUTHORITY\SYSTEM')<>1
    ALTER ROLE db_datawriter ADD MEMBER [NT AUTHORITY\SYSTEM];
IF IS_ROLEMEMBER(N'db_backupoperator',N'NT AUTHORITY\SYSTEM')<>1
    ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];
GRANT INSERT ON dbo.operational_audit TO [NT AUTHORITY\SYSTEM];

IF NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=N'NT AUTHORITY\SYSTEM')
    INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
    VALUES(N'NT AUTHORITY\SYSTEM',N'ETP Automated Operations',N'STORE_MANAGER',1,SUSER_SNAME(),N'Built-in account authorized only for installed background imports, reports and backups');

COMMIT TRANSACTION;
