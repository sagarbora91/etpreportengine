using System.Text.Json;
using System.Text.RegularExpressions;

namespace Etp.Reporting.SqlServer.Tests.Security;

/// <summary>
/// 1.11.2, lane L1 (AUDIT-2 S-01, decision 37): migration 0059 takes the customer data away from the roles that may not
/// read it directly. A Viewer gets no landing table and no customer column of an etp_r0xx table, a Store Manager no contact
/// column (names stay), a Viewer no sharing contact and only its own application user row. These tests read the migration
/// texts and ExpectedRolePermissions.json only; the behaviour per role is CustomerDataPermissionSqlTests (SQL suite).
/// </summary>
public sealed partial class CustomerDataPermissionMigrationTextTests
{
    private const string MigrationName = "0059_customer_data_permissions.sql";
    private const string SectionName = "L1_PII_PERMISSIONS";

    // 0056's role rule, character for character (inside EXEC(N'...'), so the quotes are doubled).
    private const string Rule =
        "IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1";

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string MigrationsDirectory() => Path.Combine(RepositoryRoot(), "database", "migrations");

    private static string Read(string name) => File.ReadAllText(Path.Combine(MigrationsDirectory(), name)).Replace("\r\n", "\n");

    private static string Section(string script)
    {
        var beginMarker = $"-- >>> {SectionName} begin\n";
        var begin = script.IndexOf(beginMarker, StringComparison.Ordinal);
        var end = script.IndexOf($"-- <<< {SectionName} end", StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, $"Section {SectionName} is missing or its markers are out of order.");
        return script[(begin + beginMarker.Length)..end];
    }

    private static IEnumerable<string> Code(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

    // The same column patterns as 0059 (LOWER(name) LIKE ...): contact = a way to reach the customer.
    internal static bool IsContact(string column)
    {
        var name = column.ToLowerInvariant();
        return name.Contains("mobile") || (name.Contains("phone") && !name.Contains("phonepe")) || name.Contains("mail")
            || name.Contains("contact") || name.Contains("address") || name.Contains("landline");
    }

    internal static bool IsCustomerColumn(string column)
    {
        var name = column.ToLowerInvariant();
        return IsContact(column) || name.Contains("customer") || name.Contains("cust_name") || name.Contains("custname") || name.Contains("gstin");
    }

    // Every landing and etp_r0xx table the migrations create, with its columns (none is altered after its CREATE TABLE).
    private static Dictionary<string, List<string>> CustomerTables()
    {
        var tables = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(MigrationsDirectory(), "*.sql").Order(StringComparer.Ordinal))
        {
            var script = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.DoesNotMatch(new Regex(@"ALTER\s+TABLE\s+dbo\.\[?etp_(landing_|r\d)\w*\]?\s+ADD\b", RegexOptions.IgnoreCase), script);
            foreach (Match table in TableBlock().Matches(script))
            {
                var columns = Regex.Matches(table.Groups["body"].Value, @"^\s*\[?(?<column>\w+)\]?\s+\w", RegexOptions.Multiline)
                    .Select(match => match.Groups["column"].Value)
                    .Where(column => column.ToUpperInvariant() is not ("CONSTRAINT" or "PRIMARY" or "UNIQUE" or "INDEX"))
                    .ToList();
                tables[table.Groups["name"].Value] = columns;
            }
        }
        return tables;
    }

    private sealed record Entry(string Role, string Object, string Permission, string State, string? Decision, string? Expect);

