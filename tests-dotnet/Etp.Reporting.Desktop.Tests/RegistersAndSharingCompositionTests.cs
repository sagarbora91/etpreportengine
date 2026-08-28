namespace Etp.Reporting.Desktop.Tests;

public sealed class RegistersAndSharingCompositionTests
{
    [Fact]
    public void Registers_and_contacts_use_injected_application_services()
    {
        var root = FindRepositoryRoot();
        var productisation = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.Productisation.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));

        Assert.Contains("digitalRegisterServiceFactory(connectionState.ConnectionString).LoadAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("digitalRegisterServiceFactory(connectionState.ConnectionString).SaveAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("sharingContactsServiceFactory(connectionState.ConnectionString).LoadAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("sharingContactsServiceFactory(connectionState.ConnectionString).SaveAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("Func<string, DigitalRegisterService> digitalRegisterServiceFactory", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Func<string, SharingContactsService> sharingContactsServiceFactory", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new SqlServerDigitalRegisterService(value)", composition, StringComparison.Ordinal);
        Assert.Contains("new SqlServerSharingContactsService(value)", composition, StringComparison.Ordinal);
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
