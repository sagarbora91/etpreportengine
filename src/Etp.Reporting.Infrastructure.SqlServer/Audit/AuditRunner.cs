using System.Diagnostics;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Audit;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Workbooks;

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
                _ => await NotYetAsync(command).ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("ImportAudit: cancelled. Temporary folders were removed.").ConfigureAwait(false);
            return AuditExitCodes.Cancelled;
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

    private async Task<int> NotYetAsync(AuditCommand command)
    {
        await error.WriteLineAsync($"ImportAudit: {AuditCommandLine.Name(command.Kind)} is not available in this build.").ConfigureAwait(false);
        return AuditExitCodes.Internal;
    }

    /// <summary>Progress lines written at once, in order (<see cref="Progress{T}"/> would post them to the thread pool).</summary>
    private sealed class LineProgress(TextWriter writer) : IProgress<string>
    {
        public void Report(string value) => writer.WriteLine(value);
    }
}
