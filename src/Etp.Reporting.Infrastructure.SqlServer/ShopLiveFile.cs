using Etp.Reporting.Application.DailyReadiness;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// 1.12.1 lane P7 (decision 51): reads <c>shop-live.json</c>, the one file that says which shops are live and from which
/// business day. The Workpc receive script reads the same file (<c>scripts/onedrive/OneDriveCommon.ps1</c>, same path,
/// same environment override), so what the receive log, the Today checklist, DATA-01 and Morning expect cannot drift apart.
/// <para>Default path: <c>%ProgramData%\EtpReporting\Operations\shop-live.json</c>; environment variable
/// <see cref="PathVariable"/> overrides it (tests, a repair). No file, an empty file or an invalid file gives
/// <see cref="ShopLiveSchedule.Empty"/>: every shop is expected, as before 1.12.1. Read-only; the file holds no customer data.</para>
/// </summary>
public static class ShopLiveFile
{
    public const string PathVariable = "ETP_SHOP_LIVE_FILE";
    public const string FileName = "shop-live.json";

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Operations", FileName);

    public static string ResolvePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(PathVariable);
        return string.IsNullOrWhiteSpace(overridePath) ? DefaultPath() : overridePath;
    }

    /// <summary>The schedule in the file at <paramref name="path"/> (default: <see cref="ResolvePath"/>); Empty when it is absent or unusable.</summary>
    public static ShopLiveSchedule Load(string? path = null)
    {
        try
        {
            var file = string.IsNullOrWhiteSpace(path) ? ResolvePath() : path;
            if (!File.Exists(file)) return ShopLiveSchedule.Empty;
            return ShopLiveSchedule.TryParse(File.ReadAllText(file), out var schedule, out _) ? schedule : ShopLiveSchedule.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ShopLiveSchedule.Empty;
        }
    }
}
