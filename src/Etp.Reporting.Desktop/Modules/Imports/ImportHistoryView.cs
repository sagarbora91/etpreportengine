extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using HistoryEntry = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryEntry;
using HistoryScope = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryScope;
using ImportHistoryMessages = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryMessages;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>Re-queries persisted import outcomes on every activation; never uses session results.</summary>
public sealed class ImportHistoryView : UserControl
{
    private readonly Func<HistoryScope, Task<IReadOnlyList<HistoryEntry>>> load;
    private HistoryScope? scope;
    private int revision;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private readonly DataGrid rows = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly DataGrid diagnostics = new() { AutoGenerateColumns = false, IsReadOnly = true, Visibility = Visibility.Collapsed };
    // 1.12 U2 (wave plan C-c): the Fix buttons of the selected row, one QuietButton per action.
    private readonly WrapPanel fixes = new() { Visibility = Visibility.Collapsed };
    private int selectionRevision;
    /// <summary>The Fix buttons shown for the selected row (tests read them).</summary>
    public IReadOnlyList<ImportFixButtonModel> SelectedFixes { get; private set; } = [];
    /// <summary>The Fix buttons' services; <see cref="ImportFixServices.Current"/> unless a test sets its own.</summary>
    internal ImportFixServices? FixServices { get; set; } = ImportFixServices.Current;
    internal ImportFixHandlers FixHandlers { get; }
    public IReadOnlyList<HistoryEntry> Entries { get; private set; } = [];
    public string StatusText => status.Text;
    public string DetailText => detail.Text;
    public bool IsLoading { get; private set; }

    // Service interim (0048): a Service row carries the Service Centre's store code; the Store column names it
    // "Service Centre (code)" through the label the shell gives (StoreScopeCatalog). Retail codes are shown unchanged.
    private Func<string?, string?> storeLabel = code => code;
    public void SetStoreLabels(Func<string?, string?> label) { storeLabel = label ?? (code => code); rows.Items.Refresh(); }
    /// <summary>The text the Store column shows for a stored store code.</summary>
    public string? StoreText(string? code) => storeLabel(code);

