using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Desktop;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed partial class PhaseFiveFullWindowCaptureTests
{
    private sealed record DestinationFingerprint(string Id, string Structure, string? PopulatedData);

    private static DestinationFingerprint Fingerprint(MainWindow window, string id)
    {
        var host = (FrameworkElement)window.FocusedWorkspaceHost.Content;
        var visible = Attached(host).OfType<FrameworkElement>().Where(element => element.IsVisible).ToArray();
        var headings = visible.OfType<TextBlock>().Where(block => block.FontSize >= 17 && block.FontWeight >= FontWeights.SemiBold)
            .Select(block => block.Text).Distinct().Order().ToArray();
        var controls = visible.Where(element => element is DataGrid or TextBox or ComboBox or DatePicker or CheckBox)
            .Select(element => element.GetType().Name + ":" + ControlName(element) + (element is DataGrid grid ? ":" + string.Join(",", ColumnPaths(grid)) : ""))
            .Order().ToArray();
        var actions = visible.OfType<Button>().Select(button => ControlName(button).Length > 0 ? ControlName(button) : button.Content?.ToString() ?? "")
            .Distinct().Order().ToArray();
        var structure = JsonSerializer.Serialize(new { Workspace = host.GetType().FullName, Headings = headings, Controls = controls, Actions = actions });
        // Different workspace classes and labels can still expose precisely the same
        // result. Compare populated grid values as well, independent of task IDs/titles.
        var grids = visible.OfType<DataGrid>().Select(GridData).Where(data => data is not null).Order().ToArray();
        return new(id, structure, grids.Length == 0 ? null : string.Join("|", grids));
    }

    private static string ControlName(FrameworkElement element) => element.Name.Length > 0 ? element.Name : AutomationProperties.GetName(element);

    private static string[] ColumnPaths(DataGrid grid) => grid.Columns.Where(column => column.Visibility == Visibility.Visible)
        .OrderBy(column => column.DisplayIndex).OfType<DataGridBoundColumn>()
        .Select(column => (column.Binding as Binding)?.Path?.Path).OfType<string>().ToArray();

    private static string? GridData(DataGrid grid)
    {
        var paths = ColumnPaths(grid);
        var rows = grid.Items.Cast<object>().Where(row => row != CollectionView.NewItemPlaceholder).ToArray();
        if (paths.Length == 0 || rows.Length == 0) return null;
        var values = rows.Select(row => JsonSerializer.Serialize(paths.Select(path => BoundGridValue(row, path)).ToArray())).Order().ToArray();
        var serialised = JsonSerializer.Serialize(new { Columns = paths, Rows = values });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialised)));
    }

    private static object? BoundGridValue(object row, string path)
    {
        // Typed report tables expose exact column names (including spaces). Read
        // their values directly: the real cell converter renders DBNull as a dash,
        // while a converter-free Tag binding cannot transfer that sentinel.
        if (row is DataRowView dataRow && dataRow.DataView.Table!.Columns.Contains(path))
            return dataRow[path];
        // Use the same WPF path semantics as the grid: property paths, DataRowView
        // columns, numeric indexers and dictionary indexers all occur in report rows.
        var probe = new TextBlock();
        var expression = (BindingExpression)BindingOperations.SetBinding(probe, FrameworkElement.TagProperty,
            new Binding(path) { Source = row, Mode = BindingMode.OneTime });
        try
        {
            expression.UpdateTarget();
            if (expression.Status != BindingStatus.Active || expression.HasError)
                throw new InvalidOperationException($"Cannot read bound grid value '{path}' from {row.GetType().FullName}; fingerprinting cannot substitute null.");
            return probe.Tag;
        }
        finally { BindingOperations.ClearBinding(probe, FrameworkElement.TagProperty); }
    }

    [Theory]
    [InlineData("property")]
    [InlineData("indexed")]
    [InlineData("dictionary")]
    [InlineData("data-row")]
    public void Grid_fingerprints_use_actual_bound_values_across_row_and_workspace_shapes(string shape)
    {
        FingerprintSta(() =>
        {
            var path = shape switch { "indexed" => "[0]", "dictionary" => "[Amount]", _ => "Amount" };
            object Row(decimal amount)
            {
                if (shape == "indexed") return new object[] { amount };
                if (shape == "dictionary") return new Dictionary<string, object> { ["Amount"] = amount };
                if (shape == "data-row")
                {
                    var table = new DataTable(); table.Columns.Add("Amount", typeof(decimal)); table.Rows.Add(amount);
                    return table.DefaultView[0];
                }
                return new { Amount = amount };
            }
            DataGrid Grid(string name, decimal amount)
            {
                var grid = new DataGrid { Name = name, AutoGenerateColumns = false, ItemsSource = new[] { Row(amount) } };
                grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(path) });
                return grid;
            }
            var first = Grid("OperationsResult", 120m);
            var second = Grid("ReportResult", 80m);
            _ = new UserControl { Content = first };
            var differentWorkspace = new DockPanel(); differentWorkspace.Children.Add(second);
            Assert.Equal(120m, BoundGridValue(first.Items[0], path));
            Assert.NotEqual(GridData(first), GridData(second));
            second.ItemsSource = new[] { Row(120m) };
            Assert.Equal(GridData(first), GridData(second));
        });
    }

    [Fact]
    public void Fingerprints_read_cash_columns_with_spaces_and_explicit_database_nulls()
    {
        FingerprintSta(() =>
        {
            var table = new DataTable();
            table.Columns.Add("Dr — Amount", typeof(decimal));
            table.Rows.Add(120m);
            table.Rows.Add(DBNull.Value);
            var grid = new DataGrid { ItemsSource = table.DefaultView };
            TablePresentation.Configure(grid);
            var column = Assert.IsType<DataGridTextColumn>(Assert.Single(grid.Columns));
            var actual = Assert.IsType<Binding>(column.Binding);
            Assert.Equal("Dr — Amount", actual.Path.Path);
            for (var index = 0; index < table.DefaultView.Count; index++)
            {
                var row = table.DefaultView[index];
                Assert.True(row.DataView.Table!.Columns.Contains(actual.Path.Path));
                Assert.Equal(index == 0 ? (object)120m : DBNull.Value, row[actual.Path.Path]);
                var cell = new TextBlock { DataContext = row };
                var expression = (BindingExpression)BindingOperations.SetBinding(cell, TextBlock.TextProperty,
                    new Binding
                    {
                        Path = actual.Path, Mode = actual.Mode, Converter = actual.Converter,
                        ConverterCulture = actual.ConverterCulture, ConverterParameter = actual.ConverterParameter,
                        StringFormat = actual.StringFormat, TargetNullValue = actual.TargetNullValue,
                        FallbackValue = actual.FallbackValue
                    });
                expression.UpdateTarget();
                Assert.Equal(BindingStatus.Active, expression.Status);
                Assert.False(expression.HasError);
                Assert.Equal(index == 0 ? "120.00" : "—", cell.Text);
                BindingOperations.ClearBinding(cell, TextBlock.TextProperty);
            }
            Assert.Equal(120m, BoundGridValue(table.DefaultView[0], actual.Path.Path));
            Assert.Same(DBNull.Value, BoundGridValue(table.DefaultView[1], actual.Path.Path));
            Assert.Throws<InvalidOperationException>(() => BoundGridValue(table.DefaultView[1], "Missing Amount"));
        });
    }

    [Fact]
    public void Grid_fingerprinting_refuses_an_unresolved_binding_instead_of_hashing_null()
    {
        FingerprintSta(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => BoundGridValue(new { Amount = 120m }, "MissingAmount"));
            Assert.Contains("cannot substitute null", error.Message, StringComparison.Ordinal);
        });
    }

    private static void FingerprintSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Fingerprint dispatcher did not close.");
        if (failure is not null) throw new InvalidOperationException("Destination fingerprint proof failed.", failure);
    }

    private static IEnumerable<string> DuplicateDestinations(IReadOnlyList<DestinationFingerprint> fingerprints)
    {
        // Add a pair only with an explanation of the distinct operation or query it
        // intentionally represents. IDs never participate in either fingerprint.
        var allowedStructurePairs = new Dictionary<string, string>(StringComparer.Ordinal);
        void PermitSharedTemplate(string reason, params string[] ids)
        {
            var ordered = ids.Order(StringComparer.Ordinal).ToArray();
            for (var first = 0; first < ordered.Length; first++)
                for (var second = first + 1; second < ordered.Length; second++)
                    allowedStructurePairs.Add(ordered[first] + "|" + ordered[second], reason);
        }
        PermitSharedTemplate("RegistersTaskNavigation.SelectTask fixes a different RegisterTypeInput value; RefreshRegistersAsync filters by that type.",
            "register-courier", "register-credit", "register-expense", "register-inward", "register-outward", "register-service", "register-transfer", "register-vendor");
        PermitSharedTemplate("RunReportAsync selects distinct Daily, Store, Returns, Brand, BrandSegment and Item query dimensions within one sales table template.",
            "report-sales-store", "report-sales-combined", "report-sales-returns", "report-sales-brand", "report-sales-segment", "report-sales-item");
        PermitSharedTemplate("RunStockInventoryAsync(SLOW) excludes zero stock and ACTIVE movement rows; CLOSING retains them.",
            "report-stock-closing", "report-stock-slow");
        PermitSharedTemplate("OperationsTaskState.ApplyIssueFilter excludes RESOLVED/WAIVED issues only for open-items; data-quality retains history.",
            "data-quality", "open-items");
        foreach (var kind in new[] { "structure", "populated data" })
        {
            foreach (var group in fingerprints.GroupBy(fingerprint => kind == "structure" ? fingerprint.Structure : fingerprint.PopulatedData).Where(group => group.Key is not null && group.Count() > 1))
            {
                var matches = group.OrderBy(fingerprint => fingerprint.Id).ToArray();
                for (var first = 0; first < matches.Length; first++)
                    for (var second = first + 1; second < matches.Length; second++)
                    {
                        var pair = matches[first].Id + "|" + matches[second].Id;
                        if (kind != "structure" || !allowedStructurePairs.ContainsKey(pair))
                            yield return $"OWNER: destinations {matches[first].Id} and {matches[second].Id} have identical {kind} fingerprints. Give each a distinct purpose or document this exact intentional pair.";
                    }
            }
        }
    }
}
