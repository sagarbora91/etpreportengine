using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.2, lane L1 (AUDIT-2 S-01, decision 37): after migration 0059 a Viewer cannot read a landing table, a customer column
/// of an etp_r0xx table, a sharing contact or another user's application user row, and a Store Manager cannot read a customer
/// contact column (names stay readable). The columns are found by scanning sys.columns with 0059's patterns, so a table added
/// later without its denies fails here. The synthetic etp-sample workbooks go through the importer (C#) first, so the probes
/// run against real rows. Users without login in each role, entered with EXECUTE AS on a non-pooled connection; every probe
/// runs in TRY/CATCH and the batch REVERTs itself.
/// </summary>
public sealed class CustomerDataPermissionSqlTests
{
    // 0059's column patterns, as one SQL predicate on c.name.
    private const string Contact = """
        (LOWER(c.name) LIKE N'%mobile%' OR (LOWER(c.name) LIKE N'%phone%' AND LOWER(c.name) NOT LIKE N'%phonepe%')
         OR LOWER(c.name) LIKE N'%mail%' OR LOWER(c.name) LIKE N'%contact%' OR LOWER(c.name) LIKE N'%address%' OR LOWER(c.name) LIKE N'%landline%')
        """;

    private const string Customer = $"""
        ({Contact} OR LOWER(c.name) LIKE N'%customer%' OR LOWER(c.name) LIKE N'%cust[_]name%' OR LOWER(c.name) LIKE N'%custname%' OR LOWER(c.name) LIKE N'%gstin%')
        """;

    private sealed record Column(string Table, string Name, bool IsLanding, bool IsContact);

