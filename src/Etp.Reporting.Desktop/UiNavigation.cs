extern alias EtpApplication;

using System.Text.Json;
using System.IO;
using Etp.Reporting.Reporting;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;

namespace Etp.Reporting.Desktop;

public enum UiDensity { Comfortable, Compact }

public sealed record UiPreferences(UiDensity Density, IReadOnlyList<string> PinnedModuleIds, IReadOnlyList<string> FavouriteReportCodes)
{
    public static UiPreferences Default { get; } = new(UiDensity.Comfortable, [], ["dsr", "stock-closing", "staff"]);
}

public static class UiPreferenceStore
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting");
    public static string FilePath => Path.Combine(DirectoryPath, "ui-preferences.json");

    public static UiPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return UiPreferences.Default;
            return JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(FilePath)) ?? UiPreferences.Default;
        }
        catch (Exception) when (File.Exists(FilePath)) { return UiPreferences.Default; }
    }

    public static void Save(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
