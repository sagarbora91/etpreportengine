namespace Etp.Reporting.Desktop.Tests;

public sealed class RegistersAndSharingCompositionTests
{
    [Fact]
    public void Registers_and_contacts_use_injected_application_services()
    {
        var root = FindRepositoryRoot();
        var productisationPath = Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.Productisation.cs");
        Assert.False(File.Exists(productisationPath));
        const string productisation = "";
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var registersWorkspace = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Registers", "RegistersWorkspaceView.xaml.cs"));
        var archiveWorkspace = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Archive", "ArchiveWorkspaceView.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));

        Assert.Contains("session.RefreshAsync(connectionStringProvider()", registersWorkspace, StringComparison.Ordinal);
        Assert.Contains("session.SaveAsync(connectionStringProvider()", registersWorkspace, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterGrid", productisation, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveRegisterEntry_Click", productisation, StringComparison.Ordinal);
        Assert.Contains("session.LoadContactsAsync(connectionStringProvider())", archiveWorkspace, StringComparison.Ordinal);
        Assert.Contains("session.SaveContactAsync(connectionStringProvider()", archiveWorkspace, StringComparison.Ordinal);
        Assert.DoesNotContain("SharingContactsGrid", productisation, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSharingContact_Click", productisation, StringComparison.Ordinal);
        Assert.Contains("RegistersWorkspaceView registersWorkspaceView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ArchiveWorkspaceView archiveWorkspaceView", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<string, DigitalRegisterService>", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<string, SharingContactsService>", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new SqlServerDigitalRegisterService(value)", composition, StringComparison.Ordinal);
        Assert.Contains("new SqlServerSharingContactsService(value)", composition, StringComparison.Ordinal);
        Assert.Contains("new RegistersPresentationSession(digitalRegisterServiceFactory)", composition, StringComparison.Ordinal);
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
