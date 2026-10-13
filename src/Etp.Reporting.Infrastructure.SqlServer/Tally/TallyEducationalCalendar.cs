using System.Globalization;
using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Decision 49 (1): the VM's TallyPrime runs in EDUCATIONAL mode, which accepts vouchers only on some days of each
/// month: assumed the 1st, 2nd and 31st until the VM visit verifies it, so the days are a setting, not a rule baked in.
/// A company counts as Educational when its build label (Settings → Tally companies) contains "EDUCATIONAL".</summary>
public sealed class TallyEducationalCalendar
{
    public static TallyEducationalCalendar Default { get; } = new([1, 2, 31]);

    public TallyEducationalCalendar(IReadOnlyList<int> allowedDays)
    {
        ArgumentNullException.ThrowIfNull(allowedDays);
        if (allowedDays.Count == 0 || allowedDays.Any(day => day is < 1 or > 31))
            throw new ArgumentException("Give at least one day of the month from 1 to 31.", nameof(allowedDays));
        AllowedDays = allowedDays.Distinct().Order().ToArray();
    }

    public IReadOnlyList<int> AllowedDays { get; }

    public static bool AppliesTo(TallyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.TallyBuildLabel?.Contains("EDUCATIONAL", StringComparison.OrdinalIgnoreCase) == true;
    }

    public bool IsAllowed(DateOnly date) => AllowedDays.Contains(date.Day);

    /// <summary>The first allowed date on or after <paramref name="from"/>.</summary>
    public DateOnly NextAllowed(DateOnly from)
    {
        for (var date = from; date <= from.AddDays(62); date = date.AddDays(1))
            if (IsAllowed(date)) return date;
        throw new InvalidOperationException("No allowed day within two months; check the allowed days.");
    }

    /// <summary>Every allowed date from <paramref name="from"/> to <paramref name="to"/>, oldest first.</summary>
    public IReadOnlyList<DateOnly> AllowedBetween(DateOnly from, DateOnly to)
    {
        var result = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
            if (IsAllowed(date)) result.Add(date);
        return result;
    }

    /// <summary>Null when every date is allowed; otherwise the sentence to show, naming the dates Tally would refuse.</summary>
    public string? Refusal(IEnumerable<DateOnly> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var refused = dates.Where(date => !IsAllowed(date)).Distinct().Order().ToArray();
        if (refused.Length == 0) return null;
        return "TallyPrime in Educational mode accepts vouchers only on day(s) "
               + string.Join(", ", AllowedDays.Select(day => day.ToString(CultureInfo.InvariantCulture)))
               + " of a month, so it would refuse "
               + string.Join(", ", refused.Take(5).Select(date => date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)))
               + (refused.Length > 5 ? " and more" : "")
               + ". Use test data on an allowed day.";
    }
}
