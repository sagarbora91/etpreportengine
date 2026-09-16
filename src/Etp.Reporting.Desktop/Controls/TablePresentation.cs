using System.Collections;
using System.ComponentModel;
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
            var row = items?.Cast<object>().FirstOrDefault();
            var type = row?.GetType() ?? items?.GetType().GetInterfaces()
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (type is null) return;
            grid.Columns.Clear();
            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(type))
            {
                if (!property.IsBrowsable) continue;
                var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var numeric = valueType == typeof(decimal) || valueType == typeof(double) || valueType == typeof(float) || valueType == typeof(int) || valueType == typeof(long);
                var money = valueType == typeof(decimal) && Regex.IsMatch(property.Name, "Amount|Value|Sales|Cash|Card|Bank|GiftCard|Opening|Closing|Total|Balance|Expense|UPI", RegexOptions.IgnoreCase)
                    && !Regex.IsMatch(property.Name,"Count|Quantity|Rows|Units",RegexOptions.IgnoreCase);
                var header = Regex.Replace(property.Name, "(?<=[a-z0-9])(?=[A-Z])", " ");
                if (property.Name == "StoreCode") header = "Store";
                if (money) header += " ₹";
                var binding = new Binding(property.Name) { Converter = new CellFormat(valueType,numeric,property.Name), Mode = BindingMode.OneWay };
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,numeric ? TextAlignment.Right : TextAlignment.Left));
                style.Setters.Add(new Setter(TextBlock.PaddingProperty,new Thickness(6,4,6,4)));
                grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = binding, ElementStyle = style, MinWidth = numeric ? 100 : 120, IsReadOnly = true });
            }
        }
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty,typeof(DataGrid)).AddValueChanged(grid,(_,_) => Rebuild());
        Rebuild();
    }
    private sealed class CellFormat(Type type,bool numeric,string name) : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture) => value switch
        {
            null => "—",
            string store when name is "Store" or "StoreCode" => StoreLabel(store),
            DateTime date => date.ToString("dd MMM yyyy",PresentationCulture.Indian),
            DateOnly date => date.ToString("dd MMM yyyy",PresentationCulture.Indian),
            IFormattable number when numeric => number.ToString(type == typeof(int) || type == typeof(long) ? "N0" : "N2",PresentationCulture.Indian),
            _ => value.ToString() ?? "—"
        };
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture) => throw new NotSupportedException();
    }
    internal static string StoreLabel(string value) => value switch
    {
        "WLMHW" or "Titan" => "Titan World", "HEMW" => "Helios", "COMBINED" or "All" => "Both stores", _ => value
    };
}
