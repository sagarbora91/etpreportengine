namespace Etp.Reporting.Desktop.Modules.Settings;

public partial class SettingsWorkspaceView
{
    public System.Windows.Controls.UserControl CreateDataTruthMastersView() =>
        new DataTruthMastersView(() => new Etp.Reporting.Infrastructure.SqlServer.DataTruthMasterRepository(session.ConnectionString), () => access.CanAdminister);

    public System.Windows.Controls.UserControl CreateTallyCompaniesView() =>
        new Etp.Reporting.Desktop.Modules.Accounting.TallyCompaniesView(() => access.CanAdminister,
            () => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyProfileService(session.ConnectionString));

    public System.Windows.Controls.UserControl CreateTallyVouchersView() =>
        new Etp.Reporting.Desktop.Modules.Accounting.TallyVouchersView(() => access.CanAdminister,
            () => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyProfileService(session.ConnectionString),
            () => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallySalesBatchService(session.ConnectionString));

    public System.Windows.Controls.UserControl CreateTallyLedgersView() =>
        new Etp.Reporting.Desktop.Modules.Accounting.TallyLedgersView(() => access.CanAdminister,
            () => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyProfileService(session.ConnectionString),
            () => new Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyLedgerMappingService(session.ConnectionString));

    public System.Windows.Controls.UserControl CreateEveningMastersView(Func<bool> canEditBrands) =>
        new EveningMastersView(() => session.ConnectionString, () => access.CanAdminister, canEditBrands);

    /// <summary>The evidence section for Imports → Problems (spec 12); the same view Settings → Database hosts.</summary>
    public ImportEvidenceView CreateImportEvidenceView(Func<bool> canAdminister) =>
        new(() => new Etp.Reporting.Infrastructure.SqlServer.SqlServerImportEvidenceService(session.ConnectionString), canAdminister);

    private void InitializeDataTruthMasters() =>
        InitializeEveningMasters();

    private void InitializeEveningMasters()
    {
        DataTruthMastersHost.Children.Add(CreateDataTruthMastersView());
        DataTruthMastersHost.Children.Add(new EveningMastersView(() => session.ConnectionString, () => access.CanAdminister));
        DataTruthMastersHost.Children.Add(CreateImportEvidenceView(() => access.CanAdminister));
    }
}
