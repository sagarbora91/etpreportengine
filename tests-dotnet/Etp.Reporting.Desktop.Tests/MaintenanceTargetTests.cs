using System.Diagnostics;
using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class MaintenanceTargetTests
{
    [Fact]
    public void Maintenance_uses_the_selected_database_instead_of_script_defaults()
    {
        var start = new ProcessStartInfo();
        PowerShellOperationsService.AddDatabaseArguments(start, "Data Source=localhost\\SQLEXPRESS;Initial Catalog=EtpUiDisposable_123;Integrated Security=true");
        Assert.Equal(new[] { "-ServerInstance", "localhost\\SQLEXPRESS", "-Database", "EtpUiDisposable_123" }, start.ArgumentList);
    }

    [Theory]
    [InlineData("Server=localhost;Database=Etp;User ID=test;Password=not-a-real-secret")]
    [InlineData("Server=localhost;Database=Etp-name;Integrated Security=true")]
    [InlineData("Server=localhost;Integrated Security=true")]
    public void Invalid_or_credential_based_targets_are_rejected_before_launch(string connection)
    {
        var start = new ProcessStartInfo();
        Assert.Throws<InvalidOperationException>(() => PowerShellOperationsService.AddDatabaseArguments(start, connection));
        Assert.Empty(start.ArgumentList);
    }
}
