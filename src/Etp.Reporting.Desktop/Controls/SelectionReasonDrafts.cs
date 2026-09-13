using System.Windows.Controls;
namespace Etp.Reporting.Desktop;

/// <summary>Keeps review text tied to its exact record when selection or a refreshed list changes.</summary>
public sealed class SelectionReasonDrafts
{
    private readonly Dictionary<long, string> drafts = new();
    private readonly TextBox input;
    private long? editing;
    public SelectionReasonDrafts(DataGrid grid, TextBox input, Func<object?, long?> recordId)
    {
        this.input = input;
        grid.SelectionChanged += (_, _) =>
        {
            Remember(); editing = recordId(grid.SelectedItem);
            input.Text = editing is { } id ? drafts.GetValueOrDefault(id) ?? "" : "";
            input.IsEnabled = editing is not null;
        };
        input.IsEnabled = grid.SelectedItem is not null;
    }
    private void Remember()
    {
        if (editing is not { } id) return;
        if (input.Text.Length == 0) drafts.Remove(id); else drafts[id] = input.Text;
    }
    public bool HasDrafts { get { Remember(); return drafts.Count > 0; } }
    public void Discard() { drafts.Clear(); input.Clear(); }
}
