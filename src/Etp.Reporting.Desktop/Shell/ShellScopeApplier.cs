using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

internal static class ShellScopeApplier
{
    private static readonly string[] Dates = ["BusinessDateInput", "RegisterBusinessDateInput", "AccountingDateInput", "ArchiveDateInput", "DocumentDateInput", "AdjustmentDateInput"];
    private static readonly string[] Stores = ["StoreInput", "RegisterStoreInput", "AccountingStoreInput", "ArchiveStoreInput", "DocumentStoreInput", "AdjustmentStoreInput"];
    public static void Apply(UserControl view, DateTime date, string store)
    {
        if (view is Modules.Registers.RegistersWorkspaceView registers) registers.ApplyHeaderScope(date, store);
        foreach (var name in Dates)
            if (view.FindName(name) is DatePicker picker) { picker.SelectedDate = date; Hide(picker); }
        foreach (var name in Stores)
        {
            if (view.FindName(name) is TextBox text) { text.Text = store; Hide(text); }
            if (view.FindName(name) is ComboBox combo)
            {
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Content?.ToString() == (store.Length == 0 && name == "ArchiveStoreInput" ? "All" : store));
                Hide(combo);
            }
        }
    }
    private static void Hide(FrameworkElement field)
    {
        field.Visibility = Visibility.Collapsed;
        if (field.Parent is Panel parent)
        {
            var index = parent.Children.IndexOf(field);
            if (index > 0 && parent.Children[index-1] is TextBlock label) label.Visibility = Visibility.Collapsed;
        }
    }
}
