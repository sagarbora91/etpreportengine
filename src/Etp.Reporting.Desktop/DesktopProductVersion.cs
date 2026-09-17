using System.Reflection;

namespace Etp.Reporting.Desktop;

/// <summary>
/// The product name and build shown in the window title. Staff and the owner
/// need to see at a glance which build a machine is running. The release number
/// alone does not change between candidate builds, so the short commit is shown
/// beside it.
/// </summary>
public static class DesktopProductVersion
{
    public const string ProductName = "ETP Reporting Engine";

    public static string Display { get; } = Describe(
        typeof(DesktopProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public static string WindowTitle { get; } = $"{ProductName} {Display}";

    internal static string Describe(string? informationalVersion)
    {
        // A missing version must never blank the title or throw during startup.
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "(version unavailable)";
        var value = informationalVersion.Trim();
        var separator = value.IndexOf('+');
        if (separator < 0) return value;
        var release = value[..separator];
        var build = value[(separator + 1)..];
        if (release.Length == 0) return "(version unavailable)";
        if (build.Length == 0) return release;
        return $"{release} ({(build.Length > 7 ? build[..7] : build)})";
    }
}
