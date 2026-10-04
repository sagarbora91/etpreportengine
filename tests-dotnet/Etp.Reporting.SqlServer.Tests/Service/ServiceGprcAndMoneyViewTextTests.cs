using System.Text.Json;
using System.Text.RegularExpressions;
using Etp.Reporting.Import.Service;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// Lane L10 (decision 16): the two read views waiting for section C_SERVICE_READ of 0048 (lane L4 generates section C),
/// kept in scripts/service-centre/c_service_read_gprc_and_money.sql. The GPRC union reads S023 and S041 without counting
/// a claim document twice (S041 wins per document); the manual-money view marks the one shop whose SERVICE_* entries
/// are compared. Every landing column the views name must exist in the frozen families.spec.json.
/// </summary>
public sealed partial class ServiceGprcAndMoneyViewTextTests
{
    private const string BlockBegin = "-- >>> L10 block begin";
    private const string BlockEnd = "-- <<< L10 block end";

    [GeneratedRegex(@"\br\.([a-z_][a-z0-9_]*)")]
    private static partial Regex LandingColumn();

    private static string Script() => File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "service-centre", "c_service_read_gprc_and_money.sql")).Replace("\r\n", "\n");

    private static string Block()
    {
        var text = Script();
        var begin = text.IndexOf(BlockBegin + "\n", StringComparison.Ordinal);
        var end = text.IndexOf("\n" + BlockEnd, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, "The script must hold one L10 block between its marker lines.");
        Assert.Single(Regex.Matches(text, Regex.Escape(BlockBegin)));
        Assert.Single(Regex.Matches(text, Regex.Escape(BlockEnd)));
        return text[(begin + BlockBegin.Length + 1)..end];
    }

    private static string View(string block, string name)
    {
        var start = block.IndexOf($"EXEC(N'CREATE OR ALTER VIEW dbo.{name} AS", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} is missing.");
        var end = block.IndexOf("');", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{name} does not end with ');'.");
        return block[start..(end + 3)];
    }

    [Fact]
    public void The_block_holds_only_the_two_views_as_section_C_writes_them()
    {
        var block = Block();
        Assert.Equal(["v_service_gprc_claims", "v_service_manual_money"],
            Regex.Matches(block, @"^EXEC\(N'CREATE OR ALTER VIEW dbo\.(\w+) AS$", RegexOptions.Multiline).Select(match => match.Groups[1].Value));
        Assert.DoesNotContain("BEGIN TRAN", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT", block, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?im)^\s*(INSERT|UPDATE|DELETE|MERGE)\b", block);
        // Inside EXEC(N'...') every quote is doubled: no lone quote between the opening and the closing.
        foreach (var name in new[] { "v_service_gprc_claims", "v_service_manual_money" })
        {
            var view = View(block, name);
            var body = view[("EXEC(N'".Length)..^"');".Length];
            Assert.DoesNotMatch("(?<!')'(?!')", body.Replace("''", ""));
        }
    }

    [Fact]
    public void The_GPRC_union_reads_each_family_by_its_own_date_rule_and_lets_S041_win_per_claim_document()
    {
        var view = View(Block(), "v_service_gprc_claims");
        Assert.Contains("FROM dbo.etp_landing_s041 r JOIN dbo.v_service_datelog_readings w ON w.report_code=''S041'' AND w.reading_rank=1", view, StringComparison.Ordinal);
        Assert.Contains("AND w.import_file_id=r.import_file_id AND w.business_date=r.transaction_date", view, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.etp_landing_s023 r JOIN dbo.v_service_datelog_readings w ON w.report_code=''S023'' AND w.reading_rank=1", view, StringComparison.Ordinal);
        Assert.Contains("AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate", view, StringComparison.Ordinal);
        // The union rule.
        Assert.Contains("WHERE NOT EXISTS(SELECT 1 FROM claims c WHERE c.document_number=h.document_number)", view, StringComparison.Ordinal);
        Assert.Contains("AND (h.document_number IS NOT NULL OR NOT EXISTS(SELECT 1 FROM claims c WHERE c.business_date=h.business_date))", view, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(view, @"\bUNION ALL\b"));
        Assert.DoesNotMatch(@"\bUNION\s+SELECT\b", view);
        // The two SELECTs give the same columns in the same order.
        Assert.Contains("SELECT ''S041'' report_code,business_date,document_number,job_order_number,item_id,quantity,account_number,price,net_amount,", view, StringComparison.Ordinal);
        Assert.Contains("SELECT ''S023'',h.business_date,h.document_number,h.job_order_number,h.item_id,h.quantity,h.account_number,h.price,h.net_amount,", view, StringComparison.Ordinal);
        Assert.Equal(["S023", "S041"], ServiceInterimFamilies.GprcClaimFamilies);
    }

    [Fact]
    public void Every_landing_column_the_GPRC_union_names_is_in_the_frozen_spec()
    {
        var view = View(Block(), "v_service_gprc_claims");
        var claims = view[view.IndexOf("claims AS (", StringComparison.Ordinal)..view.IndexOf("), history AS (", StringComparison.Ordinal)];
        var history = view[view.IndexOf("), history AS (", StringComparison.Ordinal)..view.IndexOf("SELECT ''S041''", StringComparison.Ordinal)];
        foreach (var (code, part) in new[] { ("S041", claims), ("S023", history) })
        {
            var columns = SpecColumns(code);
            var named = LandingColumn().Matches(part).Select(match => match.Groups[1].Value).Where(name => name != "import_file_id").Distinct().ToArray();
            Assert.True(named.Length >= 15, $"{code}: only {named.Length} columns named.");
            Assert.All(named, name => Assert.True(columns.ContainsKey(name), $"{code} has no column '{name}' in families.spec.json."));
            Assert.Equal("Date", columns[code == "S041" ? "transaction_date" : "transdate"]);
        }
    }

    [Fact]
    public void The_manual_money_view_marks_one_shop_lists_every_service_field_and_leaves_WDC_without_a_tender()
    {
        var view = View(Block(), "v_service_manual_money");
        Assert.Contains("FROM dbo.manual_operational_inputs m LEFT JOIN dbo.stores s ON s.store_code=m.store_code", view, StringComparison.Ordinal);
        Assert.Contains("WHERE m.field_code LIKE ''SERVICE[_]%'' AND m.numeric_value IS NOT NULL", view, StringComparison.Ordinal);
        Assert.Contains("CASE m.field_code WHEN ''SERVICE_CASH'' THEN ''CASH'' WHEN ''SERVICE_CARD'' THEN ''CARD'' WHEN ''SERVICE_UPI'' THEN ''UPI'' END tender", view, StringComparison.Ordinal);
        Assert.DoesNotContain("SERVICE_WDC", view, StringComparison.Ordinal);
        // Exactly one store code decides is_service_money_shop, and it is the seeded Titan World code of migration 0011.
        var codes = Regex.Matches(view, @"m\.store_code=''([A-Z0-9]+)''").Select(match => match.Groups[1].Value).ToArray();
        var code = Assert.Single(codes);
        var seed = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "migrations", "0011_phase2_operations.sql"));
        Assert.Contains($"VALUES(''{code}'',N''Titan World''", seed, StringComparison.Ordinal);
        Assert.Contains($"CONVERT(bit,CASE WHEN m.store_code=''{code}'' THEN 1 ELSE 0 END) is_service_money_shop", view, StringComparison.Ordinal);
        // The columns ServiceManualMoneyEntry needs.
        Assert.StartsWith("EXEC(N'CREATE OR ALTER VIEW dbo.v_service_manual_money AS\nSELECT m.business_date,m.store_code,s.store_name,m.field_code,m.numeric_value amount,", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Comments_name_no_store_code()
    {
        var comments = Script().Split('\n').Where(line => line.TrimStart().StartsWith("--", StringComparison.Ordinal));
        Assert.DoesNotContain(comments, line => Regex.IsMatch(line, @"\b(WLMHW|HEMW|AW330)\b"));
    }

    private static Dictionary<string, string> SpecColumns(string code)
    {
        using var spec = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "service-centre", "families.spec.json")));
        var family = spec.RootElement.EnumerateArray().Single(entry => entry.GetProperty("FamilyCode").GetString() == code);
        return family.GetProperty("Columns").EnumerateArray().ToDictionary(
            column => column.GetProperty("CanonicalField").GetString()!, column => column.GetProperty("DataType").GetString()!, StringComparer.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }
}
