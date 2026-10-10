namespace Etp.Reporting.Desktop.Modules.Settings;

public partial class SettingsWorkspaceView
{
    public System.Windows.Controls.UserControl CreateDataTruthMastersView() =>
        new DataTruthMastersView(() => session.ConnectionString, () => access.CanAdminister);

    public System.Windows.Controls.UserControl CreateTallyCompaniesView() =>
        new Etp.Reporting.Desktop.Modules.Accounting.TallyCompaniesView(() => session.ConnectionString, () => access.CanAdminister,
            connection => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyProfileService(connection));

    public System.Windows.Controls.UserControl CreateTallyVouchersView() =>
        new Etp.Reporting.Desktop.Modules.Accounting.TallyVouchersView(() => session.ConnectionString, () => access.CanAdminister,
            connection => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyProfileService(connection),
            connection => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallySalesBatchService(connection));

    public System.Windows.Controls.UserControl CreateTallyLedgersView() =>
        new Etp.Reporting.Desktop.Modules.Accounting.TallyLedgersView(() => session.ConnectionString, () => access.CanAdminister,
            connection => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyProfileService(connection),
            connection => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyLedgerMappingService(connection));

    public System.Windows.Controls.UserControl CreateEveningMastersView(Func<bool> canEditBrands) =>
        new EveningMastersView(() => session.ConnectionString, () => access.CanAdminister, canEditBrands);

    /// <summary>The evidence section for Imports → Problems (spec 12); the same view Settings → Database hosts.</summary>
    public ImportEvidenceView CreateImportEvidenceView(Func<bool> canAdminister) =>
        new(() => session.ConnectionString, canAdminister);

    private void InitializeDataTruthMasters() =>
        InitializeEveningMasters();

    private void InitializeEveningMasters()
    {
        DataTruthMastersHost.Children.Add(CreateDataTruthMastersView());
        DataTruthMastersHost.Children.Add(new EveningMastersView(() => session.ConnectionString, () => access.CanAdminister));
        DataTruthMastersHost.Children.Add(new ImportEvidenceView(() => session.ConnectionString, () => access.CanAdminister));
    }
}
