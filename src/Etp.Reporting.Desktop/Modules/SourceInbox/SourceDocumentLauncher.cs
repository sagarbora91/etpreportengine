using System.Diagnostics;
using System.IO;

namespace Etp.Reporting.Desktop.Modules.SourceInbox;

public interface ISourceDocumentLauncher
{
    void Open(string managedFilePath);
}

public sealed class SourceDocumentLauncher : ISourceDocumentLauncher
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".xlsx", ".zip"
    };

    public void Open(string managedFilePath)
    {
        var validatedPath = ValidateManagedDocumentPath(managedFilePath);
        Process.Start(new ProcessStartInfo(validatedPath) { UseShellExecute = true });
    }

    public static string ValidateManagedDocumentPath(string managedFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedFilePath);
        var fullPath = Path.GetFullPath(managedFilePath);
        if (!AllowedExtensions.Contains(Path.GetExtension(fullPath)))
            throw new InvalidOperationException("Only retained ETP workbooks, ZIP packages, PDFs and images can be opened from Source Inbox.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The retained source document is no longer available.", fullPath);
        return fullPath;
    }
}
