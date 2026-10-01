using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Etp.Reporting.Import.Documents;

/// <summary>The unit of truth of a family (spec 3; <c>fact_documents.document_scope</c>).</summary>
public enum DocumentScope
{
    /// <summary>An invoice or stock document, keyed by financial year and number.</summary>
    Document,
    /// <summary>One business day of a landing-only family.</summary>
    Date,
    /// <summary>One store-report-date snapshot.</summary>
    Snapshot,
    /// <summary>One export period of an undated list.</summary>
    Period
}

/// <summary>
/// The identity of one document (spec 7.1): report code (never family code), store and key text.
/// <see cref="Hash"/> is <c>document_key_hash</c>: SHA-256 (UTF-8) of <c>REPORT|STORE|key text</c>.
/// No Descriptive value is ever part of a key, so a key never holds a customer value.
/// </summary>
public sealed record DocumentKey
{
    public DocumentKey(string reportCode, string storeCode, DocumentScope scope, string keyText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyText);
        ReportCode = reportCode.Trim().ToUpperInvariant();
        StoreCode = storeCode.Trim().ToUpperInvariant();
        Scope = scope;
        KeyText = keyText.Trim();
        Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{ReportCode}|{StoreCode}|{KeyText}")));
    }

    public string ReportCode { get; }
    public string StoreCode { get; }
    public DocumentScope Scope { get; }
    /// <summary><c>2027|100000068</c>, <c>2026-08-25</c>, <c>2026-09-29|R010</c> or <c>2024-09-01..2024-11-30</c>.</summary>
    public string KeyText { get; }
    /// <summary><c>document_key_hash</c> as lowercase hex.</summary>
    public string Hash { get; }

    public byte[] HashBytes() => Convert.FromHexString(Hash);

    /// <summary>The financial year that ends in March of the returned year: 1 Apr 2026 to 31 Mar 2027 is 2027 (OD-1).</summary>
    public static int FinancialYearEnd(DateOnly date) => date.Month >= 4 ? date.Year + 1 : date.Year;

    /// <summary><c>{FY}|{UPPER(TRIM(number))}</c> with the financial year of the document's own date (OD-1).</summary>
    public static DocumentKey ForDocument(string reportCode, string storeCode, DateOnly documentDate, string documentNumber) =>
        ForDocument(reportCode, storeCode, FinancialYearEnd(documentDate), documentNumber);

    public static DocumentKey ForDocument(string reportCode, string storeCode, int financialYearEnd, string documentNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        return new(reportCode, storeCode, DocumentScope.Document,
            $"{financialYearEnd.ToString(CultureInfo.InvariantCulture)}|{documentNumber.Trim().ToUpperInvariant()}");
    }

    public static DocumentKey ForDate(string reportCode, string storeCode, DateOnly businessDate) =>
        new(reportCode, storeCode, DocumentScope.Date, Text(businessDate));

    public static DocumentKey ForSnapshot(string reportCode, string storeCode, DateOnly snapshotDate) =>
        new(reportCode, storeCode, DocumentScope.Snapshot, $"{Text(snapshotDate)}|{reportCode.Trim().ToUpperInvariant()}");

    public static DocumentKey ForPeriod(string reportCode, string storeCode, DateOnly from, DateOnly to) =>
        from > to ? throw new ArgumentException("A period cannot end before it starts.", nameof(to))
            : new(reportCode, storeCode, DocumentScope.Period, $"{Text(from)}..{Text(to)}");

    public override string ToString() => $"{ReportCode}|{StoreCode}|{KeyText}";

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
