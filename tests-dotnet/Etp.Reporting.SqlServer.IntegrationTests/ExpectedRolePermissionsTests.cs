using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.0 L4, design 5.5 and Q16 (decision 36): the permissions of the four etp roles on a fresh database with every
/// migration applied must equal <c>ExpectedRolePermissions.json</c>. A migration that changes a grant fails here until
/// the file is regenerated (and the change is named in the CHANGELOG). To regenerate: run this test with
/// <c>ETP_REGENERATE_EXPECTED_PERMISSIONS=1</c>; it rewrites the source file and still fails once, so the diff is seen.
/// Without the variable it writes <c>ExpectedRolePermissions.generated.json</c> next to the test assembly.
/// </summary>
public sealed class ExpectedRolePermissionsTests
{
    private const string RegenerateVariable = "ETP_REGENERATE_EXPECTED_PERMISSIONS";

    /// <summary>The catalog itself, to prove <c>diag.load_role_permissions</c> returns exactly the etp roles' rows.</summary>
    private const string CatalogSql = """
        SELECT DISTINCT r.name, p.class_desc,
               CASE WHEN p.class = 1 THEN SCHEMA_NAME(o.schema_id) WHEN p.class = 3 THEN SCHEMA_NAME(p.major_id) END,
               CASE WHEN p.class = 1 THEN o.name + CASE WHEN p.minor_id <> 0 THEN N'(' + COL_NAME(p.major_id, p.minor_id) + N')' ELSE N'' END END,
               p.permission_name, p.state_desc
        FROM sys.database_permissions AS p
        JOIN sys.database_principals AS r ON r.principal_id = p.grantee_principal_id
        LEFT JOIN sys.objects AS o ON p.class = 1 AND o.object_id = p.major_id
        WHERE r.name IN (N'etp_owner', N'etp_store_manager', N'etp_viewer', N'etp_automation');
        """;

    [Fact]
    public async Task A_fresh_database_has_exactly_the_expected_role_permissions()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var snapshot = await new SqlServerHealthCheckStore(database.ConnectionString).LoadRolePermissionsAsync();
            var fromProcedure = snapshot.Permissions.Select(RolePermissionKey.From).ToHashSet();
            var fromCatalog = (await ReadCatalogAsync(database.ConnectionString)).Select(RolePermissionKey.From).ToHashSet();
            Assert.True(fromCatalog.SetEquals(fromProcedure),
                "diag.load_role_permissions does not return the etp roles' catalog rows:" + Environment.NewLine
                + Diff(fromCatalog, fromProcedure, "catalog only", "procedure only"));

            var expected = ExpectedRolePermissionMatrix.LoadEmbedded();
            Assert.True(expected is not null, "ExpectedRolePermissions.json is not embedded (EmbeddedResource in Etp.Reporting.Infrastructure.SqlServer.csproj).");

            var violated = expected!.Absent.Where(entry => fromProcedure.Contains(entry.Key)).ToArray();
            var differs = !expected.Expected.SetEquals(fromProcedure) || violated.Length > 0;
            if (differs)
            {
                var text = ExpectedRolePermissionMatrix.Generate(snapshot.Permissions, expected);
                var written = Path.Combine(AppContext.BaseDirectory, "ExpectedRolePermissions.generated.json");
                await File.WriteAllTextAsync(written, text);
                if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1" && FindSourceFile() is { } source)
                {
                    await File.WriteAllTextAsync(source, text);
                    written = source;
                }
                Assert.Fail(
                    "The etp role permissions differ from ExpectedRolePermissions.json (Q16: regenerate it and name the change in the CHANGELOG)."
                    + Environment.NewLine + "Regenerated file: " + written + Environment.NewLine
                    + Diff(fromProcedure, expected.Expected, "+ in the database, not expected", "- expected, not in the database")
                    + string.Concat(violated.Select(entry => Environment.NewLine + "! decision " + entry.Decision + " violated: " + Describe(entry.Key))));
            }

            // The same comparison SEC-01 makes at run time.
            Assert.Equal(HealthOutcome.Pass, SecurityProbes.EvaluatePermissions(snapshot.Permissions, expected).Outcome);
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task<IReadOnlyList<RolePermission>> ReadCatalogAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(CatalogSql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<RolePermission>();
        while (await reader.ReadAsync())
        {
            rows.Add(new RolePermission(
                reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }
        return rows;
    }

    private static string Diff(IReadOnlySet<RolePermissionKey> left, IReadOnlySet<RolePermissionKey> right, string leftLabel, string rightLabel)
    {
        var onlyLeft = ExpectedRolePermissionMatrix.Sorted(left.Where(key => !right.Contains(key))).Select(key => leftLabel + ": " + Describe(key));
        var onlyRight = ExpectedRolePermissionMatrix.Sorted(right.Where(key => !left.Contains(key))).Select(key => rightLabel + ": " + Describe(key));
        var lines = onlyLeft.Concat(onlyRight).ToArray();
        return string.Join(Environment.NewLine, lines.Take(80)) + (lines.Length > 80 ? Environment.NewLine + $"... and {lines.Length - 80} more" : "");
    }

    private static string Describe(RolePermissionKey key) =>
        $"{key.Role} {key.State} {key.Permission} on {key.ClassDesc} {key.Schema}.{key.Object}";

    private static string? FindSourceFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Etp.Reporting.Infrastructure.SqlServer", "HealthCheck", "ExpectedRolePermissions.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
