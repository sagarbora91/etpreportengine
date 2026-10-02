namespace Etp.Reporting.Desktop.Modules.Settings;

public partial class SettingsWorkspaceView
{
    public System.Windows.Controls.UserControl CreateDataTruthMastersView() =>
        new DataTruthMastersView(() => session.ConnectionString, () => access.CanAdminister);

    public System.Windows.Controls.UserControl CreateTallyCompaniesView() =>
        new Etp.Reporting.Desktop.Modules.Accounting.TallyCompaniesView(() => session.ConnectionString, () => access.CanAdminister,
            connection => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyProfileService(connection));

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
