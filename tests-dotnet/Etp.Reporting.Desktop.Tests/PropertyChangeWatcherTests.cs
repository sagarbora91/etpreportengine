using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Tests;

// No XAML is loaded here, so these UI threads cannot race Phase3ShellTests inside Application.LoadComponent.
[Collection(WpfViewCollection.Name)]
public sealed class PropertyChangeWatcherTests
{
    [Fact]
    public void Watcher_reports_later_changes_only_and_stops_when_disposed()
    {
        RunSta(() =>
        {
            var text = new TextBlock { Text = "Starting" };
            var seen = new List<string>();
            var watcher = PropertyChangeWatcher.Watch(text, TextBlock.TextProperty, () => seen.Add(text.Text));
            Assert.Empty(seen);
            text.Text = "Settings saved";
            text.Text = "Settings saved";
            text.Text = "Import completed";
            Assert.Equal(["Settings saved", "Import completed"], seen);
            watcher.Dispose();
            text.Text = "Report completed";
            Assert.Equal(["Settings saved", "Import completed"], seen);
        });
    }

    [Fact]
    public void Configured_grid_rebuilds_its_columns_when_the_items_source_is_replaced()
    {
        RunSta(() =>
        {
            var grid = new DataGrid { AutoGenerateColumns = true };
            TablePresentation.Configure(grid);
            Assert.Empty(grid.Columns);
            grid.ItemsSource = new[] { new { StoreCode = "WLMHW", NetAmount = 10m } };
            Assert.Equal(new[] { "Store", "Net Amount ₹" }, grid.Columns.Select(column => column.Header));
            grid.ItemsSource = new[] { new { Invoice = "00123", Quantity = 2 } };
            Assert.Equal(new[] { "Invoice", "Quantity" }, grid.Columns.Select(column => column.Header));
        });
    }

    [Fact]
    public void Grids_configured_on_parallel_ui_threads_share_no_listener_state()
    {
        // WPF's shared property descriptor corrupted its unlocked listener Dictionary under this load.
        var failures = new List<Exception>();
        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 500; i++)
                {
                    var grid = new DataGrid { AutoGenerateColumns = true };
                    TablePresentation.Configure(grid);
                    grid.ItemsSource = new[] { new { Invoice = "00123", Quantity = i } };
                    Assert.Equal(new[] { "Invoice", "Quantity" }, grid.Columns.Select(column => column.Header));
                }
            }
            catch (Exception exception) { lock (failures) failures.Add(exception); }
        })).ToList();
        foreach (var thread in threads) thread.SetApartmentState(ApartmentState.STA);
        threads.ForEach(thread => thread.Start());
        Assert.All(threads, thread => Assert.True(thread.Join(TimeSpan.FromSeconds(60))));
        Assert.Empty(failures);
    }

    [Fact]
    public void Product_code_does_not_listen_through_wpf_shared_property_descriptors()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var offenders = Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => File.ReadAllText(path).Contains(".AddValueChanged(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root.FullName, path));
        Assert.Empty(offenders);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        if (failure is not null) throw failure;
    }
}
