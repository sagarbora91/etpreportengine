extern alias EtpApplication;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;
using EtpApplication::Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Themes;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

/// <summary>The questions the Error register asks the Owner before it changes an entry. The default shows small dialogs; tests inject answers.</summary>
public interface IErrorRegisterPrompts
{
    /// <summary>A reason (Mark expected). Null when cancelled.</summary>
    string? Reason(string title, string intro);

    /// <summary>A fix ticket id such as FT-007 (Link ticket). Null when cancelled.</summary>
    string? Ticket();

    /// <summary>The version a fix shipped in, an optional ticket and a reason (Mark fixed in version). Null when cancelled.</summary>
    (string Version, string? Ticket, string Reason)? Fixed(IReadOnlyList<string> versionsSeen);

    /// <summary>The entry to merge <paramref name="from"/> into, and why. Null when cancelled.</summary>
    (int IntoId, string Reason)? Merge(ErrorRegisterEntry from, IReadOnlyList<ErrorRegisterEntry> candidates);
}

/// <summary>One register row as the grid shows it.</summary>
public sealed record ErrorRow(ErrorRegisterEntry Entry)
{
    public string Id => Entry.DisplayId;
    /// <summary>DS-22: the event code read as a sentence ("IMPORT_HISTORY_LOAD_FAILED" is "Import history load failed").</summary>
    public string WhatHappened => PlainEvent(Entry.EventCode, Entry.SubCode);
    public static string PlainEvent(string eventCode, string? subCode)
    {
        static string Words(string code)
        {
            var text = code.Replace('_', ' ').Trim().ToLowerInvariant();
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
        }
        var main = Words(eventCode);
        return string.IsNullOrWhiteSpace(subCode) ? main : main + " (" + subCode.Replace('_', ' ').Trim().ToLowerInvariant() + ")";
    }
    public string StatusKind => Entry.Status.ToString();
    public string Status => ErrorRegisterView.StatusLabel(Entry);
    public string Event => string.IsNullOrEmpty(Entry.SubCode) ? Entry.EventCode : $"{Entry.EventCode} / {Entry.SubCode}";
    public string Where => string.IsNullOrEmpty(Entry.Site) ? Entry.Source : $"{Entry.Source} / {Entry.Site}";
    public string Error => ErrorRegisterView.ErrorLabel(Entry);
    public string Count => Entry.OccurrenceCount.ToString("N0", PresentationCulture.Indian);
    public string FirstSeen => Entry.FirstSeenUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", PresentationCulture.Indian);
    public string LastSeen => Entry.LastSeenUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", PresentationCulture.Indian);
    public string Versions => string.Equals(Entry.FirstVersion, Entry.LastVersion, StringComparison.Ordinal)
        ? Entry.LastVersion : $"{Entry.FirstVersion} to {Entry.LastVersion}";
    public string Ticket => string.IsNullOrEmpty(Entry.FixedTicket) ? "-" : Entry.FixedTicket!;
}

/// <summary>One of the latest occurrences of the selected entry.</summary>
public sealed record ErrorOccurrenceRow(ErrorOccurrenceRecord Occurrence)
{
    public string Time => Occurrence.OccurredUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", PresentationCulture.Indian);
    public string Version => Occurrence.AppVersion;
    public string Report => Occurrence.ReportCode ?? "";
    public string Store => Occurrence.StoreCode ?? "";
    public string Date => (Occurrence.BusinessDate ?? Occurrence.DateFrom)?.ToString("dd MMM yyyy", PresentationCulture.Indian) ?? "";
    public string Ref => Occurrence.Reference;
    public string Role => Occurrence.ReporterRole;
}

/// <summary>
/// Settings &gt; Database &gt; Error register (design 9.2): the entries the diagnostics logs have produced, Find Ref (the
/// "Ref: 1A2B3C4D5E6F" a user reads from an error message), status actions, merge and Copy summary. Owner only; every
/// action re-checks the role. Data comes only through <see cref="IErrorRegisterStore"/>.
/// </summary>
public sealed class ErrorRegisterView : UserControl
{
    public const string ScreenTitle = "Error register";
    public const string AboutText = "Every failure ETP has logged, grouped by cause. Type the Ref from an error message to find its entry. Nothing here holds customer data.";
    public const string OwnerOnlyText = "The error register is for the Owner. Ask the Owner to open Settings > Database > Error register.";
    public const string NotAvailableText = "The error register is not available in this build.";
    public const string RefNotFoundText = "That Ref is not in the register. If the error happened on another PC or account, open the register there, or press Refresh here to read this account's log, then find it again.";
    public const string BadRefText = "A Ref is 12 letters and digits, for example 1A2B3C4D5E6F. Copy it from the error message after \"Ref:\".";

