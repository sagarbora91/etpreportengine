using System.Globalization;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Import.Audit;

namespace Etp.Reporting.Infrastructure.SqlServer.Audit;

/// <summary>
/// <c>baseline</c> (design 3.5): <c>scripts/check-import-upgrade.sql</c>, embedded byte for byte, run SELECT-only. Its
/// result sets are the environment, the check summary, the fact counts and, only at <c>--detail rows</c>, the samples
/// behind each check (they hold document numbers). <c>--compare</c> diffs the fact counts and the blocking checks with
/// an earlier baseline report.
/// </summary>
public static class BaselineQuery
{
    public const string ResourceName = "Etp.Reporting.Infrastructure.SqlServer.Audit.check-import-upgrade.sql";

    /// <summary>The script's bytes, exactly as the repository holds them.</summary>
    public static byte[] ScriptBytes()
    {
        using var stream = typeof(BaselineQuery).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded check-import-upgrade.sql is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The script as text (a UTF-8 byte-order mark, if any, removed).</summary>
    public static string Script()
    {
        var bytes = ScriptBytes();
        return new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');
    }

    /// <summary>Runs the script; null result sets mean the database refused (it predates migration 0018).</summary>
    public static async Task<IReadOnlyList<AuditResultSet>?> RunAsync(ReadOnlyAuditConnection connection, bool withSamples, CancellationToken cancellationToken)
    {
        var sets = await connection.QuerySetsAsync(Script(), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (sets.Count == 1 && sets[0].Columns.Count == 1 && sets[0].Columns[0] == "refused") return null;
        var result = new List<AuditResultSet>();
        for (var index = 0; index < sets.Count; index++)
        {
            var (columns, rows) = sets[index];
            var name = index switch
            {
                0 => "environment",
                1 => "summary",
                2 => "fact counts",
                _ => "sample " + (rows.Count > 0 ? Convert.ToString(rows[0][0], CultureInfo.InvariantCulture) : "")
            };
            if (index > 2 && !withSamples) continue;
            result.Add(new AuditResultSet(name, columns, rows.Select(row => (IReadOnlyList<string?>)row.Select(Text).ToArray()).ToArray()));
        }
        return result;
    }

    /// <summary>The blocking checks that found something (summary rows with <c>blocks_upgrade</c> = 1 and findings).</summary>
    public static IReadOnlyList<string> Blocking(IReadOnlyList<AuditResultSet> sets)
    {
        var summary = sets.FirstOrDefault(set => set.Name == "summary");
        if (summary is null) return [];
        int Column(string name) => summary.Columns.ToList().IndexOf(name);
        var (code, findings, blocks) = (Column("check_code"), Column("findings"), Column("blocks_upgrade"));
        if (code < 0 || findings < 0 || blocks < 0) return [];
        return summary.Rows.Where(row => row[blocks] is "1" or "True" && long.TryParse(row[findings], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            .Select(row => $"{row[code]} ({row[findings]})").ToArray();
    }

    /// <summary>Differences of fact counts and blocking checks against an earlier baseline report (<c>report.json</c>).</summary>
    public static IReadOnlyList<string> Compare(IReadOnlyList<AuditResultSet> current, string earlierReportPath)
    {
        var earlier = LoadResultSets(earlierReportPath);
        var differences = new List<string>();
        var before = Counts(earlier);
        var after = Counts(current);
        foreach (var table in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            var was = before.GetValueOrDefault(table);
            var now = after.GetValueOrDefault(table);
            if (was != now) differences.Add($"{table}: {Show(was)} -> {Show(now)}");
        }
        var blockingBefore = Blocking(earlier).ToHashSet(StringComparer.Ordinal);
        foreach (var check in Blocking(current).Where(check => !blockingBefore.Contains(check))) differences.Add($"blocking check now: {check}");
        foreach (var check in blockingBefore.Where(check => !Blocking(current).Contains(check))) differences.Add($"blocking check no longer: {check}");
        return differences;
    }

    private static string Show(string? value) => value ?? "absent";

    /// <summary>The result sets of an earlier baseline <c>report.json</c>; anything else is an input error (exit 3).</summary>
    public static IReadOnlyList<AuditResultSet> LoadResultSets(string path)
    {
        if (!File.Exists(path)) throw new AuditInputException("The --compare file does not exist.");
        try
        {
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != AuditReport.SchemaName)
                throw new AuditInputException($"The --compare file's schema must be {AuditReport.SchemaName}.");
            if (!root.TryGetProperty("baseline", out var baseline) || !baseline.TryGetProperty("resultSets", out var sets))
                throw new AuditInputException("The --compare file is not a baseline report.");
            return sets.EnumerateArray().Select(set => new AuditResultSet(set.GetProperty("name").GetString()!,
                set.GetProperty("columns").EnumerateArray().Select(column => column.GetString()!).ToArray(),
                set.GetProperty("rows").EnumerateArray().Select(row => (IReadOnlyList<string?>)row.EnumerateArray()
                    .Select(cell => cell.ValueKind == JsonValueKind.Null ? null : cell.GetString()).ToArray()).ToArray())).ToArray();
        }
        catch (JsonException) { throw new AuditInputException("The --compare file is not valid JSON."); }
        catch (InvalidOperationException) { throw new AuditInputException("The --compare file is not a baseline report."); }
        catch (KeyNotFoundException) { throw new AuditInputException("The --compare file is not a baseline report."); }
    }

    private static Dictionary<string, string?> Counts(IReadOnlyList<AuditResultSet> sets)
    {
        var counts = sets.FirstOrDefault(set => set.Name == "fact counts");
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (counts is null || counts.Columns.Count < 2) return result;
        foreach (var row in counts.Rows) if (row[0] is { } table) result[table] = row[1];
        return result;
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "1" : "0",
        DateTime time => time.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexStringLower(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
