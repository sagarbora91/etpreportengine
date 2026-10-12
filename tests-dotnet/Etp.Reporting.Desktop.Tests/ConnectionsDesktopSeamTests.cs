using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Infrastructure.SqlServer.Connections;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 A1 Desktop seams: the endpoint-policy setting, the connection state's factory, the screen registry and the
/// shell banner slot. No database: every connection here is validated, never opened.
/// </summary>
public sealed class ConnectionsDesktopSeamTests : IDisposable
{
    private const string Local = @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;Encrypt=Optional";

    private readonly string root = Path.Combine(Path.GetTempPath(), "EtpA1Seams_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Saved_settings_carry_the_LocalOnly_endpoint_policy_and_still_load()
    {
        var store = new DesktopSettingsStore(root);
        Assert.Null(store.LoadEndpointPolicyName());

        store.Save(Local);

        using (var document = JsonDocument.Parse(File.ReadAllText(store.SettingsPath)))
            Assert.Equal("LocalOnly", document.RootElement.GetProperty("database").GetProperty("endpointPolicy").GetString());
        Assert.Equal("LocalOnly", store.LoadEndpointPolicyName());
        Assert.Contains("Initial Catalog=EtpReporting", Assert.IsType<DesktopConnectionSettings>(store.Load()).ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void A_saved_connection_keeps_the_endpoint_policy_it_found()
    {
        var store = new DesktopSettingsStore(root);
        Directory.CreateDirectory(root);
        File.WriteAllText(store.SettingsPath, "{\"ConnectionString\":\"" + Local.Replace(@"\", @"\\") + "\",\"database\":{\"endpointPolicy\":\"ApprovedServer\"}}");

        store.Save(Local.Replace("EtpReporting", "Other"));

        Assert.Equal("ApprovedServer", store.LoadEndpointPolicyName());
        Assert.Contains("Initial Catalog=Other", store.Load()!.ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_refuses_any_endpoint_policy_but_LocalOnly()
    {
        var store = new DesktopSettingsStore(root);
        Directory.CreateDirectory(root);
        File.WriteAllText(store.SettingsPath, "{\"database\":{\"endpointPolicy\":\"ApprovedServer\"}}");

        var composition = new DesktopCompositionRoot(AppContext.BaseDirectory, DesktopCompositionRoot.DefaultConnectionString, root);
        var refusal = Assert.Throws<InvalidOperationException>(() => composition.ApplyEndpointPolicy());
        Assert.Equal("This ETP build allows only this PC's SQL Server.", refusal.Message);
        Assert.Equal(LocalOnlySqlEndpointPolicy.PolicyName, EtpSqlEndpoints.Policy.Name);
    }

    [Fact]
    public void LocalOnly_or_no_setting_starts_and_can_be_applied_again()
    {
        var composition = new DesktopCompositionRoot(AppContext.BaseDirectory, DesktopCompositionRoot.DefaultConnectionString, root);
        composition.ApplyEndpointPolicy();
        new DesktopSettingsStore(root).Save(Local);
        composition.ApplyEndpointPolicy();
        Assert.Equal(LocalOnlySqlEndpointPolicy.PolicyName, EtpSqlEndpoints.Policy.Name);
    }

    [Fact]
    public void The_connection_state_hands_out_a_factory_for_its_current_connection()
    {
        var state = new DesktopConnectionState(Local);
        Assert.Equal("EtpReporting", state.Factory.DatabaseName);

        Assert.True(state.TryUpdate(Local.Replace("EtpReporting", "Other"), out _));
        Assert.Equal("Other", state.Factory.DatabaseName);

        Assert.False(state.TryUpdate("Server=remotehost;Database=Third;Integrated Security=True", out var error));
        Assert.Equal("Choose a SQL Server instance on this computer.", error);
        Assert.Equal("Other", state.Factory.DatabaseName);
    }

    [Fact]
    public void Registered_screens_open_by_task_id_once_each()
    {
        RunSta(() =>
        {
            var registry = new ScreenRegistry();
            ScreenRequest? seen = null;
            registry.Register("morning", request => { seen = request; return new UserControl(); });

            Assert.Throws<InvalidOperationException>(() => registry.Register("morning", _ => new UserControl()));
            Assert.Throws<ArgumentException>(() => registry.Register(" ", _ => new UserControl()));
            Assert.True(registry.Contains("morning"));
            Assert.False(registry.TryCreate(Request("collections-setup"), out var none));
            Assert.Null(none);

            Assert.True(registry.TryCreate(Request("morning"), out var view));
            Assert.IsType<UserControl>(view);
            Assert.Equal("morning", seen!.TaskId);
            Assert.Equal("WLMHW", seen.StoreCode);
            Assert.Equal(new DateOnly(2026, 10, 11), seen.BusinessDate);
            Assert.Equal(["morning"], registry.TaskIds);
        });
    }

    [Fact]
    public void The_banner_slot_shows_the_highest_priority_notice_and_collapses_when_empty()
    {
        RunSta(() =>
        {
            var host = new ContentControl();
            var slot = new ShellBannerSlot(host);
            Assert.Equal(Visibility.Collapsed, host.Visibility);

            var low = new Banner("shop-result", 1);
            var high = new Banner("update-ready", 5);
            slot.Show(low);
            slot.Show(high);
            Assert.Same(high, slot.Current);
            Assert.Same(high.Content, host.Content);
            Assert.Equal(Visibility.Visible, host.Visibility);

            high.Close();
            Assert.Same(low, slot.Current);
            slot.Show(new Banner("shop-result", 1));
            Assert.Single(slot.Pending);

            slot.Remove("shop-result");
            Assert.Null(slot.Current);
            Assert.Null(host.Content);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
        });
    }

    private static ScreenRequest Request(string taskId) =>
        new(taskId, null, new DateOnly(2026, 10, 11), "WLMHW", ShellAccess.Owner, _ => { });

    private sealed class Banner(string key, int priority) : IShellBanner
    {
        public string Key => key;
        public int Priority => priority;
        public UIElement Content { get; } = new TextBlock { Text = key };
        public event EventHandler? Closed;
        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA test failed", failure);
    }
}