    /// <summary>The status filter choices, in list order. "Open" is New, Seen and Recurred (design 9.2).</summary>
    public static IReadOnlyList<string> FilterChoices { get; } = ["Open (New, Seen, Recurred)", "All", "New", "Seen", "Expected", "Fixed", "Recurred"];

    private static readonly Regex RefPattern = new("^[0-9A-Fa-f]{12}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex TicketPattern = new("^FT-[0-9]{1,8}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex VersionPattern = new("^[0-9]{1,5}(\\.[0-9]{1,5}){1,3}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly Func<IErrorRegisterStore?> store;
    private readonly Func<CancellationToken, Task<ErrorIngestResult?>>? ingest;
    private readonly Func<bool> isOwner;
    private readonly IErrorRegisterPrompts prompts;
    private readonly Action<string> copy;
    private readonly TextBox findInput = new() { MinWidth = 180, MinHeight = 44, Padding = new Thickness(6), VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 24 };
    private readonly ComboBox filterBox = new() { MinHeight = 44, MinWidth = 220, ItemsSource = FilterChoices, SelectedIndex = 0 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    // DS-22: a headline band ("3 open problems, last 09:12 today") before the table.
    private readonly Border headlineBand = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(16), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    private readonly TextBlock headlineText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly DataGrid occurrenceGrid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, MaxHeight = 200 };
    private readonly TextBlock detailHeader = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
    private readonly TextBox detailText = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button[] actionButtons;
    private IReadOnlyList<ErrorRegisterEntry> loaded = [];
    private ErrorRegisterEntryDetail? detail;
    private string? foundReference;
    private int revision;
    private int detailRevision;

    public ErrorRegisterView(
        Func<IErrorRegisterStore?> store,
        Func<CancellationToken, Task<ErrorIngestResult?>>? ingest,
        Func<bool> isOwner,
        IErrorRegisterPrompts? prompts = null,
        Action<string>? copy = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.ingest = ingest;
        this.isOwner = isOwner ?? throw new ArgumentNullException(nameof(isOwner));
        this.prompts = prompts ?? new DialogPrompts(this);
        this.copy = copy ?? CopyToClipboard;

        var root = new Grid { Margin = new Thickness(4) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var heading = new StackPanel();
        // DS-22/DS-24: the shell header owns the title. The explanation is a tooltip on the headline, not a paragraph.
        headlineText.SetResourceReference(FrameworkElement.StyleProperty, "MetricText");
        headlineBand.Child = headlineText;
        headlineBand.ToolTip = AboutText;
        AutomationProperties.SetName(headlineBand, "Error register headline");
        status.SetResourceReference(FrameworkElement.StyleProperty, "CaptionText");
        heading.Children.Add(headlineBand);
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        bar.Children.Add(new TextBlock { Text = "Find Ref:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        bar.Children.Add(findInput);
        var find = new Button { Content = "Find", MinHeight = 44, Margin = new Thickness(8, 0, 16, 0) };
        find.Click += async (_, _) => await FindAsync(findInput.Text);
        findInput.KeyDown += async (_, args) => { if (args.Key == System.Windows.Input.Key.Enter) await FindAsync(findInput.Text); };
        bar.Children.Add(find);
        bar.Children.Add(new TextBlock { Text = "Status:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        bar.Children.Add(filterBox);
        var refresh = new Button { Content = "Refresh", MinHeight = 44, Margin = new Thickness(16, 0, 0, 0) };
        refresh.Click += async (_, _) => await RefreshAsync();
        bar.Children.Add(refresh);
        heading.Children.Add(bar);
        heading.Children.Add(status);
        root.Children.Add(heading);

        AutomationProperties.SetName(findInput, "Find Ref");
        AutomationProperties.SetName(filterBox, "Status filter");
        AutomationProperties.SetName(grid, "Error register entries");
        AutomationProperties.SetName(occurrenceGrid, "Latest occurrences of the selected entry");
        AutomationProperties.SetName(detailText, "Selected entry details");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        BuildColumns();
        grid.RowStyle = BuildRowStyle();
        grid.SelectionChanged += async (_, _) => await ShowDetailAsync();
        filterBox.SelectionChanged += (_, _) => { foundReference = null; ApplyFilter(); };
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var footer = new StackPanel();
        footer.Children.Add(detailHeader);
        footer.Children.Add(detailText);
        footer.Children.Add(occurrenceGrid);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actionButtons =
        [
            MakeButton("Mark seen", async () => await MarkSeenAsync()),
            MakeButton("Mark expected…", async () => await MarkExpectedAsync()),
            MakeButton("Link ticket FT-…", async () => await LinkTicketAsync()),
            MakeButton("Mark fixed in version…", async () => await MarkFixedAsync()),
            MakeButton("Merge into…", async () => await MergeAsync()),
            MakeButton("Copy summary", () => { CopySummary(); return Task.CompletedTask; })
        ];
        // DS-23: Copy summary (primary) and Mark seen stay visible; the developer actions sit under "More".
        actionButtons[5].SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        actions.Children.Add(actionButtons[5]);
        actions.Children.Add(actionButtons[0]);
        var more = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in actionButtons.Skip(1).Take(4)) more.Children.Add(button);
        var moreFold = new Expander { Header = "More", Content = more, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(moreFold, "More error register actions");
        actions.Children.Add(moreFold);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        AutomationProperties.SetName(this, ScreenTitle);
        ApplyAccess();
    }

    // ---- state the tests read ------------------------------------------------------------------------------------

    public IReadOnlyList<ErrorRow> Rows { get; private set; } = [];
    public IReadOnlyList<ErrorRegisterEntry> LoadedEntries => loaded;
    public IReadOnlyList<ErrorOccurrenceRow> Occurrences { get; private set; } = [];
    public ErrorRegisterEntryDetail? Detail => detail;
    public ErrorRow? SelectedRow => grid.SelectedItem as ErrorRow;
    public string StatusText { get => status.Text; private set => status.Text = value; }
    public string DetailHeaderText => detailHeader.Text;
    public string DetailText => detailText.Text;
    public string FindText { get => findInput.Text; set => findInput.Text = value; }
    public string SelectedFilter { get => filterBox.SelectedItem as string ?? FilterChoices[0]; set => filterBox.SelectedItem = value; }
    public bool ActionsEnabled => actionButtons.All(button => button.IsEnabled);
    public DataGrid Table => grid;
    public DataGrid OccurrenceTable => occurrenceGrid;

    public void Select(int errorRegisterId) => grid.SelectedItem = Rows.FirstOrDefault(row => row.Entry.ErrorRegisterId == errorRegisterId);

    // ---- pure rules ----------------------------------------------------------------------------------------------

    public static string StatusLabel(ErrorRegisterEntry entry) => entry.Status switch
    {
        ErrorStatus.Fixed when !string.IsNullOrEmpty(entry.FixedInVersion) => $"Fixed {entry.FixedInVersion}",
        ErrorStatus.Recurred when !string.IsNullOrEmpty(entry.RecurredVersion) => $"Recurred {entry.RecurredVersion}",
        var other => other.ToString()
    };

    public static string ErrorLabel(ErrorRegisterEntry entry)
    {
        var type = entry.ExceptionType;
        var dot = type.LastIndexOf('.');
        if (dot >= 0 && dot < type.Length - 1) type = type[(dot + 1)..];
        return entry.SqlNumber != 0 ? $"{type} {entry.SqlNumber.ToString(CultureInfo.InvariantCulture)}" : type;
    }

    /// <summary>True for a Ref as printed in an error message: 12 hex digits, with or without a leading "Ref:".</summary>
    public static bool TryParseReference(string? text, out string reference)
    {
        var value = (text ?? "").Trim();
        if (value.StartsWith("Ref:", StringComparison.OrdinalIgnoreCase)) value = value[4..].Trim();
        reference = value.ToUpperInvariant();
        return RefPattern.IsMatch(value);
    }

    public static string? ValidateTicket(string? text) =>
        TicketPattern.IsMatch((text ?? "").Trim()) ? null : "A ticket id looks like FT-007 (FT, a dash and up to 8 digits).";

    public static string? ValidateVersion(string? text) =>
        VersionPattern.IsMatch((text ?? "").Trim()) && (text ?? "").Trim().Length <= 20 ? null : "A version looks like 1.11.0.";

    /// <summary>The filter of the status box over the loaded entries; "Open" is New, Seen and Recurred.</summary>
    public static IReadOnlyList<ErrorRegisterEntry> ApplyFilter(IReadOnlyList<ErrorRegisterEntry> entries, string filter) => filter switch
    {
        "All" => entries,
        var choice when choice == FilterChoices[0] => entries.Where(entry => entry.Status is ErrorStatus.New or ErrorStatus.Seen or ErrorStatus.Recurred).ToArray(),
        var choice when Enum.TryParse<ErrorStatus>(choice, out var wanted) => entries.Where(entry => entry.Status == wanted).ToArray(),
        _ => entries
    };

    /// <summary>The versions table, and "also seen on 1.x (not upgraded)" for a version older than the fix.</summary>
    public static string VersionsText(ErrorRegisterEntryDetail detail)
    {
        var fixedIn = TryVersion(detail.Entry.FixedInVersion);
        var text = new StringBuilder("Versions:");
        foreach (var version in detail.Versions)
        {
            text.Append(' ').Append(version.AppVersion).Append(" (").Append(version.OccurrenceCount.ToString("N0", PresentationCulture.Indian)).Append(')');
            var seen = TryVersion(version.AppVersion);
            if (fixedIn is not null && seen is not null && seen < fixedIn && detail.Entry.Status is ErrorStatus.Fixed or ErrorStatus.Recurred)
                text.Append(" also seen on this version (not upgraded)");
            text.Append(';');
        }
        return text.ToString().TrimEnd(';');
    }

    private static Version? TryVersion(string? text) => Version.TryParse(text, out var version) ? version : null;

    /// <summary>
    /// The privacy-safe text block for a chat with support: the same tokens the register holds (event code, source, site,
    /// exception type, SQL number, counts, versions, dates, status). No messages, no row data, no names, no stack.
    /// </summary>
    public static string SummaryFor(ErrorRegisterEntryDetail detail)
    {
        var entry = detail.Entry;
        var culture = PresentationCulture.Indian;
        var text = new StringBuilder();
        text.AppendLine($"{entry.DisplayId}  {StatusLabel(entry)}");
        text.AppendLine($"Event {new ErrorRow(entry).Event}");
        text.AppendLine($"Where {new ErrorRow(entry).Where}");
        text.AppendLine($"Error {ErrorLabel(entry)}");
        text.AppendLine($"Seen {entry.OccurrenceCount.ToString("N0", culture)} times, {entry.FirstSeenUtc.ToLocalTime().ToString("dd MMM yyyy", culture)} to {entry.LastSeenUtc.ToLocalTime().ToString("dd MMM yyyy", culture)}");
        text.AppendLine(VersionsText(detail));
        if (!string.IsNullOrEmpty(entry.FixedTicket)) text.AppendLine($"Ticket {entry.FixedTicket}");
        return text.ToString().TrimEnd();
    }

    // ---- loading -------------------------------------------------------------------------------------------------

    /// <summary>Loads the register through the store (no log ingest) and shows the rows the status filter allows.</summary>
    public async Task LoadAsync()
    {
        var mine = ++revision;
        ApplyAccess();
        if (!isOwner()) { StatusText = OwnerOnlyText; Clear(); return; }
        var source = store();
        if (source is null) { StatusText = NotAvailableText; Clear(); return; }
        StatusText = "Loading the error register…";
        try
        {
            var entries = await source.LoadRegisterAsync();
            if (mine != revision) return;
            loaded = entries;
            foundReference = null;
            ApplyFilter();
        }
        catch (Exception exception)
        {
            if (mine != revision) return;
            StatusText = FailureText("The error register could not be loaded. ", exception, "ERROR_REGISTER_LOAD_FAILED");
        }
    }

    /// <summary>Refresh: reads this account's own log into the register first (when wired), then reloads.</summary>
    public async Task RefreshAsync()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return; }
        var note = "";
        if (ingest is not null)
        {
            try
            {
                var result = await ingest(CancellationToken.None);
                if (result is not null)
                    note = result.Accepted == 0 ? "Nothing new in this account's log. " : $"{result.Accepted.ToString("N0", PresentationCulture.Indian)} new log line(s) added from this account's log. ";
            }
            catch (Exception exception)
            {
                note = FailureText("This account's log could not be read. ", exception, "ERROR_REGISTER_INGEST_FAILED") + " ";
            }
        }
        await LoadAsync();
        if (note.Length > 0) StatusText = note + StatusText;
    }

    /// <summary>Find Ref: shows the entry that holds the occurrence with this Ref, or says it is not in the register yet.</summary>
    public async Task<bool> FindAsync(string? text)
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        if (!TryParseReference(text, out var reference)) { StatusText = BadRefText; return false; }
        var source = store();
        if (source is null) { StatusText = NotAvailableText; return false; }
        try
        {
            var entries = await source.LoadRegisterAsync(null, reference);
            if (entries.Count == 0)
            {
                StatusText = RefNotFoundText;
                return false;
            }
            foundReference = reference;
            Rows = entries.Select(entry => new ErrorRow(entry)).ToArray();
            grid.ItemsSource = Rows;
            grid.SelectedItem = Rows[0];
            StatusText = $"Ref {reference} is in {entries[0].DisplayId}. Choose a status or press Refresh to see the whole register again.";
            return true;
        }
        catch (Exception exception)
        {
            StatusText = FailureText("The Ref could not be searched. ", exception, "ERROR_REGISTER_FIND_FAILED");
            return false;
        }
    }

    private void Clear()
    {
        loaded = []; Rows = []; grid.ItemsSource = null; detail = null; Occurrences = []; occurrenceGrid.ItemsSource = null;
        detailHeader.Text = ""; detailText.Text = ""; headlineBand.Visibility = Visibility.Collapsed;
    }

    private void ApplyFilter()
    {
        var keep = SelectedRow?.Entry.ErrorRegisterId;
        var shown = ApplyFilter(loaded, SelectedFilter);
        Rows = shown.Select(entry => new ErrorRow(entry)).ToArray();
        grid.ItemsSource = Rows;
        if (keep is { } id) grid.SelectedItem = Rows.FirstOrDefault(row => row.Entry.ErrorRegisterId == id);
        StatusText = Rows.Count == 0
            ? (loaded.Count == 0 ? "The error register is empty. Nothing has been logged, or the logs have not been read yet; press Refresh." : "No entries have this status.")
            : $"{Rows.Count.ToString("N0", PresentationCulture.Indian)} of {loaded.Count.ToString("N0", PresentationCulture.Indian)} entries shown.";
        if (SelectedRow is null) { detail = null; detailHeader.Text = ""; detailText.Text = ""; Occurrences = []; occurrenceGrid.ItemsSource = null; }
        ShowHeadline();
        ApplyAccess();
    }

    /// <summary>DS-22: "3 open problems, last 09:12 today" - open is New, Seen and Recurred, as the default filter reads it.</summary>
    public static string HeadlineFor(IReadOnlyList<ErrorRegisterEntry> entries, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var open = entries.Where(entry => entry.Status is ErrorStatus.New or ErrorStatus.Seen or ErrorStatus.Recurred).ToArray();
        if (open.Length == 0) return "No open problems";
        var last = open.Max(entry => entry.LastSeenUtc).ToLocalTime().DateTime;
        var when = last.Date == now.Date ? "today" : last.Date == now.Date.AddDays(-1) ? "yesterday" : last.ToString("dd MMM", PresentationCulture.Indian);
        return $"{open.Length.ToString("N0", PresentationCulture.Indian)} open {(open.Length == 1 ? "problem" : "problems")}, last {last.ToString("HH:mm", PresentationCulture.Indian)} {when}";
    }

    private void ShowHeadline()
    {
        if (loaded.Count == 0) { headlineBand.Visibility = Visibility.Collapsed; return; }
        headlineText.Text = HeadlineFor(loaded, DateTime.Now);
        var recurred = loaded.Any(entry => entry.Status == ErrorStatus.Recurred);
        var open = loaded.Any(entry => entry.Status is ErrorStatus.New or ErrorStatus.Seen or ErrorStatus.Recurred);
        var (fill, edge) = recurred ? ("CriticalSoft", "Critical") : open ? ("WarningSoft", "Warning") : ("SuccessSoft", "Success");
        headlineBand.SetResourceReference(Border.BackgroundProperty, fill);
        headlineBand.SetResourceReference(Border.BorderBrushProperty, edge);
        headlineText.SetResourceReference(TextBlock.ForegroundProperty, edge);
        headlineBand.Visibility = Visibility.Visible;
    }

    private async Task ShowDetailAsync()
    {
        var row = SelectedRow;
        ApplyAccess();
        var mine = ++detailRevision;
        if (row is null) { detail = null; detailHeader.Text = ""; detailText.Text = ""; Occurrences = []; occurrenceGrid.ItemsSource = null; return; }
        detailHeader.Text = $"Details ({row.Id})";
        var source = store();
        if (source is null || !isOwner()) return;
        try
        {
            var loadedDetail = await source.LoadEntryAsync(row.Entry.ErrorRegisterId);
            if (mine != detailRevision) return;
            detail = loadedDetail;
            if (loadedDetail is null) { detailText.Text = "This entry is no longer in the register. Press Refresh."; Occurrences = []; occurrenceGrid.ItemsSource = null; return; }
            var text = new StringBuilder();
            text.AppendLine("Latest stack (method names only):");
            foreach (var frame in loadedDetail.Entry.LatestStack) text.AppendLine("  " + frame);
            if (loadedDetail.Entry.LatestStack.Count == 0) text.AppendLine("  none recorded");
            text.AppendLine(VersionsText(loadedDetail));
            if (!string.IsNullOrEmpty(loadedDetail.Entry.StatusReason)) text.AppendLine("Reason: " + loadedDetail.Entry.StatusReason);
            detailText.Text = text.ToString().TrimEnd();
            Occurrences = loadedDetail.LatestOccurrences.Select(item => new ErrorOccurrenceRow(item)).ToArray();
            occurrenceGrid.ItemsSource = Occurrences;
            if (foundReference is not null)
                occurrenceGrid.SelectedItem = Occurrences.FirstOrDefault(item => string.Equals(item.Ref, foundReference, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            if (mine != detailRevision) return;
            detailText.Text = FailureText("The entry could not be loaded. ", exception, "ERROR_REGISTER_DETAIL_FAILED");
        }
    }

    /// <summary>Shows one entry's detail now and returns when it has loaded (tests, and after an action).</summary>
    public Task ShowSelectedDetailAsync() => ShowDetailAsync();

    private void ApplyAccess()
    {
        var enabled = isOwner() && SelectedRow is not null;
        foreach (var button in actionButtons) button.IsEnabled = enabled;
    }

    // ---- actions -------------------------------------------------------------------------------------------------

    private async Task<bool> ChangeAsync(string done, Func<IErrorRegisterStore, ErrorRegisterEntry, Task> change, string eventId)
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        var row = SelectedRow;
        if (row is null) { StatusText = "Select an entry first."; return false; }
        var source = store();
        if (source is null) { StatusText = NotAvailableText; return false; }
        try
        {
            await change(source, row.Entry);
        }
        catch (Exception exception)
        {
            StatusText = FailureText("The entry was not changed. ", exception, eventId);
            return false;
        }
        var id = row.Entry.ErrorRegisterId;
        await LoadAsync();
        Select(id);
        await ShowDetailAsync();
        StatusText = $"{row.Id} {done}";
        return true;
    }

    public Task<bool> MarkSeenAsync() =>
        ChangeAsync("marked seen.", (source, entry) => source.SetStatusAsync(entry.ErrorRegisterId, ErrorStatus.Seen, null, null, null), "ERROR_REGISTER_STATUS_FAILED");

    public async Task<bool> MarkExpectedAsync()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        if (SelectedRow is null) { StatusText = "Select an entry first."; return false; }
        if (prompts.Reason("Mark expected", "Say in plain words why this error is expected. The entry stays in the register but is no longer an open problem.") is not { } reason) return false;
        var problem = HealthCheckView.ValidateSafeText(reason, "a reason");
        if (problem is not null) { StatusText = problem; return false; }
        return await ChangeAsync("marked expected.", (source, entry) => source.SetStatusAsync(entry.ErrorRegisterId, ErrorStatus.Expected, null, null, reason.Trim()), "ERROR_REGISTER_STATUS_FAILED");
    }

    public async Task<bool> LinkTicketAsync()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        if (SelectedRow is null) { StatusText = "Select an entry first."; return false; }
        if (prompts.Ticket() is not { } ticket) return false;
        var problem = ValidateTicket(ticket);
        if (problem is not null) { StatusText = problem; return false; }
        return await ChangeAsync($"linked to {ticket.Trim().ToUpperInvariant()} and marked seen.", (source, entry) =>
            source.SetStatusAsync(entry.ErrorRegisterId, ErrorStatus.Seen, null, ticket.Trim().ToUpperInvariant(), null), "ERROR_REGISTER_STATUS_FAILED");
    }

    public async Task<bool> MarkFixedAsync()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        if (SelectedRow is null) { StatusText = "Select an entry first."; return false; }
        var versions = (detail?.Versions.Select(version => version.AppVersion) ?? [])
            .Concat(new[] { SelectedRow.Entry.LastVersion, SelectedRow.Entry.FirstVersion })
            .Where(version => !string.IsNullOrWhiteSpace(version)).Distinct().ToArray();
        if (prompts.Fixed(versions) is not { } answer) return false;
        var versionProblem = ValidateVersion(answer.Version);
        if (versionProblem is not null) { StatusText = versionProblem; return false; }
        if (!string.IsNullOrWhiteSpace(answer.Ticket) && ValidateTicket(answer.Ticket) is { } ticketProblem) { StatusText = ticketProblem; return false; }
        var reasonProblem = HealthCheckView.ValidateSafeText(answer.Reason, "a reason");
        if (reasonProblem is not null) { StatusText = reasonProblem; return false; }
        var ticket = string.IsNullOrWhiteSpace(answer.Ticket) ? null : answer.Ticket.Trim().ToUpperInvariant();
        return await ChangeAsync($"marked fixed in {answer.Version.Trim()}.", (source, entry) =>
            source.SetStatusAsync(entry.ErrorRegisterId, ErrorStatus.Fixed, answer.Version.Trim(), ticket, answer.Reason.Trim()), "ERROR_REGISTER_STATUS_FAILED");
    }

    public async Task<bool> MergeAsync()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        var row = SelectedRow;
        if (row is null) { StatusText = "Select an entry first."; return false; }
        var candidates = loaded.Where(entry => entry.ErrorRegisterId != row.Entry.ErrorRegisterId).ToArray();
        if (candidates.Length == 0) { StatusText = "There is no other entry to merge into."; return false; }
        if (prompts.Merge(row.Entry, candidates) is not { } answer) return false;
        if (candidates.All(entry => entry.ErrorRegisterId != answer.IntoId)) { StatusText = "Choose one of the listed entries to merge into."; return false; }
        var problem = HealthCheckView.ValidateSafeText(answer.Reason, "a reason");
        if (problem is not null) { StatusText = problem; return false; }
        var source = store();
        if (source is null) { StatusText = NotAvailableText; return false; }
        try
        {
            await source.MergeAsync(row.Entry.ErrorRegisterId, answer.IntoId, answer.Reason.Trim());
        }
        catch (Exception exception)
        {
            StatusText = FailureText("The entries were not merged. ", exception, "ERROR_REGISTER_MERGE_FAILED");
            return false;
        }
        await LoadAsync();
        Select(answer.IntoId);
        await ShowDetailAsync();
        StatusText = $"{row.Id} was merged into ER-{answer.IntoId:0000}.";
        return true;
    }

    /// <summary>Puts the privacy-safe summary of the selected entry on the clipboard.</summary>
    public bool CopySummary()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        if (detail is null || SelectedRow is null || detail.Entry.ErrorRegisterId != SelectedRow.Entry.ErrorRegisterId) { StatusText = "Select an entry and wait for its details to load."; return false; }
        try
        {
            copy(SummaryFor(detail));
            StatusText = $"The summary of {SelectedRow.Id} was copied. It holds no customer data.";
            return true;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            StatusText = "The clipboard is in use by another program. Try again.";
            return false;
        }
    }

