using System.Globalization;
using Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>One SQL Server major version ETP supports, with the build ETP recommends.</summary>
public sealed record KnownSqlBuild(int Major, string Product, Version RtmBuild, Version RecommendedBuild, string RecommendedLabel);

/// <summary>What OPS-06 concludes from <c>SERVERPROPERTY('ProductVersion')</c> and <c>('ProductUpdateLevel')</c>.</summary>
public sealed record SqlBuildAssessment(HealthOutcome Outcome, string FindingCode, string Explanation, Version? Build, KnownSqlBuild? Known);

/// <summary>
/// OPS-06's table (design 5.2; decision 36, Q15): ETP supports SQL Server 2022 (16) and 2025 (17). A build at or above
/// the recommended one is Pass; RTM or a build below the recommended one is Warning (ETP works; install the latest
/// cumulative update with SQL-CU-AND-AUTOCLOSE-RUNBOOK.md); an older major version is Fail; a newer one ETP has not been
/// tested on is Warning. Update <see cref="All"/> when a new CU is adopted (CU9 for 2025 is 17.0.5005.3, 11 Oct 2026).
/// </summary>
public static class KnownSqlBuilds
{
    public const int OldestSupportedMajor = 16;

    public static IReadOnlyList<KnownSqlBuild> All { get; } =
    [
        // 2022: any cumulative update (CU1 = 16.0.4003.1); RTM 16.0.1000.6. Raise when a specific CU is adopted.
        new(16, "SQL Server 2022", new Version(16, 0, 1000, 6), new Version(16, 0, 4003, 1), "a cumulative update"),
        // 2025: RTM 17.0.1000.7 crashed once on Workpc (4 Oct 2026, IE-RT-10); CU9 is the current recommendation.
        new(17, "SQL Server 2025", new Version(17, 0, 1000, 7), new Version(17, 0, 5005, 3), "CU9 (17.0.5005.3)")
    ];

    public static KnownSqlBuild? For(int major) => All.FirstOrDefault(build => build.Major == major);

    public static SqlBuildAssessment Assess(string? productVersion, string? productUpdateLevel)
    {
        if (!TryParse(productVersion, out var build))
            return new(HealthOutcome.Warning, "SQL_BUILD_UNKNOWN", "The SQL Server version could not be read.", null, null);

        var version = HealthCheckTexts.Token(build.ToString());
        var known = For(build.Major);
        if (known is null)
        {
            return build.Major < OldestSupportedMajor
                ? new(HealthOutcome.Fail, "SQL_UNSUPPORTED",
                    $"SQL Server {version} is older than ETP supports (SQL Server 2022 or newer). Upgrade SQL Server.", build, null)
                : new(HealthOutcome.Warning, "SQL_UNTESTED",
                    $"SQL Server {version} is newer than the versions ETP was tested on. Report it with the support package.", build, null);
        }

        var level = HealthCheckTexts.Token(productUpdateLevel);
        var rtm = string.IsNullOrWhiteSpace(productUpdateLevel)
            || string.Equals(productUpdateLevel.Trim(), "RTM", StringComparison.OrdinalIgnoreCase)
            || build < known.RecommendedBuild && build.Build < 2000;
        if (rtm)
            return new(HealthOutcome.Warning, "SQL_RTM",
                $"{known.Product} is on its first release build ({version}, no cumulative update). Install {known.RecommendedLabel} with the SQL CU runbook.", build, known);
        if (build < known.RecommendedBuild)
            return new(HealthOutcome.Warning, "SQL_BELOW_RECOMMENDED",
                $"{known.Product} {version} ({(level.Length == 0 ? "unknown update" : level)}) is below the recommended build. Install {known.RecommendedLabel} with the SQL CU runbook.", build, known);
        return new(HealthOutcome.Pass, HealthCheckTexts.Ok,
            $"{known.Product} {version}{(level.Length == 0 ? "" : " (" + level + ")")} is at or above the recommended build.", build, known);
    }

    public static bool TryParse(string? productVersion, out Version build)
    {
        build = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(productVersion)) return false;
        var parts = productVersion.Trim().Split('.');
        if (parts.Length is < 2 or > 4) return false;
        var numbers = new int[4];
        for (var index = 0; index < parts.Length; index++)
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index])) return false;
        build = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }
}
