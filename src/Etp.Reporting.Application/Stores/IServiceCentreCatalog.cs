using System.Text.RegularExpressions;

namespace Etp.Reporting.Application.Stores;

// 1.12 wave 2, lane A4 (contract frozen in 112-W2-PLAN.md section 4): stores, brands and the Service Centre are data.
// A Service Centre is a store of a business unit whose unit_kind is SERVICE (0067); its job numbers start with the store's
// service_job_prefix. No store code is written in C#: everything below reads the catalogue.

/// <summary>
/// One Service Centre: its store code, its catalogue name and the prefix of its job order numbers (0067
/// <c>stores.service_job_prefix</c>, for example <c>JO</c> followed by the store code).
/// </summary>
public sealed record ServiceCentre(string StoreCode, string Name, string JobNumberPrefix)
{
    /// <summary>The shape of a well-formed job order number: the prefix and 8 or 9 digits.</summary>
    public string JobNumberPattern => $"^{JobNumberPrefix}[0-9]{{8,9}}$";
}

/// <summary>
/// The Service Centres of this database. Infrastructure: <c>SqlServerServiceCentreCatalog(IEtpConnectionFactory)</c> reads
/// <c>business_units.unit_kind='SERVICE'</c> (COL_LENGTH-guarded: on a database before 0067 it returns the one legacy
/// service centre of the SERVICE business unit, with the prefix <c>JO</c> plus its store code).
/// </summary>
public interface IServiceCentreCatalog
{
    Task<IReadOnlyList<ServiceCentre>> LoadAsync(CancellationToken ct = default);
}

/// <summary>A catalogue over a fixed list (tests, fakes, and a screen that already holds the list).</summary>
public sealed class FixedServiceCentreCatalog(IEnumerable<ServiceCentre> centres) : IServiceCentreCatalog
{
    private readonly IReadOnlyList<ServiceCentre> centres = [.. centres ?? throw new ArgumentNullException(nameof(centres))];

    public Task<IReadOnlyList<ServiceCentre>> LoadAsync(CancellationToken ct = default) => Task.FromResult(centres);
}

/// <summary>
/// Where a Service file that names no store (S011, S013, a lone file) goes: the only Service Centre, or a plain refusal
/// when there is none or more than one (then the file must name its store).
/// </summary>
public sealed record ServiceCentreDefault(string? StoreCode, string? RefusalCode, string? RefusalMessage)
{
    public bool IsRefused => StoreCode is null;
}

/// <summary>The pure Service Centre rules (no I/O): the default store, job-number matching, prefix checks and screen names.</summary>
public static class ServiceCentreRules
{
    /// <summary>The longest prefix a job number may have (0067 CHECK on <c>stores.service_job_prefix</c>).</summary>
    public const int MaxPrefixLength = 20;

    /// <summary>The shortest prefix: two characters, so a prefix never matches nearly every number.</summary>
    public const int MinPrefixLength = 2;

    /// <summary>A Service file names no store and two or more Service Centres exist.</summary>
    public const string StoreAmbiguousCode = "SERVICE_STORE_AMBIGUOUS";

    /// <summary>A Service file names no store and no Service Centre is set up.</summary>
    public const string StoreNotSetUpCode = "SERVICE_STORE_NOT_SET_UP";

    public const string StoreAmbiguousMessage =
        "This Service file names no store, and there are several Service Centres. Keep it beside a file of its Service Centre, or export it with its store.";

    public const string StoreNotSetUpMessage =
        "This Service file names no store, and no Service Centre store is set up. Add it in Settings > Stores, then import the file again.";

    /// <summary>
    /// The Information note when the only Service Centre was used (SERVICE_STORE_DEFAULTED). A fixed text, so the import
    /// diagnostic catalogue can hold it exactly; the store code is on the file row itself.
    /// </summary>
    public const string StoreDefaultedMessage =
        "This Service Centre file names no store and no file beside it does, so it is imported under the only Service Centre.";

    /// <summary>The only Service Centre, or a refusal (none, or two or more).</summary>
    public static ServiceCentreDefault DefaultFor(IReadOnlyList<ServiceCentre> centres)
    {
        ArgumentNullException.ThrowIfNull(centres);
        return centres.Count switch
        {
            1 => new(centres[0].StoreCode, null, null),
            0 => new(null, StoreNotSetUpCode, StoreNotSetUpMessage),
            _ => new(null, StoreAmbiguousCode, StoreAmbiguousMessage)
        };
    }

