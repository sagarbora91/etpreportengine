namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopConnectionAuthorizationTests
{
    [Fact]
    public void Successful_connection_switch_refreshes_access_before_using_the_new_database()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Settings", "SettingsWorkspaceView.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var adoptConnection = view.IndexOf("session.CompleteHealthCheck", StringComparison.Ordinal);
        var publishCompletion = view.IndexOf("SettingsWorkspaceOperation.ConnectionTest", adoptConnection, StringComparison.Ordinal);
        var relayStart = main.IndexOf("case SettingsWorkspaceOperation.ConnectionTest:", StringComparison.Ordinal);
        var refreshAccess = main.IndexOf("await RefreshAccessAsync()", relayStart, StringComparison.Ordinal);
        var useAccess = main.IndexOf("if (currentAccess.CanView)", refreshAccess, StringComparison.Ordinal);

        Assert.True(adoptConnection >= 0, "The validated connection must be explicitly adopted.");
        Assert.True(publishCompletion > adoptConnection, "The connection must be adopted before MainWindow is notified.");
        Assert.True(refreshAccess > relayStart, "Access must be reloaded after the connection changes.");
        Assert.True(useAccess > refreshAccess, "No prior-database access decision may be reused against the new database.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
