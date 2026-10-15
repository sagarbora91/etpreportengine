extern alias EtpApplication;

using ServiceCentre = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentre;
using ServiceCentreRules = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentreRules;
using ServiceCentreScope = EtpApplication::Etp.Reporting.Application.Service.ServiceCentreScope;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// 1.12.1 P8: the Service Centre picker's extra choice, "Not matched to any centre". P1's <c>ServiceCentreServices.Choices</c> lists
/// "All Service Centres" and each centre; <see cref="WithNotMatched"/> appends this one while there are two or more centres,
/// and <see cref="Resolve"/> reads the session's selected code as a centre, the not-matched choice or all centres.
/// </summary>
public static class ServiceCentreChoices
{
    /// <summary>The picker's choices with "Not matched to any centre" last (only with two or more centres).</summary>
    public static IReadOnlyList<ServiceListChoice> WithNotMatched(IReadOnlyList<ServiceListChoice> choices, int centreCount)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return ServiceCentreScope.OffersNotMatched(centreCount) && choices.Count > 0
            ? [.. choices, new ServiceListChoice(ServiceCentreScope.NotMatchedCode, ServiceCentreScope.NotMatchedLabel)]
            : choices;
    }

    /// <summary>What a selected store code means for these centres.</summary>
    public static ServiceCentreSelection Resolve(IReadOnlyList<ServiceCentre> centres, string? selectedStoreCode)
    {
        ArgumentNullException.ThrowIfNull(centres);
        if (centres.Count < 2) return ServiceCentreSelection.All;
        if (ServiceCentreScope.IsNotMatched(selectedStoreCode))
            return new ServiceCentreSelection(null, true, ServiceCentreScope.NotMatchedCode, ServiceCentreScope.NotMatchedLabel);
        var chosen = ServiceCentreServices.Chosen(centres, selectedStoreCode);
        return chosen is null
            ? ServiceCentreSelection.All
            : new ServiceCentreSelection(chosen, false, chosen.StoreCode, ServiceCentreRules.DisplayName(chosen));
    }
}

/// <summary>The Service Centre a screen shows: all, one centre, or the rows that match none.</summary>
public sealed record ServiceCentreSelection(ServiceCentre? Centre, bool NotMatched, string? StoreCode, string Name)
{
    /// <summary>Every Service Centre (no filter).</summary>
    public static ServiceCentreSelection All { get; } = new(null, false, null, "");

    /// <summary>True when the screen is filtered (one centre or "Not matched").</summary>
    public bool IsFiltered => StoreCode is not null;
}
