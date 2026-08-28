using System.IO;
using System.Text.Json;

namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed class DesktopSettingsStore
{
    private const string SettingsFileName = "settings.json";
    private readonly string settingsDirectory;

    public DesktopSettingsStore(string settingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(settingsDirectory))
            throw new ArgumentException("A settings directory is required.", nameof(settingsDirectory));
        if (!Path.IsPathFullyQualified(settingsDirectory))
            throw new ArgumentException("The settings directory must be an absolute path.", nameof(settingsDirectory));

        this.settingsDirectory = Path.GetFullPath(settingsDirectory);
        RejectReparsePoints(this.settingsDirectory);
    }

    public string SettingsPath => Path.Combine(settingsDirectory, SettingsFileName);

    public DesktopConnectionSettings? Load()
    {
        RejectReparsePoints(settingsDirectory);
        if (!Directory.Exists(settingsDirectory) || !File.Exists(SettingsPath)) return null;
        RejectReparsePoints(SettingsPath);

        try
        {
            DesktopConnectionSettings? settings;
            using (var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                RejectReparsePoints(SettingsPath);
                settings = JsonSerializer.Deserialize<DesktopConnectionSettings>(stream);
            }

            var validation = ConnectionStringValidation.Validate(settings?.ConnectionString);
            return validation.IsValid
                ? new DesktopConnectionSettings(validation.ConnectionString!)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string connectionString)
    {
        var validation = ConnectionStringValidation.Validate(connectionString);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error, nameof(connectionString));

        RejectReparsePoints(settingsDirectory);
        Directory.CreateDirectory(settingsDirectory);
        RejectReparsePoints(settingsDirectory);
        if (File.Exists(SettingsPath)) RejectReparsePoints(SettingsPath);

        var temporaryPath = Path.Combine(settingsDirectory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(
                    stream,
                    new DesktopConnectionSettings(validation.ConnectionString!));
                stream.Flush(flushToDisk: true);
            }

            RejectReparsePoints(settingsDirectory);
            RejectReparsePoints(temporaryPath);
            if (File.Exists(SettingsPath)) RejectReparsePoints(SettingsPath);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
            RejectReparsePoints(SettingsPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void RejectReparsePoints(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException("The settings path has no filesystem root.");
        var current = root;

        if (Directory.Exists(current) &&
            (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Desktop settings cannot use linked or reparse-point paths.");
        }

        foreach (var segment in Path.GetRelativePath(root, fullPath)
                     .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     .Where(segment => segment.Length > 0 && segment != "."))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current)) continue;

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Desktop settings cannot use linked or reparse-point paths.");
        }
    }
}
