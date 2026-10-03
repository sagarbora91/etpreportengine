using System.Globalization;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// How precisely an export time is known (spec 6.3). <see cref="Unknown"/> is the default, so an export
/// time nobody set never counts as known.
/// </summary>
public enum ExportBasis { Unknown, Minute, Second, Date }

/// <summary>How export <c>a</c> relates to export <c>b</c> (<see cref="ExportOrder.Compare"/>).</summary>
public enum ExportOrderResult { Unknown, Older, Same, Newer }

/// <summary>
/// When ETP produced an export, in IST as written in the file name or contract. Times are never converted
/// and never estimated: an unknown time stays <see cref="Unknown"/>.
/// </summary>
public readonly record struct ExportTime
{
    private ExportTime(DateTime instant, ExportBasis basis)
    {
        Instant = DateTime.SpecifyKind(instant, DateTimeKind.Unspecified);
        Basis = basis;
    }

    /// <summary>The time, truncated to the basis; for <see cref="ExportBasis.Date"/> midnight of the date. Null when unknown.</summary>
    public DateTime? Instant { get; }
    public ExportBasis Basis { get; }

    public static ExportTime Unknown => default;
    public bool IsKnown => Basis != ExportBasis.Unknown;
    public DateOnly? ExportDate => Instant is { } instant ? DateOnly.FromDateTime(instant) : null;

    public static ExportTime AtMinute(DateTime value) =>
        new(new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0), ExportBasis.Minute);

    public static ExportTime AtSecond(DateTime value) =>
        new(new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second), ExportBasis.Second);

    public static ExportTime OnDate(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), ExportBasis.Date);

    /// <summary>Rebuilds a stored time (<c>export_time</c>, <c>export_time_basis</c>).</summary>
    public static ExportTime FromStored(DateTime? instant, ExportBasis basis)
    {
        if (basis == ExportBasis.Unknown) return Unknown;
        if (instant is not { } value) throw new ArgumentException("A known export time needs its instant.", nameof(instant));
        return basis switch
        {
            ExportBasis.Minute => AtMinute(value),
            ExportBasis.Second => AtSecond(value),
            ExportBasis.Date => OnDate(DateOnly.FromDateTime(value)),
            _ => throw new ArgumentOutOfRangeException(nameof(basis))
        };
    }

    /// <summary>
    /// Reads a contract <c>export_time</c> cell (contract 3.3): <c>yyyy-MM-ddTHH:mm</c>, <c>yyyy-MM-ddTHH:mm:ss</c>
    /// or <c>yyyy-MM-dd</c>, with no offset. A blank or any other text is not a time.
    /// </summary>
    public static bool TryParseContract(string? text, out ExportTime time)
    {
        time = Unknown;
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value)) return false;
        var culture = CultureInfo.InvariantCulture;
        if (DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm", culture, DateTimeStyles.None, out var minute))
            time = AtMinute(minute);
        else if (DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss", culture, DateTimeStyles.None, out var second))
            time = AtSecond(second);
        else if (DateOnly.TryParseExact(value, "yyyy-MM-dd", culture, DateTimeStyles.None, out var date))
            time = OnDate(date);
        return time.IsKnown;
    }

    /// <summary>The contract spelling of this time; null when unknown.</summary>
    public string? ToContractText() => Basis switch
    {
        ExportBasis.Minute => Instant!.Value.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
        ExportBasis.Second => Instant!.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        ExportBasis.Date => Instant!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => null
    };

    public override string ToString() => ToContractText() is { } text ? $"{text} ({Basis})" : "unknown";
}