    /// <summary>The Service Centre whose prefix starts the job number (the longest prefix wins), or null.</summary>
    public static ServiceCentre? CentreForJob(IReadOnlyList<ServiceCentre> centres, string? jobOrderNumber)
    {
        ArgumentNullException.ThrowIfNull(centres);
        if (string.IsNullOrWhiteSpace(jobOrderNumber)) return null;
        var key = jobOrderNumber.Trim();
        return centres.Where(centre => !string.IsNullOrWhiteSpace(centre.JobNumberPrefix) &&
                key.StartsWith(centre.JobNumberPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(centre => centre.JobNumberPrefix.Length).FirstOrDefault();
    }

    /// <summary>True when the trimmed key is a prefix of one of the centres followed by 8 or 9 digits.</summary>
    public static bool IsWellFormedJobNumber(IReadOnlyList<ServiceCentre> centres, string? jobOrderNumber)
    {
        ArgumentNullException.ThrowIfNull(centres);
        if (string.IsNullOrWhiteSpace(jobOrderNumber)) return false;
        var key = jobOrderNumber.Trim();
        return centres.Any(centre => IsValidPrefix(centre.JobNumberPrefix) &&
            Regex.IsMatch(key, centre.JobNumberPattern, RegexOptions.CultureInvariant));
    }

    /// <summary>The prefix the store editor offers for a new Service Centre: <c>JO</c> and the store code.</summary>
    public static string SuggestedPrefix(string storeCode) =>
        string.IsNullOrWhiteSpace(storeCode) ? "" : "JO" + storeCode.Trim().ToUpperInvariant();

    /// <summary>Two to twenty capital letters A-Z or digits (the 0067 CHECK).</summary>
    public static bool IsValidPrefix(string? prefix) =>
        prefix is { Length: >= MinPrefixLength and <= MaxPrefixLength } && prefix.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');

    /// <summary>The store editor's sentence for a prefix that is not valid, or null when it is.</summary>
    public static string? PrefixProblem(string? prefix, IReadOnlyList<ServiceCentre> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        var value = prefix?.Trim() ?? "";
        if (value.Length == 0) return "Enter the job-number prefix of this Service Centre, for example JO and the store code.";
        if (!IsValidPrefix(value))
            return $"The job-number prefix uses capital letters A-Z and digits only, {MinPrefixLength} to {MaxPrefixLength} characters.";
        var clash = others.FirstOrDefault(centre => !string.IsNullOrWhiteSpace(centre.JobNumberPrefix) &&
            (centre.JobNumberPrefix.StartsWith(value, StringComparison.Ordinal) || value.StartsWith(centre.JobNumberPrefix, StringComparison.Ordinal)));
        return clash is null ? null : $"The job-number prefix overlaps the prefix of {DisplayName(clash)}. Each Service Centre needs its own prefix.";
    }

    /// <summary>The catalogue name of a centre, or "Service Centre" and its code when it has no name.</summary>
    public static string DisplayName(ServiceCentre centre)
    {
        ArgumentNullException.ThrowIfNull(centre);
        return string.IsNullOrWhiteSpace(centre.Name) ? $"Service Centre {centre.StoreCode.Trim()}" : centre.Name.Trim();
    }

    /// <summary>
    /// The name the Service screens show beside their title: the only centre's catalogue name, "Service Centre" when none is
    /// set up (or the catalogue could not be read), and "All Service Centres" when there are several.
    /// </summary>
    public static string ScreenLabel(IReadOnlyList<ServiceCentre>? centres) => centres switch
    {
        null or { Count: 0 } => "Service Centre",
        { Count: 1 } => DisplayName(centres[0]),
        _ => "All Service Centres"
    };
}

/// <summary>
/// Settings > Stores, Owner only (1.12 A4): adds a Service Centre store or changes its name and job-number prefix. The store
/// is placed in the Service business unit and stays inactive (it is not a shop store). A refusal is an
/// <see cref="InvalidOperationException"/> (or <see cref="UnauthorizedAccessException"/>) with a plain sentence.
/// </summary>
public interface IServiceCentreAdministration
{
    Task SaveServiceCentreAsync(string storeCode, string name, string jobNumberPrefix, string reason, CancellationToken ct = default);
}
