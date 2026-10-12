using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Etp.Reporting.Import.Collections;

/// <summary>
/// The SHA-256 identity of a statement line (master plan 8a task 3), unique per bank account or provider in SQL:
/// <c>scope | line date | reference (or, without one, the narration with its spaces collapsed) | credit | debit | ordinal</c>,
/// upper-cased, amounts as F4 invariant, joined by '|'. The ordinal is the 1-based position among the lines of the same file
/// whose other fields are identical, so two real identical bank lines both stay. The running balance and the posting date are
/// not part of it, so the CSV and the XLSX export of one statement give the same identities. Amount and date alone never
/// identify a line.
/// </summary>
public static class LineIdentity
{
    /// <summary>The identity fields of a bank line, without the ordinal.</summary>
    public static string BankPayload(string accountKey, DateOnly lineDate, string? reference, string? narration, decimal? credit, decimal? debit) =>
        Join(accountKey, Day(lineDate), Words(reference) ?? Words(narration) ?? string.Empty, Amount(credit), Amount(debit));

    /// <summary>The identity fields of a provider line, without the ordinal. A transaction time when the file has one,
    /// otherwise the transaction or settlement date.</summary>
    public static string ProviderPayload(string providerKey, DateTime? txnDateTime, DateOnly? txnDate, DateOnly? settlementDate,
        string? txnReference, string? settlementId, string? invoiceReference, string lineKind, decimal? gross, decimal? net) =>
        Join(providerKey,
            txnDateTime?.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) ?? (txnDate ?? settlementDate)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            Words(txnReference) ?? string.Join('/', new[] { Words(settlementId), Words(invoiceReference) }.Where(part => part is not null)),
            lineKind, Amount(gross), Amount(net));

    /// <summary>The identity of a line that could not be read: unique in its file by its row number and cells.</summary>
    public static string ForUnreadable(string scopeKey, string fileSha256, int rowNumber, string rawJson) =>
        Hash(Join("UNREADABLE", scopeKey, fileSha256, rowNumber.ToString(CultureInfo.InvariantCulture), rawJson));

    public static string Compute(string payload, int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        return Hash(Join(payload, ordinal.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Assigns ordinals in file order: the n-th line with the same payload gets n.</summary>
    public static IReadOnlyList<string> ComputeAll(IEnumerable<string> payloads)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var payload in payloads)
        {
            var ordinal = seen.TryGetValue(payload, out var count) ? count + 1 : 1;
            seen[payload] = ordinal;
            result.Add(Compute(payload, ordinal));
        }
        return result;
    }

    private static string? Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Amount(decimal? value) => value?.ToString("F4", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Join(params string[] parts) => string.Join('|', parts);

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
