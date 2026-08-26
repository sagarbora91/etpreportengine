using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Infrastructure.SqlServer;

public interface IDocumentTextExtractor
{
    string Method { get; }
    Task<DocumentExtractionResult> ExtractAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class NativePdfTextExtractor : IDocumentTextExtractor
{
    private static readonly Regex LiteralText = new(@"\((?<text>(?:\\.|[^\\)])*)\)\s*T[Jj]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public string Method => "NATIVE_PDF";

    public async Task<DocumentExtractionResult> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return new(Method, "native-pdf-lite/1", string.Empty, null, ReviewStatus: "REVIEW_REQUIRED");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            throw new InvalidDataException("The selected file is not a valid PDF document.");
        var source = Encoding.Latin1.GetString(bytes);
        var text = string.Join(" ", LiteralText.Matches(source).Select(match => DecodePdfLiteral(match.Groups["text"].Value)))
            .Replace('\0', ' ').Trim();
        var usable = text.Count(char.IsLetterOrDigit) >= 20;
        return new(Method, "native-pdf-lite/1", usable ? text : string.Empty, usable ? 1m : null,
            ReviewStatus: usable ? "REVIEW_REQUIRED" : "REVIEW_REQUIRED");
    }

    private static string DecodePdfLiteral(string value) => value
        .Replace("\\n", " ", StringComparison.Ordinal).Replace("\\r", " ", StringComparison.Ordinal)
        .Replace("\\t", " ", StringComparison.Ordinal).Replace("\\(", "(", StringComparison.Ordinal)
        .Replace("\\)", ")", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
}

public sealed class PaddleOcrProcessExtractor(string helperPath, string? modelPath = null, TimeSpan? timeout = null) : IDocumentTextExtractor
{
    public string Method => "PADDLE_OCR";
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMinutes(2);

    public async Task<DocumentExtractionResult> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var helper = SafeExistingFile(helperPath, "OCR helper");
        if (!string.IsNullOrWhiteSpace(modelPath)) SafeExistingDirectory(modelPath, "OCR model");
        var output = Path.Combine(Path.GetTempPath(), $"etp-ocr-{Guid.NewGuid():N}.json");
        try
        {
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add("--input"); start.ArgumentList.Add(Path.GetFullPath(path));
            start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
            if (!string.IsNullOrWhiteSpace(modelPath)) { start.ArgumentList.Add("--model-dir"); start.ArgumentList.Add(Path.GetFullPath(modelPath)); }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The OCR helper could not be started.");
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("OCR extraction failed. The document remains safely stored for manual review.");
            var payload = JsonSerializer.Deserialize<OcrPayload>(await File.ReadAllTextAsync(output, cancellationToken).ConfigureAwait(false))
                ?? throw new InvalidDataException("The OCR helper returned an invalid result.");
            var confidence = Math.Clamp(payload.Confidence, 0m, 1m);
            return new(Method, payload.Version ?? "paddleocr-helper/unknown", payload.Text ?? string.Empty, confidence,
                payload.Page, payload.BoundingBoxes is null ? null : JsonSerializer.Serialize(payload.BoundingBoxes),
                payload.Fields is null ? null : JsonSerializer.Serialize(payload.Fields),
                confidence >= 0.90m && !string.IsNullOrWhiteSpace(payload.Text) ? "REVIEW_REQUIRED" : "REVIEW_REQUIRED");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("OCR extraction timed out. The document remains safely stored for manual review.");
        }
        finally { try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { } }
    }

    private static string SafeExistingFile(string path, string label)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException($"{label} is unavailable.", full);
        return full;
    }

    private static string SafeExistingDirectory(string path, string label)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new DirectoryNotFoundException($"{label} is unavailable.");
        return full;
    }

    private sealed record OcrPayload(string? Text, decimal Confidence, string? Version, int? Page,
        object? BoundingBoxes, object? Fields);
}

public static class ManagedDocumentRepository
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".xlsx", ".zip" };

    public static async Task<(string ManagedPath, string Sha256, long Size)> StoreAsync(
        string sourcePath, string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected document no longer exists.", source);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked document paths are not allowed.");
        var extension = Path.GetExtension(source);
        if (!AllowedExtensions.Contains(extension)) throw new InvalidOperationException("Select a supported PDF or image document.");
        var info = new FileInfo(source);
        if (info.Length is <= 0 or > 100 * 1024 * 1024) throw new InvalidOperationException("The document is empty or exceeds the 100 MB safety limit.");
        var root = Path.GetFullPath(repositoryRoot);
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The document repository cannot be a linked folder.");
        var hash = await ComputeHashAsync(source, cancellationToken).ConfigureAwait(false);
        var folder = Path.Combine(root, DateTime.Today.ToString("yyyy"), DateTime.Today.ToString("MM"));
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, hash + extension.ToLowerInvariant());
        if (!File.Exists(destination))
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        if (!string.Equals(hash, await ComputeHashAsync(destination, cancellationToken).ConfigureAwait(false), StringComparison.Ordinal))
            throw new IOException("The managed document failed its integrity check.");
        return (destination, hash, info.Length);
    }

    public static async Task<bool> VerifyIntegrityAsync(string managedPath,string expectedSha256,CancellationToken cancellationToken=default)
    {
        var full=Path.GetFullPath(managedPath);if(!File.Exists(full)||(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)return false;
        return string.Equals(await ComputeHashAsync(full,cancellationToken).ConfigureAwait(false),SqlServerImportFileRepository.NormalizeHash(expectedSha256),StringComparison.Ordinal);
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }
}
