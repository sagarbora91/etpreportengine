namespace Etp.Reporting.Desktop.Modules.Settings;

public partial class SettingsWorkspaceView
{
    public System.Windows.Controls.UserControl CreateDataTruthMastersView() =>
        new DataTruthMastersView(() => session.ConnectionString, () => access.CanAdminister);

    public System.Windows.Controls.UserControl CreateEveningMastersView(Func<bool> canEditBrands) =>
        new EveningMastersView(() => session.ConnectionString, () => access.CanAdminister, canEditBrands);

    private void InitializeDataTruthMasters() =>
        InitializeEveningMasters();

    private void InitializeEveningMasters()
    {
        DataTruthMastersHost.Children.Add(CreateDataTruthMastersView());
        DataTruthMastersHost.Children.Add(new EveningMastersView(() => session.ConnectionString, () => access.CanAdminister));
    }
}
