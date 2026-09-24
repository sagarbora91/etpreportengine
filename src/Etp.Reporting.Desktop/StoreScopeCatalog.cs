using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop;

public sealed class StoreScopeCatalog
{
    public const string AllStores = "All stores";
    public IReadOnlyList<StoreCatalogEntry> Stores { get; private set; } = [];
    public void Replace(IEnumerable<StoreCatalogEntry> stores) => Stores = stores.Where(x => x.IsActive).ToArray();
    public static string Label(StoreCatalogEntry store) => store.Name == store.Code ? store.Code : $"{store.Name} ({store.Code})";
    public string[] Labels => Stores.Select(Label).ToArray();
    public string? Resolve(string? value)
    {
        if (value?.StartsWith("Custom: ", StringComparison.Ordinal) == true) return value[8..];
        var matches = Stores.Where(x => string.Equals(x.Code, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Label(x), value, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0].Code : null;
    }
    public string Display(string? code) => code?.Contains(',') == true ? "Custom: " + code
        : Stores.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)) is { } store ? Label(store)
        : string.IsNullOrWhiteSpace(code) ? AllStores : "Custom: " + code;
}
