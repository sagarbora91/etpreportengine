using System.Text;
using System.Text.RegularExpressions;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// <c>scripts/check-import-upgrade.sql</c> builds several checks as dynamic SQL inside string literals, where every
/// quote is doubled. Check 16 (OPEN_MOVEMENT_CONFLICTS_ON_STORED_ROWS) lost the doubled quotes of its last two
/// <c>N''</c> and <c>N''/''</c>, so on the live database (3 Oct 2026) its sp_executesql failed with Msg 102 and the
/// check reported NULL. These tests read the script as T-SQL does, without SQL Server; the SQL integration test
/// <c>RestatementTargetSqlTests.Upgrade_check_script_runs_select_only_and_reports_its_findings</c> runs it.
/// </summary>
public sealed partial class CheckImportUpgradeScriptTests
{
    [Fact]
    public void No_dynamic_SQL_in_the_script_has_an_N_prefix_without_its_string()
    {
        // An N prefix followed by anything but a quote is a doubled quote that was lost: N''/'' became N/.
        var broken = Literals(Read("scripts", "check-import-upgrade.sql"))
            .Where(literal => BareNPrefix().IsMatch(literal.Text))
            .Select(literal => $"line {literal.Line}: {literal.Text[..Math.Min(80, literal.Text.Length)]}")
            .ToArray();

        Assert.True(broken.Length == 0, "Dynamic SQL with a lost quote:\n" + string.Join('\n', broken));
    }

    [Fact]
    public void Check_16_builds_the_movement_identity_exactly_as_the_1_9_2_procedure_did()
    {
        var checker = Literals(Read("scripts", "check-import-upgrade.sql"))
            .Single(literal => literal.Text.StartsWith("LEFT(CONCAT(m.store_code", StringComparison.Ordinal)).Text;
        // 0014's persist_stock_movement (unchanged until 0041) built business_identity into an nvarchar(400).
        var procedure = Literals(Read("database", "migrations", "0014_productisation.sql"))
            .Select(literal => MovementIdentity().Match(literal.Text))
            .Single(match => match.Success).Groups["concat"].Value;

        var asProcedure = Squash(checker);
        foreach (var (column, parameter) in new[]
                 {
                     ("m.store_code", "@store"), ("m.invoice_year", "@year"), ("m.document_number", "@doc"),
                     ("m.document_date", "@date"), ("m.product_code", "@product"), ("m.source_transaction_type", "@type"),
                     ("m.from_location", "@from"), ("m.to_location", "@to"),
                 })
            asProcedure = asProcedure.Replace(column, parameter, StringComparison.Ordinal);

        Assert.Equal("LEFT(" + Squash(procedure) + ",400)", asProcedure);
    }

    [Fact]
    public void The_scan_finds_the_defect_the_live_check_hit()
    {
        const string broken = "DECLARE @x nvarchar(max) = N'LEFT(CONCAT(m.store_code,N''/'',ISNULL(m.from_location,N),N/,ISNULL(m.to_location,N)),400)';";
        const string fixedText = "DECLARE @x nvarchar(max) = N'LEFT(CONCAT(m.store_code,N''/'',ISNULL(m.from_location,N''''),N''/'',ISNULL(m.to_location,N'''')),400)';";

        Assert.Matches(BareNPrefix(), Assert.Single(Literals(broken)).Text);
        Assert.Equal("LEFT(CONCAT(m.store_code,N'/',ISNULL(m.from_location,N''),N'/',ISNULL(m.to_location,N'')),400)", Assert.Single(Literals(fixedText)).Text);
        Assert.DoesNotMatch(BareNPrefix(), Assert.Single(Literals(fixedText)).Text);
    }

    [GeneratedRegex(@"(?<![\w@#.\[\]])N(?=[^'\w])")]
    private static partial Regex BareNPrefix();

    [GeneratedRegex(@"SET @identity=(?<concat>CONCAT\(@store,N'/',@year,N'/',@doc,N'/',@date,.*?\));", RegexOptions.Singleline)]
    private static partial Regex MovementIdentity();

    private static string Squash(string text) => Regex.Replace(text, @"\s+", "");

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir.FullName, .. parts]));
    }

    // Every string literal of a T-SQL text, decoded ('' is one quote), skipping -- and /* */ comments.
    private static List<(int Line, string Text)> Literals(string sql)
    {
        var result = new List<(int, string)>();
        for (var i = 0; i < sql.Length; i++)
        {
            if (string.CompareOrdinal(sql, i, "--", 0, 2) == 0)
            {
                var newline = sql.IndexOf('\n', i);
                if (newline < 0) break;
                i = newline;
            }
            else if (string.CompareOrdinal(sql, i, "/*", 0, 2) == 0)
            {
                var close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                Assert.True(close >= 0, "Unclosed comment.");
                i = close + 1;
            }
            else if (sql[i] == '\'')
            {
                var line = 1 + sql.AsSpan(0, i).Count('\n');
                var text = new StringBuilder();
                var j = i + 1;
                while (true)
                {
                    Assert.True(j < sql.Length, $"Unclosed string literal from line {line}.");
                    if (sql[j] == '\'')
                    {
                        if (j + 1 < sql.Length && sql[j + 1] == '\'') { text.Append('\''); j += 2; continue; }
                        break;
                    }
                    text.Append(sql[j++]);
                }
                result.Add((line, text.ToString()));
                i = j;
            }
        }
        return result;
    }
}
