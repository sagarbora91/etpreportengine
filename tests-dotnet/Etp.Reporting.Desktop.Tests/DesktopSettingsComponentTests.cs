using System.Text.Json;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopSettingsComponentTests : IDisposable
{
    private const string DefaultConnectionString =
        @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";

    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "EtpDesktopSettingsTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Store_requires_an_absolute_settings_directory()
    {
        Assert.Throws<ArgumentException>(() => new DesktopSettingsStore("relative-settings"));
    }

    [Fact]
    public void Missing_and_corrupt_settings_load_as_none()
    {
        var store = CreateStore();

        Assert.Null(store.Load());

        Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsPath)!);
        File.WriteAllText(store.SettingsPath, "{not-json");
        Assert.Null(store.Load());

        File.WriteAllText(
            store.SettingsPath,
            JsonSerializer.Serialize(new DesktopConnectionSettings("Server=.;Database=EtpReporting;User ID=sa;Password=secret")));
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_atomically_replaces_the_settings_file_and_loads_canonical_values()
    {
        var store = CreateStore();
        store.Save(DefaultConnectionString);

        var updated = @"Data Source=.\SQLEXPRESS;Initial Catalog=EtpReportingV2;Trusted_Connection=SSPI;Encrypt=True";
        store.Save(updated);

        var loaded = Assert.IsType<DesktopConnectionSettings>(store.Load());
        var validation = ConnectionStringValidation.Validate(updated);
        Assert.Equal(validation.ConnectionString, loaded.ConnectionString);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(store.SettingsPath)!, "*.tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a connection string")]
    [InlineData("Server=.;Database=EtpReporting")]
    [InlineData("Server=.;Database=EtpReporting;Integrated Security=False;User ID=sa;Password=secret")]
    [InlineData("Server=.;Database=EtpReporting;Integrated Security=True;UID=sa;PWD=secret")]
    [InlineData("Server=.;Database=EtpReporting;Integrated Security=True;User=sa")]
    [InlineData("Server=.;Database=EtpReporting;Integrated Security=True;User ID=")]
    [InlineData("Server=.;Database=EtpReporting;Integrated Security=True;Password=")]
    [InlineData("Database=EtpReporting;Integrated Security=True")]
    [InlineData("Server=.\\SQLEXPRESS;Integrated Security=True")]
    public void Unsafe_or_incomplete_connections_are_not_persisted(string connectionString)
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.Save(connectionString));
        Assert.False(File.Exists(store.SettingsPath));
    }

    [Fact]
    public void Connection_state_accepts_only_validated_updates()
    {
        var state = new DesktopConnectionState(DefaultConnectionString);
        var initial = state.ConnectionString;

        Assert.False(state.TryUpdate(
            "Server=.;Database=Other;User ID=sa;Password=secret",
            out var error));
        Assert.NotNull(error);
        Assert.Equal(initial, state.ConnectionString);

        Assert.True(state.TryUpdate(
            "Server=.;Database=Other;Integrated Security=SSPI;TrustServerCertificate=True",
            out error));
        Assert.Null(error);
        Assert.Contains("Initial Catalog=Other", state.ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_default_connection_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new DesktopConnectionState("Server=.;Database=EtpReporting;User ID=sa;Password=secret"));
    }

    [Fact]
    public void Reparse_point_directories_and_files_are_rejected_when_supported()
    {
        Directory.CreateDirectory(testRoot);
        var actualDirectory = Path.Combine(testRoot, "actual");
        var linkedDirectory = Path.Combine(testRoot, "linked");
        Directory.CreateDirectory(actualDirectory);

        if (!TryCreateDirectoryLink(linkedDirectory, actualDirectory)) return;

        Assert.Throws<InvalidOperationException>(() => new DesktopSettingsStore(linkedDirectory));

        var store = new DesktopSettingsStore(actualDirectory);
        var externalFile = Path.Combine(testRoot, "external.json");
        File.WriteAllText(externalFile, JsonSerializer.Serialize(new DesktopConnectionSettings(DefaultConnectionString)));
        if (!TryCreateFileLink(store.SettingsPath, externalFile)) return;

        Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Throws<InvalidOperationException>(() => store.Save(DefaultConnectionString));
    }

    public void Dispose()
    {
        if (!Directory.Exists(testRoot)) return;

        foreach (var file in Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(testRoot, recursive: true);
    }

    private DesktopSettingsStore CreateStore() =>
        new(Path.Combine(testRoot, "settings"));

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
