namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.4, migration 0046. Report audit of 3 October 2026 (HEMW FIX-05, WLMHW FIX-11 and FIX-12): the KPI
/// catalogue said NET_SALES = SUM(R025.NETVALUE) and described INVOICE_COUNT as every document, while the
/// reports sum GST-inclusive NETAMOUNT (D1) and the DSR counts INV documents only (G5). Amended in place for the
/// owner's answers (decision 13 Q2 gift cards, Q6 Invoices + Returns; decision 17 D-L9-3). Wording only.
/// These tests read the shipped scripts; ReportLabelCorrectionSqlTests in the SQL integration suite runs
/// the upgrade from 0045.
/// </summary>
public sealed class ReportLabelCorrectionMigrationTests
{
    private const string MigrationName = "0046_report_label_corrections.sql";

    private static string MigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "database", "migrations");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(MigrationsDirectory(), name)).Replace("\r\n", "\n");

    private static string Code(string migration) =>
        string.Join('\n', migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void The_seed_text_0046_corrects_is_still_the_0014_seed()
    {
        // If 0014's seed ever changes, this migration's reasoning has to be looked at again.
        var seed = Read("0014_productisation.sql");
        Assert.Contains("N'SUM(R025.NETVALUE)',N'Canonical sales lines sourced from R025 NETVALUE'", seed, StringComparison.Ordinal);
        Assert.Contains("N'Distinct business documents in the selected scope.',N'COUNT(DISTINCT store + year + document)'", seed, StringComparison.Ordinal);
    }

    [Fact]
    public void Net_sales_is_described_as_GST_inclusive_NETAMOUNT()
    {
        var code = Code(Read(MigrationName));
        Assert.Contains("@net_sales_formula nvarchar(1000)=N'SUM(R025.NETAMOUNT)'", code, StringComparison.Ordinal);
        Assert.Contains("@net_sales_source nvarchar(500)=N'Canonical sales lines from R025 NETAMOUNT (GST-inclusive)'", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NETVALUE", code, StringComparison.Ordinal);
        Assert.Contains("WHERE kpi_code='NET_SALES'", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Net_sales_says_the_dsr_value_leaves_gift_cards_out()
    {
        // Decision 13 Q2 / decision 17 (D-L9-3: 0046 amended in place).
        var definition = Value(Code(Read(MigrationName)), "@net_sales_definition");
        Assert.StartsWith("Primary sales value including GST, with sales returns retaining their negative signs.", definition, StringComparison.Ordinal);
        Assert.Contains("leaves gift-card lines (item GIFT CARD or BRAND GC) out and shows them on their own GIFT CARD line", definition, StringComparison.Ordinal);
        Assert.Contains("the other sales reports include them", definition, StringComparison.Ordinal);
        Assert.True(definition.Length <= 1000, "kpi_catalogue.definition is nvarchar(1000).");
    }

    [Fact]
    public void Invoice_count_says_exactly_what_it_counts()
    {
        var code = Code(Read(MigrationName));
        var definition = Value(code, "@invoice_definition");
        Assert.StartsWith("Distinct INV documents (store + financial year + document number)", definition, StringComparison.Ordinal);
        Assert.Contains("Sales returns (SR) and bill cancellations (BC) keep their value and quantity but are not counted as invoices", definition, StringComparison.Ordinal);
        // Decision 13 Q6: every screen says "Invoices" for INV documents and shows a separate "Returns" count.
        Assert.Contains("shown as \"Invoices\" on every screen", definition, StringComparison.Ordinal);
        Assert.Contains("a separate \"Returns\" count of distinct SR or BC documents", definition, StringComparison.Ordinal);
        foreach (var screen in new[] { "Sales Summary", "Management Trend", "Operations sales and control trend", "Customer-wise" })
            Assert.Contains(screen, definition, StringComparison.Ordinal);
        Assert.DoesNotContain("Documents (incl. returns)", definition, StringComparison.Ordinal);
        Assert.Contains("a bill whose only INV lines are gift cards is not counted", definition, StringComparison.Ordinal);
        Assert.DoesNotContain("Distinct business documents", definition, StringComparison.Ordinal);
        Assert.Contains("over documents with an INV line", Value(code, "@invoice_formula"), StringComparison.Ordinal);
        Assert.True(definition.Length <= 1000, "kpi_catalogue.definition is nvarchar(1000).");
        Assert.True(Value(code, "@invoice_source").Length <= 500, "kpi_catalogue.data_source is nvarchar(500).");
    }

    [Fact]
    public void The_update_is_idempotent_and_keeps_approval_and_dates()
    {
        var code = Code(Read(MigrationName));
        Assert.StartsWith("SET XACT_ABORT ON;", code.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.kpi_catalogue',N'U') IS NOT NULL", code, StringComparison.Ordinal);
        // Each row changes only while it still differs, so a second run changes nothing and the version goes up once.
        Assert.Equal(2, code.Split("version=version+1").Length - 1);
        Assert.Contains("AND (definition COLLATE Latin1_General_100_BIN2<>@net_sales_definition OR formula COLLATE Latin1_General_100_BIN2<>@net_sales_formula", code, StringComparison.Ordinal);
        Assert.Contains("OR data_source COLLATE Latin1_General_100_BIN2<>@net_sales_source)", code, StringComparison.Ordinal);
        Assert.Contains("AND (definition COLLATE Latin1_General_100_BIN2<>@invoice_definition", code, StringComparison.Ordinal);
        foreach (var kept in new[] { "effective_date", "approval_status", "approved_by", "is_active", "business_name" })
            Assert.DoesNotContain(kept, code, StringComparison.Ordinal);
        // One batch, like every migration: the runner wraps the whole script in one transaction.
        Assert.DoesNotMatch(@"(?im)^\s*GO\s*$", code);
        Assert.DoesNotContain("BEGIN TRAN", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRECHECK", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_0046_touches_the_catalogue_text_after_0014()
    {
        var later = Directory.GetFiles(MigrationsDirectory(), "*.sql").Select(Path.GetFileName).OfType<string>()
            .Where(name => string.CompareOrdinal(name, "0015") > 0)
            .Where(name => Code(Read(name)).Contains("UPDATE dbo.kpi_catalogue", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal([MigrationName], later);
    }

    private static string Value(string code, string variable)
    {
        var start = code.IndexOf(variable + " nvarchar(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{variable} is missing.");
        var open = code.IndexOf("=N'", start, StringComparison.Ordinal) + 3;
        var close = code.IndexOf("';\n", open, StringComparison.Ordinal);
        Assert.True(close > open, $"{variable} is not closed.");
        return code[open..close].Replace("''", "'");
    }
}
