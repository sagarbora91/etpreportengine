namespace Etp.Reporting.SqlServer.Tests;

/// <summary>1.10.1, migration 0053 (decision 32, RA-OPS-13): the five unread Stock inputs are hidden, never deleted.</summary>
public sealed class RetireUnusedManualInputsMigrationTests
{
    private static string Migrations()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "database", "migrations");
    }

    private static string[] Statements() => File.ReadAllText(Path.Combine(Migrations(), "0053_retire_unused_manual_inputs.sql"))
        .Replace("\r\n", "\n").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("--", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void Only_the_five_stock_fields_are_set_inactive_and_nothing_is_deleted()
    {
        var sql = string.Join("\n", Statements());
        Assert.StartsWith("SET XACT_ABORT ON;", sql);
        Assert.Contains("UPDATE dbo.manual_input_definitions", sql);
        Assert.Contains("SET is_active=0", sql);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var code in new[] { "DISPLAY_STOCK", "BACKSTOCK", "DEFECTIVE_STOCK", "Y_LOCATION_STOCK", "PHYSICAL_STOCK" })
            Assert.Contains($"'{code}'", sql);
    }

    [Theory]
    [InlineData("WALK_INS")]
    [InlineData("OPERATIONAL_REMARK")]
    [InlineData("OPENING_CASH")]
    [InlineData("EXPENSES")]
    [InlineData("CASH_DEPOSIT")]
    [InlineData("CASH_ADJUSTMENT")]
    [InlineData("CLOSING_CASH_COUNTED")]
    [InlineData("SERVICE_CASH")]
    [InlineData("SERVICE_CARD")]
    [InlineData("SERVICE_UPI")]
    [InlineData("SERVICE_WDC")]
    [InlineData("WCC_SALES")]
    [InlineData("WCC_WALKIN")]
    [InlineData("WDC_BILLS")]
    public void Daily_inputs_fields_are_not_touched(string code) =>
        Assert.DoesNotContain(code, string.Join("\n", Statements()));
}
