extern alias EtpApplication;

using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Etp.Reporting.Desktop;

/// <summary>Creates typed, read-only columns; amounts and dates never use default ToString.</summary>
public static class TablePresentation
{
    public static void Configure(DataGrid grid)
    {
        if (!grid.AutoGenerateColumns)
        {
            foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
                if (column.Binding is Binding { Path.Path: "StoreCode" or "Store" } binding && binding.Converter is null)
                {
                    column.Binding = new Binding(binding.Path.Path) { Converter = new CellFormat(typeof(string),false,"StoreCode") };
                    column.MinWidth = 110;
                }
            return;
        }
        grid.AutoGenerateColumns = false;
        void Rebuild()
        {
            var items = grid.ItemsSource;
            var source = (items as ICollectionView)?.SourceCollection ?? items;
            var row = items?.Cast<object>().FirstOrDefault();
            var type = row?.GetType() ?? source?.GetType().GetInterfaces()
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            // DataRowView exposes the table's columns through its instance descriptor.
            // ITypedList also retains that schema when the table or filtered view is empty.
            var properties = (source as ITypedList)?.GetItemProperties(null)
                ?? (row is not null ? TypeDescriptor.GetProperties(row) : type is not null ? TypeDescriptor.GetProperties(type) : null);
            if (properties is null || properties.Count == 0 && row is null && type == typeof(object)) return;
            var table = (source as DataView)?.Table ?? (row as DataRowView)?.DataView.Table;
            grid.Columns.Clear();
            foreach (var column in Columns(properties, type, table))
            {
                var binding = new Binding(column.Property.Name) { Converter = column.Format, Mode = BindingMode.OneWay };
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,column.Numeric ? TextAlignment.Right : TextAlignment.Left));
                style.Setters.Add(new Setter(TextBlock.PaddingProperty,new Thickness(6,4,6,4)));
                grid.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = binding, ElementStyle = style, MinWidth = column.Numeric ? 100 : 120, IsReadOnly = true });
            }
        }
        PropertyChangeWatcher.Watch(grid,ItemsControl.ItemsSourceProperty,Rebuild);
        Rebuild();
    }

    private sealed record GridColumn(PropertyDescriptor Property, string Header, bool Numeric, CellFormat Format);

    /// <summary>
    /// The visible columns of a row type, in order. A report row type listed in <see cref="ReportGridColumns"/> takes the
    /// Excel export's headers and order (RA-EXPORT-06, 9 Oct 2026); a DataTable keeps its captions; anything else splits
    /// the property name and guesses money from it.
    /// </summary>
    private static IEnumerable<GridColumn> Columns(PropertyDescriptorCollection properties, Type? type, DataTable? table)
    {
        static (Type ValueType, bool Numeric) Shape(PropertyDescriptor property)
        {
            var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            return (valueType, valueType == typeof(decimal) || valueType == typeof(double) || valueType == typeof(float) || valueType == typeof(int) || valueType == typeof(long));
        }
        if (table is null && ReportGridColumns.For(type) is { } mapped)
        {
            foreach (var column in mapped)
            {
                if (properties.Find(column.Property, false) is not { } property) continue;
                var (valueType, numeric) = Shape(property);
                yield return new(property, column.Money ? column.Header + " ₹" : column.Header, numeric, new CellFormat(valueType, numeric, property.Name));
            }
            yield break;
        }
        foreach (PropertyDescriptor property in properties)
        {
            if (!property.IsBrowsable || ReportGridColumns.IsHidden(property.Name)) continue;
            var (valueType, numeric) = Shape(property);
            var money = valueType == typeof(decimal) && Regex.IsMatch(property.Name, "Amount|Value|Sales|Cash|Card|Bank|GiftCard|Opening|Closing|Total|Balance|Expense|UPI", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(property.Name,"Count|Quantity|Rows|Units",RegexOptions.IgnoreCase);
            var header = table?.Columns[property.Name]?.Caption ?? ReportGridColumns.GenericHeader(property.Name) ?? Regex.Replace(property.Name, "(?<=[a-z0-9])(?=[A-Z])", " ");
            if (money && table is null) header += " ₹";
            yield return new(property, header, numeric, new CellFormat(valueType, numeric, property.Name));
        }
    }

    /// <summary>
    /// Report audit of 3 Oct 2026 (fix lists FIX-04, FIX-05, FIX-08, FIX-12) and 9 Oct 2026 (RA-EXPORT-06): the on-screen
    /// header of a report row property, the same text the Excel export uses. Wording only: the values are unchanged.
    /// </summary>
    internal static string? ReportHeader(Type? type, string property) =>
        ReportGridColumns.For(type)?.FirstOrDefault(column => column.Property == property)?.Header ?? ReportGridColumns.GenericHeader(property);

    /// <summary>
    /// RA-UI-19 / RA-EXPORT-13 (9 Oct 2026): the "Report row details" window lists the grid's headers and formatted
    /// values, not raw property names and ToString() output, and leaves internal columns out.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> DescribeRow(object row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var table = (row as DataRowView)?.DataView.Table;
        return Columns(TypeDescriptor.GetProperties(row), row.GetType(), table)
            .Select(column => (column.Header, (string)column.Format.Convert(column.Property.GetValue(row)!, typeof(string), null!, PresentationCulture.Indian)))
            .ToArray();
    }

    /// <summary>"MissingTender" reads "Missing tender", "NotRun" reads "Not run"; anything that is not a PascalCase word pair is unchanged.</summary>
    internal static string SpacedWords(string value)
    {
        if (!Regex.IsMatch(value, "^[A-Z][a-z]+(?:[A-Z][a-z]+)+$")) return value;
        var words = Regex.Split(value, "(?<=[a-z])(?=[A-Z])");
        return words[0] + " " + string.Join(" ", words.Skip(1).Select(word => word.ToLowerInvariant()));
    }

    private sealed class CellFormat(Type type,bool numeric,string name) : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture) => value switch
        {
            null or DBNull => "—",
            string store when name is "Store" or "StoreCode" => StoreLabel(store),
            // Growth Status and Availability carry MetricAvailability names ("MissingSource") as text.
            string status when name is "GrowthStatus" or "Availability" => SpacedWords(status),
            Enum member => SpacedWords(member.ToString()),
            DateTime date => date.ToString("dd MMM yyyy",PresentationCulture.Indian),
            DateOnly date => date.ToString("dd MMM yyyy",PresentationCulture.Indian),
            // A year is an identifier: 2027, never "2,027" (RA-UI-18).
            IFormattable number when numeric && (type == typeof(int) || type == typeof(long)) => number.ToString(name.EndsWith("Year", StringComparison.Ordinal) ? "D" : "N0",PresentationCulture.Indian),
            IFormattable number when numeric => number.ToString("N2",PresentationCulture.Indian),
            _ => value.ToString() ?? "—"
        };
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture) => throw new NotSupportedException();
    }
    internal static string StoreLabel(string value) => value switch
    {
        "COMBINED" or "All" => "All stores", _ => value
    };
}
