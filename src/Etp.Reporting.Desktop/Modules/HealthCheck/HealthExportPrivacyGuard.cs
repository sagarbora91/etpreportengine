using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Modules.HealthCheck;

/// <summary>
/// 1.11.0 self-diagnostics, design section 8 and section 10. Every string that goes into a health export passes
/// through one of these checks before it is written; anything that fails is replaced with <see cref="Redacted"/> and
/// counted (<c>privacy.redactions</c> in the file, and a Warning on ERR-01). No customer data may leave the PC:
/// no names, phone numbers, account or card numbers, e-mail addresses, paths, quotes or invoice numbers.
/// <list type="bullet">
/// <item><b>Tokens</b> (ids, codes, versions, dates, store and report codes, method names, machine name): the
/// <c>SafeFrame</c> character set without spaces, up to 200 characters; a comma-separated list of such tokens
/// (<c>"INSERT, UPDATE"</c>) is one token.</item>
/// <item><b>Texts</b> (explanation, fix): only a template of the catalogue (<c>HealthCheckTexts</c>, plus the
/// writer's own) whose placeholders are filled with tokens. A sentence that matches no template is redacted, so a
/// probe that formats free text into an explanation is caught, not exported.</item>
/// <item><b>Fingerprints</b>: exactly 64 lowercase hex characters. <b>Refs</b>: 12 hex characters.</item>
/// <item><b>Numbers</b>: an integer part of 10 or more digits (a phone or account number in a metric) is dropped.</item>
/// </list>
/// Every kind also refuses a run of 10 or more digits and an invoice-like value (letters then 5 or more digits, as
/// <c>INV001234</c> or <c>HE-0012345</c>); <c>@</c>, <c>\</c>, <c>/</c> and quotes are outside every character set.
/// </summary>
public sealed class HealthExportPrivacyGuard
{
    /// <summary>Written into every export as <c>privacy.guard</c>; change the number when the rules change.</summary>
    public const string Name = "HealthExportPrivacyGuard/1";

    /// <summary>What a refused value is replaced with.</summary>
    public const string Redacted = "redacted";

