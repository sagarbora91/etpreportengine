using System.Collections.ObjectModel;
using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public static class ApprovedImportProfileRegistry
{
    private static readonly ReadOnlyCollection<ImportProfile> Profiles = Array.AsReadOnly(new[]
    {
        RetailSalesProfiles.R025,
        RetailSalesProfiles.R022,
        RetailSalesProfiles.R013,
        RetailSalesProfiles.R003,
        StockImportProfiles.VariantStockLedger,
        StockImportProfiles.ClosingStock
    });

    public static IReadOnlyList<ImportProfile> All => Profiles;

    public static ImportProfile Resolve(ImportProfileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var matches = All.Where(profile => profile.Identity == identity).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                "The matched import profile identity is not present in the approved profile registry."),
            _ => throw new InvalidOperationException(
                "The approved profile registry contains a duplicate import profile identity.")
        };
    }
}
