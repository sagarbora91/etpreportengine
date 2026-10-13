using System.Diagnostics;
using Etp.Reporting.Desktop;
using Etp.Reporting.Infrastructure.SqlServer.Connections;

namespace Etp.Reporting.Desktop.Tests;

public sealed class MaintenanceTargetTests
{
    [Fact]
    public void Maintenance_uses_the_selected_database_instead_of_script_defaults()
    {
        var start = new ProcessStartInfo();
        var target = MaintenanceTarget.For(new EtpConnectionFactory("Data Source=localhost\\SQLEXPRESS;Initial Catalog=EtpUiDisposable_123;Integrated Security=true"));
        PowerShellOperationsService.AddDatabaseArguments(start, target);
        Assert.Equal(new[] { "-ServerInstance", "localhost\\SQLEXPRESS", "-Database", "EtpUiDisposable_123" }, start.ArgumentList);
    }

    // 1.12 A1: a credential-based or database-less target never becomes a factory, so no maintenance target exists.
    [Theory]
    [InlineData("Server=localhost;Database=Etp;User ID=test;Password=not-a-real-secret")]
    [InlineData("Server=localhost;Integrated Security=true")]
    public void Invalid_or_credential_based_targets_are_rejected_before_launch(string connection) =>
        Assert.Throws<ArgumentException>(() => MaintenanceTarget.For(new EtpConnectionFactory(connection)));

    [Fact]
    public void A_database_name_the_scripts_cannot_take_is_rejected_before_launch()
    {
        var start = new ProcessStartInfo();
        var target = MaintenanceTarget.For(new EtpConnectionFactory("Server=localhost;Database=Etp-name;Integrated Security=true"));
        Assert.Throws<InvalidOperationException>(() => PowerShellOperationsService.AddDatabaseArguments(start, target));
        Assert.Empty(start.ArgumentList);
    }
}
