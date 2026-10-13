using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop;

public sealed class StoreScopeCatalog
{
    public const string AllStores = "All stores";
    /// <summary>Active shop stores: every store picker and Retail scope. Never a Service Centre store.</summary>
    public IReadOnlyList<StoreCatalogEntry> Stores { get; private set; } = [];
    /// <summary>
    /// Service Centre stores (0048 and 1.12 A4: inactive, of a SERVICE unit; one or more). Kept for display only, so that
    /// Import History names Service rows "Service Centre (code)" instead of "Custom: code".
    /// </summary>
    public IReadOnlyList<StoreCatalogEntry> ServiceStores { get; private set; } = [];
    public void Replace(IEnumerable<StoreCatalogEntry> stores)
    {
        var all = stores.ToArray();
        Stores = all.Where(x => x.IsActive && !x.IsServiceCentre).ToArray();
        ServiceStores = all.Where(x => x.IsServiceCentre).ToArray();
    }
    public static string Label(StoreCatalogEntry store) => store.IsServiceCentre ? ServiceCentreStores.Label(store.Code)
        : store.Name == store.Code ? store.Code : $"{store.Name} ({store.Code})";
    public string[] Labels => Stores.Select(Label).ToArray();
    public string? Resolve(string? value)
    {
        if (value?.StartsWith("Custom: ", StringComparison.Ordinal) == true) return value[8..];
        var matches = Stores.Where(x => string.Equals(x.Code, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Label(x), value, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1) return matches[0].Code;
        // Display gives a Service store its label instead of "Custom: code"; the label resolves
        // back to the code, as the Custom form did. A bare Service code still resolves to nothing.
        return matches.Length == 0 && ServiceStore(value, byLabel: true) is { } service ? service.Code : null;
    }
    public string Display(string? code) => code?.Contains(',') == true ? "Custom: " + code
        : Stores.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)) is { } store ? Label(store)
        : ServiceStore(code, byLabel: false) is { } service ? Label(service)
        : string.IsNullOrWhiteSpace(code) ? AllStores : "Custom: " + code;
    /// <summary>True when the scope text is a Service store's label, which a store picker shows like a Custom scope.</summary>
    public bool IsServiceLabel(string? value) => ServiceStore(value, byLabel: true) is not null;
    /// <summary>
    /// The Store column of Import History: a Service store's label, or the code unchanged, so
    /// Retail rows read exactly as before.
    /// </summary>
    public string? HistoryStore(string? code) => ServiceStore(code, byLabel: false) is { } service ? Label(service) : code;
    private StoreCatalogEntry? ServiceStore(string? value, bool byLabel) => string.IsNullOrWhiteSpace(value) ? null
        : ServiceStores.FirstOrDefault(x => byLabel
            ? string.Equals(Label(x), value, StringComparison.OrdinalIgnoreCase)
            : string.Equals(x.Code, value.Trim(), StringComparison.OrdinalIgnoreCase));
}
