extern alias EtpApplication;

using System.Globalization;
using Etp.Reporting.Reporting;
using EtpApplication::Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>One line of "Collections: expected vs evidence" as the grid shows it. The money columns go through
/// <see cref="IndianFormat"/> (headers carry the rupee sign once); a missing figure is an em dash, never 0.</summary>
public sealed class StatementRow(CollectionsStatementLine line, bool hideBankReference)
{
    public CollectionsStatementLine Line { get; } = line;
    public string Store => Line.StoreCode;
    public string Date => IndianFormat.Date(Line.BusinessDate);
    public string Mode => Line.Mode;
    public string Provider => Line.ProviderKey ?? CollectionsScreens.MissingValue;
    public string Expected => IndianFormat.MoneyCell(Line.Expected);
    public string Evidence => IndianFormat.MoneyCell(Line.Evidence);
    public string Difference => IndianFormat.MoneyCell(Line.Difference);

    /// <summary>Cash: the day's cash tenders, for information (the deposit is what is compared).</summary>
    public string CashTenders => IndianFormat.MoneyCell(Line.TenderTotal);

    /// <summary>The bank's reference. Empty for a Viewer: it is not even read for that role.</summary>
    public string BankReference => hideBankReference ? string.Empty : Line.BankReference ?? CollectionsScreens.MissingValue;

    /// <summary>The state as a word (ST1); the colour is only the second channel. "Totals only, not matched yet" is a word on the screen on purpose.</summary>
    public string StatusWord => Line.State switch
    {
        CollectionsLineState.DepositCredited => "Bank credited",
        CollectionsLineState.DepositCreditedDifferentAmount => "Credited, different amount",
        CollectionsLineState.DepositInTransit => "In transit",
        CollectionsLineState.CreditedNotOnSheet => "Credited, not on sheet",
        CollectionsLineState.NoDepositRecorded => "No deposit recorded",
        CollectionsLineState.TotalsOnly => CollectionsStatementBuilder.TotalsOnlyText,
        CollectionsLineState.NoSettlementLinesYet => "No settlement lines yet",
        CollectionsLineState.SettlementWithoutSales => "Settlement without sales",
        CollectionsLineState.NoOutsideEvidenceExpected => "No outside evidence expected",
        CollectionsLineState.ProviderNotSetUp => CollectionsScreens.ProviderNotSetUpChip,
        _ => "Payment code not mapped"
    };

    public StatusTone Tone => Line.State switch
    {
        CollectionsLineState.DepositCredited => StatusTone.Success,
        CollectionsLineState.TotalsOnly => StatusTone.Information,
        CollectionsLineState.DepositInTransit or CollectionsLineState.DepositCreditedDifferentAmount or CollectionsLineState.CreditedNotOnSheet
            or CollectionsLineState.SettlementWithoutSales or CollectionsLineState.ProviderNotSetUp => StatusTone.Warning,
        CollectionsLineState.TenderCodeNotMapped => StatusTone.Critical,
        _ => StatusTone.Neutral
    };

    /// <summary>The report's own sentence (<see cref="CollectionsStatementLine.ShownAs"/>), shown as it is, except that the
    /// internal sample-set reference "(D19)" is not part of a sentence a person reads.</summary>
    public string Detail => Line.ShownAs.Replace(" (D19)", string.Empty, StringComparison.Ordinal);

    public static bool IsTotalsOnly(CollectionsStatementLine line) => line.State == CollectionsLineState.TotalsOnly;
}

/// <summary>
/// Reports > Collections: expected vs evidence (Report template; plan U4): store x date x mode against the evidence ETP
/// holds. The report is "totals only, not matched yet" and says so in a visible word and notice. Every role may open it;
/// the Viewer's statement arrives <see cref="CollectionsStatement.WithoutBankReferences"/> and the bank-reference column
/// is not shown. Pure presentation logic over <see cref="ICollectionsStatementQuery"/>.
/// </summary>
public sealed class CollectionsStatementModel
{
    public const string AllModes = "All modes";
    public const string TotalsOnlyNoticeTitle = "Totals only, not matched yet";
    public const string TotalsOnlyNoticeBody = "UPI and card figures compare the day's total with the provider's settlement total. Payments are not matched one by one yet.";
    public const string NoLinesText = "Nothing to show for these dates. Import the daily sheets, or bank and settlement files, first.";
    public const string HiddenReferencesText = "Bank references are not shown for your role.";
    public const string ExportReportName = "Collections: expected vs evidence";
    public const string ExportRuleVersion = "collections-statement-1";
    public const string ExportWithoutBankColumnsText = "The bank columns (credited on, bank reference) are for the Owner and are not in this file.";
    private const string MoneyFormat = "#,##0.00";
    public const int MaximumDays = 366;
    private readonly Func<ICollectionsStatementQuery> query;
    private readonly Func<DateOnly> today;
    private CollectionsStatement? statement;
    private string modeFilter = AllModes;
    private int revision;

