extern alias EtpApplication;

using ServiceCentre = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentre;
using ServiceCentreAdministration = EtpApplication::Etp.Reporting.Application.Stores.IServiceCentreAdministration;
using ServiceCentreCatalog = EtpApplication::Etp.Reporting.Application.Stores.IServiceCentreCatalog;
using ServiceCentreRules = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentreRules;
using ServiceCentreScope = EtpApplication::Etp.Reporting.Application.Service.ServiceCentreScope;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;
using SqlServerServiceReportQuery = Etp.Reporting.Infrastructure.SqlServer.SqlServerServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// The Service Centre catalogue and the Settings > Stores Service Centre save, as the composition root builds them
/// (1.12 A4, <c>DesktopCompositionRoot.Service.cs</c>). Screens read the factories when they open, so a connection changed in
/// Settings is the one they use. Until the composition root sets them (and in a test that does not), the catalogue is
/// empty: the Service screens then say "Service Centre", and Settings > Stores cannot add a Service Centre.
/// </summary>
public static class ServiceCentreServices
{
    /// <summary>The catalogue factory; null when no composition root set one.</summary>
    public static Func<ServiceCentreCatalog>? Catalog { get; set; }

    /// <summary>The Owner's Service Centre save; null when no composition root set one.</summary>
    public static Func<ServiceCentreAdministration>? Administration { get; set; }

    /// <summary>
    /// The Service Centres, or an empty list when there is no catalogue or it could not be read. A screen only labels itself
    /// with the result, so a failure here is recorded and never stops the screen.
    /// </summary>
    public static async Task<IReadOnlyList<ServiceCentre>> LoadOrEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (Catalog is not { } factory) return [];
        try { return await factory().LoadAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DesktopDiagnostics.Record(exception, "Service.Catalogue", "SERVICE_CENTRE_CATALOGUE_FAILED");
            return [];
        }
    }

    /// <summary>The label beside every Service screen title: the only centre's name, "All Service Centres" or "Service Centre".</summary>
    public static string Label(IReadOnlyList<ServiceCentre> centres) => ServiceCentreRules.ScreenLabel(centres);

    // ------------------------------------------------------------------ 1.12.1 P1: one Service Centre at a time

    /// <summary>The first choice of the Service Centre picker (every centre's jobs together).</summary>
    public const string AllCentresLabel = "All Service Centres";

    /// <summary>
    /// The Service Centre the Service screens show (its store code), or null for all of them. One choice for the whole Service
    /// rail and this session: a screen opened next shows the same centre. A code that is no longer a centre reads as null.
    /// </summary>
    public static string? SelectedStoreCode { get; set; }

    /// <summary>
    /// Scopes a Service query to one centre, or returns null when the query cannot be scoped (then the screens show every
    /// centre and offer no picker). Production: <see cref="SqlServerServiceReportQuery.ForServiceCentre"/>; a test may set it.
    /// </summary>
    public static Func<ServiceReportQuery, string, ServiceReportQuery?> Scope { get; set; } = DefaultScope;

    /// <summary>The production scope: a SQL Server query reads <c>v_service_job.service_store_code</c>; any other query cannot be scoped.</summary>
    public static ServiceReportQuery? DefaultScope(ServiceReportQuery query, string storeCode) =>
        query is SqlServerServiceReportQuery sql ? sql.ForServiceCentre(storeCode) : null;

    /// <summary>The picker's choices: "All Service Centres", then each centre by its catalogue name (code = store code).</summary>
    public static IReadOnlyList<ServiceListChoice> Choices(IReadOnlyList<ServiceCentre> centres)
    {
        ArgumentNullException.ThrowIfNull(centres);
        // 1.12.1 P8: "Not matched to any centre" comes last while there are two or more centres.
        return ServiceCentreChoices.WithNotMatched(
            [new(null, AllCentresLabel), .. centres.Select(centre => new ServiceListChoice(centre.StoreCode, ServiceCentreRules.DisplayName(centre)))],
            centres.Count);
    }

    /// <summary>The chosen centre when it is still one of <paramref name="centres"/>, else null (all centres).</summary>
    public static ServiceCentre? Chosen(IReadOnlyList<ServiceCentre> centres, string? storeCode)
    {
        ArgumentNullException.ThrowIfNull(centres);
        if (string.IsNullOrWhiteSpace(storeCode) || centres.Count < 2) return null;
        return centres.FirstOrDefault(centre => string.Equals(centre.StoreCode.Trim(), storeCode.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The line under a Service screen's title while one centre is chosen: what follows the choice and what does not. Since
    /// 1.12.1 P8 the parts invoices and the closing stock follow it too; money, goods in transit and file dates carry no centre.
    /// </summary>
    public static string ScopeNote(string centreName) => ServiceCentreScope.ScopeNote(centreName);
}
