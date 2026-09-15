using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>
/// Reuses each module's live controls and handlers while presenting only the selected task.
/// Controls are retained for the lifetime of their workspace; navigation never recreates a job or draft.
/// </summary>
public static class FocusedTaskLayout
{
    private sealed record Parts(Panel Original, UIElement[] Children, List<(Panel Parent, Button Button, int Index)> Buttons);
    private static readonly ConditionalWeakTable<UserControl, Parts> Saved = new();

    public static void Show(UserControl view, string title, int[] content, int[] actions)
    {
        if (!Saved.TryGetValue(view, out var parts))
        {
            var root = view.Content is Border border ? border.Child : view.Content;
            if (root is not Panel panel) throw new InvalidOperationException($"{view.GetType().Name} requires an explicit task layout.");
            LabelFields(panel);
            var buttons = panel.Children.OfType<Panel>().SelectMany(parent => parent.Children.OfType<Button>()
                .Select(button => (parent, button, parent.Children.IndexOf(button)))).ToList();
            parts = new(panel, panel.Children.Cast<UIElement>().ToArray(), buttons);
            Saved.Add(view, parts);
        }
        foreach (var (parent, button, index) in parts.Buttons)
        {
            if (button.Parent is Panel current && current != parent) { current.Children.Remove(button); parent.Children.Insert(Math.Min(index, parent.Children.Count), button); }
        }
        foreach (var element in parts.Children)
        {
            if (element is FrameworkElement { Parent: Panel parent }) parent.Children.Remove(element);
        }
        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var toolbar = new WrapPanel();
        foreach (var index in actions)
        {
            if (parts.Children[index] is Panel panel)
            {
                foreach (var button in panel.Children.OfType<Button>().ToArray()) { panel.Children.Remove(button); toolbar.Children.Add(button); }
            }
        }
        actionPanel.Children.Add(toolbar);
        var available = toolbar.Children.OfType<Button>().Where(button => button.Visibility == Visibility.Visible).ToArray();
        var primary = available.FirstOrDefault(button => button.Content?.ToString() is { } caption && (caption.StartsWith("Save", StringComparison.Ordinal) || caption.StartsWith("Submit", StringComparison.Ordinal) || caption.StartsWith("Update", StringComparison.Ordinal))) ?? available.FirstOrDefault();
        foreach (var button in available)
        {
            if (button.Content?.ToString() is { } caption && (caption.StartsWith("Reject", StringComparison.Ordinal) || caption == "Waive")) continue;
            if (button == primary) button.SetResourceReference(Control.StyleProperty, "PrimaryButton"); else button.ClearValue(Control.StyleProperty);
        }
        var guidance = title switch
        {
            "Support Package" => "Create an aggregate-only diagnostic package. Source rows and confidential identifiers are excluded. The result shows the saved package location.",
            "Backups" => "Create a checksum backup and verify it. The result reports whether verification succeeded and identifies the backup location.",
            "Restore & Recovery Drill" => "Restore a backup into an isolated temporary database, check integrity and lineage, then remove that temporary database. The active database is retained.",
            _ => null
        };
        if (guidance is not null) actionPanel.Children.Add(new TextBlock { Text = guidance, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,0), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left });
        layout.Children.Add(actionPanel);
        var body = new StackPanel();
        var indices = content.Concat(actions).Distinct().ToArray();
        for (var position = 0; position < indices.Length; position++)
        {
            var index = indices[position];
            // A toolbar's old title block is already represented by the persistent task header.
            if (parts.Children[index] is DockPanel && !content.Contains(index)) continue;
            if (title == "Watch Folder" && index == 12)
            {
                var fields = new WrapPanel { ItemWidth = 350 };
                for (var labelIndex = 12; labelIndex <= 18; labelIndex += 2)
                {
                    var pair = new StackPanel { Margin = new Thickness(0,0,12,8) };
                    pair.Children.Add(parts.Children[labelIndex]); pair.Children.Add(parts.Children[labelIndex + 1]); fields.Children.Add(pair);
                }
                fields.SizeChanged += (_, e) => fields.ItemWidth = e.NewSize.Width >= 560 ? e.NewSize.Width / 2 : e.NewSize.Width;
                body.Children.Add(fields); position += 7; continue;
            }
            body.Children.Add(parts.Children[index]);
        }
        var taskBody = TaskBodyLayout.Create(view, title, body);
        Grid.SetRow(taskBody, 1); layout.Children.Add(taskBody);
        AutomationProperties.SetName(layout, title + " focused task");
        view.Content = layout;
    }

    private static void LabelFields(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToArray()) LabelFields(child);
        if (root is not WrapPanel panel) return;
        foreach (var input in panel.Children.OfType<Control>().Where(x => x is TextBox or ComboBox or DatePicker).ToArray())
        {
            if (input is DatePicker) input.MinWidth = 180;
            var label = AutomationProperties.GetName(input);
            if (string.IsNullOrWhiteSpace(label)) continue;
            var index = panel.Children.IndexOf(input);
            if (index > 0 && panel.Children[index - 1] is TextBlock) continue;
            panel.Children.Remove(input);
            var field = new StackPanel { Margin = new Thickness(0,4,8,4), Width = double.IsNaN(input.Width) ? Math.Max(220, input.MinWidth) : Math.Max(input.Width, input.MinWidth) };
            field.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(UIElement.Visibility)) { Source = input });
            field.Children.Add(new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,4) });
            input.Margin = new Thickness(0); field.Children.Add(input); panel.Children.Insert(index, field);
        }
    }
}
