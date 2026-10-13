using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 A3 guards on the repository (text, no database). New views, procedures and functions go in database/objects, not
/// in migrations; a migration never calls a repeatable procedure (objects apply after migrations); every object file
/// parses; each GRANTS block equals its rows in ExpectedRolePermissions.json in both directions; migration 0062 holds the
/// 1.12 audit types.
/// </summary>
public sealed partial class SchemaObjectGuardTests
{
    /// <summary>Migrations before this number may create views, procedures and functions; from it on they may not.</summary>
    private const int FirstMigrationWithObjectFiles = 62;

    [Fact]
    public async Task Every_object_file_in_the_repository_parses_and_names_one_object_once()
    {
        var set = await new DirectorySchemaObjectSource(SchemaSourceText.ObjectsDirectory()).DiscoverAsync();
        Assert.All(set.Objects, file => Assert.Equal(file.FileName, $"{file.Layer:00}-{file.Schema}.{file.Name}.sql"));
    }

    [Fact]
    public void No_migration_from_0062_creates_or_alters_a_view_procedure_or_function_unless_it_is_excluded()
    {
        var excluded = ExcludedNames();
        var problems = new List<string>();
        foreach (var (id, sql) in MigrationsFrom(FirstMigrationWithObjectFiles))
            foreach (Match match in ModuleDefinition().Matches(sql))
            {
                var name = Clean(match.Groups["name"].Value);
                if (!excluded.Contains(name)) problems.Add($"{id}: {match.Groups["verb"].Value} {match.Groups["kind"].Value} {name} (put it in database/objects)");
            }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task No_migration_from_0062_calls_a_procedure_that_is_an_object_file()
    {
        var set = await new DirectorySchemaObjectSource(SchemaSourceText.ObjectsDirectory()).DiscoverAsync();
        var procedures = set.Objects.Where(file => file.Kind == SchemaObjectKind.Procedure).Select(file => file.ObjectName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var (id, sql) in MigrationsFrom(FirstMigrationWithObjectFiles))
            foreach (Match match in ProcedureCall().Matches(sql))
            {
                var name = Clean(match.Groups["name"].Value);
                if (procedures.Contains(name)) problems.Add($"{id} calls {name}, which applies only after the migrations.");
            }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_guards_see_a_view_in_a_migration_and_a_call_inside_EXEC_text()
    {
        Assert.Equal("dbo.v_x", Clean(ModuleDefinition().Match("EXEC(N'CREATE OR ALTER VIEW dbo.v_x AS SELECT 1 AS a');").Groups["name"].Value));
        Assert.Equal("dbo.p", Clean(ModuleDefinition().Match("ALTER PROC [dbo].[p] AS SELECT 1").Groups["name"].Value));
        Assert.False(ModuleDefinition().IsMatch("CREATE TRIGGER dbo.trg_x ON dbo.t AFTER UPDATE AS SELECT 1"));
        Assert.Equal("dbo.record_operational_audit", Clean(ProcedureCall().Match("EXEC dbo.record_operational_audit ''Approval''").Groups["name"].Value));
        Assert.False(ProcedureCall().IsMatch("EXEC(N'SELECT 1')"));
    }

    [Fact]
    public async Task Every_GRANTS_block_equals_its_rows_in_ExpectedRolePermissions_in_both_directions()
    {
        var matrix = ExpectedRolePermissionMatrix.LoadEmbedded();
        Assert.NotNull(matrix);
        var roles = ExpectedRolePermissionMatrix.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var set = await new DirectorySchemaObjectSource(SchemaSourceText.ObjectsDirectory()).DiscoverAsync();
        var problems = new List<string>();
        foreach (var file in set.Objects)
        {
            var fromFile = SchemaObjectFiles.PermissionRows(file)
                .Where(row => roles.Contains(row.Role))
                .Select(row => new RolePermissionKey(row.Role, "OBJECT_OR_COLUMN", row.Schema, row.Object, row.Permission, row.State))
                .ToHashSet();
            var fromMatrix = matrix!.Expected
                .Where(key => key.ClassDesc.Equals("OBJECT_OR_COLUMN", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(key.Schema, file.Schema, StringComparison.OrdinalIgnoreCase)
                    && key.Object is { } name
                    && (name.Equals(file.Name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(file.Name + "(", StringComparison.OrdinalIgnoreCase)))
                .ToHashSet();
            problems.AddRange(fromFile.Except(fromMatrix).Select(key => $"{file.FileName}: {key.State} {key.Permission} to {key.Role} is not in ExpectedRolePermissions.json"));
            problems.AddRange(fromMatrix.Except(fromFile).Select(key => $"{file.FileName}: ExpectedRolePermissions.json expects {key.State} {key.Permission} to {key.Role} on {key.Object}; the GRANTS block does not give it"));
        }
        // The integrator regenerates ExpectedRolePermissions.json at each gate; a new object file fails here until then.
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Migration_0062_preallocates_every_1_12_audit_type_and_keeps_the_old_ones()
    {
        var sql = File.ReadAllText(Path.Combine(SchemaSourceText.MigrationsDirectory(), "0062_schema_objects_journal.sql"));
        var check = sql[sql.IndexOf("ADD CONSTRAINT CK_operational_audit_type", StringComparison.Ordinal)..];
        foreach (var type in new[] { "CollectionsSetup", "CollectionsImport", "CashDepositConfirmed", "SoftwareUpdate", "ShopResultPublished", "TallyReadback", "ImportFixAction" })
            Assert.Contains($"'{type}'", check, StringComparison.Ordinal);
        var previous = File.ReadAllText(Path.Combine(SchemaSourceText.MigrationsDirectory(), "0015_operational_audit_contract.sql"));
        foreach (Match type in Regex.Matches(previous[previous.IndexOf("ADD CONSTRAINT", StringComparison.Ordinal)..], "'([A-Za-z]+)'"))
            Assert.Contains(type.Value, check, StringComparison.Ordinal);
        // The pre-check (the same list) guards the WITH CHECK constraint, and the new objects are guarded with OBJECT_ID.
        Assert.Single(MigrationPrechecks.Extract(sql));
        Assert.Contains("OBJECT_ID(N'dbo.operational_audit',N'U')", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_source_text_prefers_the_object_file_and_falls_back_to_the_last_migration()
    {
        // record_operational_audit was created by 0022 and has no later CREATE OR ALTER (until the objects baseline gives it a file).
        var text = SchemaSourceText.Current("dbo.record_operational_audit");
        Assert.Contains("record_operational_audit", text, StringComparison.Ordinal);
        if (SchemaSourceText.ObjectFile("dbo.record_operational_audit") is { } file)
            Assert.Equal(File.ReadAllText(file), text);
        Assert.Throws<InvalidOperationException>(() => SchemaSourceText.Current("dbo.no_such_object_a3"));
    }

    private static HashSet<string> ExcludedNames()
    {
        var path = Path.Combine(SchemaSourceText.ObjectsDirectory(), "EXCLUDED.md");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return names;
        foreach (Match match in Regex.Matches(File.ReadAllText(path), @"`(?<name>[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*)`"))
            names.Add(match.Groups["name"].Value);
        return names;
    }

    private static IEnumerable<(string Id, string Sql)> MigrationsFrom(int number) =>
        Directory.EnumerateFiles(SchemaSourceText.MigrationsDirectory(), "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => (Id: Path.GetFileNameWithoutExtension(path), Path: path))
            .Where(item => item.Id.Length > 4 && int.TryParse(item.Id[..4], out var value) && value >= number)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => (item.Id, File.ReadAllText(item.Path)));

    private static string Clean(string name) => WhiteSpace().Replace(name.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal), "");

    [GeneratedRegex(@"\b(?<verb>CREATE(\s+OR\s+ALTER)?|ALTER)\s+(?<kind>VIEW|PROCEDURE|PROC|FUNCTION)\s+(?<name>(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModuleDefinition();

    [GeneratedRegex(@"\bEXEC(UTE)?\s+(@[A-Za-z0-9_]+\s*=\s*)?(?<name>(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProcedureCall();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhiteSpace();
}
