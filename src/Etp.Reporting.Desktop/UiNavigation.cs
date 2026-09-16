extern alias EtpApplication;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using Etp.Reporting.Reporting;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;

namespace Etp.Reporting.Desktop;

[JsonConverter(typeof(UiDensityJsonConverter))]
public enum UiDensity { Touch, Desktop }

public sealed class UiDensityJsonConverter : JsonConverter<UiDensity>
{
    public override UiDensity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number when reader.TryGetInt32(out var value) && value is 0 or 1 => (UiDensity)value,
        JsonTokenType.String => reader.GetString() switch
        {
            "Touch" or "Comfortable" => UiDensity.Touch,
            "Desktop" or "Compact" => UiDensity.Desktop,
            _ => throw new JsonException("Unknown display density.")
        },
        _ => throw new JsonException("Unknown display density.")
    };

    public override void Write(Utf8JsonWriter writer, UiDensity value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == UiDensity.Desktop ? "Desktop" : "Touch");
}

public sealed record UiPreferences(UiDensity Density, IReadOnlyList<string> PinnedModuleIds, IReadOnlyList<string> FavouriteReportCodes)
{
    public static UiPreferences Default { get; } = new(UiDensity.Touch, [], ["dsr", "stock-closing", "staff"]);
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
