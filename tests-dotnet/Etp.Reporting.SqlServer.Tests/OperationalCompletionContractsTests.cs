using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class OperationalCompletionContractsTests
{
    [Fact]
    public void Physical_stock_keeps_component_and_counted_evidence_independent()
    {
        var count = new ManualStockCount("WLMHW", new(2026, 8, 25), "GAUTO", 754m, 55m, 3m, 0m, 812m,
            "Verified", DateTime.UtcNow, "manager");

        Assert.Equal(812m, count.ComponentTotal);
        Assert.Equal(0m, count.CompositionVariance);
    }

    [Fact]
    public void Missing_stock_components_do_not_become_a_false_zero_total()
    {
        var count = new ManualStockCount("HEMW", new(2026, 8, 25), "GROUP", null, null, null, null, 501m,
            null, DateTime.UtcNow, "manager");

        Assert.Null(count.ComponentTotal);
        Assert.Null(count.CompositionVariance);
    }
}