    [SqlFact]
    public async Task A_viewer_reads_no_customer_data_and_a_store_manager_no_contact_column()
    {
        var database = new SqlDatabaseFixture();
        var folder = Path.Combine(Path.GetTempPath(), "EtpPiiPermissions_" + Guid.NewGuid().ToString("N"));
        try
        {
            await database.InitializeAsync();
            await ImportSamplesAsync(database, folder);
            await database.ExecuteAsync("""
                CREATE USER pii_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER pii_viewer;
                CREATE USER pii_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER pii_manager;
                CREATE USER pii_owner WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER pii_owner; ALTER ROLE db_owner ADD MEMBER pii_owner;
                """);

            // The imported synthetic rows: a name and a contact number reached the etp_r0xx tables.
            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.etp_r024 WHERE customer_name IS NOT NULL") > 0);
            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.etp_r025 WHERE customer_phone IS NOT NULL") > 0);

            var landing = await ListAsync(database, "SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name LIKE N'etp[_]landing[_]%' ORDER BY name");
            Assert.Equal(52, landing.Count);
            var columns = (await ListAsync(database, $"""
                SELECT t.name+N'|'+c.name+N'|'+CASE WHEN t.name LIKE N'etp[_]landing[_]%' THEN N'1' ELSE N'0' END+N'|'+CASE WHEN {Contact} THEN N'1' ELSE N'0' END
                FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
                WHERE t.schema_id=SCHEMA_ID(N'dbo') AND (t.name LIKE N'etp[_]landing[_]%' OR t.name LIKE N'etp[_]r[0-9]%') AND {Customer}
                ORDER BY t.name,c.name
                """)).Select(line => line.Split('|')).Select(part => new Column(part[0], part[1], part[2] == "1", part[3] == "1")).ToList();
            Assert.Contains(columns, column => column is { Table: "etp_landing_s014", Name: "mobilenumber", IsContact: true });
            Assert.Contains(columns, column => column is { Table: "etp_r024", Name: "customer_name", IsContact: false });
            Assert.DoesNotContain(columns, column => column.Name.Contains("phonepe", StringComparison.OrdinalIgnoreCase));

            // Viewer: every landing table, every customer column of an etp_r0xx table, the contacts and the user history.
            var viewerDenied = landing.Select(table => $"SELECT TOP(0) etp_row_id FROM dbo.[{table}]")
                .Concat(columns.Where(column => !column.IsLanding).Select(column => $"SELECT TOP(0) [{column.Name}] FROM dbo.[{column.Table}]"))
                .Append("SELECT TOP(0) email_address FROM dbo.sharing_contacts")
                .Append("SELECT TOP(0) display_name FROM dbo.sharing_contacts")
                .Append("SELECT TOP(0) windows_identity FROM dbo.application_user_history")
                .ToList();
            AssertAllDenied("pii_viewer", viewerDenied, await ProbeAsync(database, "pii_viewer", viewerDenied));

            // Store Manager: every contact column; every name and other customer column stays readable.
            var managerDenied = columns.Where(column => column.IsContact).Select(column => $"SELECT TOP(0) [{column.Name}] FROM dbo.[{column.Table}]").ToList();
            AssertAllDenied("pii_manager", managerDenied, await ProbeAsync(database, "pii_manager", managerDenied));
            var managerAllowed = columns.Where(column => !column.IsContact).Select(column => $"SELECT TOP(0) [{column.Name}] FROM dbo.[{column.Table}]")
                .Append("SELECT TOP(0) email_address,phone_e164 FROM dbo.sharing_contacts").ToList();
            AssertAllAllowed("pii_manager", managerAllowed, await ProbeAsync(database, "pii_manager", managerAllowed));

            // Owner (db_owner and etp_owner, as configure_application_role makes it): everything, as before.
            var everything = viewerDenied.Concat(managerDenied).ToList();
            AssertAllAllowed("pii_owner", everything, await ProbeAsync(database, "pii_owner", everything));

            // What a Viewer keeps: the non-customer columns of the etp_r0xx tables and the views over the landing tables
            // (ownership chaining; 0056/0057 already hide the name there).
            var viewerAllowed = new List<string>
            {
                "SELECT TOP(0) store_code,inv_number,inv_date FROM dbo.etp_r024",
                "SELECT TOP(1) report_code FROM dbo.v_service_status_view_rows",
                "SELECT COUNT(*) FROM dbo.v_service_job",
                "SELECT TOP(1) customer_name FROM dbo.v_invoice_customer_names",
                "SELECT TOP(1) display_name FROM dbo.application_users",
            };
            AssertAllAllowed("pii_viewer", viewerAllowed, await ProbeAsync(database, "pii_viewer", viewerAllowed));

            // The invoice customer name by role (lane L5's invoice summary reads it here).
            const string named = "SELECT COUNT(*) FROM dbo.v_invoice_customer_names WHERE customer_name IS NOT NULL";
            var all = await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_invoice_customer_names");
            var withName = await IntAsync(database, named);
            Assert.True(withName > 0);
            foreach (var user in new[] { "pii_owner", "pii_manager" })
                Assert.Equal(withName, await IntAsync(database, As(user, named)));
            Assert.Equal(all, await IntAsync(database, As("pii_viewer", "SELECT COUNT(*) FROM dbo.v_invoice_customer_names")));
            Assert.Equal(0, await IntAsync(database, As("pii_viewer", named)));
        }
        finally
        {
            await database.DisposeAsync();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [SqlFact]
    public async Task A_viewer_finds_its_own_role_but_no_other_application_user_and_the_health_check_still_maps_every_member()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await database.ExecuteAsync("""
                CREATE USER pii_rls_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER pii_rls_viewer;
                CREATE USER pii_rls_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER pii_rls_manager;
                CREATE USER pii_rls_owner WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER pii_rls_owner; ALTER ROLE db_owner ADD MEMBER pii_rls_owner;
                DECLARE @viewer nvarchar(200),@manager nvarchar(200);
                EXECUTE AS USER='pii_rls_viewer'; SET @viewer=SUSER_SNAME(); REVERT;
                EXECUTE AS USER='pii_rls_manager'; SET @manager=SUSER_SNAME(); REVERT;
                INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
                VALUES(@viewer,N'Synthetic viewer','VIEWER',1,N'test',N'Synthetic L1 row filter test'),
                      (@manager,N'Synthetic manager','STORE_MANAGER',1,N'test',N'Synthetic L1 row filter test');
                -- The health check maps a role member to its application user by the member's name.
                IF NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=N'pii_rls_viewer')
                  INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
                  VALUES(N'pii_rls_viewer',N'Synthetic viewer member','VIEWER',1,N'test',N'Synthetic L1 row filter test');
                """);
            var total = await IntAsync(database, "SELECT COUNT(*) FROM dbo.application_users");
            Assert.True(total >= 3);

            // Phase2OperationsRepository.LoadCurrentAccessAsync, word for word: a Viewer still finds its own role.
            const string access = """
                DECLARE @identity nvarchar(200)=SUSER_SNAME();
                SELECT COALESCE(u.role_code,'NONE')
                FROM (SELECT 1 anchor) x LEFT JOIN dbo.application_users u ON u.windows_identity=@identity
                """;
            Assert.Equal("VIEWER", await TextAsync(database, As("pii_rls_viewer", access)));
            Assert.Equal("STORE_MANAGER", await TextAsync(database, As("pii_rls_manager", access)));
            // ... and no other row.
            Assert.Equal(1, await IntAsync(database, As("pii_rls_viewer", "SELECT COUNT(*) FROM dbo.application_users")));
            Assert.Equal(0, await IntAsync(database, As("pii_rls_viewer", "SELECT COUNT(*) FROM dbo.application_users WHERE role_code='OWNER'")));
            // Everyone else sees every row, as before.
            foreach (var user in new[] { "pii_rls_manager", "pii_rls_owner" })
                Assert.Equal(total, await IntAsync(database, As(user, "SELECT COUNT(*) FROM dbo.application_users")));
            Assert.Equal(total, await IntAsync(database, "SELECT COUNT(*) FROM dbo.application_users"));

            // diag.load_role_permissions reads dbo.application_users as diag_catalog_reader, which the filter must not narrow:
            // through the C# store, the Viewer member still maps to its active application user.
            var snapshot = await new SqlServerHealthCheckStore(database.ConnectionString).LoadRolePermissionsAsync();
            Assert.Contains(snapshot.Memberships, member => member.RoleName == "etp_viewer" && member.IsActiveApplicationUser
                && member.ApplicationRoleCode == "VIEWER");
            Assert.Contains(snapshot.Memberships, member => member.RoleName == "etp_store_manager");
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task ImportSamplesAsync(SqlDatabaseFixture database, string folder)
    {
        Directory.CreateDirectory(folder);
        var samples = Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample");
        foreach (var family in new[] { "R001", "R003", "R013", "R022", "R024", "R025" })
        {
            var source = Directory.GetFiles(samples, family + "_*.xlsx").Single();
            File.Copy(source, Path.Combine(folder, Path.GetFileName(source)));
        }
        // The rows the probes need are asserted by the caller (an R024 name, an R025 phone).
        await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString)).RunAsync(folder, new("Synthetic Owner"));
    }

