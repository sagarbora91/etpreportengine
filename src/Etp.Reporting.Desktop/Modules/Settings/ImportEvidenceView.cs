extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Infrastructure.SqlServer;
using EarlierImportEvidenceResult = EtpApplication::Etp.Reporting.Application.Imports.EarlierImportEvidenceResult;
using EarlierImportStop = EtpApplication::Etp.Reporting.Application.Imports.EarlierImportStop;
using ImportEvidenceBusyException = EtpApplication::Etp.Reporting.Application.Imports.ImportEvidenceBusyException;
using ImportEvidenceService = EtpApplication::Etp.Reporting.Application.Imports.IImportEvidenceService;
using ImportEvidenceSummary = EtpApplication::Etp.Reporting.Application.Imports.ImportEvidenceSummary;

namespace Etp.Reporting.Desktop.Modules.Settings;

/// <summary>
/// The size of the source files kept inside the database as import evidence, and the Owner's "Keep source
/// files for earlier imports…" (IF-023, Owner decision OD-2; spec 11.2 and 12). Settings → Database and
/// Imports → Problems both host it.
/// </summary>
public sealed class ImportEvidenceView : UserControl
{
    public const string KeepEarlierSourcesText = "Keep source files for earlier imports…";
    public const string StopText = "Stop keeping source files";
    public const string BusySummaryText = "An import is running, so the evidence size cannot be read yet. Use \"Refresh evidence size\" when it finishes.";
    public const string BusyWalkText = "An import is running, so the earlier imports cannot be listed yet. Nothing was changed; start \"" + KeepEarlierSourcesText + "\" again when it finishes.";

    private readonly Func<string> connectionString;
    private readonly Func<bool> canAdminister;
    private readonly Func<string, ImportEvidenceService> serviceFactory;
    private readonly Func<IReadOnlyList<string>?> chooseFolders;
    private readonly TextBlock size = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly Expander expander;
    private readonly Button refresh;
    private readonly Button keepEarlier;
    private readonly Button stop;
    private CancellationTokenSource? walk;
    private bool busy;
    private bool loaded;

