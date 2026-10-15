namespace Etp.Reporting.Application.Service;

/// <summary>
/// 1.12.1 P8: the pure rules of the "Not matched to any centre" choice of the Service Centre picker (P1 built the picker).
/// A job whose number starts with no Service Centre's prefix (several centres), a parts invoice or a closing-stock row that
/// names no Service Centre, belongs to no centre. The picker keeps them under one extra choice so that the sum of the centres
/// and this choice is the "All Service Centres" total. The choice travels as a store code the catalogue can never hold, so
/// the one session-wide selection of the Service screens (ServiceCentreServices.SelectedStoreCode) needs no second field.
/// </summary>
public static class ServiceCentreScope
{
    /// <summary>The picker's code for rows that match no Service Centre. A store code is letters, digits, '-' and '_'; '~' is none of them.</summary>
    public const string NotMatchedCode = "~NOT_MATCHED";

    /// <summary>The picker's text for the choice.</summary>
    public const string NotMatchedLabel = "Not matched to any centre";

    /// <summary>True when <paramref name="storeCode"/> is the "Not matched" choice (trimmed, ignoring case).</summary>
    public static bool IsNotMatched(string? storeCode) =>
        string.Equals(storeCode?.Trim(), NotMatchedCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>The choice exists only while there are two or more Service Centres (with one, every row is that centre's).</summary>
    public static bool OffersNotMatched(int centreCount) => centreCount >= 2;

    /// <summary>The line under the title while one centre is chosen, with the 1.12.1 P8 coverage: jobs, claims, parts invoices and closing stock follow the choice.</summary>
    public static string ScopeNote(string centreName) =>
        $"Jobs, claims, parts invoices and closing stock are for {centreName}. Money, goods in transit and file dates are for all Service Centres.";

    /// <summary>The same line for the "Not matched" choice.</summary>
    public static string NotMatchedScopeNote() =>
        "Showing jobs, parts invoices and closing stock that match no Service Centre. Money, goods in transit and file dates are for all Service Centres.";
}
