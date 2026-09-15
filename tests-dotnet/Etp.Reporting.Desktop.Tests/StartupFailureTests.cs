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

    [Theory]
    [InlineData("", 5)]
    [InlineData(";Connect Timeout=30", 5)]
    [InlineData(";Connect Timeout=0", 5)]
    [InlineData(";Connection Timeout=2", 2)]
    public void Saved_connections_apply_the_timeout_to_headless_and_interactive_sessions(string timeout, int expected)
    {
        var folder = Path.Combine(Path.GetTempPath(), "EtpStartupSettings_" + Guid.NewGuid().ToString("N"));
        try
        {
            var saved = @"Server=.\SQLEXPRESS;Database=EtpOtherTest;Integrated Security=True" + timeout;
            // Simulate an older installation whose settings predate timeout normalization.
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new { ConnectionString = saved }));
            var root = new DesktopCompositionRoot(AppContext.BaseDirectory, DesktopCompositionRoot.DefaultConnectionString, folder);
            var headless = new SqlConnectionStringBuilder(root.LoadConnectionString());
            Assert.Equal(expected, headless.ConnectTimeout);
            Assert.Equal("EtpOtherTest", headless.InitialCatalog);
            var interactive = new DesktopConnectionState(DesktopCompositionRoot.DefaultConnectionString);
            Assert.True(interactive.TryUpdate(new DesktopSettingsStore(folder).Load()!.ConnectionString, out _));
            Assert.Equal(expected, new SqlConnectionStringBuilder(interactive.ConnectionString).ConnectTimeout);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
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
