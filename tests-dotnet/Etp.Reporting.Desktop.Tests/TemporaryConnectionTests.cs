using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

public sealed class TemporaryConnectionTests
{
    [Fact]
    public void Explicit_startup_connection_is_validated_and_used_for_the_session()
    {
        var root = DesktopCompositionRoot.CreateForArguments(["--connection-string", @"Server=.\SQLEXPRESS;Database=EtpPhase1Test_Review;Integrated Security=True;TrustServerCertificate=True"]);
        Assert.Equal("EtpPhase1Test_Review", new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(root.LoadConnectionString()).InitialCatalog);
        Assert.Throws<ArgumentException>(() => DesktopCompositionRoot.CreateForArguments(["--connection-string"]));
        Assert.Throws<ArgumentException>(() => DesktopCompositionRoot.CreateForArguments(["--connection-string", "Server=.;User ID=user;Password=test"]));
        Assert.Equal(DesktopStartupMode.InitializeDatabase, DesktopStartupCoordinator.Route(["--initialize-database", "--connection-string", "connection"]));
    }

    [Fact]
    public void Temporary_connection_neither_loads_nor_overwrites_saved_connection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpTemporaryConnectionTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DesktopSettingsStore(directory);
            store.Save(@"Server=.\SQLEXPRESS;Database=SavedShop;Integrated Security=True;TrustServerCertificate=True");
            var session = new DesktopSettingsPresentationSession(store,
                new DesktopConnectionState(@"Server=.\SQLEXPRESS;Database=EtpPhase1Test_Review;Integrated Security=True;TrustServerCertificate=True"), temporaryConnection: true);
            Assert.Contains("EtpPhase1Test_Review", session.LoadConnectionString());
            var candidate = session.ValidateCandidate(session.ConnectionString);
            session.CompleteHealthCheck(candidate, true, "Connected", "SQL");
            Assert.Contains("SavedShop", store.Load()!.ConnectionString);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