    public ImportHistoryView(Func<HistoryScope, Task<IReadOnlyList<HistoryEntry>>> load)
    {
        this.load = load;
        FixHandlers = new ImportFixHandlers(this, text => detail.Text = text, ReloadAsync);
        // Template A (List): the saved outcomes fill the page; the selected row's details share the lower third (no MaxHeight).
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(2, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel();
        // DS-14/DS-24: the shell header owns the title. The first line is the count (the verdict of the period); the
        // one caption says what the times are. The longer explanation is a tooltip on the caption.
        status.SetResourceReference(FrameworkElement.StyleProperty, "BodyText");
        var caption = new TextBlock { Text = "Times are your local time.", ToolTip = "Imported files and repeat attempts are saved here. Received files is the separate document inbox. Undetected dates use the attempt's UTC date.", Margin = DesignTokens.Gap(null, "Space4", null, "Space8") };
        caption.SetResourceReference(FrameworkElement.StyleProperty, "CaptionText");
        var refresh = new Button { Content = "Refresh", HorizontalAlignment = HorizontalAlignment.Left, Margin = DesignTokens.Gap(null, null, null, "Space8") };
        refresh.Click += async (_, _) => { if (scope is not null) await ActivateAsync(scope); };
        heading.Children.Add(status); heading.Children.Add(caption); heading.Children.Add(refresh); root.Children.Add(heading);
        // 1.12 U2 (DS-12/13): the result as a tone chip comes first (Refused red, Conflict red, Already imported grey, Imported green).
        rows.Columns.Add(new DataGridTextColumn { Header = "Result", Binding = new Binding("Result") { Converter = new ResultWordConverter() }, Width = 140,
            ElementStyle = ResultChipStyle() });
        foreach (var (title, path, width) in new[] {
            // DS-14: reading order is Recorded, Verdict, File, Store, Report, Period, Rows; the codes (Outcome, Failure, Stage) come last.
            ("Recorded", "RecordedUtc", 155d), ("Verdict", "Result", 360d), ("File", "Result.FileName", 230d),
            ("Store", "Result.StoreCode", 80d), ("Report", "Result.ReportCode", 80d), ("Period", "Result.Period", 225d),
            ("Rows", "Result.RowsProcessed", 75d), ("New", "Result.NewRows", 75d), ("Present", "Result.AlreadyPresentRows", 75d), ("Conflicts", "Result.ConflictRows", 90d),
            ("Outcome", "Result.Status", 130d), ("Failure", "Result.Failure.Code", 190d), ("Stage", "Result.Failure.Stage", 80d) })
            rows.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { StringFormat = path == "RecordedUtc" ? "dd MMM yyyy HH:mm" : null,
                Converter = path == "Result.StoreCode" ? new StoreLabelConverter(this) : path == "RecordedUtc" ? new LocalTimeConverter() : path == "Result" ? new VerdictConverter() : null }, Width = width });
        TablePresentation.Configure(rows); AutomationProperties.SetName(rows, "Saved import outcomes");
        rows.SelectionChanged += (_, _) => ShowDetails(rows.SelectedItem as HistoryEntry);
        Grid.SetRow(rows, 1); root.Children.Add(rows);
        foreach (var (title, path) in new[] { ("Severity", "Severity"), ("Issue", "Code"), ("Block", "BlockNo"), ("Row", "SourceRow"),
            ("Column", "SourceColumn"), ("Document", "DocumentRef"), ("Count", "Occurrences"), ("Guidance", "Message") })
            diagnostics.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = path == "Message" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        detail.SetResourceReference(StyleProperty, "BodyText");
        detail.Margin = DesignTokens.Gap(null, "Space8", null, "Space4");
        var details = new DockPanel();
        DockPanel.SetDock(detail, Dock.Top); details.Children.Add(detail);
        DockPanel.SetDock(fixes, Dock.Top); details.Children.Add(fixes);
        details.Children.Add(diagnostics);
        Grid.SetRow(details, 2); root.Children.Add(details); Content = root;
        AutomationProperties.SetName(this, "Durable import history");
    }

    /// <summary>
    /// Reads the same persisted outcomes the grid shows, without disturbing the grid.
    /// Problems needs the database rather than the session's last import, and must not
    /// move the History screen underneath the operator in order to get it.
    /// </summary>
    public Task<IReadOnlyList<HistoryEntry>> FetchAsync(HistoryScope value) => load(value);

    public async Task ActivateAsync(HistoryScope value)
    {
        scope = value; var current = ++revision; IsLoading = true;
        status.Text = $"Loading saved imports for {value.From:dd MMM yyyy} – {value.To:dd MMM yyyy} · {value.StoreCode ?? "All stores"}…";
        Entries = []; rows.ItemsSource = null; ShowDetails(null);
        try
        {
            var loaded = await load(value);
            if (current != revision) return;
            Entries = loaded; rows.ItemsSource = loaded;
            status.Text = CountLine(loaded) + $" · {value.From:dd MMM yyyy} – {value.To:dd MMM yyyy} · {value.StoreCode ?? "All stores"}";
            if (loaded.Count > 0) rows.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            if (current != revision) return;
            status.Text = DesktopDiagnostics.WithReference("Saved imports could not be loaded. " + DesktopFriendlyError.Describe(exception),
                DesktopDiagnostics.Record(exception, "Imports.History", "IMPORT_HISTORY_LOAD_FAILED", operation: "Saved imports load failed"));
        }
        finally { if (current == revision) IsLoading = false; }
    }

    /// <summary>DS-14: "6 files: 5 imported, 1 refused" - the status line is the verdict of the period.</summary>
    public static string CountLine(IReadOnlyList<HistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) return "No saved imports in this range";
        var refused = entries.Count(entry => entry.Result.Failed || entry.Result.Failure is not null);
        var files = entries.Count == 1 ? "1 file" : $"{entries.Count:N0} files";
        return refused == 0 ? $"{files}: all imported" : $"{files}: {entries.Count - refused:N0} imported, {refused:N0} refused";
    }

    public void SelectEntry(HistoryEntry entry) => rows.SelectedItem = entry;

    /// <summary>Saved times are UTC; the grid shows the PC's local time like everything else in ETP.</summary>
    public static DateTime ToLocal(DateTime recordedUtc) =>
        recordedUtc.Kind == DateTimeKind.Local ? recordedUtc : DateTime.SpecifyKind(recordedUtc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>IF-031: the Verdict of an empty export (the Period column shows the day it was exported).</summary>
    public const string EmptyExportVerdict = "Empty export - no rows; dated by its export day, the days it covers are not known";

    /// <summary>One plain line per saved outcome: what happened to the file and what to do about it.</summary>
    public static string Verdict(EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Failed || result.Failure is not null) return "Refused - " + ImportProblems.NextStep(result);
        if (result.ConflictRows > 0) return $"{result.ConflictRows:N0} rows conflict with data already saved - check Problems";
        // IF-028 / 1.12.1 review M-3: a re-dated copy this import retired is said here, with any day that lost its reading.
        var redated = string.Join(" ", (result.Diagnostics ?? [])
            .Where(issue => issue.Code == EtpApplication::Etp.Reporting.Application.Imports.ImportCodes.ImportRedatedRetired)
            .Select(issue => issue.Message));
        if (redated.Length > 0)
            return (ImportHistoryMessages.IsDuplicate(result.Status) ? "Already imported under this date"
                : result.NewRows > 0 ? $"Imported - {result.NewRows:N0} new rows" : "Imported - no new rows") + " - " + redated;
        if (ImportHistoryMessages.IsDuplicate(result.Status)) return "Already imported - nothing changed";
        // IF-031: the Period of an empty export is its export day, not the window it was asked for.
        if (ImportHistoryMessages.IsEmptyExport(result.Status)) return EmptyExportVerdict;
        return result.NewRows > 0 ? $"Imported - {result.NewRows:N0} new rows" : "Imported - no new rows";
    }

    private sealed class LocalTimeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is DateTime utc ? ToLocal(utc) : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }

    private sealed class VerdictConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult result ? Verdict(result) : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }

    private sealed class StoreLabelConverter(ImportHistoryView view) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => view.StoreText(value as string);
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
    private void ShowDetails(HistoryEntry? entry)
    {
        detail.Text = entry is null ? "Select an import to see its saved diagnostics." : Describe(entry.Result);
        diagnostics.ItemsSource = entry?.Result.Diagnostics;
        diagnostics.Visibility = entry?.Result.Diagnostics?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = ShowFixesAsync(entry);
    }

    /// <summary>The selected row's Fix buttons (one location lookup per selection; a newer selection wins).</summary>
    internal async Task ShowFixesAsync(HistoryEntry? entry)
    {
        var current = ++selectionRevision;
        SelectedFixes = []; fixes.Children.Clear(); fixes.Visibility = Visibility.Collapsed;
        if (entry is null || FixServices is not { } services || !ImportProblems.IsProblem(entry.Result)) return;
        try
        {
            var access = await services.LoadAccess(CancellationToken.None);
            var found = await ImportFixRows.ForAsync(entry.Result, access, services, FixHandlers);
            if (current != selectionRevision) return;
            SelectedFixes = found;
            foreach (var fix in found) fixes.Children.Add(FixButton(fix));
            fixes.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Imports.History", "IMPORT_FIX_LOOKUP_FAILED", DesktopDiagnosticSeverity.Warning);
        }
    }

    private static Button FixButton(ImportFixButtonModel fix)
    {
        var button = new Button { Content = fix.Label, ToolTip = fix.Explanation, Margin = DesignTokens.Gap(null, null, "Space8", null) };
        button.SetResourceReference(StyleProperty, "QuietButton");
        AutomationProperties.SetName(button, fix.AutomationName);
        AutomationProperties.SetHelpText(button, fix.Explanation);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await fix.Run(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Task ReloadAsync() => scope is null ? Task.CompletedTask : ActivateAsync(scope);

    /// <summary>The Result column's word (ST2: the same word as Problems and the shop window).</summary>
    public static string ResultWord(EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Failed || result.Failure is not null) return "Refused";
        if (result.ConflictRows > 0) return "Conflict";
        if (ImportHistoryMessages.IsDuplicate(result.Status)) return "Already imported";
        return result.Status == "Unknown layout" ? "Unknown layout" : "Imported";
    }

    /// <summary>DS-13: the Result chip, keyed on the word the converter shows (grey for Already imported).</summary>
    private static Style ResultChipStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 3, 8, 3)));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(TextBlock.BackgroundProperty, Etp.Reporting.Desktop.Themes.ThemeBrushes.Get("SurfaceSecondary")));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Etp.Reporting.Desktop.Themes.ThemeBrushes.Get("SecondaryText")));
        foreach (var (word, tone) in new[] { ("Refused", StatusTone.Critical), ("Conflict", StatusTone.Critical), ("Unknown layout", StatusTone.Warning), ("Imported", StatusTone.Success) })
        {
            var (fill, _, text) = StatusTones.BrushesFor(tone);
            var trigger = new DataTrigger { Binding = new Binding("Result") { Converter = new ResultWordConverter() }, Value = word };
            trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, Etp.Reporting.Desktop.Themes.ThemeBrushes.Get(fill)));
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Etp.Reporting.Desktop.Themes.ThemeBrushes.Get(text)));
            style.Triggers.Add(trigger);
        }
        return style;
    }

    private sealed class ResultWordConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult result ? ResultWord(result) : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }

    // Failure code, stage and message as the attempt recorded them (IF-017), then commit and evidence state.
    private static string Describe(EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult result)
    {
        // A repeat file says "already imported" whichever duplicate outcome text it was saved with (FIX-08).
        var message = result.Failure is null && result.ConflictRows == 0 && ImportHistoryMessages.IsDuplicate(result.Status)
            ? ImportHistoryMessages.AlreadyImported : result.Message;
        var text = $"{result.FileName}: {result.Status}. {message}";
        if (result.Failure is { } failure)
            text += $" Failure {failure.Code} at {failure.Stage}" + (failure.SqlNumber is { } number ? $" (SQL {number})." : ".");
        if (result.CommitState is { } commit) text += $" Transaction: {commit}.";
        if (result.Evidence is { } evidence) text += $" Evidence: {evidence}.";
        return text;
    }
}
