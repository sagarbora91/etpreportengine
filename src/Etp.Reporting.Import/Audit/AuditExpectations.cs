using System.Globalization;
using System.Text.Json;

namespace Etp.Reporting.Import.Audit;

/// <summary>An input file the audit cannot use: a state or expectation file of the wrong shape (exit 3).</summary>
public sealed class AuditInputException(string message) : Exception(message);

/// <summary>
/// An expectation file, schema <c>etp-import-audit-expect/1</c> (design 7.3). Each entry matches files by store, report
/// (report or family code) and SHA-256, each optional; a field left out is not checked. <c>forbid</c> makes any matching
/// file a finding. Holds SHA-256 values and counts only.
/// </summary>
public sealed record AuditExpectations(string Name, IReadOnlyList<AuditExpectation> Files)
{
    public const string SchemaName = "etp-import-audit-expect/1";

    /// <summary>Top-level <c>unexpectedBlockers</c>: the most blocked files the run may have that no entry allows.</summary>
    public int? UnexpectedBlockers { get; init; }
    /// <summary>Top-level <c>legacyHoldsAllowed</c>: failure codes a blocked file may have without counting as unexpected.</summary>
    public IReadOnlyList<string> LegacyHoldsAllowed { get; init; } = [];

    public static AuditExpectations Load(string path)
    {
        if (!File.Exists(path)) throw new AuditInputException("The expectation file does not exist.");
        try
        {
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            return Parse(json.RootElement);
        }
        catch (JsonException) { throw new AuditInputException("The expectation file is not valid JSON."); }
        catch (InvalidOperationException) { throw new AuditInputException("The expectation file does not have the expected shape."); }
        catch (FormatException) { throw new AuditInputException("The expectation file does not have the expected shape."); }
        catch (KeyNotFoundException) { throw new AuditInputException("The expectation file does not have the expected shape."); }
    }

    public static AuditExpectations Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) || schema.GetString() != SchemaName)
            throw new AuditInputException($"The expectation file's schema must be {SchemaName}.");
        var files = new List<AuditExpectation>();
        if (root.TryGetProperty("files", out var entries))
            foreach (var entry in entries.EnumerateArray())
            {
                var match = entry.TryGetProperty("match", out var m) ? m : default;
                var expectation = new AuditExpectation
                {
                    Store = Text(match, "store"), Report = Text(match, "report"), Sha256 = Text(match, "sha256")?.ToLowerInvariant(),
                    Forbid = Text(entry, "forbid")
                };
                if (entry.TryGetProperty("planner1", out var planner))
                    expectation = expectation with
                    {
                        Result = Text(planner, "result"), New = Number(planner, "new"), Present = Number(planner, "present"),
                        Conflict = Number(planner, "conflict"),
                        PromotesOver = planner.TryGetProperty("promotesOver", out var over) ? over.EnumerateArray().Select(id => id.GetInt64()).ToArray() : null
                    };
                if (entry.TryGetProperty("snapshots", out var snapshots))
                    expectation = expectation with
                    {
                        Snapshots = snapshots.EnumerateObject().ToDictionary(pair => DateOnly.ParseExact(pair.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                            pair => pair.Value.GetInt32())
                    };
                files.Add(expectation);
            }
        return new(Text(root, "name") ?? "expectations", files)
        {
            UnexpectedBlockers = Number(root, "unexpectedBlockers"),
            LegacyHoldsAllowed = root.TryGetProperty("legacyHoldsAllowed", out var allowed) ? allowed.EnumerateArray().Select(code => code.GetString()!).ToArray() : []
        };
    }

    /// <summary>
    /// Compares the report's files with the entries: each file gets its result (pass, or the mismatched fields by name),
    /// and an entry with checks that matched no file is a run finding.
    /// </summary>
    public (IReadOnlyList<AuditFileReport> Files, IReadOnlyList<string> RunMismatches) Apply(IReadOnlyList<AuditFileReport> files)
    {
        var used = new HashSet<AuditExpectation>();
        var result = new List<AuditFileReport>();
        foreach (var file in files)
        {
            var entries = Files.Where(entry => entry.Matches(file)).ToArray();
            if (entries.Length == 0) { result.Add(file); continue; }
            var mismatches = new List<string>();
            foreach (var entry in entries)
            {
                used.Add(entry);
                mismatches.AddRange(entry.Compare(file));
            }
            result.Add(file with { Expectation = new AuditExpectationResult(mismatches.Count == 0, mismatches) });
        }
        var missing = Files.Where(entry => entry.Forbid is null && !used.Contains(entry))
            .Select(entry => $"{Name}: no input file matches the entry for {entry.Describe()}.").ToArray();
        return (result, missing);
    }

    /// <summary>Whether a blocked file is one the expectations allow (its failure code is a listed legacy hold).</summary>
    public bool AllowsBlocker(AuditFileReport file) =>
        (file.Planner1?.Code ?? file.FailureCode) is { } code && LegacyHoldsAllowed.Contains(code, StringComparer.Ordinal);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}

