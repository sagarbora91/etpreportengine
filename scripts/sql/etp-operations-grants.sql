-- Installed in master by a SQL administrator, immediately after etp-operations-broker.sql
-- has created the procedure. Substitutions are fixed, escaped installation values, never
-- values from an unattended request.
--
-- P4-13. The earlier version of this block required ##MS_DatabaseMasterKey## and a grant on
-- EtpBackupCert on every edition. On SQL Server Express the only product path that creates
-- a master key - Settings > Database > Encrypted backup recovery keys - refuses to run, so
-- the module could never be installed there and no verified backup or recovery drill was
-- possible. Two unrelated things had been tied together:
--
--   * Module signing. So that the automation account can take and verify backups without
--     being a SQL administrator, the broker borrows CREATE ANY DATABASE and VIEW SERVER
--     STATE from a signing certificate. Needed on every edition. (The recovery drill is a
--     SQL administrator's operation - see P4-15 in the broker - and borrows nothing.)
--   * Backup encryption. Needs EtpBackupCert, and only where the edition can encrypt.
--
-- Signing no longer needs a master key. The certificate is protected by a password that is
-- generated inside this batch and never leaves it - not on a command line, not in a file,
-- not known to anyone - and its private key is removed as soon as the broker is signed.
-- A signature is verified with the public key alone, so the broker keeps working while the
-- certificate can never sign anything again. That is stronger than keeping a live private
-- key under a master key. Reinstalling creates a fresh signer.
--
-- Failure removes the signer this run created and keeps the broker. A broker that is not
-- signed still works for a SQL administrator, and the next upgrade needs exactly that: setup
-- takes its mandatory pre-migration backup through the broker, as an administrator, and a
-- failed reinstall that dropped the broker would block the upgrade that fixes it. The
-- automation account cannot use an unsigned broker, so the scheduled backups fail loudly
-- until the module is reinstalled - they do not quietly pretend to work.
--
-- A precondition that is not met is reported as one ETP_MODULE_REFUSED: line and not as a
-- SQL error, so the installer can show the Owner which step comes first. Its text is fixed
-- here, apart from the account name the Owner typed.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @procedure sysname = N'__PROCEDURE__';
DECLARE @signer sysname = N'__SIGNER__';
DECLARE @identity sysname = N'__IDENTITY_LITERAL__';
DECLARE @database sysname = N'__DATABASE_LITERAL__';
DECLARE @encrypts bit = CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE N'%Express%'
                              OR CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE N'%Web%'
                             THEN 0 ELSE 1 END;
DECLARE @signerCreated bit = 0;
DECLARE @sql nvarchar(max), @message nvarchar(2048), @user sysname;

BEGIN TRY
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@procedure), N'P') IS NULL
        THROW 51332, 'The operations broker procedure is missing. Install etp-operations-broker.sql first.', 1;

    -- The account the scheduled tasks run as has to be an active Store Manager already.
    -- Settings > Users creates its Windows login, its database user and its role together.
    IF SUSER_ID(@identity) IS NULL
    BEGIN
        SET @message = N'Add ' + @identity + N' as a Store Manager in Settings > Users first. That creates the SQL login this module grants to.';
        THROW 51332, @message, 1;
    END;
    IF DB_ID(@database) IS NULL
        THROW 51332, 'The configured database does not exist on this instance.', 1;

    -- Checked inside the target database, where the roles live. Resolve the user by SID:
    -- its name need not match the login name.
    SET @sql = N'USE ' + QUOTENAME(@database) + N';
        DECLARE @u sysname = (SELECT name FROM sys.database_principals WHERE sid = SUSER_SID(@identity));
        IF @u IS NULL OR IS_ROLEMEMBER(N''etp_store_manager'', @u) <> 1
            THROW 51332, ''The automation account must be an active Store Manager. Add it in Settings > Users first.'', 1;
        IF DATABASE_PRINCIPAL_ID(N''etp_automation'') IS NULL
            THROW 51332, ''Complete the operations-status database migration first.'', 1;
        -- P4-14. Before migration 0032, Settings > Users left every Store Manager without
        -- CONNECT, so a module installed for one could never be used by it.
        IF NOT EXISTS (SELECT 1 FROM sys.database_permissions
                       WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(@u)
                         AND permission_name = N''CONNECT'' AND state IN (''G'',''W''))
            THROW 51332, ''The automation account cannot connect to the database. Install this build first: its database migration 0032 gives Store Managers back the right to connect.'', 1;';
    EXEC sys.sp_executesql @sql, N'@identity sysname', @identity = @identity;

    -- Only an edition that encrypts its backups needs the backup certificate.
    IF @encrypts = 1 AND NOT EXISTS (SELECT 1 FROM sys.certificates WHERE name = N'EtpBackupCert')
        THROW 51332, 'This SQL Server edition encrypts backups, so it needs the backup certificate. Create and export the recovery keys in Settings > Database first.', 1;

    -- A fresh signer. CREATE OR ALTER on the broker has already discarded any earlier
    -- signature, and an earlier signer has no private key left to re-sign with.
    IF SUSER_ID(@signer) IS NOT NULL
    BEGIN
        SET @sql = N'DROP LOGIN ' + QUOTENAME(@signer) + N';';
        EXEC (@sql);
    END;
    IF EXISTS (SELECT 1 FROM sys.certificates WHERE name = @signer)
    BEGIN
        SET @sql = N'DROP CERTIFICATE ' + QUOTENAME(@signer) + N';';
        EXEC (@sql);
    END;

    -- Hexadecimal cannot contain a quote, so it is safe inside the literal. The prefix and
    -- suffix satisfy the Windows password policy SQL Server applies to certificate passwords.
    DECLARE @secret nvarchar(200) = N'Etp!' + CONVERT(nvarchar(200), CRYPT_GEN_RANDOM(48), 2) + N'q';
    SET @sql = N'CREATE CERTIFICATE ' + QUOTENAME(@signer)
        + N' ENCRYPTION BY PASSWORD = N''' + @secret + N''''
        + N' WITH SUBJECT = N''Restricted ETP backup and recovery operations'';';
    EXEC (@sql);
    SET @signerCreated = 1;

    SET @sql = N'CREATE LOGIN ' + QUOTENAME(@signer) + N' FROM CERTIFICATE ' + QUOTENAME(@signer) + N';';
    EXEC (@sql);
    -- Only what the automation account's own operations need. CREATE ANY DATABASE is what
    -- RESTORE VERIFYONLY and FILELISTONLY require to read a backup's contents; VIEW SERVER
    -- STATE lets the broker refuse to overwrite an existing file. ALTER ANY DATABASE was for
    -- the recovery drill, which now runs as a SQL administrator and needs no borrowed rights.
    SET @sql = N'GRANT CREATE ANY DATABASE, VIEW SERVER STATE TO ' + QUOTENAME(@signer) + N';';
    EXEC (@sql);
    SET @sql = N'ADD SIGNATURE TO OBJECT::dbo.' + QUOTENAME(@procedure)
        + N' BY CERTIFICATE ' + QUOTENAME(@signer) + N' WITH PASSWORD = N''' + @secret + N''';';
    EXEC (@sql);
    SET @sql = N'ALTER CERTIFICATE ' + QUOTENAME(@signer) + N' REMOVE PRIVATE KEY;';
    EXEC (@sql);
    SET @secret = NULL;

    -- The automation account may run this broker and nothing else in master.
    SET @user = (SELECT name FROM sys.database_principals WHERE sid = SUSER_SID(@identity));
    IF @user IS NULL
    BEGIN
        SET @sql = N'CREATE USER ' + QUOTENAME(@identity) + N' FOR LOGIN ' + QUOTENAME(@identity) + N';';
        EXEC (@sql);
        SET @user = @identity;
    END;
    SET @sql = N'GRANT EXECUTE ON OBJECT::dbo.' + QUOTENAME(@procedure) + N' TO ' + QUOTENAME(@user) + N';';
    EXEC (@sql);
    IF @encrypts = 1
    BEGIN
        SET @sql = N'GRANT VIEW DEFINITION ON CERTIFICATE::EtpBackupCert TO ' + QUOTENAME(@user) + N';';
        EXEC (@sql);
    END;

    -- In the target database: the automation role, and the right to take a backup.
    SET @sql = N'USE ' + QUOTENAME(@database) + N';
        DECLARE @u sysname = (SELECT name FROM sys.database_principals WHERE sid = SUSER_SID(@identity));
        DECLARE @s nvarchar(max);
        IF IS_ROLEMEMBER(N''etp_automation'', @u) <> 1
        BEGIN SET @s = N''ALTER ROLE etp_automation ADD MEMBER '' + QUOTENAME(@u) + N'';''; EXEC (@s); END;
        IF IS_ROLEMEMBER(N''db_backupoperator'', @u) <> 1
        BEGIN SET @s = N''ALTER ROLE db_backupoperator ADD MEMBER '' + QUOTENAME(@u) + N'';''; EXEC (@s); END;';
    EXEC sys.sp_executesql @sql, N'@identity sysname', @identity = @identity;

    -- Builds before one-signer-per-broker used a single EtpOperationsModuleSigner for every
    -- broker, with a live private key under the master key and ALTER ANY DATABASE. Retire it
    -- once nothing it signed is left; while another database's broker still carries its
    -- signature, it stays until that module is reinstalled too.
    IF EXISTS (SELECT 1 FROM sys.certificates WHERE name = N'EtpOperationsModuleSigner')
       AND NOT EXISTS (SELECT 1 FROM sys.crypt_properties cp
                       JOIN sys.certificates c ON c.thumbprint = cp.thumbprint
                       WHERE c.name = N'EtpOperationsModuleSigner')
    BEGIN
        IF SUSER_ID(N'EtpOperationsModuleSigner') IS NOT NULL DROP LOGIN EtpOperationsModuleSigner;
        DROP CERTIFICATE EtpOperationsModuleSigner;
    END;

    SELECT N'ETP_MODULE:' + (SELECT @procedure [procedure], @signer signer,
        CASE WHEN @encrypts = 1 THEN N'AES_256' ELSE N'NONE' END backupEncryption
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
END TRY
BEGIN CATCH
    -- The broker stays (see the top of this file). A signature this run added is removed
    -- first: SQL will not drop a certificate while something is signed with it.
    IF @signerCreated = 1 AND EXISTS (SELECT 1 FROM sys.crypt_properties cp
                                      JOIN sys.certificates c ON c.thumbprint = cp.thumbprint
                                      WHERE c.name = @signer AND cp.major_id = OBJECT_ID(N'dbo.' + QUOTENAME(@procedure)))
    BEGIN
        SET @sql = N'DROP SIGNATURE FROM OBJECT::dbo.' + QUOTENAME(@procedure) + N' BY CERTIFICATE ' + QUOTENAME(@signer) + N';';
        EXEC (@sql);
    END;
    IF @signerCreated = 1 AND SUSER_ID(@signer) IS NOT NULL
    BEGIN
        SET @sql = N'DROP LOGIN ' + QUOTENAME(@signer) + N';';
        EXEC (@sql);
    END;
    IF @signerCreated = 1 AND EXISTS (SELECT 1 FROM sys.certificates WHERE name = @signer)
    BEGIN
        SET @sql = N'DROP CERTIFICATE ' + QUOTENAME(@signer) + N';';
        EXEC (@sql);
    END;
    IF ERROR_NUMBER() = 51332
        SELECT N'ETP_MODULE_REFUSED:' + ERROR_MESSAGE();
    ELSE
        THROW;
END CATCH;
