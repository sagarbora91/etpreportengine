using System.Security.AccessControl;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// P4-13. The operations broker borrows CREATE ANY DATABASE and VIEW SERVER STATE from a
/// signing certificate so that the dedicated automation account - not a SQL administrator -
/// can take and verify backups. (The recovery drill is an administrator's operation: P4-15.)
/// Every earlier test ran the broker as sysadmin,
/// which bypasses exactly the permissions the signature exists to grant, so the signed path
/// had never been shown to work at all. And on SQL Server Express it could not even be
/// installed: signing demanded a database master key that nothing on Express would create.
///
/// These tests install the exact shipped SQL into master, provision a non-sysadmin Store
/// Manager through the same statements the application uses, and then run a real backup as
/// that account and a real recovery drill as an administrator.
/// </summary>
[Collection(ServerPrincipalTests.Name)]
public sealed class OperationsModuleSigningTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    // A well-known Windows identity that exists on every machine and is never sysadmin.
    private const string Principal = @"NT AUTHORITY\LOCAL SERVICE";

    [Fact]
    public async Task A_non_sysadmin_store_manager_backs_up_through_the_signed_broker_and_only_an_administrator_can_drill()
    {
        var suffix = Guid.NewGuid().ToString("N")[..16];
        var procedure = "etp_operations_test_" + suffix;
        var signer = "EtpOperationsModuleSigner_test_" + suffix;
        // An apostrophe in the folder: the drill's restore paths go two literals deep into
        // dynamic SQL, and escaping them only once broke the restore (review finding 11).
        var root = CreateSqlWritableRoot("_it's");
        var state = new CleanupState();
        try
        {
            state.PrincipalLoginExisted = await ScalarAsync<int>("SELECT CASE WHEN SUSER_ID(@p) IS NULL THEN 0 ELSE 1 END", ("@p", Principal)) == 1;
            state.MasterUserExisted = await ScalarAsync<int>("SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.database_principals WHERE sid=SUSER_SID(@p)) THEN 1 ELSE 0 END", ("@p", Principal)) == 1;
            var masterKeyBefore = await MasterKeyExistsAsync();

            // Exactly what Settings > Users does when the Owner adds a Store Manager.
            await database.ExecuteAsync($"EXEC dbo.configure_application_role N'{Principal}','STORE_MANAGER',1;");
            await database.ExecuteAsync($"""
                MERGE dbo.application_users AS target
                USING(SELECT N'{Principal}' windows_identity) source ON target.windows_identity=source.windows_identity
                WHEN MATCHED THEN UPDATE SET role_code='STORE_MANAGER',is_active=1
                WHEN NOT MATCHED THEN INSERT(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
                  VALUES(N'{Principal}',N'Automation (test)','STORE_MANAGER',1,SUSER_SNAME(),N'P4-13 signing test');
                """);

            // A signer left by a build before one-signer-per-broker, signing nothing any more.
            // (Password-protected here only because this instance may have no master key.)
            if (!await CertificateExistsAsync(LegacySigner))
            {
                state.LegacySignerCreated = true;
                await ExecuteMasterAsync($"CREATE CERTIFICATE {LegacySigner} ENCRYPTION BY PASSWORD = N'Etp!{Guid.NewGuid():N}q' WITH SUBJECT = N'Legacy signer (test)'; CREATE LOGIN {LegacySigner} FROM CERTIFICATE {LegacySigner};");
            }

            await InstallBrokerAsync(procedure, root);
            var encrypts = await EditionEncryptsAsync();
            if (encrypts && !await CertificateExistsAsync("EtpBackupCert"))
            {
                // On an edition that encrypts, the certificate is genuinely required, and the
                // message has to name it rather than a master key.
                Assert.Contains("backup certificate", await RunGrantsRefusedAsync(procedure, signer, Principal), StringComparison.OrdinalIgnoreCase);
                await CleanupAsync(procedure, signer, root, state);
                return;
            }

            var module = await RunGrantsAsync(procedure, signer, Principal);
            using (var document = JsonDocument.Parse(module))
            {
                Assert.Equal(procedure, document.RootElement.GetProperty("procedure").GetString());
                Assert.Equal(encrypts ? "AES_256" : "NONE", document.RootElement.GetProperty("backupEncryption").GetString());
            }

            // Signed, and the private key is gone: the signature still verifies with the public
            // key, but nothing can ever be signed with this certificate again.
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.crypt_properties WHERE major_id=OBJECT_ID(@o)", ("@o", "dbo." + procedure)));
            Assert.Equal("NA", await ScalarAsync<string>("SELECT pvt_key_encryption_type FROM sys.certificates WHERE name=@s", ("@s", signer)));
            // And no master key was needed for it.
            Assert.Equal(masterKeyBefore, await MasterKeyExistsAsync());
            // The legacy shared signer, which signed nothing any more, has been retired.
            if (state.LegacySignerCreated)
            {
                Assert.False(await CertificateExistsAsync(LegacySigner), "The unused legacy signer was left behind.");
                Assert.Equal(0, await ScalarAsync<int>($"SELECT CASE WHEN SUSER_ID(N'{LegacySigner}') IS NULL THEN 0 ELSE 1 END"));
            }

            // The real test: run it as the Store Manager, not as sysadmin.
            Assert.Equal(0, await ScalarAsync<int>($"EXECUTE AS LOGIN=N'{Principal}'; SELECT COALESCE(IS_SRVROLEMEMBER('sysadmin'),0); REVERT;"));
            var file = database.Name + "-" + suffix + ".bak";
            var backup = await RunAsPrincipalAsync($"EXEC dbo.[{procedure}] 'BACKUP',N'{file}';");
            Assert.StartsWith("ETP_METADATA:", backup.Result);
            Assert.Contains("ETP_ENCRYPTION:" + (encrypts ? "AES_256" : "NONE"), backup.Messages);
            Assert.True(File.Exists(Path.Combine(root, file)), "The backup file was not written.");

            // Metadata checks are the automation account's to run as well.
            var metadata = await RunAsPrincipalAsync($"EXEC dbo.[{procedure}] 'METADATA',N'{file}';");
            Assert.Equal(backup.Result, metadata.Result);

            // P4-15, decided: the drill is a SQL administrator's operation. It restores a full
            // copy and runs DBCC CHECKDB on it, and a login that restores someone else's backup
            // owns the copy at server level without ever becoming its dbo - so the automation
            // account could never have passed the integrity check. It is refused up front, in
            // words, rather than failing halfway through a restore.
            var drillRefused = await Assert.ThrowsAsync<SqlException>(() => RunAsPrincipalAsync($"EXEC dbo.[{procedure}] 'DRILL',N'{file}';"));
            Assert.Equal(51334, drillRefused.Number);
            Assert.Contains("SQL administrator", drillRefused.Message, StringComparison.Ordinal);

            // And an administrator - the Owner, as the scheduled drill now runs - completes it,
            // integrity check included, against the very backup the automation account took.
            var drill = await ExecuteMasterAsync($"EXEC dbo.[{procedure}] 'DRILL',N'{file}';");
            Assert.Equal(backup.Result, Assert.Single(drill, row => row.StartsWith("ETP_METADATA:", StringComparison.Ordinal)));
        }
        catch (Exception failure)
        {
            await CleanupPreservingAsync(procedure, signer, root, state, failure);
            throw;
        }
        await CleanupAsync(procedure, signer, root, state);
    }

    [Fact]
    public async Task A_failed_install_keeps_the_broker_for_an_administrator_and_leaves_no_signer()
    {
        // A reinstall whose grants fail. The broker is kept: unsigned, the automation account
        // cannot use it, so its scheduled backups fail loudly - but setup's pre-migration
        // backup runs through it as an administrator, and dropping it would block the very
        // upgrade that fixes the problem. The signer this run created is removed, and the
        // Owner is told what to do first rather than handed a SQL error.
        var suffix = Guid.NewGuid().ToString("N")[..16];
        var procedure = "etp_operations_test_" + suffix;
        var signer = "EtpOperationsModuleSigner_test_" + suffix;
        var root = CreateSqlWritableRoot();
        var unprovisioned = Environment.MachineName + @"\EtpNoSuchAccount" + suffix[..8];
        try
        {
            await InstallBrokerAsync(procedure, root);

            Assert.Contains("as a Store Manager in Settings > Users first", await RunGrantsRefusedAsync(procedure, signer, unprovisioned), StringComparison.Ordinal);

            Assert.Equal(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(@o,'P') IS NULL THEN 0 ELSE 1 END", ("@o", "dbo." + procedure)));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.crypt_properties WHERE major_id=OBJECT_ID(@o)", ("@o", "dbo." + procedure)));
            Assert.Equal(0, await ScalarAsync<int>("SELECT CASE WHEN SUSER_ID(@s) IS NULL THEN 0 ELSE 1 END", ("@s", signer)));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.certificates WHERE name=@s", ("@s", signer)));

            // What setup's pre-migration backup needs: an administrator can still use it.
            var backup = await ExecuteMasterAsync($"EXEC dbo.[{procedure}] 'BACKUP',N'{database.Name}-{suffix}.bak';");
            Assert.Single(backup, row => row.StartsWith("ETP_METADATA:", StringComparison.Ordinal));
        }
        catch (Exception failure)
        {
            await CleanupPreservingAsync(procedure, signer, root, new CleanupState(), failure);
            throw;
        }
        await CleanupAsync(procedure, signer, root, new CleanupState());
    }

    [Fact]
    public async Task An_automation_account_that_cannot_connect_is_refused_before_anything_is_signed()
    {
        // P4-14. Before migration 0032, Settings > Users left every Store Manager without
        // CONNECT. A module installed for such an account signs and grants successfully and
        // then never works, so the install must refuse and say what to do.
        var suffix = Guid.NewGuid().ToString("N")[..16];
        var procedure = "etp_operations_test_" + suffix;
        var signer = "EtpOperationsModuleSigner_test_" + suffix;
        var root = CreateSqlWritableRoot();
        var state = new CleanupState();
        try
        {
            state.PrincipalLoginExisted = await ScalarAsync<int>("SELECT CASE WHEN SUSER_ID(@p) IS NULL THEN 0 ELSE 1 END", ("@p", Principal)) == 1;
            state.MasterUserExisted = await ScalarAsync<int>("SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.database_principals WHERE sid=SUSER_SID(@p)) THEN 1 ELSE 0 END", ("@p", Principal)) == 1;
            await database.ExecuteAsync($"EXEC dbo.configure_application_role N'{Principal}','STORE_MANAGER',1;");
            // Exactly what the pre-0032 procedure left behind.
            await database.ExecuteAsync($"DECLARE @u sysname=(SELECT name FROM sys.database_principals WHERE sid=SUSER_SID(N'{Principal}')); DECLARE @s nvarchar(max)=N'REVOKE CONNECT FROM '+QUOTENAME(@u)+N';'; EXEC(@s);");

            await InstallBrokerAsync(procedure, root);
            Assert.Contains("migration 0032", await RunGrantsRefusedAsync(procedure, signer, Principal), StringComparison.Ordinal);
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.crypt_properties WHERE major_id=OBJECT_ID(@o)", ("@o", "dbo." + procedure)));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.certificates WHERE name=@s", ("@s", signer)));
        }
        catch (Exception failure)
        {
            await CleanupPreservingAsync(procedure, signer, root, state, failure);
            throw;
        }
        await CleanupAsync(procedure, signer, root, state);
    }

    // ---------------------------------------------------------------- the shipped SQL

    private async Task InstallBrokerAsync(string procedure, string root)
    {
        var template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "sql", "etp-operations-broker.sql"));
        await ExecuteMasterAsync(template.Replace("__PROCEDURE__", procedure).Replace("__DATABASE_LITERAL__", database.Name)
            .Replace("__DATABASE_IDENTIFIER__", database.Name).Replace("__BACKUP_DIRECTORY__", Escape(root)).Replace("__RESTORE_DIRECTORY__", Escape(root)));
    }

    private async Task<List<string>> RunGrantsTemplateAsync(string procedure, string signer, string identity)
    {
        var template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "sql", "etp-operations-grants.sql"));
        var sql = template.Replace("__PROCEDURE__", procedure).Replace("__SIGNER__", signer)
            .Replace("__IDENTITY_LITERAL__", Escape(identity)).Replace("__DATABASE_LITERAL__", Escape(database.Name));
        return await ExecuteMasterAsync(sql);
    }

    private async Task<string> RunGrantsAsync(string procedure, string signer, string identity)
    {
        var output = await RunGrantsTemplateAsync(procedure, signer, identity);
        Assert.DoesNotContain(output, row => row.StartsWith("ETP_MODULE_REFUSED:", StringComparison.Ordinal));
        var line = Assert.Single(output, row => row.StartsWith("ETP_MODULE:", StringComparison.Ordinal));
        return line[11..];
    }

    // A precondition that is not met comes back as one line of text for the installer to show.
    private async Task<string> RunGrantsRefusedAsync(string procedure, string signer, string identity)
    {
        var output = await RunGrantsTemplateAsync(procedure, signer, identity);
        Assert.DoesNotContain(output, row => row.StartsWith("ETP_MODULE:", StringComparison.Ordinal));
        return Assert.Single(output, row => row.StartsWith("ETP_MODULE_REFUSED:", StringComparison.Ordinal))[19..];
    }

    // ---------------------------------------------------------------- plumbing

    private const string LegacySigner = "EtpOperationsModuleSigner";

    private sealed class CleanupState
    {
        public bool PrincipalLoginExisted { get; set; } = true;
        public bool MasterUserExisted { get; set; } = true;
        public bool LegacySignerCreated { get; set; }
    }

    // No pooling. A statement that raises a severe error kills its session, and a pool
    // then hands that dead session to the next caller - which surfaced as "the session is
    // in the kill state" on an innocent DROP in cleanup, hiding the statement that failed.
    private string MasterConnectionString => new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString;

    private async Task<List<string>> ExecuteMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        do { while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) rows.Add(Convert.ToString(reader.GetValue(0))!); }
        while (await reader.NextResultAsync());
        return rows;
    }

    private async Task<(string Result, string Messages)> RunAsPrincipalAsync(string statement)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        var messages = new System.Text.StringBuilder();
        connection.InfoMessage += (_, e) => messages.AppendLine(e.Message);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"EXECUTE AS LOGIN=N'{Principal}'; {statement} REVERT;", connection) { CommandTimeout = 300 };
        var result = Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
        return (result, messages.ToString());
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private Task<bool> MasterKeyExistsAsync() =>
        ScalarAsync<int>("SELECT COUNT(*) FROM sys.symmetric_keys WHERE name='##MS_DatabaseMasterKey##'").ContinueWith(t => t.Result > 0);

    private Task<bool> CertificateExistsAsync(string name) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM sys.certificates WHERE name=@n", ("@n", name)).ContinueWith(t => t.Result > 0);

    private Task<bool> EditionEncryptsAsync() =>
        ScalarAsync<int>("SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Express%' OR CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Web%' THEN 0 ELSE 1 END")
            .ContinueWith(t => t.Result == 1);

    private string CreateSqlWritableRoot(string nameSuffix = "")
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpSigningTest_" + Guid.NewGuid().ToString("N") + nameSuffix);
        Directory.CreateDirectory(root);
        var server = new SqlConnectionStringBuilder(database.ConnectionString).DataSource;
        if (!server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
        {
            var service = server.Contains('\\') ? "NT SERVICE\\MSSQL$" + server.Split('\\')[1] : "NT SERVICE\\MSSQLSERVER";
            var acl = new DirectoryInfo(root).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(service, FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(acl);
        }
        return root;
    }

    // Cleanup runs after a failure too, but its own failure is reported alongside the
    // original, never instead of it.
    private async Task CleanupPreservingAsync(string procedure, string signer, string root, CleanupState state, Exception failure)
    {
        try { await CleanupAsync(procedure, signer, root, state); }
        catch (Exception cleanup) { throw new AggregateException("The test failed, and cleanup then failed too.", failure, cleanup); }
    }

    private async Task CleanupAsync(string procedure, string signer, string root, CleanupState state)
    {
        // Every name below was generated by this test. Refuse anything else.
        if (!procedure.StartsWith("etp_operations_test_", StringComparison.Ordinal)
            || !signer.StartsWith("EtpOperationsModuleSigner_test_", StringComparison.Ordinal)
            || !Path.GetFileName(root).StartsWith("EtpSigningTest_", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe cleanup target.");
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        async Task Run(string sql) { await using var c = new SqlCommand(sql, connection); await c.ExecuteNonQueryAsync(); }

        await Run($"IF OBJECT_ID(N'dbo.[{procedure}]','P') IS NOT NULL DROP PROCEDURE dbo.[{procedure}];");
        await Run($"IF SUSER_ID(N'{signer}') IS NOT NULL DROP LOGIN [{signer}];");
        await Run($"IF EXISTS(SELECT 1 FROM sys.certificates WHERE name=N'{signer}') DROP CERTIFICATE [{signer}];");
        // Only undo what this test created: a pre-existing login or user is left as it was.
        if (!state.MasterUserExisted)
            // EXEC() takes a variable, not an expression containing a function call.
            await Run($"DECLARE @u sysname=(SELECT name FROM sys.database_principals WHERE sid=SUSER_SID(N'{Principal}')); IF @u IS NOT NULL BEGIN DECLARE @s nvarchar(max)=N'DROP USER '+QUOTENAME(@u); EXEC(@s); END;");
        if (!state.PrincipalLoginExisted)
            await Run($"IF SUSER_ID(N'{Principal}') IS NOT NULL DROP LOGIN [{Principal}];");
        // The legacy signer is removed only if this test created it and it signs nothing.
        if (state.LegacySignerCreated)
        {
            await Run($"IF SUSER_ID(N'{LegacySigner}') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sys.crypt_properties cp JOIN sys.certificates c ON c.thumbprint=cp.thumbprint WHERE c.name=N'{LegacySigner}') DROP LOGIN {LegacySigner};");
            await Run($"IF EXISTS(SELECT 1 FROM sys.certificates WHERE name=N'{LegacySigner}') AND NOT EXISTS(SELECT 1 FROM sys.crypt_properties cp JOIN sys.certificates c ON c.thumbprint=cp.thumbprint WHERE c.name=N'{LegacySigner}') DROP CERTIFICATE {LegacySigner};");
        }

        // A drill interrupted by SQL attention can escape the broker's own TRY/CATCH.
        await using (var query = new SqlCommand("SELECT DISTINCT d.name FROM sys.databases d JOIN sys.master_files f ON f.database_id=d.database_id WHERE d.name LIKE 'EtpRecovery[_]%' AND f.physical_name LIKE @root", connection))
        {
            query.Parameters.AddWithValue("@root", root + Path.DirectorySeparatorChar + "%");
            var leftovers = new List<string>();
            await using (var reader = await query.ExecuteReaderAsync()) while (await reader.ReadAsync()) leftovers.Add(reader.GetString(0));
            foreach (var leftover in leftovers)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(leftover, "^EtpRecovery_[A-Fa-f0-9]{32}$")) throw new InvalidOperationException("Unsafe recovery name.");
                await Run($"ALTER DATABASE [{leftover}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{leftover}];");
            }
        }
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
