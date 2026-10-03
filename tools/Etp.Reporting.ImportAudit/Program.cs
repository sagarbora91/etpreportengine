using Etp.Reporting.Infrastructure.SqlServer.Audit;

// ImportAudit (design: Reference\Work in progress 2026-10-01\design\IMPORTAUDIT-CLI-DESIGN.md). This file only wires the
// console: the commands live in Etp.Reporting.Infrastructure.SqlServer.Audit and Etp.Reporting.Import.Audit, where they
// are tested. Ctrl+C cancels the run; temporary ZIP folders are removed as the commands unwind (exit 130).
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
return await new AuditRunner(Console.Out, Console.Error).RunAsync(args, cancellation.Token);