    private static void CopyToClipboard(string text) => Clipboard.SetText(text);

    internal static string FailureText(string prefix, Exception exception, string eventId) =>
        DesktopDiagnostics.WithReference(prefix + DesktopFriendlyError.Describe(exception),
            DesktopDiagnostics.Record(exception, "OperationsAdministration.ErrorRegister", eventId));

    // ---- grid ----------------------------------------------------------------------------------------------------

    private static Button MakeButton(string text, Func<Task> run)
    {
        var button = new Button { Content = text, MinHeight = 44, Margin = new Thickness(0, 0, 8, 4), IsEnabled = false };
        button.Click += async (_, _) => await run();
        AutomationProperties.SetName(button, text.TrimEnd('…'));
        return button;
    }

    private void BuildColumns()
    {
        static DataGridTextColumn Text(string header, string path, double width) => new()
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
            ElementStyle = WrapStyle()
        };
        // DS-22: reading order - what happened, when, how often, status; the developer columns come last.
        grid.Columns.Add(new DataGridTextColumn { Header = "What happened", Binding = new Binding(nameof(ErrorRow.WhatHappened)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 260, ElementStyle = WrapStyle() });
        grid.Columns.Add(Text("Last seen", nameof(ErrorRow.LastSeen), 140));
        grid.Columns.Add(Text("Count", nameof(ErrorRow.Count), 70));
        grid.Columns.Add(Text("Status", nameof(ErrorRow.Status), 110));
        grid.Columns.Add(Text("ID", nameof(ErrorRow.Id), 80));
        grid.Columns.Add(Text("Event", nameof(ErrorRow.Event), 200));
        grid.Columns.Add(Text("Where", nameof(ErrorRow.Where), 220));
        grid.Columns.Add(Text("Error", nameof(ErrorRow.Error), 150));
        grid.Columns.Add(Text("First seen", nameof(ErrorRow.FirstSeen), 140));
        grid.Columns.Add(Text("Versions", nameof(ErrorRow.Versions), 120));
        grid.Columns.Add(Text("Ticket", nameof(ErrorRow.Ticket), 80));

        occurrenceGrid.Columns.Add(Text("Time", nameof(ErrorOccurrenceRow.Time), 140));
        occurrenceGrid.Columns.Add(Text("Version", nameof(ErrorOccurrenceRow.Version), 90));
        occurrenceGrid.Columns.Add(Text("Report", nameof(ErrorOccurrenceRow.Report), 110));
        occurrenceGrid.Columns.Add(Text("Store", nameof(ErrorOccurrenceRow.Store), 80));
        occurrenceGrid.Columns.Add(Text("Date", nameof(ErrorOccurrenceRow.Date), 110));
        occurrenceGrid.Columns.Add(Text("Ref", nameof(ErrorOccurrenceRow.Ref), 130));
        occurrenceGrid.Columns.Add(Text("Role", nameof(ErrorOccurrenceRow.Role), 110));
    }

