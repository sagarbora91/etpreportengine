using Etp.Reporting.Application.Access;
using App = Etp.Reporting.Application.OperationsAdministration;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>Owner-only SQL adapter for controlled master, user and product configuration.</summary>
public sealed class SqlServerAdministrationService : App.IAdministrationService
{
    private readonly IAdministrationSqlGateway gateway;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    public SqlServerAdministrationService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        gateway = new AdministrationSqlGateway(
            new Phase2OperationsRepository(validated),
            new ProductisationRepository(validated));
        loadAccess = new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
    }

    internal SqlServerAdministrationService(
        IAdministrationSqlGateway gateway,
        Func<CancellationToken, Task<ApplicationAccess>> loadAccess)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.loadAccess = loadAccess ?? throw new ArgumentNullException(nameof(loadAccess));
    }

    public async Task<App.AdministrationDashboard> LoadAsync(
        string masterType,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        var mastersTask = gateway.LoadMastersAsync(masterType, cancellationToken);
        var usersTask = gateway.LoadUsersAsync(cancellationToken);
        var kpisTask = gateway.LoadKpisAsync(cancellationToken);
        var healthTask = gateway.LoadProductHealthAsync(cancellationToken);
        var settingsTask = gateway.LoadProductConfigurationAsync(cancellationToken);
        await Task.WhenAll(mastersTask, usersTask, kpisTask, healthTask, settingsTask).ConfigureAwait(false);
        return new(
            (await mastersTask.ConfigureAwait(false)).Select(Map).ToArray(),
            (await usersTask.ConfigureAwait(false)).Select(Map).ToArray(),
            (await kpisTask.ConfigureAwait(false)).Select(Map).ToArray(),
            (await healthTask.ConfigureAwait(false)).Select(Map).ToArray(),
            Map(await settingsTask.ConfigureAwait(false)));
    }

    public async Task SaveMasterAsync(App.SaveControlledMaster command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        await gateway.SaveMasterAsync(command.MasterType, command.Code, command.DisplayName,
            command.ApprovalStatus, command.IsActive, command.Reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveUserAsync(App.SaveApplicationUser command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        if (command.Role is AccessRole.None) throw new ArgumentException("Select Owner, Store Manager or Viewer.", nameof(command));
        await gateway.SaveUserAsync(command.WindowsIdentity, command.DisplayName, RoleCode(command.Role),
            command.IsActive, command.Reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveProductConfigurationAsync(
        App.SaveProductConfiguration command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        await gateway.SaveProductConfigurationAsync(
            new(command.DocumentRepositoryPath, command.ShareFolderPath, command.OcrHelperPath,
                command.OcrModelPath, command.SmtpHost, command.SmtpPort, command.SmtpUseTls,
                command.SmtpFromAddress, command.MaximumAttachmentMb, DateTime.MinValue, string.Empty),
            command.Reason,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireOwnerAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    internal static AccessRole MapRole(string roleCode) => roleCode.Trim().Replace(' ', '_').ToUpperInvariant() switch
    {
        "VIEWER" => AccessRole.Viewer,
        "STORE_MANAGER" => AccessRole.StoreManager,
        "OWNER" => AccessRole.Owner,
        _ => AccessRole.None
    };

    private static string RoleCode(AccessRole role) => role switch
    {
        AccessRole.Viewer => "VIEWER",
        AccessRole.StoreManager => "STORE_MANAGER",
        AccessRole.Owner => "OWNER",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static App.ControlledMaster Map(ControlledMasterRow row) =>
        new(row.MasterType, row.Code, row.DisplayName, row.ApprovalStatus, row.IsActive, row.ModifiedUtc, row.ModifiedBy);
    private static App.ApplicationUser Map(ApplicationUserRow row) =>
        new(row.Id, row.WindowsIdentity, row.DisplayName, MapRole(row.RoleCode), row.IsActive, row.ModifiedUtc, row.ModifiedBy);
    private static App.KpiDefinition Map(KpiCatalogueRow row) =>
        new(row.Code, row.BusinessName, row.Definition, row.Formula, row.DataSource, row.EffectiveDate,
            row.Version, row.ApprovalStatus, row.ApprovedBy, row.IsActive);
    private static App.ProductHealth Map(ProductHealthItem row) => new(row.Component, row.Status, row.Guidance);
    private static App.ProductConfiguration Map(ProductSettings row) =>
        new(row.DocumentRepositoryPath, row.ShareFolderPath, row.OcrHelperPath, row.OcrModelPath,
            row.SmtpHost, row.SmtpPort, row.SmtpUseTls, row.SmtpFromAddress, row.MaximumAttachmentMb,
            row.ModifiedUtc, row.ModifiedBy);
}

internal interface IAdministrationSqlGateway
{
    Task<IReadOnlyList<ControlledMasterRow>> LoadMastersAsync(string masterType, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationUserRow>> LoadUsersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<KpiCatalogueRow>> LoadKpisAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductHealthItem>> LoadProductHealthAsync(CancellationToken cancellationToken);
    Task<ProductSettings> LoadProductConfigurationAsync(CancellationToken cancellationToken);
    Task SaveMasterAsync(string masterType, string code, string name, string approvalStatus, bool active, string reason, CancellationToken cancellationToken);
    Task SaveUserAsync(string identity, string displayName, string roleCode, bool active, string reason, CancellationToken cancellationToken);
    Task SaveProductConfigurationAsync(ProductSettings settings, string reason, CancellationToken cancellationToken);
}

internal sealed class AdministrationSqlGateway(
    Phase2OperationsRepository operations,
    ProductisationRepository productisation) : IAdministrationSqlGateway
{
    public Task<IReadOnlyList<ControlledMasterRow>> LoadMastersAsync(string masterType, CancellationToken token) => operations.LoadMasterValuesAsync(masterType, token);
    public Task<IReadOnlyList<ApplicationUserRow>> LoadUsersAsync(CancellationToken token) => operations.LoadUsersAsync(token);
    public Task<IReadOnlyList<KpiCatalogueRow>> LoadKpisAsync(CancellationToken token) => productisation.LoadKpiCatalogueAsync(token);
    public Task<IReadOnlyList<ProductHealthItem>> LoadProductHealthAsync(CancellationToken token) => productisation.LoadProductHealthAsync(token);
    public Task<ProductSettings> LoadProductConfigurationAsync(CancellationToken token) => productisation.LoadSettingsAsync(token);
    public Task SaveMasterAsync(string type, string code, string name, string approval, bool active, string reason, CancellationToken token) => operations.UpsertMasterValueAsync(type, code, name, approval, active, reason, token);
    public Task SaveUserAsync(string identity, string name, string role, bool active, string reason, CancellationToken token) => operations.UpsertUserAsync(identity, name, role, active, reason, token);
    public Task SaveProductConfigurationAsync(ProductSettings settings, string reason, CancellationToken token) => productisation.SaveSettingsAsync(settings, reason, token);
}
