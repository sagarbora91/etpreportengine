using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Composition;

// 1.12 wave 2, lane A4: the Service Centre is data. The Service screens keep their built-in route (TaskNavigator and
// ServiceScreens.Create); this hook gives them, and Settings > Stores, the store catalogue's Service Centres and the
// Owner's Service Centre save. Both are built from the window's current connection factory each time they are used, so
// a connection changed in Settings is the one they read.
public sealed partial class DesktopCompositionRoot
{
    partial void RegisterService(ScreenRegistry registry)
    {
        ServiceCentreServices.Catalog = () => new SqlServerServiceCentreCatalog(CurrentConnections());
        ServiceCentreServices.Administration = () => new SqlServerServiceCentreAdministration(CurrentConnections());
    }
}
