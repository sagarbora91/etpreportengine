using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;

namespace Etp.Reporting.Infrastructure.SqlServer.ShopResults;

/// <summary>What ETP decided about one file a shop sent (decision 44 #3). A verdict only: never an amount or a name.</summary>
public enum ShopResultVerdict { Accepted, AcceptedWithWarning, Refused, Duplicate }

/// <summary>One file's verdict. <c>Code</c> is a catalogue code or empty; <c>Message</c> and <c>WhatToDo</c> are catalogue text.</summary>
public sealed record ShopResultFile(string Name, string Sha256, ShopResultVerdict Verdict, string Code, string Message, string WhatToDo);

/// <summary>
/// The result file of one shop zip (schema 1). It holds the role, the business day, the ETP version, when it was produced and
/// one verdict per file. It holds no amounts, no customer or staff names and no Ref beyond what the catalogue prints.
/// </summary>
public sealed record ShopResult(string Role, DateOnly BusinessDate, int Sequence, string EtpVersion, DateTime ProducedUtc,
    IReadOnlyList<ShopResultFile> Files)
{
    public const int SchemaVersion = 1;

    /// <summary>Refused beats a warning, a warning beats accepted; a day of only duplicates is Duplicate.</summary>
    public ShopResultVerdict Overall =>
        Files.Any(f => f.Verdict == ShopResultVerdict.Refused) ? ShopResultVerdict.Refused
        : Files.Any(f => f.Verdict == ShopResultVerdict.AcceptedWithWarning) ? ShopResultVerdict.AcceptedWithWarning
        : Files.Count > 0 && Files.All(f => f.Verdict == ShopResultVerdict.Duplicate) ? ShopResultVerdict.Duplicate
        : ShopResultVerdict.Accepted;
}

/// <summary>Builds a <see cref="ShopResult"/> from the folder import's file results, and reads the shop zip name.</summary>
public static partial class ShopResultBuilder
{
    public const string ImportNotCompletedCode = "IMPORT_NOT_COMPLETED";
    public const string ImportNotCompletedMessage = "ETP could not import this file.";
    public const string ImportNotCompletedWhatToDo = "Send the file again. If it is refused again, tell the office.";

    private static readonly string[] Roles = ["TITAN", "HELIOS", "SERVICE"];

    [GeneratedRegex(@"^(?<role>TITAN|HELIOS|SERVICE)-(?<day>\d{4}-\d{2}-\d{2})(?:-r(?<seq>\d{1,3}))?\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ZipNamePattern();

    /// <summary>
    /// The zip name that <c>Receive-ShopDailyFiles.ps1</c> writes: <c>ROLE-yyyy-MM-dd.zip</c>, or <c>ROLE-yyyy-MM-dd-rN.zip</c> for a
    /// re-send. The sequence is 1 for the first zip and N for <c>-rN</c>. Any other zip (a hand-made one) is not a shop zip.
    /// </summary>
    public static bool TryParseShopZip(string? path, out string role, out DateOnly day, out int sequence)
    {
        role = string.Empty; day = default; sequence = 1;
        var match = ZipNamePattern().Match(Path.GetFileName(path ?? string.Empty));
        if (!match.Success) return false;
        if (!DateOnly.TryParseExact(match.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) return false;
        role = match.Groups["role"].Value.ToUpperInvariant();
        if (match.Groups["seq"].Success) sequence = int.Parse(match.Groups["seq"].Value, CultureInfo.InvariantCulture);
        return sequence >= 1 && Roles.Contains(role);
    }

    public static ShopResult Build(string role, DateOnly day, int sequence, string etpVersion, DateTime producedUtc,
        IEnumerable<FolderImportFileResult> files) =>
        new(role, day, sequence, etpVersion, producedUtc, files.Select(Map).OfType<ShopResultFile>().ToArray());

    /// <summary>The result for a zip the import itself could not process (an exception before any file result existed).</summary>
    public static ShopResult BuildNotCompleted(string role, DateOnly day, int sequence, string etpVersion, DateTime producedUtc, string zipName) =>
        new(role, day, sequence, etpVersion, producedUtc,
            [new ShopResultFile(SafeName(zipName), string.Empty, ShopResultVerdict.Refused, ImportNotCompletedCode,
                ImportNotCompletedMessage, ImportNotCompletedWhatToDo)]);

    /// <summary>The verdict of one file, or null when the run was cancelled before the file was decided (the next run decides it).</summary>
    public static ShopResultFile? Map(FolderImportFileResult file)
    {
        var name = SafeName(file.FileName);
        var sha = (file.SourceSha256 ?? string.Empty).ToUpperInvariant();
        if (file.Status == "Cancelled") return null;
        if (file.Status == "Failed")
        {
            var code = file.Failure?.Code
                ?? file.Diagnostics?.FirstOrDefault(i => i.Severity == ImportIssueSeverity.Blocker)?.Code
                ?? string.Empty;
            var (message, whatToDo) = Split(ImportDiagnosticCatalogue.Template(code));
            return new(name, sha, ShopResultVerdict.Refused, code, message, whatToDo);
        }
        if (file.Status == "Already present" || file.Status.StartsWith("Duplicate", StringComparison.Ordinal))
            return new(name, sha, ShopResultVerdict.Duplicate, string.Empty, "ETP already has this exact file.", string.Empty);

        // Only a warning the shop can act on is shown: the daily-inputs codes. A warning about a stock row means nothing on the shop floor.
        var warning = file.Diagnostics?.FirstOrDefault(i => i.Severity == ImportIssueSeverity.Warning
            && i.Code.StartsWith("DAILY_INPUTS_", StringComparison.Ordinal));
        if (warning is not null)
        {
            var (message, whatToDo) = Split(ImportDiagnosticCatalogue.Template(warning.Code));
            return new(name, sha, ShopResultVerdict.AcceptedWithWarning, warning.Code, message, whatToDo);
        }
        return new(name, sha, ShopResultVerdict.Accepted, string.Empty, string.Empty, string.Empty);
    }

    /// <summary>First sentence = what happened; the rest = what to do. A one-sentence template has no "what to do".</summary>
    internal static (string Message, string WhatToDo) Split(string template)
    {
        var index = template.IndexOf(". ", StringComparison.Ordinal);
        return index < 0 ? (template, string.Empty) : (template[..(index + 1)], template[(index + 2)..]);
    }

    /// <summary>A file name reduced to letters, digits and . _ - ( ) space, at most 120 characters: no path, nothing odd.</summary>
    internal static string SafeName(string? name)
    {
        var leaf = Path.GetFileName(name ?? string.Empty);
        var builder = new StringBuilder(leaf.Length);
        foreach (var c in leaf)
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '(' or ')' or ' ' ? c : '_');
        return builder.Length > 120 ? builder.ToString(0, 120) : builder.ToString();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Schema 1 JSON (camelCase names, UTF-8 text). Publish-ShopResults.ps1 signs these exact bytes.</summary>
    public static string ToJson(ShopResult result) => JsonSerializer.Serialize(new
    {
        schemaVersion = ShopResult.SchemaVersion,
        role = result.Role,
        businessDate = result.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        etpVersion = result.EtpVersion,
        producedUtc = result.ProducedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        sequence = result.Sequence,
        overall = result.Overall,
        files = result.Files.Select(f => new { name = f.Name, sha256 = f.Sha256, verdict = f.Verdict, code = f.Code, message = f.Message, whatToDo = f.WhatToDo })
    }, Options);
}
