using System.Globalization;

namespace Etp.Reporting.Application.Updates;

/// <summary>
/// 1.12 (B1). A release number as ETP publishes it: exactly three non-negative whole numbers, "1.12.0". Nothing else is a
/// version here: no "v" prefix, no pre-release label, no build suffix. A folder or manifest that carries anything else is
/// refused rather than guessed at, so "1.12.0-rc" or "1.12" can never be ordered above a real release.
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text) || text.Length > 32) return false;
        var parts = text.Split('.');
        if (parts.Length != 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            var part = parts[i];
            // Digits only (no sign, no spaces), and no leading zero except "0" itself: "01" would sort and display differently.
            if (part.Length == 0 || part.Length > 9 || (part.Length > 1 && part[0] == '0')) return false;
            foreach (var c in part) if (c is < '0' or > '9') return false;
            numbers[i] = int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public static ReleaseVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException("A release number has three whole numbers, for example 1.12.0.");

    /// <summary>
    /// The running build's number from its informational version ("1.12.0+abcdef0" or "1.12.0"). A four-part assembly
    /// version ("1.12.0.0") is accepted when the fourth part is 0. Anything else gives false: the caller then offers no update.
    /// </summary>
    public static bool TryParseRunning(string? informationalVersion, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(informationalVersion)) return false;
        var text = informationalVersion.Trim();
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        if (text.Count(c => c == '.') == 3 && text.EndsWith(".0", StringComparison.Ordinal)) text = text[..^2];
        return TryParse(text, out version);
    }

    public int CompareTo(ReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}
