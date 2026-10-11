extern alias EtpApplication;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;

namespace Etp.Reporting.Desktop.Diagnostics;

/// <summary>
/// Fingerprint rule v1 of the error register (1.11.0 design, section 4.2):
/// <c>SHA-256("v1|" + EventBase + "|" + SubCode + "|" + Source + "|" + Site + "|" + ExceptionType + "|" + SqlNumber)</c>.
/// The fingerprint is computed here and by nothing else; the database only re-validates the tokens.
/// Line numbers, report, store, business date, operation and Ref are context, never identity.
/// </summary>
public static class ErrorFingerprint
{
    public const int Version = 1;
    public const int MaxSiteLength = 200;

    private static readonly Regex SqlSuffix = new(@"\.SQL(-?\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TrailingSqlSuffix = new(@"\.SQL-?\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GeneratedName = new(@"^<+([^<>]+)>", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GenericArity = new(@"`\d+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GenericArguments = new(@"\[[^\[\]]*\]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The identity parts of one logged entry (the entry must be Error or Critical; the caller decides that).</summary>
    public static ErrorFingerprintParts PartsOf(DesktopDiagnosticEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ErrorFingerprintParts(
            EventBaseOf(entry.EventId),
            SubCodeOf(entry.EventId),
            entry.Source ?? string.Empty,
            SiteOf(entry.Stack),
            entry.ExceptionType ?? string.Empty,
            SqlNumberOf(entry));
    }

    /// <summary>64 lowercase hex characters.</summary>
    public static string Compute(ErrorFingerprintParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var text = string.Concat("v", Version.ToString(CultureInfo.InvariantCulture), "|",
            parts.EventBase, "|", parts.SubCode, "|", parts.Source, "|", parts.Site, "|", parts.ExceptionType, "|",
            parts.SqlNumber.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary><c>EventId</c> up to the first '.' or ':'. <c>DATABASE_HEALTH_CHECK_FAILED.SQL18456</c> gives <c>DATABASE_HEALTH_CHECK_FAILED</c>.</summary>
    public static string EventBaseOf(string? eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return string.Empty;
        var end = eventId.IndexOfAny(['.', ':']);
        return end < 0 ? eventId : eventId[..end];
    }

    /// <summary>The text after the first ':' (ops-scripts reason code), else empty. A trailing <c>.SQLn</c> is not part of it.</summary>
    public static string SubCodeOf(string? eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return string.Empty;
        var colon = eventId.IndexOf(':');
        if (colon < 0) return string.Empty;
        return TrailingSqlSuffix.Replace(eventId[(colon + 1)..], string.Empty);
    }

    /// <summary>The first SQL error with Class 11 or more; else the number of a <c>.SQLn</c> suffix; else 0.</summary>
    public static int SqlNumberOf(DesktopDiagnosticEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var first = entry.SqlErrors?.FirstOrDefault(error => error.Class >= 11);
        if (first is not null) return first.Number;
        var match = entry.EventId is null ? Match.Empty : SqlSuffix.Match(entry.EventId);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
    }

    /// <summary>
    /// The innermost frame (frames are logged innermost first) whose type starts with <c>Etp.</c>, normalised to
    /// <c>Type.Method</c>. Empty when there is no stack (entries before 1.9.9) or no Etp frame.
    /// </summary>
    public static string SiteOf(IReadOnlyList<string>? stack)
    {
        if (stack is null) return string.Empty;
        foreach (var frame in stack)
        {
            var site = NormaliseFrame(frame);
            if (site is not null) return site;
        }
        return string.Empty;
    }

    /// <summary>
    /// Normalises one logged frame (<c>Namespace.Type+Nested.Method</c>). Returns null for a frame outside <c>Etp.</c>.
    /// State machines (<c>Type+&lt;RunAsync&gt;d__12.MoveNext</c>), lambdas (<c>Type+&lt;&gt;c.&lt;Run&gt;b__3_0</c>) and local
    /// functions (<c>&lt;Run&gt;g__Local|3_0</c>) reduce to the enclosing method; generic arity and namespace are dropped.
    /// </summary>
    public static string? NormaliseFrame(string? frame)
    {
        if (string.IsNullOrWhiteSpace(frame)) return null;
        var text = frame.Trim();
        if (!text.StartsWith("Etp.", StringComparison.Ordinal)) return null;

        text = GenericArguments.Replace(text, string.Empty);
        text = GenericArity.Replace(text, string.Empty);

        var split = LastTopLevelDot(text);
        if (split <= 0 || split >= text.Length - 1) return null;
        var typePart = text[..split];
        var method = text[(split + 1)..];
        if (typePart.EndsWith('.')) // "Type..ctor" splits on the second dot
        {
            typePart = typePart[..^1];
            method = "." + method;
        }

        var segments = SplitTopLevel(typePart, '+');
        if (segments.Count == 0) return null;
        segments[0] = segments[0][(LastTopLevelDot(segments[0]) + 1)..]; // drop the namespace

        var name = method;
        if (method.StartsWith('<'))
        {
            var generated = GeneratedName.Match(method);
            if (generated.Success) name = generated.Groups[1].Value;
        }
        else if (method == "MoveNext" && segments[^1].StartsWith('<'))
        {
            var generated = GeneratedName.Match(segments[^1]);
            if (generated.Success) name = generated.Groups[1].Value;
        }

        var userSegments = segments.Where(segment => !segment.StartsWith('<')).ToArray();
        var type = userSegments.Length > 0 ? string.Join('+', userSegments) : segments[0].Trim('<', '>');
        var site = Safe($"{type}.{name}");
        if (site.Length > MaxSiteLength) site = site[..MaxSiteLength];
        return site.Length == 0 ? null : site;
    }

    /// <summary>
    /// An entry with no Stack (written before 1.9.9) whose other five parts match exactly one existing fingerprint is
    /// attached to it (design 4.2, remedy 2). Returns that entry, or null for zero or several matches (the caller
    /// then uses the entry's own "legacy" fingerprint, Site empty), or when the exact legacy fingerprint already
    /// exists. Merged entries are ignored.
    /// </summary>
    public static ErrorRegisterEntry? SoftMatch(ErrorFingerprintParts parts, IEnumerable<ErrorRegisterEntry> existing)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(existing);
        var own = Compute(parts);
        var candidates = new Dictionary<string, ErrorRegisterEntry>(StringComparer.Ordinal);
        foreach (var entry in existing)
        {
            if (entry.MergedIntoId is not null) continue;
            if (string.Equals(entry.Fingerprint, own, StringComparison.Ordinal)) return null;
            if (string.IsNullOrEmpty(entry.Site)) continue;
            if (entry.FingerprintVersion != Version) continue;
            if (!string.Equals(entry.EventCode, parts.EventBase, StringComparison.Ordinal)) continue;
            if (!string.Equals(entry.SubCode ?? string.Empty, parts.SubCode, StringComparison.Ordinal)) continue;
            if (!string.Equals(entry.Source, parts.Source, StringComparison.Ordinal)) continue;
            if (!string.Equals(entry.ExceptionType, parts.ExceptionType, StringComparison.Ordinal)) continue;
            if (entry.SqlNumber != parts.SqlNumber) continue;
            candidates[entry.Fingerprint] = entry;
        }
        return candidates.Count == 1 ? candidates.Values.First() : null;
    }

    private static int LastTopLevelDot(string text)
    {
        var depth = 0;
        for (var index = text.Length - 1; index >= 0; index--)
        {
            var character = text[index];
            if (character == '>') depth++;
            else if (character == '<') depth = Math.Max(0, depth - 1);
            else if (character == '.' && depth == 0) return index;
        }
        return -1;
    }

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '<') depth++;
            else if (character == '>') depth = Math.Max(0, depth - 1);
            else if (character == separator && depth == 0)
            {
                parts.Add(text[start..index]);
                start = index + 1;
            }
        }
        parts.Add(text[start..]);
        return parts.Where(part => part.Length > 0).ToList();
    }

    private static string Safe(string value) =>
        new(value.Where(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '+' or '-').ToArray());
}
