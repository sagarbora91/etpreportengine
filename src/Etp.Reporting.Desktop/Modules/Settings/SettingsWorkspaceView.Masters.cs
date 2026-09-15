namespace Etp.Reporting.Desktop.Modules.Settings;

public partial class SettingsWorkspaceView
{
    public System.Windows.Controls.UserControl CreateDataTruthMastersView() =>
        new DataTruthMastersView(() => session.ConnectionString, () => access.CanAdminister);

    private void InitializeDataTruthMasters() =>
        DataTruthMastersHost.Children.Add(CreateDataTruthMastersView());
}
