using System.Text.RegularExpressions;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// Service interim (S-2, decision 15): migration 0048 is one file with three owned sections, A_SERVICE_STORE (L0),
/// B_SERVICE_LANDING (L2) and C_SERVICE_READ (L4), in that order. These tests read the shipped script; the SQL
/// behaviour (AW330 inactive, 51900 and 51904, a second run is a no-op, WLMHW/HEMW untouched) is covered by
/// <c>Etp.Reporting.SqlServer.IntegrationTests.Service.ServiceStoreMigrationTests</c>.
/// </summary>
public sealed class ServiceMigrationSkeletonTests
{
    private const string MigrationName = "0048_service_centre_interim.sql";
    private static readonly string[] Sections = ["A_SERVICE_STORE", "B_SERVICE_LANDING", "C_SERVICE_READ"];

    private static string MigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "database", "migrations");
    }

    private static string Script() => File.ReadAllText(Path.Combine(MigrationsDirectory(), MigrationName)).Replace("\r\n", "\n");

    private static string Section(string script, string name)
    {
        var begin = script.IndexOf($"-- >>> {name} begin\n", StringComparison.Ordinal);
        var end = script.IndexOf($"-- <<< {name} end", StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, $"Section {name} is missing or its markers are out of order.");
        return script[begin..end];
    }

    // Lines that are not blank and not comments.
    private static IEnumerable<string> Code(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

    [Fact]
    public void The_migration_is_the_only_0048_and_has_the_three_sections_in_order()
    {
        Assert.Equal([MigrationName], Directory.GetFiles(MigrationsDirectory(), "0048_*.sql").Select(Path.GetFileName));
        var script = Script();

        var positions = new List<int>();
        foreach (var name in Sections)
        {
            Assert.Single(Regex.Matches(script, $@"^-- >>> {name} begin$", RegexOptions.Multiline));
            Assert.Single(Regex.Matches(script, $@"^-- <<< {name} end$", RegexOptions.Multiline));
            var begin = script.IndexOf($"-- >>> {name} begin", StringComparison.Ordinal);
            var end = script.IndexOf($"-- <<< {name} end", StringComparison.Ordinal);
            Assert.True(end > begin, $"{name} ends before it begins.");
            positions.Add(begin);
            positions.Add(end);
        }

        Assert.Equal(positions.Order(), positions);
        Assert.Equal(3, Regex.Matches(script, @"^-- >>> ", RegexOptions.Multiline).Count);
        Assert.Equal(3, Regex.Matches(script, @"^-- <<< ", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void The_script_runs_in_the_runner_transaction_only()
    {
        var script = Script();
        Assert.StartsWith("SET XACT_ABORT ON;", Code(script).First(), StringComparison.Ordinal);
        var code = string.Join("\n", Code(script));
        Assert.DoesNotMatch(new Regex(@"\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b", RegexOptions.IgnoreCase), code);
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), code);
        Assert.Contains("must ship after 0046/0047", script, StringComparison.Ordinal);
        Assert.Contains("decision 15, 3 Oct 2026", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_but_SET_XACT_ABORT_lies_outside_the_sections()
    {
        var script = Script();
        var outside = script;
        foreach (var name in Sections)
        {
            var begin = outside.IndexOf($"-- >>> {name} begin", StringComparison.Ordinal);
            var end = outside.IndexOf($"-- <<< {name} end", StringComparison.Ordinal) + $"-- <<< {name} end".Length;
            outside = outside[..begin] + outside[end..];
        }

        Assert.Equal(["SET XACT_ABORT ON;"], Code(outside));
    }

    [Fact]
    public void Section_A_guards_each_insert_and_never_updates_a_store()
    {
        var section = Section(Script(), "A_SERVICE_STORE");
        var inserts = Regex.Matches(section, @"\bINSERT\s+dbo\.(\w+)", RegexOptions.IgnoreCase);
        Assert.Equal(["business_units", "stores"], inserts.Select(match => match.Groups[1].Value));

        foreach (Match insert in inserts)
        {
            var table = insert.Groups[1].Value;
            var key = table == "stores" ? "store_code='AW330'" : "business_unit_code='SERVICE'";
            var guard = section.LastIndexOf($"IF NOT EXISTS(SELECT 1 FROM dbo.{table} WHERE {key})", insert.Index, StringComparison.Ordinal);
            Assert.True(guard >= 0, $"The INSERT into dbo.{table} has no IF NOT EXISTS guard on {key}.");
            Assert.DoesNotContain("INSERT", section[(guard + 1)..insert.Index], StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(new Regex(@"\b(UPDATE|DELETE|MERGE)\s+dbo\.", RegexOptions.IgnoreCase), section);
        Assert.Contains("'AW330',N'Service Centre AW330',b.business_unit_id,0,", section, StringComparison.Ordinal);
        Assert.Contains("VALUES('SERVICE',N'Service Centre',1)", section, StringComparison.Ordinal);
        Assert.Contains("PRINT N'0048: a store AW330 already exists", section, StringComparison.Ordinal);
        Assert.DoesNotContain("WLMHW", section, StringComparison.Ordinal);
        Assert.DoesNotContain("HEMW", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Section_A_trigger_refuses_an_active_Service_store_with_51900()
    {
        var section = Section(Script(), "A_SERVICE_STORE");
        Assert.Contains("EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_stores_service_unit_inactive ON dbo.stores\nAFTER INSERT, UPDATE AS", section, StringComparison.Ordinal);
        Assert.Contains("THROW 51900,''A Service Centre store cannot be made an active shop store.'',1;", section, StringComparison.Ordinal);
        Assert.Contains("business_unit_code=''SERVICE'' AND i.is_active=1", section, StringComparison.Ordinal);
        var numbers = Regex.Matches(section, @"THROW\s+(\d+)").Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.Equal(["51900"], numbers);
    }
}
