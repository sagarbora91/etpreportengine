extern alias EtpApplication;
using System.Windows;
using DigitalRegisterEntry = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntry;
namespace Etp.Reporting.Desktop.Modules.DailyWorkflow;

public partial class DailyWorkflowWorkspaceView
{
    private Func<string, DateOnly, CancellationToken, Task<IReadOnlyList<DigitalRegisterEntry>>>? loadDayRegisters;
    public event EventHandler<DigitalRegisterEntry>? RegisterNavigationRequested;
    public void AttachRegisters(Func<string, DateOnly, CancellationToken, Task<IReadOnlyList<DigitalRegisterEntry>>> loader) => loadDayRegisters = loader;
    public async Task RefreshDayRegistersAsync()
    {
        DayRegistersGrid.ItemsSource = null;
        if (loadDayRegisters is null || string.IsNullOrWhiteSpace(StoreCode) || BusinessDate is null) return;
        try
        {
            RequireViewAccess();
            var scope = SelectedScope();
            var entries = await loadDayRegisters(scope.StoreCode, scope.BusinessDate, CancellationToken.None);
            if (StoreCode != scope.StoreCode || BusinessDate != scope.BusinessDate.ToDateTime(TimeOnly.MinValue)) return;
            DayRegistersGrid.ItemsSource = entries;
            DayRegistersStatus.Text = $"{entries.Count:N0} entries; {entries.Count(x => x.VerificationStatus != "VERIFIED"):N0} awaiting verification.";
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "DailyWorkflow.Registers", "DAY_REGISTERS_FAILED"); DayRegistersStatus.Text = DesktopFriendlyError.Describe(ex); }
    }
    private async void RefreshDayRegisters_Click(object sender, RoutedEventArgs e) => await RefreshDayRegistersAsync();
    private void OpenDayRegister_Click(object sender, RoutedEventArgs e)
    {
        if (!access().CanImport) { DayRegistersStatus.Text = "Owner or Store Manager permission is required to open registers."; return; }
        if (DayRegistersGrid.SelectedItem is DigitalRegisterEntry entry) RegisterNavigationRequested?.Invoke(this, entry);
    }
}