    internal const int MaxTokenLength = 200;
    internal const int MaxPlaceholderLength = 64;
    internal const int MaxTextLength = 600;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    // One list item: the SafeFrame set (DesktopDiagnostics.SafeToken) without the space.
    private const string TokenItem = @"[A-Za-z0-9._:<>+\-\[\]]";
    private static readonly Regex TokenList = new(
        "^" + TokenItem + "+(?:, " + TokenItem + "+)*$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex Fingerprint = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex Reference = new("^[0-9A-Fa-f]{12}$", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex LongDigitRun = new(@"[0-9]{10,}", RegexOptions.CultureInvariant, MatchTimeout);
    // Letters, an optional separator, then five or more digits: INV001234, HE-0012345, BILL_00042.
    // "SQL18456" (an error number) and "ER-0006" (four digits) stay readable.
    private static readonly Regex InvoiceLike = new(
        @"(?<![A-Za-z0-9])(?!SQL[0-9])[A-Za-z]{1,6}[-_]?[0-9]{5,}", RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex Placeholder = new(@"\{[A-Za-z0-9_]+\}", RegexOptions.CultureInvariant, MatchTimeout);

    private readonly IReadOnlyList<Regex> templates;

    /// <param name="textTemplates">
    /// The explanation and fix templates a text may be built from, with <c>{name}</c> or <c>{0}</c> placeholders.
    /// </param>
    public HealthExportPrivacyGuard(IEnumerable<string> textTemplates)
    {
        ArgumentNullException.ThrowIfNull(textTemplates);
        templates = textTemplates
            .Where(template => !string.IsNullOrWhiteSpace(template))
            .Distinct(StringComparer.Ordinal)
            .Select(Compile)
            .ToArray();
    }

    /// <summary>Values replaced so far by this guard (one guard per export file).</summary>
    public int Redactions { get; private set; }

    /// <summary>A token or a comma-separated list of tokens; null stays null.</summary>
    public string? Token(string? value) => Check(value, IsSafeToken);

    /// <summary>An explanation or fixed instruction: a catalogue template filled with tokens; null stays null.</summary>
    public string? Text(string? value) => Check(value, IsSafeText);

    /// <summary>A task id (a token) or a fixed instruction (a template).</summary>
    public string? TokenOrText(string? value) => Check(value, candidate => IsSafeToken(candidate) || IsSafeText(candidate));

    /// <summary>A register fingerprint: 64 lowercase hex characters.</summary>
    public string? FingerprintValue(string? value) => Check(value, static candidate => Fingerprint.IsMatch(candidate));

    /// <summary>A diagnostics Ref: 12 hex characters (and not 10 or more decimal digits in a row).</summary>
    public string? ReferenceValue(string? value) =>
        Check(value, static candidate => Reference.IsMatch(candidate) && !LongDigitRun.IsMatch(candidate));

    /// <summary>A dictionary key; a refused key becomes <c>redacted</c>, <c>redacted-2</c>, ... so keys stay unique.</summary>
    public string Key(string key, ISet<string> used)
    {
        ArgumentNullException.ThrowIfNull(used);
        ArgumentNullException.ThrowIfNull(key);
        var safe = Token(key) ?? Redacted;
        var candidate = safe;
        for (var suffix = 2; !used.Add(candidate); suffix++)
            candidate = safe + "-" + suffix.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    /// <summary>A number a probe filled in; 10 or more integer digits is dropped (null) and counted.</summary>
    public decimal? Number(decimal? value)
    {
        if (value is not { } number) return null;
        if (Math.Abs(decimal.Truncate(number)) < 1_000_000_000m) return number;
        Redactions++;
        return null;
    }

    /// <inheritdoc cref="Number(decimal?)"/>
    public long? Number(long? value)
    {
        if (value is not { } number) return null;
        if (number is > -1_000_000_000L and < 1_000_000_000L) return number;
        Redactions++;
        return null;
    }

    /// <summary>Pure check used by <see cref="Token"/>.</summary>
    public static bool IsSafeToken(string value) =>
        value.Length is > 0 and <= MaxTokenLength && TokenList.IsMatch(value) && !LooksLikeCustomerData(value);

    /// <summary>Pure check used by <see cref="Text"/>.</summary>
    public bool IsSafeText(string value)
    {
        if (value.Length is 0 or > MaxTextLength) return false;
        foreach (var template in templates)
        {
            var match = template.Match(value);
            if (!match.Success) continue;
            var filled = true;
            for (var group = 1; group < match.Groups.Count && filled; group++)
                filled = IsSafePlaceholder(match.Groups[group].Value);
            if (filled) return true;
        }
        return false;
    }

    private static bool IsSafePlaceholder(string value) =>
        value.Length > 0 && value.Split(", ").All(item => item.Length <= MaxPlaceholderLength) && IsSafeToken(value);

    private static bool LooksLikeCustomerData(string value) => LongDigitRun.IsMatch(value) || InvoiceLike.IsMatch(value);

    private string? Check(string? value, Func<string, bool> isSafe)
    {
        if (value is null) return null;
        if (value.Length == 0) return value; // an empty SubCode or Site carries nothing
        bool safe;
        // A value that makes a check time out is not proven safe.
        try { safe = isSafe(value); }
        catch (RegexMatchTimeoutException) { safe = false; }
        if (safe) return value;
        Redactions++;
        return Redacted;
    }

    private static Regex Compile(string template)
    {
        var pattern = new StringBuilder("^");
        var position = 0;
        foreach (Match placeholder in Placeholder.Matches(template))
        {
            pattern.Append(Regex.Escape(template[position..placeholder.Index]));
            // Lazy and bounded; the captured value is checked as a token afterwards.
            pattern.Append("(.{1,").Append(MaxTokenLength.ToString(CultureInfo.InvariantCulture)).Append("}?)");
            position = placeholder.Index + placeholder.Length;
        }
        pattern.Append(Regex.Escape(template[position..])).Append('$');
        return new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline, MatchTimeout);
    }
}
