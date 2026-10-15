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

    /// <summary>
    /// 1.12 U1 (wave plan C-e): the screens this Windows user pinned with the star on the page title, by task id only
    /// (never a route argument: no dates, stores or Refs on disk), at most <see cref="UiPreferenceStore.MaximumTaskIds"/>.
    /// A file written before 1.12 has none.
    /// </summary>
    public IReadOnlyList<string> PinnedTaskIds { get; init; } = [];

    /// <summary>The screens opened most recently, newest first, by task id only, at most <see cref="UiPreferenceStore.MaximumTaskIds"/>.</summary>
    public IReadOnlyList<string> RecentTaskIds { get; init; } = [];

    /// <summary>What Ctrl+K lists first: the pinned screens, then the report favourites (they show as pins).</summary>
    public IReadOnlyList<string> PinnedForSearch() =>
        PinnedTaskIds.Concat(FavouriteReportCodes.Select(code => "report-" + code)).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>True when the task shows a filled star: pinned, or a report favourite.</summary>
    public bool IsPinned(TaskDestination task) =>
        task.ReportCode is { } code ? FavouriteReportCodes.Contains(code, StringComparer.Ordinal) : PinnedTaskIds.Contains(task.Id, StringComparer.Ordinal);

    /// <summary>
    /// The star: a report toggles its favourite (report favourites are pins), any other screen its pin. A full pin list
    /// (eight) refuses a ninth: the caller says so instead of dropping an old pin silently.
    /// </summary>
    public UiPreferences TogglePin(TaskDestination task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.ReportCode is { } code)
            return this with { FavouriteReportCodes = FavouriteReportCodes.Contains(code, StringComparer.Ordinal)
                ? FavouriteReportCodes.Where(x => x != code).ToArray() : [.. FavouriteReportCodes, code] };
        if (PinnedTaskIds.Contains(task.Id, StringComparer.Ordinal)) return this with { PinnedTaskIds = PinnedTaskIds.Where(x => x != task.Id).ToArray() };
        if (!UiPreferenceStore.IsStorableTaskId(task.Id) || PinnedTaskIds.Count >= UiPreferenceStore.MaximumTaskIds) return this;
        return this with { PinnedTaskIds = [.. PinnedTaskIds, task.Id] };
    }

    /// <summary>The task moves to the front of the recent list (task id only, at most eight).</summary>
    public UiPreferences WithRecent(string taskId) =>
        !UiPreferenceStore.IsStorableTaskId(taskId) ? this
            : this with { RecentTaskIds = new[] { taskId }.Concat(RecentTaskIds.Where(x => x != taskId)).Take(UiPreferenceStore.MaximumTaskIds).ToArray() };
}

public static class UiPreferenceStore
{
    // Test runs set this so neither they nor the windows they drive write the Owner's ui-preferences.json: since
    // 1.12 U1 every navigation saves the recent list. The shipped application never sets it.
    internal const string DirectoryVariable = "ETP_UI_PREFERENCES_DIRECTORY";

    /// <summary>The Owner's real file under %LOCALAPPDATA%, whatever the test override says.</summary>
    internal static string RealFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "ui-preferences.json");

    private static string DirectoryPath => Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } overridden
        ? overridden : Path.GetDirectoryName(RealFilePath)!;
    public static string FilePath => Path.Combine(DirectoryPath, "ui-preferences.json");

    public static UiPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return UiPreferences.Default;
            return Normalize(JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(FilePath)) ?? UiPreferences.Default);
        }
        catch (Exception) when (File.Exists(FilePath)) { return UiPreferences.Default; }
    }

    /// <summary>Pins and recents keep at most this many task ids each (1.12 U1).</summary>
    public const int MaximumTaskIds = 8;

    /// <summary>
    /// A task id as stored: lower-case words joined by single hyphens, at most 64 characters. Anything else (a Help
    /// topic "help:...", a route argument, a path, a date with slashes) is never written to the preferences file.
    /// </summary>
    public static bool IsStorableTaskId(string? id) =>
        id is { Length: > 0 and <= 64 } && System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]+(-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static UiPreferences Normalize(UiPreferences preferences) => preferences with
    {
        PinnedModuleIds = preferences.PinnedModuleIds ?? [],
        FavouriteReportCodes = (preferences.FavouriteReportCodes ?? []).Select(code => code is "sales-titan" or "sales-helios" ? "sales-store" : code)
            .Where(code => ProductReportCatalogue.All.Any(report => report.Code == code)).Distinct().ToArray(),
        // 1.12 U1: an old file has neither list (null after reading); a hand-edited one may hold anything.
        PinnedTaskIds = TaskIds(preferences.PinnedTaskIds),
        RecentTaskIds = TaskIds(preferences.RecentTaskIds)
    };

    private static string[] TaskIds(IReadOnlyList<string>? ids) =>
        (ids ?? []).Where(IsStorableTaskId).Distinct(StringComparer.Ordinal).Take(MaximumTaskIds).ToArray();

    public static void Save(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
