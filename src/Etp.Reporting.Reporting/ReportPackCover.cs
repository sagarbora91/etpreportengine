using System.Globalization;

namespace Etp.Reporting.Reporting;

public sealed record ReportPackCoverFigure(string Label, string Value, string Meaning, OutputTone Tone);
public sealed record ReportPackCoverCheck(string Name, string StatusWord, OutputTone Tone, string Message);

/// <summary>
/// The pack's front page (decision 45 #4, UIUX-OUTPUTS OUT-08, MOCK-OUT-08-after): what, which shop, which day, when
/// generated; the verdict as a coloured word; the four numbers people ask first; every check in plain status words.
/// Read only from the pack document's own Control Summary rows, so it can never disagree with the tables after it.
/// </summary>
public sealed record ReportPackCover(string Heading, string Subject, string Generated, string Verdict, OutputTone VerdictTone,
    string Summary, IReadOnlyList<ReportPackCoverFigure> Figures, IReadOnlyList<ReportPackCoverCheck> Checks, string Footer);

public static class ReportPackCoverBuilder
{
    // Control Summary section names (DailyReportingPackService) whose figures the cover shows.
    internal const string DsrSection = "Daily Sales Report", TenderSection = "Tender Reconciliation",
        CashSection = "Daily Cash Reconciliation", StockSection = "System Closing Stock";
    internal const int MaximumMessageLength = 160;

    public static ReportPackCover From(ReportPackDocument pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var subject = SubjectOf(pack);
        var period = pack.DateFrom == pack.DateTo ? OutputText.ShortWeekdayDate(pack.DateTo) : $"{OutputText.Date(pack.DateFrom)} – {OutputText.Date(pack.DateTo)}";
        var control = pack.Tables.FirstOrDefault(x => x.Name.EndsWith("Control Summary", StringComparison.OrdinalIgnoreCase));
        var figures = new List<ReportPackCoverFigure>();
        var checks = new List<ReportPackCoverCheck>();
        if (control is not null && Column(control, "Report") is { } report && Column(control, "Status") is { } status)
        {
            var store = Column(control, "Store"); var total = Column(control, "Control Total"); var variance = Column(control, "Variance"); var message = Column(control, "Message");
            var rows = control.Data.Rows;
            foreach (var group in rows.GroupBy(row => store is null ? string.Empty : Convert.ToString(row[store.Value], CultureInfo.InvariantCulture) ?? string.Empty))
            {
                var prefix = group.Key.Length == 0 ? string.Empty : group.Key + " · ";
                object? Pick(string section, int? column) => column is null ? null
                    : group.FirstOrDefault(candidate => string.Equals(Convert.ToString(candidate[report], CultureInfo.InvariantCulture), section, StringComparison.OrdinalIgnoreCase)) is { } match ? match[column.Value] : null;
                // A check that did not run has no number to show: say so in the neutral tone, never a reconciled green zero.
                string? NotChecked(string section)
                {
                    var match = group.FirstOrDefault(candidate => string.Equals(Convert.ToString(candidate[report], CultureInfo.InvariantCulture), section, StringComparison.OrdinalIgnoreCase));
                    if (match is null) return null;
                    var word = OutputText.StatusWord(Convert.ToString(match[status], CultureInfo.InvariantCulture));
                    return word is StatusWords.CannotRun or StatusWords.NotChecked ? word : null;
                }
                ReportPackCoverFigure Figure(string label, string section, int? column, string meaning, bool difference, bool units = false)
                    => NotChecked(section) is { } unavailable ? new(prefix + label, unavailable, meaning, OutputTone.Neutral)
                    : units ? Units(prefix + label, Pick(section, column), meaning) : Money(prefix + label, Pick(section, column), meaning, difference);
                figures.Add(Figure("Sales today", DsrSection, total, "the DSR for the day, incl. GST", difference: false));
                figures.Add(Figure("Tender difference", TenderSection, variance, "bills against tenders", difference: true));
                figures.Add(Figure("Cash difference", CashSection, variance, "counted against calculated cash", difference: true));
                figures.Add(Figure("Stock difference", StockSection, variance, "system closing stock against expected", difference: true, units: true));
                foreach (var row in group)
                {
                    var word = Convert.ToString(row[status], CultureInfo.InvariantCulture);
                    checks.Add(new(prefix + Convert.ToString(row[report], CultureInfo.InvariantCulture), OutputText.StatusWord(word), OutputText.StatusTone(word),
                        Shorten(message is null ? null : Convert.ToString(row[message.Value], CultureInfo.InvariantCulture))));
                }
            }
        }
        else
            checks.AddRange(pack.Tables.Select(table => new ReportPackCoverCheck(table.Name, OutputText.StatusWord(table.Status), OutputText.StatusTone(table.Status), Shorten(table.Message))));

        var attention = checks.Count(x => x.Tone is OutputTone.Critical or OutputTone.Warning);
        var verdict = Verdict(pack.OverallStatus) + (attention == 0 ? string.Empty : $" · {attention} to look at");
        var tone = OutputText.StatusTone(pack.OverallStatus);
        return new("ETP · DAILY REPORTING PACK", $"{subject} · {period}", $"Generated {OutputText.Generated(pack.GeneratedUtc)} · rule {pack.RuleVersion}",
            verdict, tone == OutputTone.Neutral && attention > 0 ? OutputTone.Warning : tone, pack.Message, figures, checks,
            $"Values incl. GST, in ₹. {OutputText.MissingLegend} The pages after this one hold every table of the pack.");
    }

