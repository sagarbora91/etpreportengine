using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 lane D2: text guards over migrations 0063/0064 and the collections object files (A3's format), so the rules the
/// runner and the integrator rely on hold before any SQL runs.
/// </summary>
public sealed class CollectionsMigrationTextTests
{
    private static readonly string[] ObjectFiles =
    [
        "20-dbo.v_cash_deposits_sent.sql", "20-dbo.v_collection_evidence.sql", "20-dbo.v_collection_expected.sql",
        "30-dbo.v_cash_deposit_status.sql", "30-dbo.v_cash_deposits_in_transit.sql", "40-dbo.collections_import_statement.sql"
    ];

    [Theory]
    [InlineData("0063_collections_contracts.sql")]
    [InlineData("0064_cash_deposit_confirmations.sql")]
    public void Collections_migrations_follow_the_runner_rules_and_create_no_view_or_procedure(string file)
    {
        var sql = File.ReadAllText(Path.Combine(Repository("database", "migrations"), file));

        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), sql);
        Assert.DoesNotMatch(new Regex(@"BEGIN\s+TRAN", RegexOptions.IgnoreCase), sql);
        Assert.DoesNotMatch(new Regex(@"CREATE\s+(OR\s+ALTER\s+)?(VIEW|PROC|PROCEDURE|FUNCTION)\b", RegexOptions.IgnoreCase), sql);
        Assert.StartsWith("--", sql, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", sql, StringComparison.Ordinal);
        // The lane's THROW block only.
        foreach (Match match in Regex.Matches(sql, @"THROW\s+(\d+)", RegexOptions.IgnoreCase))
            Assert.InRange(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 51950, 51959);
    }

    [Fact]
    public void Collections_pii_columns_are_denied_to_store_manager_and_viewer_and_masters_are_not_writable_by_them()
    {
        var sql = File.ReadAllText(Path.Combine(Repository("database", "migrations"), "0063_collections_contracts.sql"));

        Assert.Contains("DENY SELECT ON dbo.bank_statement_lines(narration,raw_json) TO etp_store_manager;", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT ON dbo.provider_settlement_lines(payer_name,payer_vpa,raw_json) TO etp_store_manager;", sql, StringComparison.Ordinal);
        foreach (var table in new[] { "bank_accounts", "collection_providers", "provider_merchant_ids", "collection_policies", "statement_layouts",
                     "bank_statement_lines", "provider_settlement_lines", "collections_setup_history" })
            Assert.Contains($"DENY INSERT,UPDATE,DELETE ON dbo.{table} TO etp_store_manager,etp_viewer;", sql, StringComparison.Ordinal);
        // No column can hold a full account number.
        Assert.DoesNotMatch(new Regex(@"account_number|account_no\b", RegexOptions.IgnoreCase), sql);

        var deposits = File.ReadAllText(Path.Combine(Repository("database", "migrations"), "0064_cash_deposit_confirmations.sql"));
        Assert.Contains("DENY INSERT,UPDATE,DELETE ON dbo.cash_deposit_confirmations TO etp_store_manager,etp_viewer;", deposits, StringComparison.Ordinal);
        // Review m-4 (decision 53): the Viewer reads the line and confirmation tables through the views only.
        Assert.Contains("DENY SELECT ON dbo.bank_statement_lines TO etp_viewer;", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT ON dbo.provider_settlement_lines TO etp_viewer;", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT ON dbo.cash_deposit_confirmations TO etp_viewer;", deposits, StringComparison.Ordinal);
        // Banking facts arrive after the lock: no day-lock check on confirmations.
        Assert.DoesNotContain("daily_reporting_days", deposits, StringComparison.Ordinal);
    }

    [Fact]
    public void Collections_object_files_are_in_the_objects_format_with_a_grants_block()
    {
        var folder = Repository("database", "objects");
        foreach (var file in ObjectFiles)
        {
            var text = File.ReadAllText(Path.Combine(folder, file));
            var match = Regex.Match(file, @"^(?<layer>\d{2})-dbo\.(?<name>[a-z_]+)\.sql$");
            Assert.True(match.Success, file);
            var kind = match.Groups["layer"].Value == "40" ? "PROCEDURE" : "VIEW";
            Assert.Matches(new Regex($@"^CREATE OR ALTER {kind} dbo\.{match.Groups["name"].Value}\b", RegexOptions.Multiline), text);
            Assert.Single(Regex.Matches(text, @"^CREATE OR ALTER ", RegexOptions.Multiline));
            var grants = text.IndexOf("-- >>> GRANTS", StringComparison.Ordinal);
            Assert.True(grants > 0, file);
            Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), text);
            var block = text[grants..];
            Assert.All(block.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)),
                line => Assert.Matches(new Regex(@"^(GRANT|DENY) [A-Z ,]+ ON dbo\." + match.Groups["name"].Value + @" TO [a-z_,]+;$"), line));
            // No object reads a PII column; the procedure is the one place that writes them.
            if (kind == "VIEW") Assert.DoesNotMatch(new Regex(@"narration|payer_|raw_json", RegexOptions.IgnoreCase), text[..grants]);
        }
        // A Viewer reads totals, not the in-transit list.
        Assert.Contains("DENY SELECT ON dbo.v_cash_deposits_in_transit TO etp_viewer;", File.ReadAllText(Path.Combine(folder, "30-dbo.v_cash_deposits_in_transit.sql")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_csv_statement_can_be_kept_in_the_managed_document_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpCollectionsCsv_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var source = Path.Combine(root, "statement.csv");
            await File.WriteAllTextAsync(source, "Date,Amount\n01/08/2026,1.00\n");

            var kept = await ManagedDocumentRepository.StoreAsync(source, Path.Combine(root, "kept"));

            Assert.EndsWith(".csv", kept.ManagedPath, StringComparison.Ordinal);
            Assert.True(await ManagedDocumentRepository.VerifyIntegrityAsync(kept.ManagedPath, kept.Sha256));
            var other = Path.Combine(root, "statement.txt");
            await File.WriteAllTextAsync(other, "x");
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => ManagedDocumentRepository.StoreAsync(other, Path.Combine(root, "kept")));
            Assert.Equal("Select a supported document, spreadsheet or CSV file.", refused.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string Repository(params string[] parts)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine([folder.FullName, .. parts]);
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(folder.FullName, "Etp.Reporting.slnx"))) return candidate;
        }
        throw new DirectoryNotFoundException(string.Join('/', parts));
    }
}
