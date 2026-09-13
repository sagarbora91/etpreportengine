using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Modules.DailyWorkflow;

public partial class DailyWorkflowWorkspaceView
{
    public bool IsBusy => operationInProgress || packExportInProgress;
    public void SelectPackTask(string id)
    {
        GenerateStorePackButton.Visibility = id == "combined-pack" ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        GenerateCombinedPackButton.Visibility = id == "store-daily-pack" ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }
    private TextBox[] DraftFields(string task) => task switch
    {
        "Walk-ins" => [ManualValueInput, ManualReasonInput],
        "Stock counts" => [StockGroupInput, StockDisplayInput, StockBackstockInput, StockDefectiveInput, StockYLocationInput, StockPhysicalInput, StockRemarksInput, StockReasonInput],
        "Staff targets" => [StaffTargetCroInput, StaffTargetValueInput, StaffTargetReasonInput],
        _ => throw new ArgumentOutOfRangeException(nameof(task))
    };
    public IReadOnlyList<string> UnsavedDrafts => new[] { "Walk-ins", "Stock counts", "Staff targets" }.Where(task => DraftFields(task).Any(input => input.Text.Length > 0)).ToArray();
    public void DiscardDraft(string task) { foreach (var field in DraftFields(task)) field.Clear(); }
    public async Task<bool> SaveDraftAsync(string task)
    {
        switch (task)
        {
            case "Walk-ins": await SaveManualInputAsync(); break;
            case "Stock counts": await SaveStockCountAsync(); break;
            case "Staff targets": await SaveStaffTargetAsync(); break;
            default: throw new ArgumentOutOfRangeException(nameof(task));
        }
        return !DraftFields(task).Any(input => input.Text.Length > 0);
    }
}
