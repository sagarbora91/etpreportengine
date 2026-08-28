namespace Etp.Reporting.Desktop.Tests;

public sealed class SourceInboxCompositionTests
{
    [Fact]
    public void Source_inbox_uses_the_injected_application_service()
    {
        var root = FindRepositoryRoot();
        var productisation = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.Productisation.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));

        Assert.Contains("sourceInboxServiceFactory(connectionState.ConnectionString).LoadDocumentsAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("sourceInboxServiceFactory(connectionState.ConnectionString).LoadExtractionsAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("sourceInboxServiceFactory(connectionState.ConnectionString).ReviewExtractionAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("sourceInboxServiceFactory(connectionState.ConnectionString).IntakeAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("sourceInboxServiceFactory(connectionState.ConnectionString).VerifyIntegrityAsync", productisation, StringComparison.Ordinal);
        Assert.Contains("new SqlServerSourceInboxService(value)", composition, StringComparison.Ordinal);
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
