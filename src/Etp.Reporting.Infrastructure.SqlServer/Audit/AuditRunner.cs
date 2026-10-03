using Etp.Reporting.Import.Audit;

namespace Etp.Reporting.Infrastructure.SqlServer.Audit;

/// <summary>
/// Runs one ImportAudit command line (design 3) and returns its exit code (design 9). <c>Program.cs</c> only hands it the
/// arguments, the console writers and a cancellation token, so every command can be tested without a process.
/// </summary>
public sealed class AuditRunner(TextWriter output, TextWriter error)
{
    /// <summary>The folder of migration scripts <c>seed</c> applies; the tool copies them beside itself.</summary>
    public string MigrationsDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "database", "migrations");

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
        try
        {
            return command.Kind switch
            {
                AuditCommandKind.Seed => await SeedCommand.RunAsync(command, MigrationsDirectory, output, error, cancellationToken).ConfigureAwait(false),
                _ => await NotYetAsync(command).ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("ImportAudit: cancelled.").ConfigureAwait(false);
            return AuditExitCodes.Cancelled;
        }
    }

    private async Task<int> NotYetAsync(AuditCommand command)
    {
        await error.WriteLineAsync($"ImportAudit: {AuditCommandLine.Name(command.Kind)} is not available in this build.").ConfigureAwait(false);
        return AuditExitCodes.Internal;
    }
}
