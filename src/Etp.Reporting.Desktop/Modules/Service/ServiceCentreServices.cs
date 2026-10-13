extern alias EtpApplication;

using ServiceCentre = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentre;
using ServiceCentreAdministration = EtpApplication::Etp.Reporting.Application.Stores.IServiceCentreAdministration;
using ServiceCentreCatalog = EtpApplication::Etp.Reporting.Application.Stores.IServiceCentreCatalog;
using ServiceCentreRules = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentreRules;

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
}
