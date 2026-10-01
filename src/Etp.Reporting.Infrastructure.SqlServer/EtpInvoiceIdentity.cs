using System.Globalization;
using Etp.Reporting.Import.Identity;

namespace Etp.Reporting.Infrastructure.SqlServer;

public static class EtpInvoiceIdentity
{
    public static int FinancialYearEnd(DateOnly date) => date.Month >= 4 ? date.Year + 1 : date.Year;

    public static int FinancialYearEnd(DateOnly date, IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue("invoice_year", out var value) && value is not null &&
            int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var year) && year > 1900)
            return year;
        return FinancialYearEnd(date);
    }

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