    private static List<Entry> Matrix()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Infrastructure.SqlServer", "HealthCheck", "ExpectedRolePermissions.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("permissions").EnumerateArray().Select(entry => new Entry(
            entry.GetProperty("role").GetString()!, entry.GetProperty("object").GetString() ?? "",
            entry.GetProperty("permission").GetString()!, entry.GetProperty("state").GetString()!,
            entry.TryGetProperty("decision", out var decision) ? decision.GetString() : null,
            entry.TryGetProperty("expect", out var expect) ? expect.GetString() : null)).ToList();
    }

    [Fact]
    public void The_migration_is_the_only_0059_and_holds_one_section_without_transactions_or_GO()
    {
        Assert.Equal([MigrationName], Directory.GetFiles(MigrationsDirectory(), "0059_*.sql").Select(Path.GetFileName));
        var script = Read(MigrationName);
        Assert.Single(Regex.Matches(script, $@"^-- >>> {SectionName} begin$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(script, $@"^-- <<< {SectionName} end$", RegexOptions.Multiline));
        Assert.Equal(["SET XACT_ABORT ON;"], Code(script.Replace(Section(script), "")));
        Assert.DoesNotMatch(new Regex(@"\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b", RegexOptions.IgnoreCase), string.Join("\n", Code(script)));
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), script);
    }

    [Fact]
    public void The_section_only_denies_and_grants_nothing_but_the_new_views_SELECT()
    {
        var code = string.Join("\n", Code(Section(Read(MigrationName))));
        Assert.DoesNotMatch(new Regex(@"\b(REVOKE|DROP|TRUNCATE|ALTER\s+ROLE|ALTER\s+TABLE|ALTER\s+AUTHORIZATION)\b", RegexOptions.IgnoreCase), code);
        Assert.DoesNotMatch(new Regex(@"\bDELETE\s+(FROM\s+)?dbo\.|\bUPDATE\s+dbo\.|\bINSERT\s+dbo\.", RegexOptions.IgnoreCase), code);
        Assert.Equal(["GRANT SELECT ON dbo.v_invoice_customer_names TO etp_viewer,etp_store_manager,etp_owner;"],
            Regex.Matches(code, @"\bGRANT\b[^;]*;", RegexOptions.IgnoreCase).Select(match => match.Value));
        Assert.Contains("DENY SELECT ON dbo.sharing_contacts TO etp_viewer;", code, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT ON dbo.application_user_history TO etp_viewer;", code, StringComparison.Ordinal);
        // Sections 1-3 each build their DENY statements from the catalog once.
        Assert.Equal(3, Regex.Matches(code, @"N'DENY SELECT ").Count);
    }

    [Fact]
    public void A_viewer_sees_only_its_own_application_user_row_and_everyone_else_every_row()
    {
        var section = Section(Read(MigrationName));
        var function = Regex.Match(section, @"CREATE FUNCTION dbo\.application_users_visible_row\(@windows_identity nvarchar\(200\)\)\nRETURNS TABLE WITH SCHEMABINDING AS\n(?<body>.*?)'\);", RegexOptions.Singleline);
        Assert.True(function.Success, "The row filter function is missing.");
        var body = function.Groups["body"].Value;
        Assert.Contains("COALESCE(IS_MEMBER(N''etp_viewer''),0)=0", body, StringComparison.Ordinal);
        foreach (var role in new[] { "db_owner", "etp_owner", "etp_store_manager" })
            Assert.Contains($"OR COALESCE(IS_MEMBER(N''{role}''),0)=1", body, StringComparison.Ordinal);
        // The very comparison Phase2OperationsRepository.LoadCurrentAccessAsync makes, so a Viewer still finds its role.
        Assert.Contains("OR @windows_identity=SUSER_SNAME()", body, StringComparison.Ordinal);
        Assert.Contains("ADD FILTER PREDICATE dbo.application_users_visible_row(windows_identity) ON dbo.application_users", section, StringComparison.Ordinal);
        Assert.Contains("WITH (STATE=ON,SCHEMABINDING=ON)", section, StringComparison.Ordinal);
        Assert.DoesNotContain("BLOCK PREDICATE", section, StringComparison.OrdinalIgnoreCase);
        // The column type of dbo.application_users.windows_identity (0011).
        Assert.Contains("windows_identity nvarchar(200) NOT NULL,", Read("0011_phase2_operations.sql"), StringComparison.Ordinal);
    }

    [Fact]
    public void After_0059_every_view_that_selects_a_customer_name_gives_it_by_role()
    {
        var latest = new Dictionary<string, (string File, string Body)>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(MigrationsDirectory(), "*.sql").Order(StringComparer.Ordinal))
        {
            var script = File.ReadAllText(path).Replace("\r\n", "\n");
            var blocks = AnyViewBlock().Matches(script);
            Assert.True(Regex.Matches(script, @"CREATE (OR ALTER )?VIEW dbo\.\w+ AS").Count == blocks.Count, $"{Path.GetFileName(path)}: a view was not parsed.");
            foreach (Match block in blocks) latest[block.Groups["name"].Value] = (Path.GetFileName(path), block.Groups["body"].Value);
        }
        var named = latest.Where(view => Regex.IsMatch(view.Value.Body, @"customer_?name|party_?name|cust_?name", RegexOptions.IgnoreCase))
            .Select(view => view.Key).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            ["v_invoice_customer_names", "v_service_job", "v_service_job_status_current", "v_service_pending_current", "v_service_status_view_facts", "v_service_status_view_rows"],
            named);
        foreach (var view in named) Assert.Contains(Rule, latest[view].Body, StringComparison.Ordinal);
        Assert.Equal(MigrationName, latest["v_invoice_customer_names"].File);
        var invoice = latest["v_invoice_customer_names"].Body;
        Assert.Contains($"CASE WHEN {Rule} THEN d.customer_name END customer_name", invoice, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.etp_r024 d", invoice, StringComparison.Ordinal);
        // No view anywhere hands out a contact column.
        foreach (var (view, (_, body)) in latest)
            foreach (var word in new[] { "mobile", "customer_phone", "email", "contactnumber", "customer_address", "landline" })
                Assert.False(body.Contains(word, StringComparison.OrdinalIgnoreCase), $"{view} selects {word}.");
    }

    [Fact]
    public void The_permission_matrix_lists_exactly_the_denies_0059_generates_from_the_migrated_tables()
    {
        var tables = CustomerTables();
        var landing = tables.Keys.Where(name => name.StartsWith("etp_landing_", StringComparison.Ordinal)).ToList();
        Assert.Equal(52, landing.Count);
        var expected = new HashSet<(string Role, string Object)>();
        foreach (var table in landing) expected.Add(("etp_viewer", table));
        foreach (var (table, columns) in tables)
            foreach (var column in columns.Where(IsCustomerColumn))
            {
                if (!table.StartsWith("etp_landing_", StringComparison.Ordinal)) expected.Add(("etp_viewer", $"{table}({column})"));
                if (IsContact(column)) expected.Add(("etp_store_manager", $"{table}({column})"));
            }
        expected.Add(("etp_viewer", "sharing_contacts"));
        expected.Add(("etp_viewer", "application_user_history"));

        var matrix = Matrix();
        var d37 = matrix.Where(entry => entry.Decision == "D37" && entry.Expect is null).ToList();
        Assert.All(d37, entry => Assert.Equal(("SELECT", "DENY"), (entry.Permission, entry.State)));
        Assert.Equal(expected.Order(), d37.Select(entry => (entry.Role, entry.Object)).Order());

        // Spot checks: the customer's phone and e-mail are gone for a Store Manager, the name and a PhonePe amount stay.
        Assert.Contains(("etp_store_manager", "etp_landing_s014(mobilenumber)"), expected);
        Assert.Contains(("etp_store_manager", "etp_landing_s014(endcustomercontactnumber)"), expected);
        Assert.Contains(("etp_store_manager", "etp_r024(customer_phone)"), expected);
        Assert.Contains(("etp_viewer", "etp_r024(customer_name)"), expected);
        Assert.DoesNotContain(expected, entry => entry.Role == "etp_store_manager" && Regex.IsMatch(entry.Object, @"\((customer_?name|endcustomername|cust_?name)\)"));
        Assert.DoesNotContain(expected, entry => entry.Object.Contains("phonepe", StringComparison.Ordinal));

        // Nothing in the matrix gives a Viewer SELECT on a landing table, an etp_r0xx table or a sharing contact.
        Assert.DoesNotContain(matrix, entry => entry.Role == "etp_viewer" && entry.State.StartsWith("GRANT", StringComparison.Ordinal)
            && entry.Permission == "SELECT" && Regex.IsMatch(entry.Object, @"^(etp_landing_|etp_r\d|sharing_contacts|application_user)"));
        // The new view: SELECT for the three roles, no writes for the two non-Owner roles (0057's pattern).
        foreach (var role in new[] { "etp_viewer", "etp_store_manager", "etp_owner" })
            Assert.Contains(matrix, entry => entry == new Entry(role, "v_invoice_customer_names", "SELECT", "GRANT", null, null));
        foreach (var role in new[] { "etp_viewer", "etp_store_manager" })
            foreach (var permission in new[] { "DELETE", "INSERT", "UPDATE" })
                Assert.Contains(matrix, entry => entry == new Entry(role, "v_invoice_customer_names", permission, "DENY", null, null));
    }

    [Fact]
    public void The_migration_records_S17_as_an_accepted_risk()
    {
        var header = Read(MigrationName).Split("-- >>> ")[0];
        Assert.Contains("Accepted risk (AUDIT-2 S-17", header, StringComparison.Ordinal);
        Assert.Contains("db_owner and holds server ALTER ANY LOGIN", header, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"CREATE TABLE dbo\.\[(?<name>etp_(landing_\w+|r\d\w*))\] \(\n(?<body>.*?)\n\);?", RegexOptions.Singleline)]
    private static partial Regex TableBlock();

    [GeneratedRegex(@"EXEC\(N'CREATE (OR ALTER )?VIEW dbo\.(?<name>\w+) AS\s*\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex AnyViewBlock();
}
