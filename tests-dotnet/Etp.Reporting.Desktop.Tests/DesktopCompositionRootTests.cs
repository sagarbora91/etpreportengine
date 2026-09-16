using Etp.Reporting.Desktop.Composition;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopCompositionRootTests
{
    [Fact]
    public void Machine_configuration_preserves_its_exact_target_and_ignores_other_settings()
    {
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DesktopCompositionRoot.ParseOperationsConnectionString(
            """{"serverInstance":"localhost\\ConfiguredInstance","database":"ConfiguredDatabase","connectionString":"Server=remote;Database=AnotherDatabase"}"""));
        Assert.Equal(@"localhost\ConfiguredInstance", connection.DataSource);
        Assert.Equal("ConfiguredDatabase", connection.InitialCatalog);
        Assert.True(connection.IntegratedSecurity);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"serverInstance\":\"localhost\"}")]
    [InlineData("{\"serverInstance\":null,\"database\":\"Target\"}")]
    public void Incomplete_machine_configuration_never_falls_back_to_a_default(string configuration) =>
        Assert.Throws<InvalidOperationException>(() => DesktopCompositionRoot.ParseOperationsConnectionString(configuration));

    [Theory]
    [InlineData("{\"serverInstance\":\"remote.example\",\"database\":\"Target\"}")]
    [InlineData("{\"serverInstance\":\"localhost\",\"database\":\"\"}")]
    public void Unsafe_machine_target_is_rejected_before_database_access(string configuration) =>
        Assert.Throws<ArgumentException>(() => DesktopCompositionRoot.ParseOperationsConnectionString(configuration));

    [Fact]
    public void Missing_machine_configuration_never_reads_user_settings()
    {
        var missing = Path.Combine(Path.GetTempPath(), "EtpMissingOperations", Guid.NewGuid().ToString("N"), "operations.json");
        Assert.ThrowsAny<IOException>(() => DesktopCompositionRoot.LoadOperationsConnectionString(missing));
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void Default_connection_preserves_the_installed_sql_express_database()
    {
        Assert.Equal(
            @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;Encrypt=Optional;Connect Timeout=5",
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
