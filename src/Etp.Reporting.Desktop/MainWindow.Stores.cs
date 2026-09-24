using System.Windows.Controls;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop;

public partial class MainWindow
{
    internal StoreScopeCatalog StoreScopes { get; } = new();

    internal async Task RefreshStoresAsync()
    {
        var stores = await new StoreCatalogRepository(connectionState.ConnectionString).LoadAsync();
        ApplyStoreCatalog(stores);
    }

    internal void ApplyStoreCatalog(IReadOnlyList<StoreCatalogEntry> stores)
    {
        StoreScopes.Replace(stores);
        taskNavigator!.SetStores(StoreScopes);
        reportsWorkspaceView.SetStores(StoreScopes);
        importWorkspaceView.SetKnownStores(StoreScopes.Stores.Select(x=>x.Code).ToArray());
        foreach (var (view, name, all) in new (UserControl, string, bool)[] {
            (dailyWorkflowWorkspace,"StoreInput",false), (importWorkspaceView,"ImportStoreInput",false),
            (archiveWorkspaceView,"ArchiveStoreInput",true) })
        {
            if (view.FindName(name) is not ComboBox combo) continue;
            var code = (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            combo.Items.Clear();
            if (all) combo.Items.Add(new ComboBoxItem { Content="All" });
            foreach (var store in StoreScopes.Stores) combo.Items.Add(new ComboBoxItem { Content=store.Code, ToolTip=store.Name });
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>x.Content?.ToString()==code);
            if (combo.SelectedIndex < 0 && all) combo.SelectedIndex=0;
        }
    }
}