    public ImportEvidenceView(Func<string> connectionString, Func<bool> canAdminister,
        Func<string, ImportEvidenceService>? serviceFactory = null, Func<IReadOnlyList<string>?>? chooseFolders = null)
    {
        this.connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        this.canAdminister = canAdminister ?? throw new ArgumentNullException(nameof(canAdminister));
        this.serviceFactory = serviceFactory ?? (value => new SqlServerImportEvidenceService(value));
        this.chooseFolders = chooseFolders ?? ChooseFolders;
        AutomationProperties.SetName(size, "Database evidence size");
        AutomationProperties.SetName(status, "Source file evidence status");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        refresh = Button("Refresh evidence size", () => RunAsync(LoadSummaryAsync));
        keepEarlier = Button(KeepEarlierSourcesText, KeepEarlierSourcesAsync);
        keepEarlier.ToolTip = "Owner only. Hashes the .xlsx, .csv and .zip files in the folders you choose and keeps those that match an earlier import. Nothing is imported.";
        stop = Button(StopText, () => { StopKeepingEarlierSources(); return Task.CompletedTask; });
        stop.ToolTip = "Stops after the current file. The source files already kept stay in the database.";
        var actions = new WrapPanel();
        actions.Children.Add(refresh);
        actions.Children.Add(keepEarlier);
        actions.Children.Add(stop);
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Every import keeps its source file inside the database, so the figures can be traced back to the exact file. Files imported before this release can be added from the folders that still hold them.",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray
        });
        body.Children.Add(size);
        body.Children.Add(actions);
        body.Children.Add(status);
        expander = new Expander { Header = "Database — source files kept as evidence", Content = body, Margin = new(0, 18, 0, 0) };
        AutomationProperties.SetName(expander, "Database evidence");
        expander.Expanded += async (_, _) => { if (!loaded) await RunAsync(LoadSummaryAsync); };
        Content = expander;
        RefreshAccessState();
    }

    public string SizeText => size.Text;
    public string StatusText => status.Text;
    public bool CanKeepEarlierSources => keepEarlier.IsEnabled;
    public bool CanStop => stop.IsEnabled;
    public bool IsExpanded => expander.IsExpanded;
    public bool IsOwner => canAdminister();

    public void RefreshAccessState()
    {
        keepEarlier.IsEnabled = canAdminister() && !busy;
        refresh.IsEnabled = !busy;
        stop.IsEnabled = walk is not null;
    }

    /// <summary>Opens the section and moves keyboard focus to the Owner's action, e.g. from Imports → Problems.</summary>
    public void Reveal()
    {
        expander.IsExpanded = true;
        if (keepEarlier.IsEnabled) keepEarlier.Focus();
    }

    public Task RefreshAsync() => RunAsync(LoadSummaryAsync);

    /// <summary>"Keep source files for earlier imports…": choose folders, then store the matching source files.</summary>
    public Task KeepEarlierSourcesAsync() => RunAsync(KeepEarlierSourcesCoreAsync);

    /// <summary>Stops the walk after the current file; what it already stored stays.</summary>
    public void StopKeepingEarlierSources()
    {
        if (walk is not { } running) return;
        // Before Cancel: the walk may finish, and write its counts, inside the call.
        status.Text = "Stopping after the current file…";
        running.Cancel();
    }

    private async Task LoadSummaryAsync()
    {
        try { size.Text = Describe(await serviceFactory(connectionString()).LoadSummaryAsync()); }
        catch (ImportEvidenceBusyException) { size.Text = BusySummaryText; return; }
        loaded = true;
    }

    private async Task KeepEarlierSourcesCoreAsync()
    {
        if (!canAdminister()) { status.Text = "Owner permission is required."; return; }
        var folders = chooseFolders();
        if (folders is not { Count: > 0 }) { status.Text = "No folder was chosen. Nothing changed."; return; }
        var service = serviceFactory(connectionString());
        status.Text = "Hashing source files…";
        using var cancellation = new CancellationTokenSource();
        walk = cancellation;
        RefreshAccessState();
        var progress = new Progress<int>(count =>
            Dispatcher.InvokeAsync(() => { if (walk is not null && !walk.IsCancellationRequested) status.Text = $"Hashing source files… {count:N0} checked."; }));
        EarlierImportEvidenceResult result;
        try { result = await service.RetainEarlierImportsAsync(folders, progress, cancellation.Token); }
        catch (ImportEvidenceBusyException) { status.Text = BusyWalkText; return; }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            // Stopped while the earlier imports were still being listed (a cancelled command can fail as a database error).
            status.Text = "Stopped before any file was checked. Nothing changed.";
            return;
        }
        finally { walk = null; RefreshAccessState(); }
        status.Text = Describe(result);
        await LoadSummaryAsync();
    }

    public static string Describe(ImportEvidenceSummary summary)
    {
        var text = $"{summary.FilesHeld:N0} source files held, {Megabytes(summary.BytesHeld)}, for {summary.ImportedSources:N0} imported files. Database data files: {Megabytes(summary.DatabaseDataBytes)}.";
        return summary.ImportedSourcesWithoutFile == 0 ? text
            : text + $" {summary.ImportedSourcesWithoutFile:N0} imported files have no source file held; use \"{KeepEarlierSourcesText}\" with the folders that still hold them.";
    }

    public static string Describe(EarlierImportEvidenceResult result)
    {
        var text = result.Stop switch
        {
            EarlierImportStop.Cancelled => "Stopped. ",
            EarlierImportStop.ConnectionLost => "Stopped: the database connection was lost and could not be opened again. ",
            _ => ""
        };
        text += $"{result.FilesHashed:N0} files checked; {result.Matched:N0} matched an import. {result.Retained:N0} source files kept ({Megabytes(result.BytesRetained)}), {result.AlreadyHeld:N0} already held. Nothing was imported.";
        if (result.Skipped > 0) text += $" {result.Skipped:N0} files or archives could not be read and were skipped.";
        if (result.Busy > 0) text += $" {result.Busy:N0} matching files were held by an import in progress and were not kept; run this again when it finishes.";
        if (result.DatabaseFailures > 0) text += $" {result.DatabaseFailures:N0} matching files could not be stored because of a database error; see the diagnostics log and run this again.";
        if (result.FoldersTooDeep > 0) text += $" {result.FoldersTooDeep:N0} folders lie too deep and were not checked; choose them directly.";
        if (result.Stop != EarlierImportStop.None) text += " The source files already kept stay; run it again to continue.";
        return text;
    }

    private static string Megabytes(long bytes) => $"{bytes / 1048576d:N1} MB";

    private static IReadOnlyList<string>? ChooseFolders()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folders that hold the earlier ETP exports",
            Multiselect = true
        };
        return dialog.ShowDialog() == true ? dialog.FolderNames : null;
    }

    // Only the actions are disabled while one runs, so Stop stays usable during the walk.
    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; RefreshAccessState();
        try { await action(); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.Evidence", "IMPORT_EVIDENCE_OPERATION_FAILED");
            status.Text = DesktopFriendlyError.Describe(exception);
        }
        finally { busy = false; RefreshAccessState(); }
    }

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(4) };
        button.Click += async (_, _) => await action();
        AutomationProperties.SetName(button, text);
        return button;
    }
}
