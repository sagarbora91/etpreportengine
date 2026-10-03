using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>The attachment as it was read from the validated file; nothing reopens its path.</summary>
public sealed record ReportEmailAttachment(string FileName, byte[] Content);

/// <summary>
/// S-02 check-then-reopen gap (re-audit 26 Sep 2026). Checking the attachment's path and then
/// letting the mail library open that path again left a window in which anyone who can write
/// to the sharing folder could put a link, a junction or a hard link in its place and have a
/// file from outside the folder sent. Here the file is opened once, without following a link
/// at its own name and without letting anyone write, rename or delete it; the open handle
/// itself is proved to be inside the sharing folder; and the bytes sent are read from that
/// same handle.
/// </summary>
internal static class HeldReportAttachment
{
    private const string OutsideFolder = "Report attachments must be prepared in the sharing folder.";

    public static ReportEmailAttachment Read(string fullPath, string shareFolderPath, int maximumAttachmentMb)
    {
        using var handle = OpenWithoutFollowingLinks(fullPath);
        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandle(handle, out var information)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var attributes = (FileAttributes)information.FileAttributes;
            // A link at the file's own name is opened as the link, so it shows here.
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new UnauthorizedAccessException(OutsideFolder);
            // A hard link has no reparse point and the folder's path; only the link count shows it.
            if (information.NumberOfLinks != 1) throw new UnauthorizedAccessException(OutsideFolder);
            // Where the open file really is, after every junction and link in its ancestors.
            var actual = FinalPath(handle);
            using var folderHandle = OpenFolderForQuery(Path.GetFullPath(shareFolderPath));
            var folder = FinalPath(folderHandle);
            var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
            if (!actual.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException(OutsideFolder);
        }
        var length = RandomAccess.GetLength(handle);
        if (length > maximumAttachmentMb * 1024L * 1024L)
            throw new InvalidOperationException($"The attachment exceeds the configured {maximumAttachmentMb} MB email limit.");
        var content = new byte[length];
        var read = 0;
        while (read < content.Length)
        {
            var count = RandomAccess.Read(handle, content.AsSpan(read), read);
            if (count == 0) throw new IOException("The report attachment changed while it was being read.");
            read += count;
        }
        return new(Path.GetFileName(fullPath), content);
    }

    private static SafeFileHandle OpenWithoutFollowingLinks(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // FILE_SHARE_READ only: no one may write, rename or delete it while it is held.
            var handle = CreateFile(path, GenericRead, FileShareRead, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (error is ErrorFileNotFound or ErrorPathNotFound) throw new FileNotFoundException("The report attachment was not found.", path);
                throw new IOException("The report attachment could not be opened.", new Win32Exception(error));
            }
            return handle;
        }
        catch (DirectoryNotFoundException) { throw new FileNotFoundException("The report attachment was not found.", path); }
    }

    private static SafeFileHandle OpenFolderForQuery(string path)
    {
        var handle = CreateFile(path, FileReadAttributes, FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new UnauthorizedAccessException(OutsideFolder, new Win32Exception(error));
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
        }
        if (length == 0 || length >= buffer.Capacity) throw new UnauthorizedAccessException(OutsideFolder, new Win32Exception(Marshal.GetLastWin32Error()));
        var path = buffer.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path[8..];
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    private const uint GenericRead = 0x80000000;
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint FileShareDelete = 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, int length, uint flags);
}
