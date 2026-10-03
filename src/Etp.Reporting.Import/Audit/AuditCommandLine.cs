using System.Globalization;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Import.Audit;

/// <summary>The ImportAudit commands (design 3). Only <see cref="Seed"/> writes, and only to a scratch database.</summary>
public enum AuditCommandKind { Inspect, ValidateContract, CheckImport, Baseline, Seed }

/// <summary><c>--format</c>: the human report on stdout, the JSON report in the output folder, or both.</summary>
public enum AuditFormat { Text, Json, Both }

/// <summary><c>--detail</c>: how much business data a report may hold (design 8.3).</summary>
public enum AuditDetail { Summary, Documents, Rows }

/// <summary>Exit codes (design 9). 75 is never used: <c>Invoke-Serialized.ps1</c> means "lock busy" by it.</summary>
public static class AuditExitCodes
{
    public const int Clean = 0;
    public const int Findings = 1;
    public const int Usage = 2;
    public const int Input = 3;
    public const int Database = 4;
    public const int Internal = 5;
    public const int Cancelled = 130;
}

/// <summary>One parsed ImportAudit run.</summary>
public sealed record AuditCommand(AuditCommandKind Kind)
{
    public IReadOnlyList<string> Paths { get; init; } = [];
    public AuditFormat Format { get; init; } = AuditFormat.Both;
    public string? OutDirectory { get; init; }
    public AuditDetail Detail { get; init; } = AuditDetail.Summary;
    public IReadOnlyList<string> Families { get; init; } = [];
    public string? Store { get; init; }
    public bool Quiet { get; init; }
    public bool Strict { get; init; }
    public bool Debug { get; init; }
    /// <summary>Store codes the scope detection may match in a file's context, as the app's store catalogue does.</summary>
    public IReadOnlyList<string> KnownStores { get; init; } = [];
    public string? Database { get; init; }
    public string Server { get; init; } = AuditCommandLine.DefaultServer;
    /// <summary><c>validate-contract --raw</c>: the folder of raw exports to rebuild blocks against.</summary>
    public string? RawFolder { get; init; }
    public bool RequireContract { get; init; }
    /// <summary><c>check-import --planner</c>; 1.9.3 predicts planner 1 only.</summary>
    public int Planner { get; init; } = 1;
    public string? ExpectFile { get; init; }
    /// <summary><c>baseline --compare</c>: an earlier baseline JSON.</summary>
    public string? CompareFile { get; init; }
    public bool Rebuild { get; init; }
    public bool MigrateOnly { get; init; }
    /// <summary><c>seed --folder</c>, repeatable.</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];
}

/// <summary>The parse outcome: a command, or the usage exit code with the reason. <see cref="Notice"/> goes to stderr.</summary>
public sealed record AuditParseResult(AuditCommand? Command, int ExitCode, string? Error = null, string? Notice = null)
{
    public bool Succeeded => Command is not null;
}

/// <summary>
/// The ImportAudit command line (design 3). Running with no arguments prints usage and exits 2 (it used to bootstrap
/// <c>EtpReportingHelios</c>). The old form <c>--database X [--rebuild] --folder F</c> maps to <c>seed</c> with a notice;
/// <c>--check-import</c> and <c>--validate-contract</c> are kept as aliases. Pure: it reads no file.
/// </summary>
public static partial class AuditCommandLine
{
    public const string DefaultServer = @".\SQLEXPRESS";

    public const string DeprecatedNotice =
        "ImportAudit: the form '--database X [--rebuild] --folder F' is deprecated; use 'seed --database X [--rebuild] --folder F'.";

    public static string Usage { get; } = """
        Usage: Etp.Reporting.ImportAudit <command> <path...> [options]

        Read-only commands (they never write to any database):
          inspect <path...>                     What ETP sees in each workbook: family, store, blocks, dates, documents.
          validate-contract <path...> [--raw <folder>] [--require-contract]
                                                Contract v1 checks; --raw rebuilds each block against its raw export.
          check-import <path...> --database <name> [--planner 1] [--expect <file>]
                                                Predict what 1.9.3 (planner 1) does with each file, SELECT-only.
          baseline --database <name> [--compare <baseline.json>]
                                                Run scripts/check-import-upgrade.sql (SELECT-only) and save its results.

        Writing command (scratch databases only: EtpReportingHelios, EtpPhase1Test_*, EtpAccept_*):
          seed --database <scratch> [--rebuild] [--migrate-only] [--folder <path>]...

        Options:
          --format text|json|both   Report on stdout, report.json in --out, or both (default both).
          --out <dir>               Report folder (default %LOCALAPPDATA%\EtpReporting\ImportAudit\<time>-<command>).
          --detail summary|documents|rows
                                    Business data allowed in the report (default summary: counts, codes, hashes).
          --family <code>[,<code>]  Only these report or family codes.
          --store <code>            Only this store.
          --stores <code>[,<code>]  Store codes the scope detection may match (as the app's store catalogue).
          --server <instance>       Local SQL Server instance (default .\SQLEXPRESS).
          --strict                  Expected legacy holds and warnings count as findings.
          --quiet                   No progress lines on stderr.
          --debug                   Write debug.txt (may hold workbook text; never share it).

        Exit codes: 0 clean, 1 findings, 2 usage, 3 input, 4 database, 5 internal, 130 cancelled.
        Planner 2 forecasts, dump-state, --state and preview arrive with 1.10.0.
        """;

