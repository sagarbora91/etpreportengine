using System.Diagnostics;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Audit;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Workbooks;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Audit;

/// <summary>
/// Runs one ImportAudit command line (design 3) and returns its exit code (design 9). <c>Program.cs</c> only hands it the
/// arguments, the console writers and a cancellation token, so every command can be tested without a process.
/// Nothing here writes to the app's diagnostics log (design 8.4): stdout, stderr and the <c>--out</c> folder only.
/// </summary>
public sealed class AuditRunner(TextWriter output, TextWriter error)
{
    /// <summary>The folder of migration scripts <c>seed</c> applies; the tool copies them beside itself.</summary>
    public string MigrationsDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "database", "migrations");

    /// <summary>The clock for report times and the default folder name.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public IWorkbookReader WorkbookReader { get; init; } = new OpenXmlWorkbookReader();

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var parsed = AuditCommandLine.Parse(args);
        if (parsed.Notice is { } notice) await error.WriteLineAsync(notice).ConfigureAwait(false);
        if (parsed.Command is not { } command)
        {
            if (parsed.Error is { } message) await error.WriteLineAsync("ImportAudit: " + message).ConfigureAwait(false);
            await error.WriteLineAsync(AuditCommandLine.Usage).ConfigureAwait(false);
            return parsed.ExitCode;
        }
        string? folder = null;
        if (command.Kind != AuditCommandKind.Seed && command.Format != AuditFormat.Text)
        {
            var inputs = command.Paths.Concat(command.RawFolder is null ? [] : [command.RawFolder]).ToArray();
            var (resolved, refusal) = AuditOutputLocation.Resolve(command.OutDirectory, command.Kind, inputs, Clock());
            if (refusal is not null)
            {
                await error.WriteLineAsync("ImportAudit: " + refusal).ConfigureAwait(false);
                return AuditExitCodes.Usage;
            }
            folder = resolved;
        }
        try
        {
            return command.Kind switch
            {
                AuditCommandKind.Seed => await SeedCommand.RunAsync(command, MigrationsDirectory, output, error, cancellationToken).ConfigureAwait(false),
                AuditCommandKind.Inspect or AuditCommandKind.ValidateContract =>
                    await InspectAsync(command, args, folder, cancellationToken).ConfigureAwait(false),
                AuditCommandKind.CheckImport => await CheckImportAsync(command, args, folder, cancellationToken).ConfigureAwait(false),
                _ => await BaselineAsync(command, args, folder, cancellationToken).ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("ImportAudit: cancelled. Temporary folders were removed.").ConfigureAwait(false);
            return AuditExitCodes.Cancelled;
        }
        catch (AuditDatabaseException refused)
        {
            await error.WriteLineAsync("ImportAudit: " + refused.Message).ConfigureAwait(false);
            return AuditExitCodes.Database;
        }
        catch (SqlException database)
        {
            // The number only: a SQL message can quote a value.
            await error.WriteLineAsync(database.Number == LockTimeoutNumber
                ? "ImportAudit: a read waited more than 5 seconds for a lock (an import is running). Rerun while ETP is idle."
                : $"ImportAudit: database error {database.Number}.").ConfigureAwait(false);
            await WriteDebugAsync(command, folder, database).ConfigureAwait(false);
            return AuditExitCodes.Database;
        }
        catch (AuditInputException refused)
        {
            await error.WriteLineAsync("ImportAudit: input refused: " + refused.Message).ConfigureAwait(false);
            return AuditExitCodes.Input;
        }
        catch (ImportSourceException refused)
        {
            // A missing or refused path, a refused ZIP, an unreadable expectation file: the code and the catalogue's text.
            await error.WriteLineAsync($"ImportAudit: input refused: {refused.Code}: {ImportDiagnosticCatalogue.SafeFailureMessage(refused.Code, refused.Message, null)}").ConfigureAwait(false);
            return AuditExitCodes.Input;
        }
        catch (Exception unexpected) when (unexpected is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or InvalidDataException)
        {
            await error.WriteLineAsync($"ImportAudit: input error ({unexpected.GetType().Name}).").ConfigureAwait(false);
            await WriteDebugAsync(command, folder, unexpected).ConfigureAwait(false);
            return AuditExitCodes.Input;
        }
        catch (Exception unexpected)
        {
            // Never the exception's message: OpenXml, the stager and SQL can quote cell text (design 8.1).
            await error.WriteLineAsync($"ImportAudit: internal error ({unexpected.GetType().Name}). {ImportDiagnosticCatalogue.GenericMessage}").ConfigureAwait(false);
            await WriteDebugAsync(command, folder, unexpected).ConfigureAwait(false);
            return AuditExitCodes.Internal;
        }
    }

    private async Task<int> InspectAsync(AuditCommand command, IReadOnlyList<string> args, string? folder, CancellationToken cancellationToken)
    {
        var started = Clock();
        var clock = Stopwatch.StartNew();
        var inspector = new SourceInspector(WorkbookReader, command.KnownStores) { Families = command.Families, Store = command.Store };
        var run = await inspector.InspectAsync(command.Paths, Progress(command), cancellationToken).ConfigureAwait(false);
        var raw = command.RawFolder is { } rawFolder
            ? await RawExportIndex.BuildAsync(rawFolder, cancellationToken: cancellationToken).ConfigureAwait(false)
            : RawExportIndex.Empty;
        var files = new List<AuditFileReport>();
        foreach (var inspected in run.Files)
        {
            var file = AuditReportBuilder.File(inspected, command.Detail);
            var blocked = inspected.Outcome is InspectionOutcome.Failed or InspectionOutcome.UnknownLayout;
            if (command.Kind == AuditCommandKind.ValidateContract)
            {
                var (contract, extra) = await ContractAudit.CheckAsync(inspected, raw, command.RequireContract, WorkbookReader, cancellationToken).ConfigureAwait(false);
                file = file with
                {
                    Contract = contract,
                    Diagnostics = [.. file.Diagnostics, .. extra.Select(diagnostic => AuditReportBuilder.Diagnostic(diagnostic, command.Detail))]
                };
                blocked = contract.Blockers > 0 || (inspected.Source is null && blocked);
            }
            files.Add(file with { Verdict = AuditReportBuilder.Verdict(file, blocked, command.Strict) });
        }
        var report = new AuditReport
        {
            Command = AuditCommandLine.Name(command.Kind),
            Arguments = AuditReportBuilder.EchoArguments(args),
            Tool = AuditToolStamp.Current(files.Select(file => file.FamilyCode).OfType<string>()),
            Detail = command.Detail,
            StartedUtc = started,
            ElapsedMs = clock.ElapsedMilliseconds,
            Files = files,
            Summary = AuditReportBuilder.Summary(files, run.SkippedCsvFiles)
        };
        await WriteAsync(command, report, folder).ConfigureAwait(false);
        return report.Summary.ExitCode;
    }

    private IProgress<string>? Progress(AuditCommand command) => command.Quiet ? null : new LineProgress(error);

    /// <summary>The human report to stdout, the JSON and text reports to the folder (design 7).</summary>
    private async Task WriteAsync(AuditCommand command, AuditReport report, string? folder)
    {
        var text = AuditReportWriter.Text(report);
        if (command.Format is AuditFormat.Text or AuditFormat.Both) await output.WriteAsync(text).ConfigureAwait(false);
        if (folder is null) return;
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "report.json"), AuditReportWriter.Json(report)).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, "report.txt"), text).ConfigureAwait(false);
        if (!command.Quiet) await error.WriteLineAsync($"Report written to {folder}").ConfigureAwait(false);
    }

    /// <summary><c>--debug</c>: the stack trace, under a banner, in the report folder only (design 8.5).</summary>
    private async Task WriteDebugAsync(AuditCommand command, string? folder, Exception exception)
    {
        if (!command.Debug) return;
        var target = folder ?? AuditOutputLocation.DefaultFolder(command.Kind, Clock());
        try
        {
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "debug.txt"),
                "THIS FILE MAY CONTAIN WORKBOOK TEXT OR CUSTOMER DATA. DO NOT SHARE IT.\r\n\r\n" + exception).ConfigureAwait(false);
            await error.WriteLineAsync($"Debug details written to {target}").ConfigureAwait(false);
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync("ImportAudit: debug.txt could not be written.").ConfigureAwait(false);
        }
    }

    private const int LockTimeoutNumber = 1222;

    /// <summary>
    /// <c>check-import --planner 1</c> (design 3.4, 5.3): inspect, then predict each file against the database,
    /// SELECT-only, in the app's order. A change to the import tables while reading is <c>STATE_CHANGED_DURING_READ</c>.
    /// </summary>
    private async Task<int> CheckImportAsync(AuditCommand command, IReadOnlyList<string> args, string? folder, CancellationToken cancellationToken)
    {
        var started = Clock();
        var clock = Stopwatch.StartNew();
        // Read the expectation file first: a bad one is an input error before any database work.
        var expectations = command.ExpectFile is { } expect ? AuditExpectations.Load(expect) : null;
        await using var connection = await OpenAsync(command, cancellationToken).ConfigureAwait(false);
        var schema = await SqlSchemaFacts.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!schema.Supported)
            throw new AuditDatabaseException("This database predates migration 0018. Upgrade it to release 1.8 or later before checking.");
        var before = await SqlStateMark.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        var stores = (await connection.QueryAsync(AuditQueries.ActiveStores, cancellationToken: cancellationToken).ConfigureAwait(false))
            .Select(row => (string)row[0]!).Concat(command.KnownStores).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var inspector = new SourceInspector(WorkbookReader, stores) { Families = command.Families, Store = command.Store };
        var run = await inspector.InspectAsync(command.Paths, Progress(command), cancellationToken).ConfigureAwait(false);
        var predictor = new PlannerOnePredictor(new SqlPlannerOneState(connection, schema), ReadOnlyAuditConnection.ConnectionString(command.Server, command.Database!));
        var files = new List<AuditFileReport>();
        foreach (var inspected in run.Files)
        {
            var file = AuditReportBuilder.File(inspected, command.Detail);
            var planner = await predictor.PredictAsync(inspected, command.Detail == AuditDetail.Rows, cancellationToken).ConfigureAwait(false);
            file = file with { Planner1 = planner };
            files.Add(file);
            if (!command.Quiet) await error.WriteLineAsync($"predicted {file.File}: {planner.Result}{(planner.Code is null ? "" : " " + planner.Code)}").ConfigureAwait(false);
        }
        var after = await SqlStateMark.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        return await FinishCheckAsync(command, args, folder, started, clock, schema, files, run.SkippedCsvFiles, expectations, before != after).ConfigureAwait(false);
    }

    /// <summary>Expectations, verdicts, summary and report of a <c>check-import</c> run.</summary>
    internal async Task<int> FinishCheckAsync(AuditCommand command, IReadOnlyList<string> args, string? folder, DateTimeOffset started, Stopwatch clock,
        SqlSchemaFacts? schema, IReadOnlyList<AuditFileReport> predicted, int skippedCsv, AuditExpectations? expectations, bool stateChanged)
    {
        var files = predicted.ToList();
        var runDiagnostics = new List<AuditDiagnosticReport>();
        var runFinding = false;
        if (stateChanged)
        {
            runDiagnostics.Add(new(AuditCodes.StateChangedDuringRead, "Blocker", AuditCodes.StateChangedDuringReadMessage));
            runFinding = true;
        }
        if (expectations is not null)
        {
            var (expected, missing) = expectations.Apply(files);
            files = expected.ToList();
            foreach (var mismatch in missing)
                runDiagnostics.Add(new(AuditCodes.ExpectationMismatch, "Blocker", AuditCodes.Messages[AuditCodes.ExpectationMismatch] + " " + mismatch));
            runFinding |= missing.Count > 0;
        }
        files = files.Select(file => file with
        {
            Verdict = AuditReportBuilder.Verdict(file, file.Planner1?.Result is "Failed" or "Unknown layout", command.Strict)
        }).ToList();
        var summary = AuditReportBuilder.Summary(files, skippedCsv, runFinding, expectations is null ? null : expectations.AllowsBlocker);
        if (expectations?.UnexpectedBlockers is { } allowed && summary.UnexpectedBlockers > allowed)
            summary = summary with { ExitCode = AuditExitCodes.Findings };
        var report = new AuditReport
        {
            Command = AuditCommandLine.Name(command.Kind),
            Arguments = AuditReportBuilder.EchoArguments(args),
            Tool = AuditToolStamp.Current(files.Select(file => file.FamilyCode).OfType<string>()),
            Database = schema is null ? null : Stamp(command, schema),
            Detail = command.Detail,
            StartedUtc = started,
            ElapsedMs = clock.ElapsedMilliseconds,
            OrderNote = PlannerOnePredictor.OrderNote,
            Files = files,
            RunDiagnostics = runDiagnostics,
            Summary = summary
        };
        await WriteAsync(command, report, folder).ConfigureAwait(false);
        return report.Summary.ExitCode;
    }

    /// <summary><c>baseline</c> (design 3.5): the pre-upgrade check script, SELECT-only, as JSON.</summary>
    private async Task<int> BaselineAsync(AuditCommand command, IReadOnlyList<string> args, string? folder, CancellationToken cancellationToken)
    {
        var started = Clock();
        var clock = Stopwatch.StartNew();
        await using var connection = await OpenAsync(command, cancellationToken).ConfigureAwait(false);
        var schema = await SqlSchemaFacts.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        var sets = await BaselineQuery.RunAsync(connection, command.Detail == AuditDetail.Rows, cancellationToken).ConfigureAwait(false)
            ?? throw new AuditDatabaseException("This database predates migration 0018. Upgrade it to release 1.8 or later before checking.");
        var differences = command.CompareFile is { } earlier ? BaselineQuery.Compare(sets, earlier) : null;
        var blocking = BaselineQuery.Blocking(sets);
        var runDiagnostics = blocking.Select(check => new AuditDiagnosticReport(AuditCodes.BlockingCheck, "Blocker",
            AuditCodes.Messages[AuditCodes.BlockingCheck] + " " + check + ".")).ToList();
        var exit = blocking.Count > 0 || differences is { Count: > 0 } ? AuditExitCodes.Findings : AuditExitCodes.Clean;
        var report = new AuditReport
        {
            Command = AuditCommandLine.Name(command.Kind),
            Arguments = AuditReportBuilder.EchoArguments(args),
            Tool = AuditToolStamp.Current([]),
            Database = Stamp(command, schema),
            Detail = command.Detail,
            StartedUtc = started,
            ElapsedMs = clock.ElapsedMilliseconds,
            RunDiagnostics = runDiagnostics,
            Baseline = new AuditBaselineReport(sets) { Differences = differences },
            Summary = new AuditSummary { ExitCode = exit }
        };
        await WriteAsync(command, report, folder).ConfigureAwait(false);
        return exit;
    }

    private static async Task<ReadOnlyAuditConnection> OpenAsync(AuditCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadOnlyAuditConnection.OpenAsync(command.Server, command.Database!, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            throw new AuditDatabaseException("The connection was refused: ImportAudit reads only a SQL Server on this computer, with Windows authentication.");
        }
        catch (SqlException sql)
        {
            throw new AuditDatabaseException($"The database could not be reached (SQL error {sql.Number}).");
        }
    }

    private AuditDatabaseStamp Stamp(AuditCommand command, SqlSchemaFacts schema)
    {
        var notes = new List<string>();
        if (schema.Emulated0041)
            notes.Add("The database predates 0041: stock line numbers and the snapshot source were emulated as 0041 sets them. The Closing Stock rows 0041 rebuilds from logged outcomes are not emulated.");
        var tool = ToolLatestMigration();
        if (tool is not null && schema.LatestMigration is { } database && string.CompareOrdinal(database, tool) > 0)
            notes.Add($"The database ({database}) is newer than this tool ({tool}); run the audit from the installed release.");
        return new AuditDatabaseStamp(command.Server, command.Database!)
        {
            LatestMigration = schema.LatestMigration, Emulated0041 = schema.Emulated0041, LoginCouldWrite = schema.LoginCouldWrite, Notes = notes
        };
    }

    private string? ToolLatestMigration()
    {
        try
        {
            return Directory.Exists(MigrationsDirectory)
                ? Directory.EnumerateFiles(MigrationsDirectory, "*.sql").Select(Path.GetFileNameWithoutExtension).OfType<string>().Max(StringComparer.Ordinal)
                : null;
        }
        catch (IOException) { return null; }
    }

    /// <summary>A database refusal or an unreachable database (exit 4); the message is the audit's own.</summary>
    private sealed class AuditDatabaseException(string message) : Exception(message);

    /// <summary>Progress lines written at once, in order (<see cref="Progress{T}"/> would post them to the thread pool).</summary>
    private sealed class LineProgress(TextWriter writer) : IProgress<string>
    {
        public void Report(string value) => writer.WriteLine(value);
    }
}
