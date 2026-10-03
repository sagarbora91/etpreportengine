using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.3, migration 0044. Phase 5 re-audit (26 Sep 2026), defect 3: the SQL reason checks in 0029 and 0033
/// trimmed spaces only, so a reason of tabs or non-breaking spaces passed. 0044 makes every Phase 5 reason
/// check refuse what the app's string.IsNullOrWhiteSpace refuses. These tests read the shipped migrations;
/// the SQL behaviour is covered by ReasonWhitespaceSqlTests in the SQL integration suite.
/// </summary>
public sealed partial class ReasonWhitespaceMigrationTests
{
    private const string MigrationName = "0044_phase5_reason_whitespace.sql";
    private const string NewCheck = "dbo.is_blank_text(";

    // The reason checks 0044 replaces, as they appear inside EXEC(N'...') in 0022, 0033 and 0035.
    private static readonly (string Old, string New)[] Replacements =
    [
        ("@reason IS NULL OR LEN(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))))=0", "dbo.is_blank_text(@reason)=1"),
        ("NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))),'''') IS NULL", "dbo.is_blank_text(@reason)=1"),
        ("LEN(LTRIM(RTRIM(@reason)))=0", "dbo.is_blank_text(@reason)=1"),
        ("NULLIF(LTRIM(RTRIM(approval_reason)),'''') IS NULL", "dbo.is_blank_text(approval_reason)=1"),
    ];

    public static TheoryData<string, string> Redefined => new()
    {
        { "TRIGGER", "trg_accounting_batch_states" },
        { "PROCEDURE", "reject_accounting_batch" },
        { "PROCEDURE", "decide_approval_request" },
        { "PROCEDURE", "save_register_entry" },
        { "PROCEDURE", "submit_controlled_adjustment" },
        { "PROCEDURE", "request_import_restatement" },
        { "PROCEDURE", "replace_import_facts_internal" },
    };

