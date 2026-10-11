using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.HealthCheck;

/// <summary>One in-memory sample: the family it stands for and the workbook built from the family's own headers.</summary>
public sealed record SyntheticFamilySample(EtpReportFamily Family, WorkbookSnapshot Workbook);

/// <summary>
/// What recognising one sample gave. <see cref="MatchedReportCode"/> is the profile the preflight chose (null when none);
/// <see cref="Blockers"/> are the Blocker diagnostic codes (codes only, never messages).
/// </summary>
public sealed record SyntheticFamilyCheck(
    string FamilyCode,
    string ReportCode,
    string? MatchedReportCode,
    IReadOnlyList<string> Blockers)
{
    /// <summary>True when the sample matched its own family's profile and nothing blocked it.</summary>
    public bool Recognised => Blockers.Count == 0 && string.Equals(MatchedReportCode, ReportCode, StringComparison.Ordinal);
}

/// <summary>
/// Health check IMP-01 (design 5.2): for every family of <see cref="EtpReportFamilyRegistry"/> an in-memory workbook with the
/// family's own header list and one synthetic row, run through <see cref="ImportPreflight"/> against the approved profiles.
/// A layout change that breaks recognition (a header edited, a profile altered) shows here before a real export is refused.
/// <para>Nothing is written to disk and nothing reaches persistence. Values are fixed and invented: store <c>ZZSYN</c>,
/// dates 2001-01-01, item <c>SYN-0001</c>, amounts 1.00. No customer, store or item of the business appears.</para>
/// <para>The file name carries the family code (<c>ETP_R025_SYN.xlsx</c>), the same naming rule the folder import uses to
/// settle families that share a header signature (S001, S014-S018, S031-S035 share one). A family whose code is not an
/// R or S code (SOR_AGEING) is named by its catalogue name.</para>
/// <para>D001 (the daily-inputs sheet) is not in the registry and has no header profile: it is recognised by content
/// (<see cref="DailyInputsTemplate.IsDailyInputsWorkbook"/>). <see cref="CreateDailyInputsSample"/> and
/// <see cref="CheckDailyInputs"/> cover it for callers that want it as an extra sub-result.</para>
/// </summary>
public static class SyntheticFamilySamples
{
    public const string StoreCode = "ZZSYN", ItemCode = "SYN-0001", TextValue = "SYN";
    private static readonly DateTime SyntheticDate = new(2001, 1, 1);

    /// <summary>The fixed sample file name of a family.</summary>
    public static string FileNameFor(EtpReportFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return $"ETP_{FamilyNameToken(family)}_SYN.xlsx";
    }

    /// <summary>One sample per registered family, in registry order (72 in 1.11.0).</summary>
    public static IReadOnlyList<SyntheticFamilySample> CreateAll() => CreateAll(EtpReportFamilyRegistry.Families);

    public static IReadOnlyList<SyntheticFamilySample> CreateAll(IEnumerable<EtpReportFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        return families.Select(Create).ToArray();
    }

    /// <summary>The sample of one family: one data sheet, the family's headers, one invented row.</summary>
    public static SyntheticFamilySample Create(EtpReportFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        var cells = family.Headers.Select(header => new WorkbookCell(ValueFor(family, header))).ToArray();
        var sheet = new WorkbookSheet("Sheet0", 1, family.Headers.ToArray(), [new WorkbookRow(2, cells)]);
        var fileName = FileNameFor(family);
        return new SyntheticFamilySample(family, new WorkbookSnapshot(fileName, 1024, Sha256Of(fileName), [sheet]));
    }

    /// <summary>Recognises every sample against <paramref name="profiles"/> (default: the approved registry).</summary>
    public static IReadOnlyList<SyntheticFamilyCheck> CheckAll(IReadOnlyList<ImportProfile>? profiles = null) =>
        CheckAll(CreateAll(), profiles);

    public static IReadOnlyList<SyntheticFamilyCheck> CheckAll(IEnumerable<SyntheticFamilySample> samples, IReadOnlyList<ImportProfile>? profiles = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var approved = profiles ?? ApprovedImportProfileRegistry.All;
        var preflight = new ImportPreflight();
        return samples.Select(sample => Check(preflight, sample, approved)).ToArray();
    }

    private static SyntheticFamilyCheck Check(ImportPreflight preflight, SyntheticFamilySample sample, IReadOnlyList<ImportProfile> profiles)
    {
        var result = preflight.Inspect(sample.Workbook, profiles);
        var blockers = result.Diagnostics.Where(d => d.Severity == ImportDiagnosticSeverity.Blocker)
            .Select(d => d.Code).Distinct(StringComparer.Ordinal).ToArray();
        return new SyntheticFamilyCheck(sample.Family.FamilyCode, sample.Family.ReportCode, result.Profile?.ReportCode, blockers);
    }

    /// <summary>A minimal daily-inputs sheet (retail, store ZZSYN, 2001-01-01); enough for content recognition, not for a parse.</summary>
    public static WorkbookSnapshot CreateDailyInputsSample()
    {
        string[] headers = [.. DailyInputsTemplate.Headers];
        static WorkbookRow Row(int number, string key, string input, string value) =>
            new(number, [new WorkbookCell(key), new WorkbookCell(input), new WorkbookCell(value), new WorkbookCell(null)]);
        var rows = new[]
        {
            Row(2, DailyInputsTemplate.TemplateKey, "Template version", DailyInputsTemplate.CurrentVersion),
            Row(3, DailyInputsTemplate.KindKey, "Sheet type", DailyInputsTemplate.RetailText),
            Row(4, DailyInputsTemplate.StoreKey, "Store", StoreCode),
            Row(5, DailyInputsTemplate.BusinessDateKey, "Business date", SyntheticDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        };
        var fileName = DailyInputsTemplate.FileName(StoreCode, DailyInputsKind.Retail, DateOnly.FromDateTime(SyntheticDate));
        return new WorkbookSnapshot(fileName, 1024, Sha256Of(fileName),
            [new WorkbookSheet(DailyInputsTemplate.SheetName, 1, headers, rows)]);
    }

    /// <summary>True when the sample is recognised as the daily-inputs family D001 (by content, never by file name).</summary>
    public static bool CheckDailyInputs() => DailyInputsTemplate.IsDailyInputsWorkbook(CreateDailyInputsSample());

    // "_R025_" matches the family-code pattern, "_SOR_AGEING_" does not; the latter is then named by the catalogue name.
    private static string FamilyNameToken(EtpReportFamily family) =>
        EtpReportFamilyRegistry.FamilyCodePattern.IsMatch($"_{family.FamilyCode}_") ? family.FamilyCode : family.Name;

    private static object? ValueFor(EtpReportFamily family, string header)
    {
        var column = family.Columns.FirstOrDefault(c => string.Equals(c.SourceHeader, header, StringComparison.OrdinalIgnoreCase));
        if (column is null) return TextValue;
        if (column.CanonicalField.Equals("store_code", StringComparison.OrdinalIgnoreCase)) return StoreCode;
        return column.DataType switch
        {
            CanonicalDataType.Date => SyntheticDate,
            CanonicalDataType.Decimal => 1.00m,
            CanonicalDataType.Integer => 1m,
            CanonicalDataType.Boolean => false,
            CanonicalDataType.Identifier => ItemCode,
            _ => TextValue
        };
    }

    private static string Sha256Of(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