    private static Style WrapStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 4, 6, 4)));
        return style;
    }

    /// <summary>Recurred rows are red, New rows bold (design 9.2).</summary>
    private static Style BuildRowStyle()
    {
        var style = new Style(typeof(DataGridRow));
        var recurred = new DataTrigger { Binding = new Binding(nameof(ErrorRow.StatusKind)), Value = nameof(ErrorStatus.Recurred) };
        recurred.Setters.Add(new Setter(Control.ForegroundProperty, ThemeBrushes.Get("Critical")));
        recurred.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Triggers.Add(recurred);
        var created = new DataTrigger { Binding = new Binding(nameof(ErrorRow.StatusKind)), Value = nameof(ErrorStatus.New) };
        created.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Triggers.Add(created);
        var quiet = new DataTrigger { Binding = new Binding(nameof(ErrorRow.StatusKind)), Value = nameof(ErrorStatus.Expected) };
        quiet.Setters.Add(new Setter(Control.ForegroundProperty, ThemeBrushes.Get("SecondaryText")));
        style.Triggers.Add(quiet);
        return style;
    }

    // ---- the default prompts -------------------------------------------------------------------------------------

    private sealed class DialogPrompts(DependencyObject owner) : IErrorRegisterPrompts
    {
        public string? Reason(string title, string intro)
        {
            var answer = DiagnosticsDialog.Ask(owner, title, intro, title, [new("Reason (plain words)", DialogFieldKind.Text)],
                values => HealthCheckView.ValidateSafeText(values[0], "a reason"));
            return answer?[0];
        }

        public string? Ticket()
        {
            var answer = DiagnosticsDialog.Ask(owner, "Link ticket", "Link the fix ticket from FIX-TICKET-REGISTER.md. The entry is marked seen.", "Link ticket",
                [new("Ticket id (for example FT-007)", DialogFieldKind.Text, "FT-")], values => ValidateTicket(values[0]));
            return answer?[0];
        }

        public (string Version, string? Ticket, string Reason)? Fixed(IReadOnlyList<string> versionsSeen)
        {
            var answer = DiagnosticsDialog.Ask(owner, "Mark fixed in version",
                "Use this for a fix that shipped before the fix manifest existed. A later occurrence on that version or newer marks the entry Recurred.", "Mark fixed",
                [
                    new("Fixed in version (type or choose)", DialogFieldKind.Choice, versionsSeen.FirstOrDefault() ?? "", versionsSeen),
                    new("Ticket id (optional, for example FT-007)", DialogFieldKind.Text),
                    new("Reason (plain words)", DialogFieldKind.Text)
                ],
                values => ValidateVersion(values[0])
                    ?? (values[1].Length > 0 ? ValidateTicket(values[1]) : null)
                    ?? HealthCheckView.ValidateSafeText(values[2], "a reason"));
            return answer is null ? null : (answer[0], answer[1].Length > 0 ? answer[1] : null, answer[2]);
        }

        public (int IntoId, string Reason)? Merge(ErrorRegisterEntry from, IReadOnlyList<ErrorRegisterEntry> candidates)
        {
            var labels = candidates.Select(entry => $"{entry.DisplayId}  {entry.EventCode}  {ErrorLabel(entry)}").ToArray();
            var answer = DiagnosticsDialog.Ask(owner, "Merge into",
                $"{from.DisplayId} and the entry you choose are the same fault under two names. All occurrences move to the chosen entry; {from.DisplayId} leaves the list.", "Merge",
                [
                    new("Merge into", DialogFieldKind.Choice, labels.FirstOrDefault() ?? "", labels),
                    new("Reason (plain words)", DialogFieldKind.Text)
                ],
                values => labels.Contains(values[0]) ? HealthCheckView.ValidateSafeText(values[1], "a reason") : "Choose the entry to merge into.");
            if (answer is null) return null;
            var chosen = Array.IndexOf(labels, answer[0]);
            return chosen < 0 ? null : (candidates[chosen].ErrorRegisterId, answer[1]);
        }
    }
}