    private static string As(string user, string sql) =>
        $"EXECUTE AS USER='{user}'; BEGIN TRY {sql}; END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;";

    /// <summary>One batch per user: each probe in TRY/CATCH through sp_executesql; returns 0 or the error number per probe.</summary>
    private static async Task<int[]> ProbeAsync(SqlDatabaseFixture database, string user, IReadOnlyList<string> probes)
    {
        var batch = new System.Text.StringBuilder($"SET NOCOUNT ON; DECLARE @r nvarchar(max)=N''; EXECUTE AS USER='{user}';\n");
        foreach (var probe in probes)
            batch.Append("BEGIN TRY EXEC sys.sp_executesql N'").Append(probe.Replace("'", "''", StringComparison.Ordinal))
                .Append("'; SET @r+=N'0,'; END TRY BEGIN CATCH SET @r+=CONVERT(nvarchar(10),ERROR_NUMBER())+N','; END CATCH;\n");
        batch.Append("REVERT; SELECT @r probe_results;");
        await using var connection = new SqlConnection(TestSqlConnections.ForDatabase(database.Name, pooling: false));
        await connection.OpenAsync();
        await using var command = new SqlCommand(batch.ToString(), connection) { CommandTimeout = 300 };
        // Each probe that succeeds returns its own (empty or one-row) result set; the answer is the last one.
        string? text = null;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                if (reader.FieldCount == 1 && reader.GetName(0) == "probe_results" && await reader.ReadAsync()) text = reader.GetString(0);
            } while (await reader.NextResultAsync());
        }
        Assert.NotNull(text);
        var results = text.TrimEnd(',').Split(',').Select(int.Parse).ToArray();
        Assert.Equal(probes.Count, results.Length);
        return results;
    }

    // 229: permission denied on the object; 230: on the column.
    private static void AssertAllDenied(string user, IReadOnlyList<string> probes, int[] results)
    {
        var wrong = probes.Zip(results).Where(pair => pair.Second is not (229 or 230)).Select(pair => $"{pair.First} -> {pair.Second}").ToList();
        Assert.True(wrong.Count == 0, $"{user} should be refused:\n" + string.Join("\n", wrong));
    }

    private static void AssertAllAllowed(string user, IReadOnlyList<string> probes, int[] results)
    {
        var wrong = probes.Zip(results).Where(pair => pair.Second != 0).Select(pair => $"{pair.First} -> {pair.Second}").ToList();
        Assert.True(wrong.Count == 0, $"{user} should be allowed:\n" + string.Join("\n", wrong));
    }

    private static async Task<List<string>> ListAsync(SqlDatabaseFixture database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }

    private static async Task<int> IntAsync(SqlDatabaseFixture database, string sql) => Convert.ToInt32(await database.ExecuteAsync(sql, 300));

    private static async Task<string?> TextAsync(SqlDatabaseFixture database, string sql) => (await database.ExecuteAsync(sql, 300))?.ToString();
}
