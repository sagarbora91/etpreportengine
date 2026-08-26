using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Etp.Reporting.Reporting;

public sealed record ReportPackageFile(string RelativePath, long SizeBytes, string Sha256);
public sealed record ReportPackageManifest(DateOnly BusinessDate, int Generation, IReadOnlyList<string> Stores,
    string CreatedBy, DateTimeOffset CreatedUtc, DateTimeOffset CreatedLocal, string Status, IReadOnlyList<ReportPackageFile> Files);
public sealed record ReportPackageResult(string Path, string Sha256, string ManifestJson, IReadOnlyList<ReportPackageFile> Files);

public sealed class ReportPackageService
{
    public async Task<ReportPackageResult> CreateAsync(
        string outputPath,
        ReportPackDocument document,
        int generation,
        string storeCode,
        bool isFinal,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        if (!Path.GetExtension(outputPath).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The report package must use a .zip extension.", nameof(outputPath));
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        if (File.Exists(fullOutput)) throw new IOException("A report package with this name already exists. Choose a new file name so prior evidence is not overwritten.");
        var stage = Path.Combine(Path.GetTempPath(), "EtpReporting", "Package", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var temporaryZip = fullOutput + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var generationFolder = Path.Combine(stage, "ETP-Reports", document.DateTo.ToString("yyyy-MM-dd"), $"Generation-{generation:D2}", SafeSegment(storeCode));
            Directory.CreateDirectory(generationFolder);
            var excel = Path.Combine(generationFolder, "Report-Pack.xlsx");
            var pdf = Path.Combine(generationFolder, "Report-Pack.pdf");
            new OpenXmlReportPackExporter().Export(excel, document);
            new SimplePdfReportPackExporter().Export(pdf, document);
            var files = new List<ReportPackageFile>
            {
                await DescribeAsync(stage, excel, cancellationToken).ConfigureAwait(false),
                await DescribeAsync(stage, pdf, cancellationToken).ConfigureAwait(false)
            };
            var now = DateTimeOffset.Now;
            var manifest = new ReportPackageManifest(document.DateTo, generation, [storeCode], createdBy, now.ToUniversalTime(), now,
                isFinal ? "Final" : "Draft", files);
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            var manifestPath = Path.Combine(stage, "ETP-Reports", document.DateTo.ToString("yyyy-MM-dd"), $"Generation-{generation:D2}", "manifest.json");
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken).ConfigureAwait(false);
            ZipFile.CreateFromDirectory(stage, temporaryZip, CompressionLevel.Optimal, false);
            File.Move(temporaryZip, fullOutput, false);
            return new(fullOutput, await HashAsync(fullOutput, cancellationToken).ConfigureAwait(false), manifestJson, files);
        }
        finally
        {
            try { if (File.Exists(temporaryZip)) File.Delete(temporaryZip); } catch (IOException) { }
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<ReportPackageFile> DescribeAsync(string root, string path, CancellationToken token) =>
        new(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), new FileInfo(path).Length, await HashAsync(path, token).ConfigureAwait(false));
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }
    private static string SafeSegment(string value)
    {
        var cleaned = string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Combined" : cleaned;
    }
}
