using Etp.Reporting.Desktop.Composition;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopCompositionRootTests
{
    [Fact]
    public void Default_connection_preserves_the_installed_sql_express_database()
    {
        Assert.Equal(
            @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True",
            DesktopCompositionRoot.DefaultConnectionString);
    }

    [Fact]
    public void Migration_directory_is_resolved_below_the_application_base_directory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "EtpCompositionRoot", Guid.NewGuid().ToString("N"));
        var root = new DesktopCompositionRoot(baseDirectory, DesktopCompositionRoot.DefaultConnectionString);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(baseDirectory), "database", "migrations"),
            root.MigrationDirectory);
    }

    [Fact]
    public void Settings_directory_can_be_injected_for_a_safe_desktop_host()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "EtpCompositionSettings", Guid.NewGuid().ToString("N"));
        var root = new DesktopCompositionRoot(
            AppContext.BaseDirectory,
            DesktopCompositionRoot.DefaultConnectionString,
            settingsDirectory);

        Assert.Equal(Path.GetFullPath(settingsDirectory), root.SettingsDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_base_directory_is_rejected(string baseDirectory)
    {
        Assert.Throws<ArgumentException>(() =>
            new DesktopCompositionRoot(baseDirectory, DesktopCompositionRoot.DefaultConnectionString));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_connection_string_is_rejected(string connectionString)
    {
        Assert.Throws<ArgumentException>(() =>
            new DesktopCompositionRoot(AppContext.BaseDirectory, connectionString));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Explicit_blank_settings_directory_is_rejected(string settingsDirectory)
    {
        Assert.Throws<ArgumentException>(() => new DesktopCompositionRoot(
            AppContext.BaseDirectory,
            DesktopCompositionRoot.DefaultConnectionString,
            settingsDirectory));
    }
}
