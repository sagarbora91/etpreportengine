extern alias EtpApplication;

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;
using EtpApplication::Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.Desktop.Diagnostics;

/// <summary>One manifest line: the entry with this fingerprint is fixed from <see cref="FixedIn"/>, under <see cref="Ticket"/>.</summary>
public sealed record ErrorFixLine(string Fingerprint, string FixedIn, string Ticket);

/// <summary>
/// The fix manifest shipped with the app (<c>Diagnostics\ErrorFixManifest.json</c>, design 4.4). The release integrator
/// adds one line per closed fix ticket; the first time a version runs (Owner at start-up, or the nightly run) it hands
/// the raw JSON and its own version to <c>diag.apply_error_fix_manifest</c>, which marks matching entries Fixed only
/// when that version is at or above <c>fixedIn</c>. Idempotent.
/// </summary>
public static class ErrorFixManifest
{
    public const int SupportedManifest = 1;
    public const string FileName = "ErrorFixManifest.json";
    public const string FailureEventId = "ERROR_FIX_MANIFEST_FAILED";
    public const string FailureSource = "Diagnostics.ErrorRegister";

    private static readonly Regex Fingerprint = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TicketPattern = new("^[A-Za-z0-9-]{1,12}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The manifest beside the application: <c>{BaseDirectory}\Diagnostics\ErrorFixManifest.json</c>.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Diagnostics", FileName);

    /// <summary>The well-formed lines of <paramref name="json"/>; <paramref name="problems"/> lists everything wrong (empty when valid).</summary>
    public static IReadOnlyList<ErrorFixLine> Parse(string json, out IReadOnlyList<string> problems)
    {
        var found = new List<string>();
        var lines = new List<ErrorFixLine>();
        problems = found;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException)
        {
            found.Add("The manifest is not valid JSON.");
            return lines;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                found.Add("The manifest root must be an object.");
                return lines;
            }

            if (!root.TryGetProperty("manifest", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != SupportedManifest)
                found.Add($"\"manifest\" must be {SupportedManifest}.");

            if (!root.TryGetProperty("fixes", out var fixes) || fixes.ValueKind != JsonValueKind.Array)
            {
                found.Add("\"fixes\" must be an array.");
                return lines;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var item in fixes.EnumerateArray())
            {
                index++;
                var label = $"fixes[{index}]";
                if (item.ValueKind != JsonValueKind.Object) { found.Add($"{label} must be an object."); continue; }
                var fingerprint = StringOf(item, "fingerprint");
                var fixedIn = StringOf(item, "fixedIn");
                var ticket = StringOf(item, "ticket");
                var ok = true;
                if (fingerprint is null || !Fingerprint.IsMatch(fingerprint)) { found.Add($"{label}: fingerprint must be 64 lowercase hex characters."); ok = false; }
                if (!IsVersion(fixedIn)) { found.Add($"{label}: fixedIn must be a version such as 1.11.1."); ok = false; }
                if (ticket is null || !TicketPattern.IsMatch(ticket)) { found.Add($"{label}: ticket must be 1-12 letters, digits or hyphens."); ok = false; }
                if (ok && !seen.Add(fingerprint!)) { found.Add($"{label}: fingerprint appears twice."); ok = false; }
                if (ok) lines.Add(new ErrorFixLine(fingerprint!, fixedIn!, ticket!));
            }
        }

        return lines;
    }

    /// <summary>
    /// Applies the manifest file for the running version. Returns null when the file is missing, invalid, or the store
    /// call failed; the last two write one ERROR_FIX_MANIFEST_FAILED entry and never throw (start-up must not stop).
    /// </summary>
    public static async Task<ErrorFixManifestResult?> ApplyAsync(
        IErrorRegisterStore store,
        string runningVersion,
        string? manifestPath = null,
        string? logDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var path = manifestPath ?? DefaultPath;
        try
        {
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var lines = Parse(json, out var problems);
            if (problems.Count > 0) throw new InvalidDataException("The error fix manifest is not well-formed.");
            if (lines.Count == 0) return new ErrorFixManifestResult(0, 0, 0, 0, 0);
            return await store.ApplyFixManifestAsync(json, runningVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, FailureSource, FailureEventId, logDirectory: logDirectory, operation: "Error fix manifest");
            return null;
        }
    }

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsVersion(string? text) =>
        !string.IsNullOrEmpty(text) && text.Length <= 20 && text.All(character => char.IsAsciiDigit(character) || character == '.') &&
        System.Version.TryParse(text, out var parsed) && parsed.Major >= 0 && text.Count(character => character == '.') >= 2;
}
