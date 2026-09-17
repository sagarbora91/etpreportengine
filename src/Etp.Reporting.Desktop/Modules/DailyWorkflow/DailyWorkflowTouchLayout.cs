using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Etp.Reporting.Desktop.Modules.DailyWorkflow;

public partial class DailyWorkflowWorkspaceView
{
    private bool walkinsMode;
    private bool cashInputsMode;
    public void PrepareTouchTask(string id)
    {
        walkinsMode = id == "walk-ins";
        cashInputsMode = id == "cash-input";
        if (walkinsMode) ManualFieldInput.SelectedValue = "WALK_INS";
        if (cashInputsMode) ManualFieldInput.SelectedValue = "OPENING_CASH";
        ((UIElement)BusinessDateInput.Parent).Visibility = Visibility.Collapsed;
        ManualValueInput.InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } };
        if (ManualValueInput.Parent is Panel entry && entry.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            label.Text = walkinsMode ? "Walk-in count *" : "Amount *";
        ManualFieldInput.Visibility = walkinsMode ? Visibility.Collapsed : Visibility.Visible;
        SaveManualInputButton.Content = walkinsMode ? "Save walk-ins" : "Save entry";
        if (ManualReasonInput.Parent is Panel reasonPanel && reasonPanel.Children.OfType<TextBlock>().FirstOrDefault() is { } fieldLabel)
            fieldLabel.Text = walkinsMode ? "Reason (optional)" : "Reason *";
        if (id == "walk-ins")
        {
            if (ManualReasonInput.Parent is Panel reason && reason.Children.OfType<TextBlock>().FirstOrDefault() is { } reasonLabel) reasonLabel.Text = "Reason (optional)";
            ManualReasonInput.ToolTip = "Optional. Defaults to Daily count.";
            Dispatcher.BeginInvoke(() => ManualValueInput.Focus());
        }
        if (string.IsNullOrWhiteSpace(StoreCode))
            WorkflowMessage.Text = "Choose Titan World or Helios in the header.";
    }
}
