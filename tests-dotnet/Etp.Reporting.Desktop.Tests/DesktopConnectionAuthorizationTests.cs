namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopConnectionAuthorizationTests
{
    [Fact]
    public void Successful_connection_switch_refreshes_access_before_using_the_new_database()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Etp.Reporting.Desktop",
            "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf(
            "private async Task CheckConnectionAndRefreshAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void SetConnectionState",
            methodStart,
            StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        var adoptConnection = method.IndexOf(
            "connectionState.TryUpdate(validation.ConnectionString",
            StringComparison.Ordinal);
        var refreshAccess = method.IndexOf(
            "await RefreshAccessAsync()",
            adoptConnection,
            StringComparison.Ordinal);
        var useAccess = method.IndexOf(
            "if (currentAccess.CanView)",
            adoptConnection,
            StringComparison.Ordinal);

        Assert.True(adoptConnection >= 0, "The validated connection must be explicitly adopted.");
        Assert.True(refreshAccess > adoptConnection, "Access must be reloaded after the connection changes.");
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
