using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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

    public static string ContentHash(IEnumerable<KeyValuePair<string, object?>> values) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x =>
                $"{x.Key.Length}:{x.Key}:{Format(x.Value).Length}:{Format(x.Value)}")))));

    private static string Format(object? value) => value switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal d => decimal.Round(d,4,MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? ""
    };

    public static IReadOnlyDictionary<int, string> LineKeys(
        IEnumerable<Etp.Reporting.Import.Staging.StagedImportRow> rows)
    {
        string[] keys = ["store_code", "invoice_year", "invoice_number", "transaction_date", "product_code",
            "source_transaction_type", "source_quantity", "source_net_amount", "source_net_value", "source_tax_amount",
            "cro_number", "scheme_discount", "user_discount", "pre_discount"];
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new Dictionary<int, string>();
        foreach (var row in rows)
        {
            var hash = ContentHash(row.Values.Where(x => keys.Contains(x.Key, StringComparer.Ordinal)));
            occurrences.TryGetValue(hash, out var sequence);
            occurrences[hash] = ++sequence;
            result[row.SourceRowNumber] = $"{hash}:{sequence}";
        }
        return result;
    }
}