    /// <summary>"ETP Daily Reporting Pack — TW01" gives "TW01"; a title without the dash is used as it is.</summary>
    public static string SubjectOf(ReportPackDocument pack)
    {
        var title = pack.Title ?? string.Empty; var dash = title.LastIndexOf('—');
        var subject = dash >= 0 ? title[(dash + 1)..].Trim() : title.Trim();
        return subject.Length == 0 ? "ETP" : subject;
    }

    internal static string Verdict(string? overallStatus) => OutputText.StatusTone(overallStatus) switch
    {
        OutputTone.Success => "Reconciled",
        OutputTone.Critical => "Not reconciled",
        OutputTone.Warning => "Reconciled with warnings",
        _ => "Not complete yet"
    };

    private static ReportPackCoverFigure Money(string label, object? value, string meaning, bool difference)
    {
        var amount = AsDecimal(value);
        if (amount is null) return new(label, DsrDisplay.Missing, meaning, OutputTone.Neutral);
        var text = amount.Value.ToString("₹#,##0.00;−₹#,##0.00;₹0.00", OutputText.India);
        return new(label, text, meaning, !difference ? OutputTone.Neutral : amount == 0 ? OutputTone.Success : OutputTone.Critical);
    }

    private static ReportPackCoverFigure Units(string label, object? value, string meaning)
    {
        var amount = AsDecimal(value);
        if (amount is null) return new(label, DsrDisplay.Missing, meaning, OutputTone.Neutral);
        return new(label, amount.Value.ToString("#,##0.##;−#,##0.##;0", OutputText.India), meaning, amount == 0 ? OutputTone.Success : OutputTone.Critical);
    }

    private static decimal? AsDecimal(object? value) => value switch
    {
        decimal d => d, int i => i, long l => l, double db => (decimal)db, float f => (decimal)f, _ => null
    };

    private static int? Column(ReportPackTable table, string header)
    {
        for (var i = 0; i < table.Data.Columns.Count; i++)
            if (string.Equals(table.Data.Columns[i].Header, header, StringComparison.OrdinalIgnoreCase)) return i;
        return null;
    }

    /// <summary>The first sentence, at most 160 characters; the full text is in the Control Summary table.</summary>
    internal static string Shorten(string? message)
    {
        var text = (message ?? string.Empty).Trim();
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        if (end > 0) text = text[..(end + 1)];
        return text.Length <= MaximumMessageLength ? text : text[..(MaximumMessageLength - 1)].TrimEnd() + "…";
    }
}