    private static string MigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "database", "migrations");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(MigrationsDirectory(), name)).Replace("\r\n", "\n");

    private static string[] EarlierMigrations() => Directory.GetFiles(MigrationsDirectory(), "*.sql").Select(path => Path.GetFileName(path)!)
        .Where(name => string.CompareOrdinal(name, MigrationName) < 0).Order(StringComparer.Ordinal).ToArray();

    // The text of the last EXEC(N'... dbo.<name> ...') definition in a script, from EXEC to its closing END').
    private static string? LastDefinition(string script, string kind, string name)
    {
        var start = -1;
        foreach (Match match in Regex.Matches(script, $@"EXEC\(N'(CREATE|ALTER|CREATE OR ALTER) {kind} dbo\.{name}\b"))
            start = match.Index;
        if (start < 0) return null;
        var end = script.IndexOf("\nEND');", start, StringComparison.Ordinal);
        Assert.True(end > start, $"The definition of dbo.{name} is not closed.");
        return script[start..(end + "\nEND');".Length)];
    }

    private static string Code(string sql) => string.Join('\n', sql.Split('\n').Select(line => line.Split("--")[0]));

    private static int[] CodePoints(string list) => list.Split(',').Select(value => int.Parse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    // Every UTF-16 code unit .NET counts as white space: what string.IsNullOrWhiteSpace and string.Trim use.
    private static int[] DotNetWhiteSpace() => Enumerable.Range(0, 0x10000).Where(code => char.IsWhiteSpace((char)code)).ToArray();

    [GeneratedRegex(@"NOT IN\(([0-9,]+)\)")]
    private static partial Regex CodePointList();

    [Theory]
    [MemberData(nameof(Redefined))]
    public void Each_redefinition_is_the_one_it_replaces_with_only_its_reason_check_changed(string kind, string name)
    {
        string? previous = null, source = null;
        foreach (var migration in EarlierMigrations())
            if (LastDefinition(Read(migration), kind, name) is { } definition) (previous, source) = (definition, migration);
        Assert.True(previous is not null, $"No migration before 0044 defines dbo.{name}.");

        var expected = Regex.Replace(previous, $@"^EXEC\(N'(CREATE|ALTER|CREATE OR ALTER) {kind}", $"EXEC(N'CREATE OR ALTER {kind}");
        var replaced = 0;
        foreach (var (old, replacement) in Replacements)
        {
            replaced += Regex.Matches(expected, Regex.Escape(old)).Count;
            expected = expected.Replace(old, replacement, StringComparison.Ordinal);
        }
        Assert.True(replaced == 1, $"dbo.{name} in {source} holds {replaced} known reason checks; 0044 replaces exactly one.");

        var actual = LastDefinition(Read(MigrationName), kind, name);
        Assert.True(actual is not null, $"0044 does not redefine dbo.{name}.");
        Assert.True(expected == actual, $"0044's dbo.{name} differs from {source}'s by more than its reason check. Copy the latest definition again.");
        Assert.Contains(NewCheck, actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Redefinitions_keep_the_error_numbers_and_messages_of_their_checks()
    {
        var script = Read(MigrationName);
        foreach (var expected in new[]
        {
            "dbo.is_blank_text(approval_reason)=1 OR blocking_reason IS NOT NULL))\n THROW 51457,''A balanced, unblocked batch and an approval reason are required.'',1;",
            "IF dbo.is_blank_text(@reason)=1 OR LEN(@reason)>1000 THROW 51431,''Enter a rejection reason of at most 1000 characters.'',1;",
            "IF @approve IS NULL OR dbo.is_blank_text(@reason)=1\n  THROW 51314,''Choose a decision and enter its reason.'',1;",
            "IF dbo.is_blank_text(@reason)=1 THROW 51551,''Enter a register change reason.'',1;",
            "IF dbo.is_blank_text(@reason)=1\n  THROW 51314,''Enter an adjustment reason.'',1;",
            "IF dbo.is_blank_text(@reason)=1 THROW 51039,''A restatement reason is required.'',1;",
        })
            Assert.Contains(expected, script, StringComparison.Ordinal);
        // request_import_restatement and replace_import_facts_internal both refuse with 51039.
        Assert.Equal(2, Regex.Matches(script, Regex.Escape("IF dbo.is_blank_text(@reason)=1 THROW 51039,")).Count);
    }

    [Fact]
    public void No_space_only_trim_remains_in_a_reason_check_of_0044()
    {
        var code = Code(Read(MigrationName));
        foreach (var (old, _) in Replacements) Assert.DoesNotContain(old, code, StringComparison.Ordinal);
        Assert.DoesNotContain("LTRIM(RTRIM(@reason))))", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LTRIM(RTRIM(approval_reason))", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_function_and_the_precheck_refuse_exactly_what_string_IsNullOrWhiteSpace_refuses()
    {
        var lists = CodePointList().Matches(Code(Read(MigrationName))).Select(match => CodePoints(match.Groups[1].Value)).ToArray();
        Assert.Equal(2, lists.Length); // dbo.is_blank_text and PRECHECK_BLANK_APPROVAL_REASON
        var expected = DotNetWhiteSpace();
        Assert.Equal(25, expected.Length);
        Assert.All(lists, list => Assert.Equal(expected, list.Order().ToArray()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t")]
    [InlineData("\r\n")]
    [InlineData("\u00A0")]
    [InlineData("\u00A0\u2007\u202F")]
    [InlineData("\u3000 \u2028\u2029\u0085\u1680\u205F\u000B\u000C")]
    [InlineData("\u200B")]
    [InlineData("\uFEFF")]
    [InlineData("\u00A0Checked")]
    [InlineData("Checked\t")]
    [InlineData("x")]
    public void The_sql_rule_agrees_with_string_IsNullOrWhiteSpace(string? reason)
    {
        // dbo.is_blank_text, step by step: NULL is blank; otherwise blank while every code unit is in the list.
        var list = CodePointList().Match(Code(Read(MigrationName))).Groups[1].Value;
        var whiteSpace = CodePoints(list).ToHashSet();
        var sqlBlank = reason is null || reason.All(character => whiteSpace.Contains(character));
        Assert.Equal(string.IsNullOrWhiteSpace(reason), sqlBlank);
    }

    [Fact]
    public void The_function_is_created_once_schema_bound_and_the_constraint_is_rebuilt_trusted_with_it()
    {
        var script = Read(MigrationName);
        Assert.Contains("IF OBJECT_ID(N'dbo.is_blank_text',N'FN') IS NULL\nEXEC(N'CREATE FUNCTION dbo.is_blank_text(@value nvarchar(max))\nRETURNS bit WITH SCHEMABINDING", script, StringComparison.Ordinal);
        Assert.Contains(" IF @value IS NULL RETURN 1;", script, StringComparison.Ordinal);
        var drop = script.IndexOf("IF OBJECT_ID(N'dbo.CK_accounting_batches_approval_reason',N'C') IS NOT NULL\n ALTER TABLE dbo.accounting_batches DROP CONSTRAINT CK_accounting_batches_approval_reason;", StringComparison.Ordinal);
        var add = script.IndexOf("EXEC(N'ALTER TABLE dbo.accounting_batches WITH CHECK ADD CONSTRAINT CK_accounting_batches_approval_reason\n CHECK (approval_reason IS NULL OR dbo.is_blank_text(approval_reason)=0);');", StringComparison.Ordinal);
        var function = script.IndexOf("EXEC(N'CREATE FUNCTION dbo.is_blank_text", StringComparison.Ordinal);
        Assert.True(function > 0 && drop > function && add > drop, "Create the function, then drop and re-add the constraint WITH CHECK.");
        // Each procedure and the trigger come after the function they call.
        foreach (var row in Redefined)
        {
            var definition = script.IndexOf($"EXEC(N'CREATE OR ALTER {row[0]} dbo.{row[1]}", StringComparison.Ordinal);
            Assert.True(definition > function, $"dbo.{row[1]} is not defined after dbo.is_blank_text.");
        }
    }

    [Fact]
    public void The_precheck_refuses_with_51562_and_compiles_without_the_approval_reason_column()
    {
        var prechecks = MigrationPrechecks.Extract(File.ReadAllText(Path.Combine(MigrationsDirectory(), MigrationName)));
        var precheck = Assert.Single(prechecks).Replace("\r\n", "\n");
        Assert.Contains("THROW 51562,@blank_reason_message,1;", precheck, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.accounting_batches',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.accounting_batches',N'approval_reason') IS NOT NULL", precheck, StringComparison.Ordinal);
        // A database from before 0029 has no approval_reason column, and a pre-check may not use EXEC, so the
        // column is never named in the query: naming it would not compile there and would block every upgrade.
        var code = Code(precheck).Replace("N'approval_reason'", "", StringComparison.Ordinal).Replace("N'$.approval_reason'", "", StringComparison.Ordinal);
        Assert.DoesNotContain("approval_reason", code, StringComparison.Ordinal);
        // 51562 is new: no earlier migration throws it.
        Assert.All(EarlierMigrations(), name => Assert.DoesNotContain("THROW 51562", Read(name), StringComparison.Ordinal));
    }
}
