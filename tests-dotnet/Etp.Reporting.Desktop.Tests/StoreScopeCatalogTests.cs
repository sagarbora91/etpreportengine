using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Tests;

public sealed class StoreScopeCatalogTests
{
    [Fact]
    public void Active_database_stores_define_labels_and_resolve_unambiguously()
    {
        var catalog = new StoreScopeCatalog();
        catalog.Replace([new("NORTH", "North shop", true),new("SOUTH", "South shop", true),new("OLD", "Old shop", false)]);
        Assert.Equal(["North shop (NORTH)","South shop (SOUTH)"],catalog.Labels);
        Assert.Equal("SOUTH",catalog.Resolve("South shop (SOUTH)"));
        Assert.Equal("NORTH",catalog.Resolve("north"));
        Assert.Null(catalog.Resolve("OLD"));
        catalog.Replace([new("A","Same name",true),new("B","Same name",true)]);
        Assert.Null(catalog.Resolve("Same name"));
        Assert.Equal("B",catalog.Resolve("Same name (B)"));
    }

    [Fact]
    public void Cash_default_uses_the_active_store_count_without_reassigning_the_third_store()
    {
        Assert.Equal(2,Modules.Reports.ReportTaskScope.StoreIndexForReport("cash",2,3));
        Assert.Equal(0,Modules.Reports.ReportTaskScope.StoreIndexForReport("cash",3,3));
        Assert.Equal(0,Modules.Reports.ReportTaskScope.StoreIndexForReport("cash",0,0));
    }
}

internal static class TestStoreCatalog
{
    internal static StoreCatalogEntry[] Entries => [new("WLMHW","Titan World",true),new("HEMW","Helios",true)];
    internal static StoreScopeCatalog Create() { var result=new StoreScopeCatalog();result.Replace(Entries);return result; }
}
