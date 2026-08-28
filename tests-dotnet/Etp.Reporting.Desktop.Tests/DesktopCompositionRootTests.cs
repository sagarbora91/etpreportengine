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
}
