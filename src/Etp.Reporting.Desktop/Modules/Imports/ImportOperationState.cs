using System.Windows;
using System.Windows.Controls;
namespace Etp.Reporting.Desktop.Modules.Imports;

public partial class ImportWorkspaceView
{
    private static string ImportFailureMessage(bool committed, string failure) => committed
        ? $"The data import completed, but its follow-up evidence retention or display refresh did not finish: {failure} Verify the source in Source Inbox. Existing duplicate protections remain active."
        : $"Import failed: {failure}";
    public bool IsBusy { get; private set; }
    private Control[] importControls = [];
    private void CaptureImportControls()
    {
        IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
        }
        importControls = Descendants(this).OfType<Control>().Where(control => control is Button or TextBox or ComboBox or DatePicker or CheckBox).ToArray();
    }
    private IDisposable? BeginImportOperation()
    {
        if (IsBusy) return null;
        IsBusy = true;
        RetryAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        var states = importControls.Select(control => (Control: control, Enabled: control.IsEnabled)).ToArray();
        foreach (var state in states) state.Control.IsEnabled = false;
        return new ImportCompletion(() =>
        {
            foreach (var state in states) state.Control.IsEnabled = state.Enabled;
            IsBusy = false; CancelBatchButton.IsEnabled = false;
            RetryAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        });
    }
    private sealed class ImportCompletion(Action complete) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; complete(); }
    }
}