/// <summary>One entry of an expectation file.</summary>
public sealed record AuditExpectation
{
    public string? Store { get; init; }
    public string? Report { get; init; }
    public string? Sha256 { get; init; }
    public string? Forbid { get; init; }
    public string? Result { get; init; }
    public int? New { get; init; }
    public int? Present { get; init; }
    public int? Conflict { get; init; }
    public IReadOnlyList<long>? PromotesOver { get; init; }
    public IReadOnlyDictionary<DateOnly, int>? Snapshots { get; init; }

    public bool Matches(AuditFileReport file) =>
        (Store is null || string.Equals(Store, file.Store, StringComparison.OrdinalIgnoreCase)) &&
        (Report is null || string.Equals(Report, file.ReportCode, StringComparison.OrdinalIgnoreCase) || string.Equals(Report, file.FamilyCode, StringComparison.OrdinalIgnoreCase)) &&
        (Sha256 is null || string.Equals(Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase));

    public string Describe() => string.Join(" ", new[] { Store, Report, Sha256 is null ? null : Sha256[..Math.Min(12, Sha256.Length)] }.Where(part => part is not null));

    /// <summary>Every checked field that differs, by name (design 12.2 ExpectationTests).</summary>
    public IEnumerable<string> Compare(AuditFileReport file)
    {
        if (Forbid is not null)
        {
            yield return $"forbid: {Forbid}";
            yield break;
        }
        var planner = file.Planner1;
        if (Result is not null && !string.Equals(Result, planner?.Result, StringComparison.OrdinalIgnoreCase))
            yield return $"planner1.result: expected {Result}, predicted {planner?.Result ?? "none"}";
        if (New is { } expectedNew && expectedNew != planner?.Rows.New)
            yield return $"planner1.new: expected {expectedNew}, predicted {planner?.Rows.New}";
        if (Present is { } expectedPresent && expectedPresent != planner?.Rows.Present)
            yield return $"planner1.present: expected {expectedPresent}, predicted {planner?.Rows.Present}";
        if (Conflict is { } expectedConflict && expectedConflict != planner?.Rows.Conflict)
            yield return $"planner1.conflict: expected {expectedConflict}, predicted {planner?.Rows.Conflict}";
        if (PromotesOver is { } over && !over.Order().SequenceEqual((planner?.PromotesOver ?? []).Order()))
            yield return $"planner1.promotesOver: expected {string.Join(",", over)}, predicted {string.Join(",", planner?.PromotesOver ?? [])}";
        if (Snapshots is { } snapshots)
            foreach (var (date, rows) in snapshots.OrderBy(pair => pair.Key))
            {
                var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var predicted = planner?.SnapshotRows?.GetValueOrDefault(key)
                    ?? file.SnapshotDates.Where(entry => entry.Date == date).Sum(entry => entry.Rows);
                if (predicted != rows) yield return $"snapshots.{key}: expected {rows}, predicted {predicted}";
            }
    }
}
