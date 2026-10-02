using System.Globalization;
using Etp.Reporting.Import.Identity;

namespace Etp.Reporting.Infrastructure.SqlServer;

public static class EtpInvoiceIdentity
{
    /// <summary>
    /// The financial year of an invoice or stock document's own date, the one year rule for every family (OD-1).
    /// ETP's INVOICEYEAR is only a label: a return dated 1 April carries the year before, and keying by it split
    /// R022 from R025's header (IF-019). <see cref="Etp.Reporting.Import.Staging.InvoiceYearLabels"/> reports the
    /// rows whose label differs.
    /// </summary>
    public static int FinancialYearEnd(DateOnly date) => Etp.Reporting.Import.Documents.DocumentKey.FinancialYearEnd(date);

    // The text rules live in the one canonicaliser shared with planner 2 (spec 7.1).
    public static string ContentHash(IEnumerable<KeyValuePair<string, object?>> values) => FactCanonicalizer.Instance.Hash(values);

    public static IReadOnlyDictionary<int, string> LineKeys(
        IEnumerable<Etp.Reporting.Import.Staging.StagedImportRow> rows)
    {
        var staged = rows.ToArray();
        var labels = PlannerOneLabels.LineKeys(FactCanonicalizer.Instance, staged.Select(row => row.Values));
        var result = new Dictionary<int, string>();
        for (var i = 0; i < staged.Length; i++) result[staged[i].SourceRowNumber] = labels[i];
        return result;
    }
}