    private static readonly HashSet<string> CommonFlags = new(StringComparer.Ordinal) { "--quiet", "--strict", "--debug" };
    private static readonly HashSet<string> CommonValues = new(StringComparer.Ordinal)
        { "--format", "--out", "--detail", "--family", "--store", "--stores" };
    private static readonly HashSet<string> DatabaseValues = new(StringComparer.Ordinal) { "--database", "--server" };

    // The options a later release adds; named here so the refusal says when they arrive instead of "unknown option".
    private static readonly HashSet<string> LaterOptions = new(StringComparer.Ordinal) { "--state", "--role", "--provisional", "--state-out", "--for" };
    private static readonly HashSet<string> LaterCommands = new(StringComparer.Ordinal) { "dump-state", "preview" };

    public static AuditParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0) return Fail("No command given.");
        var first = args[0];
        string? notice = null;
        AuditCommandKind kind;
        var rest = args.Skip(1).ToList();
        switch (first)
        {
            case "inspect": kind = AuditCommandKind.Inspect; break;
            case "validate-contract": kind = AuditCommandKind.ValidateContract; break;
            case "check-import": kind = AuditCommandKind.CheckImport; break;
            case "baseline": kind = AuditCommandKind.Baseline; break;
            case "seed": kind = AuditCommandKind.Seed; break;
            case "--check-import": kind = AuditCommandKind.CheckImport; break;
            case "--validate-contract": kind = AuditCommandKind.ValidateContract; break;
            case "--database" or "--folder" or "--rebuild" or "--server":
                kind = AuditCommandKind.Seed;
                rest = args.ToList();
                notice = DeprecatedNotice;
                break;
            case "-h" or "--help" or "help" or "/?":
                return Fail(null);
            default:
                if (LaterCommands.Contains(first)) return Fail($"The '{first}' command arrives with ETP 1.10.0.");
                return Fail($"Unknown command '{first}'.");
        }

        var command = new AuditCommand(kind);
        var paths = new List<string>();
        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < rest.Count; index++)
        {
            var token = rest[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                if (kind is AuditCommandKind.Baseline or AuditCommandKind.Seed)
                    return Fail($"The {Name(kind)} command takes no paths; '{Shorten(token)}' was not expected.");
                paths.Add(token);
                continue;
            }
            if (LaterOptions.Contains(token)) return Fail($"The option '{token}' arrives with ETP 1.10.0.");
            if (!Allowed(kind, token)) return Fail($"Unknown option '{token}' for {Name(kind)}.");
            var takesValue = !CommonFlags.Contains(token) && token is not ("--require-contract" or "--rebuild" or "--migrate-only");
            string? value = null;
            if (takesValue)
            {
                if (index + 1 >= rest.Count || rest[index + 1].StartsWith("--", StringComparison.Ordinal))
                    return Fail($"The option '{token}' needs a value.");
                value = rest[++index];
            }
            // Repeating a single-valued option is ambiguous; only --folder repeats.
            if (token != "--folder" && !seen.Add(token)) return Fail($"The option '{token}' is given twice.");
            switch (token)
            {
                case "--quiet": command = command with { Quiet = true }; break;
                case "--strict": command = command with { Strict = true }; break;
                case "--debug": command = command with { Debug = true }; break;
                case "--require-contract": command = command with { RequireContract = true }; break;
                case "--rebuild": command = command with { Rebuild = true }; break;
                case "--migrate-only": command = command with { MigrateOnly = true }; break;
                case "--format":
                    if (!Enum.TryParse<AuditFormat>(value, true, out var format) || !Enum.IsDefined(format) || IsNumber(value!))
                        return Fail("--format takes text, json or both.");
                    command = command with { Format = format };
                    break;
                case "--detail":
                    if (!Enum.TryParse<AuditDetail>(value, true, out var detail) || !Enum.IsDefined(detail) || IsNumber(value!))
                        return Fail("--detail takes summary, documents or rows.");
                    command = command with { Detail = detail };
                    break;
                case "--out": command = command with { OutDirectory = value }; break;
                case "--family": command = command with { Families = SplitCodes(value!) }; break;
                case "--store": command = command with { Store = value!.Trim().ToUpperInvariant() }; break;
                case "--stores": command = command with { KnownStores = SplitCodes(value!) }; break;
                case "--database": command = command with { Database = value }; break;
                case "--server": command = command with { Server = value! }; break;
                case "--raw": command = command with { RawFolder = value }; break;
                case "--expect": command = command with { ExpectFile = value }; break;
                case "--compare": command = command with { CompareFile = value }; break;
                case "--folder": folders.Add(value!); break;
                case "--planner":
                    if (value == "1") break;
                    return value is "2" or "both"
                        ? Fail("The planner 2 forecast arrives with ETP 1.10.0; 1.9.3 predicts planner 1 only (--planner 1).")
                        : Fail("--planner takes 1.");
            }
        }
        command = command with { Paths = paths, Folders = folders };
        return Validate(command, notice);
    }

    /// <summary>
    /// A database <c>seed</c> may write: <c>EtpReportingHelios</c>, <c>EtpPhase1Test_*</c> or <c>EtpAccept_*</c>, the
    /// suffix letters, digits and underscores only. Live <c>EtpReporting</c> never qualifies.
    /// </summary>
    public static bool IsScratchDatabase(string? name) => name is not null && ScratchName().IsMatch(name);

    [GeneratedRegex(@"^(EtpReportingHelios|EtpPhase1Test_[A-Za-z0-9_]+|EtpAccept_[A-Za-z0-9_]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ScratchName();

    public static string Name(AuditCommandKind kind) => kind switch
    {
        AuditCommandKind.Inspect => "inspect",
        AuditCommandKind.ValidateContract => "validate-contract",
        AuditCommandKind.CheckImport => "check-import",
        AuditCommandKind.Baseline => "baseline",
        _ => "seed"
    };

    private static AuditParseResult Validate(AuditCommand command, string? notice)
    {
        switch (command.Kind)
        {
            case AuditCommandKind.Inspect or AuditCommandKind.ValidateContract:
                if (command.Paths.Count == 0) return Fail($"{Name(command.Kind)} needs at least one folder, .xlsx or .zip path.");
                break;
            case AuditCommandKind.CheckImport:
                if (command.Paths.Count == 0) return Fail("check-import needs at least one folder, .xlsx or .zip path.");
                if (string.IsNullOrWhiteSpace(command.Database))
                    return Fail("check-import needs --database <name>; there is no default database (a state dump arrives with 1.10.0).");
                break;
            case AuditCommandKind.Baseline:
                if (string.IsNullOrWhiteSpace(command.Database)) return Fail("baseline needs --database <name>; there is no default database.");
                break;
            case AuditCommandKind.Seed:
                if (string.IsNullOrWhiteSpace(command.Database))
                    return Fail("seed needs --database <scratch name>: EtpReportingHelios, EtpPhase1Test_* or EtpAccept_*.");
                if (!IsScratchDatabase(command.Database))
                    return Fail("seed writes only to a scratch database: EtpReportingHelios, EtpPhase1Test_* or EtpAccept_*.");
                if (command.MigrateOnly && command.Folders.Count > 0) return Fail("--migrate-only imports nothing; leave out --folder.");
                break;
        }
        if (command.Database is { } database && !SafeDatabaseName().IsMatch(database))
            return Fail("--database takes a plain database name (letters, digits and underscores).");
        return new(command, AuditExitCodes.Clean, Notice: notice);
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeDatabaseName();

    private static bool Allowed(AuditCommandKind kind, string option)
    {
        if (CommonFlags.Contains(option) || CommonValues.Contains(option))
            return kind != AuditCommandKind.Seed || option is "--quiet";
        return kind switch
        {
            AuditCommandKind.Inspect => false,
            AuditCommandKind.ValidateContract => option is "--raw" or "--require-contract",
            AuditCommandKind.CheckImport => DatabaseValues.Contains(option) || option is "--planner" or "--expect",
            AuditCommandKind.Baseline => DatabaseValues.Contains(option) || option is "--compare",
            _ => DatabaseValues.Contains(option) || option is "--rebuild" or "--migrate-only" or "--folder"
        };
    }

    private static IReadOnlyList<string> SplitCodes(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(code => code.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();

    private static bool IsNumber(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    // A refused argument may be a path; echo at most a short prefix of it.
    private static string Shorten(string token) => token.Length <= 40 ? token : token[..40] + "...";

    private static AuditParseResult Fail(string? error) => new(null, AuditExitCodes.Usage, error);
}