    public CollectionsStatementModel(Func<ICollectionsStatementQuery> query, ShellAccess access, string? storeCode, DateOnly businessDate, Func<DateOnly>? today = null)
    {
        this.query = query ?? throw new ArgumentNullException(nameof(query));
        Access = access ?? throw new ArgumentNullException(nameof(access));
        this.today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
        StoreCode = string.IsNullOrWhiteSpace(storeCode) ? null : storeCode.Trim().ToUpperInvariant();
        To = businessDate;
        From = new DateOnly(businessDate.Year, businessDate.Month, 1);
    }

    public ShellAccess Access { get; }
    public string? StoreCode { get; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public bool IsAllowed => CollectionsScreens.CanReadStatement(Access);
    public string? RefusalText => IsAllowed ? null : CollectionsScreens.NotSignedInText;
    public string ScopeText => $"{IndianFormat.DateRange(From, To)} · {(StoreCode is null ? "All stores" : StoreCode)}";
    public bool HasLoaded => statement is not null;
    public string StatusText { get; private set; } = string.Empty;
    public StatusTone StatusTone { get; private set; } = StatusTone.Neutral;
    public IReadOnlyList<StatementRow> Rows { get; private set; } = [];

    /// <summary>True when the role may not see bank references: the column is left out, not blanked.</summary>
    public bool WithoutBankReferences => statement?.WithoutBankReferences ?? !(Access.CanImport && Access.HasAssignedRole);

    public bool ShowsTotalsOnlyNotice => Rows.Any(row => StatementRow.IsTotalsOnly(row.Line));
    public IReadOnlyList<string> Modes => [AllModes, .. (statement?.Lines.Select(line => line.Mode.Split(" + ")[0]).Distinct(StringComparer.Ordinal) ?? [])];

    public string ModeFilter
    {
        get => modeFilter;
        set { modeFilter = string.IsNullOrWhiteSpace(value) ? AllModes : value; Rebuild(); }
    }

    public event Action? Changed;

    /// <summary>The page footer: the counts and the unit, once (ST6).</summary>
    public string FooterText => statement is null ? string.Empty
        : $"{IndianFormat.Plural(Rows.Count, "line")} · All amounts in INR" + (WithoutBankReferences ? " · " + HiddenReferencesText : string.Empty);

    /// <summary>The dates are usable: the end is not before the start, and the span is at most a year (the service says the same).</summary>
    public string? DateProblem =>
        To < From ? "The end date is before the start date." :
        To.DayNumber - From.DayNumber >= MaximumDays ? "Choose at most one year." : null;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAllowed) { Fail(CollectionsScreens.NotSignedInText); return; }
        if (DateProblem is { } problem) { Fail(problem); return; }
        var mine = ++revision;
        StatusText = "Loading..."; StatusTone = StatusTone.Neutral; Changed?.Invoke();
        try
        {
            var asOf = today();
            if (asOf < To) asOf = To;
            var loaded = await query().LoadStatementAsync(new CollectionsStatementRequest(StoreCode is null ? null : [StoreCode], From, To, asOf), cancellationToken).ConfigureAwait(true);
            if (mine != revision) return; // a slower earlier load never overwrites a newer one
            statement = loaded;
            if (!Modes.Contains(modeFilter, StringComparer.Ordinal)) modeFilter = AllModes;
            StatusText = string.Empty;
            Rebuild();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            if (mine == revision) Fail(CollectionsScreens.Sentence(exception));
        }
    }

    // ---- 1.12.1 P2: Export to Excel ---------------------------------------------------------------------------------------

    /// <summary>The bank columns ("Credited on", "Bank reference") go into the file for the Owner only. A Store Manager's file has
    /// no bank columns; a Viewer's has none either (the Viewer's statement carries no bank reference at all, and a Viewer never
    /// gets more than a Store Manager).</summary>
    public bool ExportIncludesBankColumns => CollectionsScreens.CanSetUp(Access) && statement is { WithoutBankReferences: false };

    public bool CanExport => IsAllowed && statement is not null && Rows.Count > 0;

    public string ExportDisabledReason => !IsAllowed ? RefusalText ?? string.Empty : statement is null ? "Load the statement first." : Rows.Count == 0 ? "There are no lines to export." : string.Empty;

    public string ExportFileName => string.Create(CultureInfo.InvariantCulture, $"Collections_expected_vs_evidence_{From:yyyyMMdd}_{To:yyyyMMdd}.xlsx");

    /// <summary>The file's columns in order: the screen's, then the Age in days, then (Owner only) the bank columns.</summary>
    public IReadOnlyList<ExcelReportColumn> ExportColumns
    {
        get
        {
            List<ExcelReportColumn> columns =
            [
                new("Store"), new("Business date"), new("Mode"), new("Provider"),
                new(IndianFormat.MoneyHeader("Expected"), MoneyFormat), new(IndianFormat.MoneyHeader("Evidence"), MoneyFormat),
                new(IndianFormat.MoneyHeader("Difference"), MoneyFormat), new(IndianFormat.MoneyHeader("Cash tenders"), MoneyFormat),
                new("Status"), new("Detail"), new("Age (days)", "#,##0")
            ];
            if (ExportIncludesBankColumns) columns.AddRange([new("Credited on"), new("Bank reference")]);
            return columns;
        }
    }

    /// <summary>The rows on screen (the mode filter applies), as values: dates as dates, money as numbers, a missing figure as an
    /// empty cell (the writer shows its dash), the state as its word. Nothing the role may not see is put in a cell.</summary>
    public ExcelReportData BuildExportData()
    {
        var bank = ExportIncludesBankColumns;
        var rows = Rows.Select(row =>
        {
            var line = row.Line;
            List<object?> cells =
            [
                line.StoreCode, line.BusinessDate, line.Mode, line.ProviderKey, line.Expected, line.Evidence, line.Difference, line.TenderTotal,
                row.StatusWord, row.Detail, line.AgeDays
            ];
            if (bank) cells.AddRange([line.CreditedDate, line.BankReference]);
            return (IReadOnlyList<object?>)cells;
        }).ToList();
        return new ExcelReportData(ExportColumns, rows);
    }

    /// <summary>The file's header block: the report, the dates, "Totals only, not matched yet", the scope and what the role's
    /// file leaves out.</summary>
    public ExcelReportMetadata BuildExportMetadata(DateTimeOffset generatedUtc)
    {
        var message = TotalsOnlyNoticeTitle + ". " + TotalsOnlyNoticeBody
            + (ExportIncludesBankColumns ? string.Empty : " " + (WithoutBankReferences ? HiddenReferencesText + " " : string.Empty) + ExportWithoutBankColumnsText)
            + (modeFilter == AllModes ? string.Empty : $" Only the {modeFilter} lines.");
        return new ExcelReportMetadata(ExportReportName, From, To, TotalsOnlyNoticeTitle, ExportRuleVersion, message, generatedUtc,
            StoreCode is null ? "All stores" : "Store " + StoreCode);
    }

    private void Fail(string text)
    {
        statement = null; Rows = [];
        StatusText = text; StatusTone = StatusTone.Critical;
        Changed?.Invoke();
    }

    private void Rebuild()
    {
        var lines = statement?.Lines ?? [];
        var hide = WithoutBankReferences;
        Rows = lines
            .Where(line => modeFilter == AllModes || string.Equals(line.Mode.Split(" + ")[0], modeFilter, StringComparison.Ordinal))
            .Select(line => new StatementRow(line, hide))
            .ToList();
        if (statement is not null && statement.Lines.Count == 0) { StatusText = NoLinesText; StatusTone = StatusTone.Neutral; }
        Changed?.Invoke();
    }
}
