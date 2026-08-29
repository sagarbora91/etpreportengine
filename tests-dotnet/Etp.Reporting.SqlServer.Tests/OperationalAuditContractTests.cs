using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed partial class OperationalAuditContractTests
{
    private static readonly string[] AuditInvocationNames =
        ["RecordAuditAsync", "auditRecorder", "recordAuditAsync", "AuditRequestedAsync"];

    [Fact]
    public void Final_database_constraint_and_application_event_catalogue_match_exactly()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "0015_operational_audit_contract.sql"));
        var databaseEvents = ConstraintEventTypes(migration).Order(StringComparer.Ordinal).ToArray();
        var applicationEvents = OperationalAuditRepository.SupportedEventTypes.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(databaseEvents, applicationEvents);
    }

    [Fact]
    public void Active_literal_audit_emitters_use_supported_events_and_outcomes()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);
        var scriptFiles = Directory.EnumerateFiles(Path.Combine(root, "scripts"), "*.ps1", SearchOption.AllDirectories);
        var emissions = sourceFiles.Concat(scriptFiles).SelectMany(file => FindLiteralEmissions(root, file)).ToArray();

        Assert.Contains(emissions, emission => emission.Kind == AuditLiteralKind.Event && emission.Value == "ApplicationStart");
        Assert.Contains(emissions, emission => emission.Kind == AuditLiteralKind.Event && emission.Value == "DocumentExtractionReview");
        Assert.Contains(emissions, emission => emission.Kind == AuditLiteralKind.Event && emission.Value == "VisualRender");
        Assert.Contains(emissions, emission => emission.Kind == AuditLiteralKind.Event && emission.Value == "Backup");
        Assert.Contains(emissions, emission => emission.Kind == AuditLiteralKind.Outcome && emission.Value == "Succeeded");

        var unsupportedEvents = emissions
            .Where(emission => emission.Kind == AuditLiteralKind.Event && !OperationalAuditRepository.SupportedEventTypes.Contains(emission.Value))
            .Distinct()
            .OrderBy(emission => emission.File, StringComparer.Ordinal)
            .ThenBy(emission => emission.Value, StringComparer.Ordinal)
            .ToArray();
        var unsupportedOutcomes = emissions
            .Where(emission => emission.Kind == AuditLiteralKind.Outcome && !OperationalAuditRepository.SupportedOutcomes.Contains(emission.Value))
            .Distinct()
            .OrderBy(emission => emission.File, StringComparer.Ordinal)
            .ThenBy(emission => emission.Value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unsupportedEvents.Length == 0, Describe("event", unsupportedEvents));
        Assert.True(unsupportedOutcomes.Length == 0, Describe("outcome", unsupportedOutcomes));
    }

    private static IReadOnlySet<string> ConstraintEventTypes(string migration)
    {
        const string marker = "CK_operational_audit_type CHECK";
        var start = migration.LastIndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The final operational-audit constraint was not found.");
        var end = migration.IndexOf("));", start, StringComparison.Ordinal);
        Assert.True(end > start, "The final operational-audit constraint is incomplete.");
        return SingleQuotedSqlLiteral().Matches(migration[start..end])
            .Select(match => match.Groups["value"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<AuditLiteral> FindLiteralEmissions(string root, string file)
    {
        var source = File.ReadAllText(file);
        var relativeFile = Path.GetRelativePath(root, file).Replace('\\', '/');

        foreach (Match match in SqlAuditEvent().Matches(source))
            yield return new(relativeFile, AuditLiteralKind.Event, match.Groups["value"].Value);
        foreach (Match match in SqlAuditOutcome().Matches(source))
            yield return new(relativeFile, AuditLiteralKind.Outcome, match.Groups["value"].Value);

        if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) yield break;
        foreach (var invocationName in AuditInvocationNames)
        {
            for (var offset = 0; (offset = source.IndexOf(invocationName, offset, StringComparison.Ordinal)) >= 0; offset += invocationName.Length)
            {
                if (offset > 0 && (char.IsLetterOrDigit(source[offset - 1]) || source[offset - 1] == '_')) continue;
                var openParenthesis = offset + invocationName.Length;
                while (openParenthesis < source.Length && char.IsWhiteSpace(source[openParenthesis])) openParenthesis++;
                if (openParenthesis >= source.Length || source[openParenthesis] != '(') continue;
                var arguments = ReadArguments(source, openParenthesis);
                if (arguments.Count < 2) continue;
                foreach (var value in ResultStringLiterals(arguments[0]))
                    yield return new(relativeFile, AuditLiteralKind.Event, value);
                foreach (var value in ResultStringLiterals(arguments[1]))
                    yield return new(relativeFile, AuditLiteralKind.Outcome, value);
            }
        }
    }

    private static IReadOnlyList<string> ReadArguments(string source, int openParenthesis)
    {
        var arguments = new List<string>();
        var argumentStart = openParenthesis + 1;
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        var inString = false;
        var inCharacter = false;
        var escaped = false;
        for (var index = argumentStart; index < source.Length; index++)
        {
            var current = source[index];
            if (inString || inCharacter)
            {
                if (escaped) { escaped = false; continue; }
                if (current == '\\') { escaped = true; continue; }
                if (inString && current == '"') inString = false;
                if (inCharacter && current == '\'') inCharacter = false;
                continue;
            }
            if (current == '"') { inString = true; continue; }
            if (current == '\'') { inCharacter = true; continue; }
            switch (current)
            {
                case '(': parentheses++; break;
                case ')' when parentheses == 0 && brackets == 0 && braces == 0:
                    arguments.Add(source[argumentStart..index]);
                    return arguments;
                case ')': parentheses--; break;
                case '[': brackets++; break;
                case ']': brackets--; break;
                case '{': braces++; break;
                case '}': braces--; break;
                case ',' when parentheses == 0 && brackets == 0 && braces == 0:
                    arguments.Add(source[argumentStart..index]);
                    argumentStart = index + 1;
                    break;
            }
        }
        return [];
    }

    private static IEnumerable<string> ResultStringLiterals(string expression)
    {
        var trimmed = expression.Trim();
        var direct = CSharpStringLiteral().Match(trimmed);
        if (direct.Success && direct.Index == 0 && direct.Length == trimmed.Length)
        {
            yield return Regex.Unescape(direct.Groups["value"].Value);
            yield break;
        }

        var conditional = trimmed.IndexOf('?');
        if (conditional >= 0)
            foreach (Match match in CSharpStringLiteral().Matches(trimmed[(conditional + 1)..]))
                yield return Regex.Unescape(match.Groups["value"].Value);

        foreach (Match arm in SwitchArmStringLiteral().Matches(trimmed))
            yield return Regex.Unescape(arm.Groups["value"].Value);
    }

    private static string Describe(string kind, IReadOnlyList<AuditLiteral> unsupported) =>
        unsupported.Count == 0
            ? string.Empty
            : $"Unsupported operational-audit {kind} literal(s): {string.Join(", ", unsupported.Select(value => $"{value.File} -> {value.Value}"))}";

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private enum AuditLiteralKind { Event, Outcome }
    private sealed record AuditLiteral(string File, AuditLiteralKind Kind, string Value);

    [GeneratedRegex("'(?<value>[^']+)'")]
    private static partial Regex SingleQuotedSqlLiteral();

    [GeneratedRegex(@"INSERT\s+dbo\.operational_audit\s*\(\s*event_type\s*,\s*outcome[^)]*\)\s*VALUES\s*\(\s*N?'(?<value>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex SqlAuditEvent();

    [GeneratedRegex(@"INSERT\s+dbo\.operational_audit\s*\(\s*event_type\s*,\s*outcome[^)]*\)\s*VALUES\s*\(\s*N?'[^']+'\s*,\s*N?'(?<value>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex SqlAuditOutcome();

    [GeneratedRegex("\"(?<value>(?:\\\\.|[^\"\\\\])*)\"")]
    private static partial Regex CSharpStringLiteral();

    [GeneratedRegex("=>\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"")]
    private static partial Regex SwitchArmStringLiteral();
}
