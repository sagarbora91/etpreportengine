using System.IO;

namespace Etp.Reporting.Desktop.Modules.Reports;

internal static class ExportStaging
{
    public static async Task WriteAsync(string destination, Func<string,Task> render, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(destination);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, ".etp-" + Guid.NewGuid().ToString("N") + Path.GetExtension(fullPath));
        try
        {
            await render(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary,fullPath,true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
