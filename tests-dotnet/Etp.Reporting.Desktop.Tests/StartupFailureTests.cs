using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Settings;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Tests;

public sealed class StartupFailureTests
{
    [Theory]
    [InlineData(18456, "login failed")]
    [InlineData(18452, "login failed")]
    [InlineData(229, "permission denied")]
    [InlineData(4060, "permission denied")]
    [InlineData(0, "unreachable")]
    [InlineData(258, "unreachable")]
    [InlineData(53, "unreachable")]
    [InlineData(-2, "unreachable")]
    [InlineData(10061, "unreachable")]
    public void Connection_failures_give_distinct_remedial_messages(int number, string expected)
    {
        Assert.Contains(expected, DesktopFriendlyError.DescribeConnectionFailure(number));
    }

    [Fact]
    public void Saved_settings_and_default_timeout_are_used_without_reading_source_text()
    {
        var folder = Path.Combine(Path.GetTempPath(), "EtpStartupSettings_" + Guid.NewGuid().ToString("N"));
        try
        {
            var root = new DesktopCompositionRoot(AppContext.BaseDirectory, DesktopCompositionRoot.DefaultConnectionString, folder);
            Assert.Equal(5, new SqlConnectionStringBuilder(root.LoadConnectionString()).ConnectTimeout);
            new DesktopSettingsStore(folder).Save(@"Server=.\SQLEXPRESS;Database=EtpOtherTest;Integrated Security=True");
            Assert.Equal("EtpOtherTest", new SqlConnectionStringBuilder(root.LoadConnectionString()).InitialCatalog);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
