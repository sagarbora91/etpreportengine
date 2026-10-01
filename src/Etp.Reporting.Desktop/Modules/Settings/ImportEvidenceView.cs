extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Infrastructure.SqlServer;
using EarlierImportEvidenceResult = EtpApplication::Etp.Reporting.Application.Imports.EarlierImportEvidenceResult;
using ImportEvidenceService = EtpApplication::Etp.Reporting.Application.Imports.IImportEvidenceService;
using ImportEvidenceSummary = EtpApplication::Etp.Reporting.Application.Imports.ImportEvidenceSummary;

namespace Etp.Reporting.Desktop.Modules.Settings;

/// <summary>
/// Settings → Database: the size of the source files kept inside the database as import evidence, and the
/// Owner's "Keep source files for earlier imports…" (IF-023, Owner decision OD-2; spec 11.2 and 12).
/// </summary>
public sealed class ImportEvidenceView : UserControl
{
    public const string KeepEarlierSourcesText = "Keep source files for earlier imports…";

    private readonly Func<string> connectionString;
    private readonly Func<bool> canAdminister;
    private readonly Func<string, ImportEvidenceService> serviceFactory;
    private readonly Func<IReadOnlyList<string>?> chooseFolders;
    private readonly TextBlock size = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly Button keepEarlier;
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
        var refresh = Button("Refresh evidence size", () => RunAsync(LoadSummaryAsync));
        keepEarlier = Button(KeepEarlierSourcesText, KeepEarlierSourcesAsync);
        keepEarlier.ToolTip = "Owner only. Hashes the .xlsx, .csv and .zip files in the folders you choose and keeps those that match an earlier import. Nothing is imported.";
        var actions = new WrapPanel();
        actions.Children.Add(refresh);
        actions.Children.Add(keepEarlier);
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Every import keeps its source file inside the database, so the figures can be traced back to the exact file. Files imported before this release can be added from the folders that still hold them.",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray
        });
        body.Children.Add(size);
        body.Children.Add(actions);
        body.Children.Add(status);
        var expander = new Expander { Header = "Database — source files kept as evidence", Content = body, Margin = new(0, 18, 0, 0) };
        AutomationProperties.SetName(expander, "Database evidence");
        expander.Expanded += async (_, _) => { if (!loaded) await RunAsync(LoadSummaryAsync); };
        Content = expander;
        RefreshAccessState();
    }

    public string SizeText => size.Text;
    public string StatusText => status.Text;
    public bool CanKeepEarlierSources => keepEarlier.IsEnabled;

    public void RefreshAccessState() => keepEarlier.IsEnabled = canAdminister() && !busy;

    public Task RefreshAsync() => RunAsync(LoadSummaryAsync);

    /// <summary>"Keep source files for earlier imports…": choose folders, then store the matching source files.</summary>
    public Task KeepEarlierSourcesAsync() => RunAsync(KeepEarlierSourcesCoreAsync);

    private async Task LoadSummaryAsync()
    {
        size.Text = Describe(await serviceFactory(connectionString()).LoadSummaryAsync());
        loaded = true;
    }

    private async Task KeepEarlierSourcesCoreAsync()
    {
        if (!canAdminister()) { status.Text = "Owner permission is required."; return; }
        var folders = chooseFolders();
        if (folders is not { Count: > 0 }) { status.Text = "No folder was chosen. Nothing changed."; return; }
        var service = serviceFactory(connectionString());
        status.Text = "Hashing source files…";
        var progress = new Progress<int>(count =>
            Dispatcher.InvokeAsync(() => { if (busy) status.Text = $"Hashing source files… {count:N0} checked."; }));
        var result = await service.RetainEarlierImportsAsync(folders, progress);
        status.Text = Describe(result);
        size.Text = Describe(await service.LoadSummaryAsync());
        loaded = true;
    }

    public static string Describe(ImportEvidenceSummary summary)
    {
        var text = $"{summary.FilesHeld:N0} source files held, {Megabytes(summary.BytesHeld)}, for {summary.ImportedSources:N0} imported files. Database data files: {Megabytes(summary.DatabaseDataBytes)}.";
        return summary.ImportedSourcesWithoutFile == 0 ? text
            : text + $" {summary.ImportedSourcesWithoutFile:N0} imported files have no source file held; use \"{KeepEarlierSourcesText}\" with the folders that still hold them.";
    }

    public static string Describe(EarlierImportEvidenceResult result)
    {
        var text = $"{result.FilesHashed:N0} files checked; {result.Matched:N0} matched an import. {result.Retained:N0} source files kept ({Megabytes(result.BytesRetained)}), {result.AlreadyHeld:N0} already held. Nothing was imported.";
        return result.Skipped == 0 ? text : text + $" {result.Skipped:N0} files or archives could not be read and were skipped.";
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

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; IsEnabled = false;
        try { await action(); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.Evidence", "IMPORT_EVIDENCE_OPERATION_FAILED");
            status.Text = DesktopFriendlyError.Describe(exception);
        }
        finally { busy = false; IsEnabled = true; RefreshAccessState(); }
    }

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(4) };
        button.Click += async (_, _) => await action();
        AutomationProperties.SetName(button, text);
        return button;
    }
}
