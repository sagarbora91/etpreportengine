using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class ImportEvidenceViewTests
{
    [Fact]
    public void Owner_keeps_source_files_for_earlier_imports_from_the_chosen_folders()
    {
        RunSta(() =>
        {
            var service = new FakeEvidenceService();
            var view = new ImportEvidenceView(() => "synthetic", () => true, value =>
            {
                Assert.Equal("synthetic", value);
                return service;
            }, () => [@"C:\Synthetic\Package", @"C:\Synthetic\Fix"]);

            Assert.True(view.CanKeepEarlierSources);
            Assert.NotNull(FindButton(view, ImportEvidenceView.KeepEarlierSourcesText));
            view.KeepEarlierSourcesAsync().GetAwaiter().GetResult();

            Assert.Equal([@"C:\Synthetic\Package", @"C:\Synthetic\Fix"], Assert.Single(service.Runs));
            Assert.Contains("12 files checked; 3 matched an import. 2 source files kept (2.0 MB), 1 already held.", view.StatusText);
            Assert.Contains("Nothing was imported.", view.StatusText);
            Assert.StartsWith("5 source files held, 13.0 MB, for 9 imported files.", view.SizeText);
            Assert.Contains("4 imported files have no source file held", view.SizeText);
        });
    }

    [Fact]
    public void Only_the_owner_can_keep_source_files_and_a_cancelled_choice_changes_nothing()
    {
        RunSta(() =>
        {
            var service = new FakeEvidenceService();
            var owner = false;
            var view = new ImportEvidenceView(() => "synthetic", () => owner, _ => service, () => null);

            Assert.False(view.CanKeepEarlierSources);
            view.KeepEarlierSourcesAsync().GetAwaiter().GetResult();
            Assert.Equal("Owner permission is required.", view.StatusText);
            Assert.Empty(service.Runs);

            owner = true;
            view.RefreshAccessState();
            Assert.True(view.CanKeepEarlierSources);
            view.KeepEarlierSourcesAsync().GetAwaiter().GetResult();
            Assert.Equal("No folder was chosen. Nothing changed.", view.StatusText);
            Assert.Empty(service.Runs);

            // Every role can see the evidence size.
            owner = false;
            view.RefreshAsync().GetAwaiter().GetResult();
            Assert.StartsWith("5 source files held", view.SizeText);
        });
    }

    [Fact]
    public void Settings_hosts_the_evidence_view_and_refreshes_its_owner_action()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "EtpEvidenceViewTests", Guid.NewGuid().ToString("N"));
            var settings = new SettingsWorkspaceView(
                new(new DesktopSettingsStore(root), new DesktopConnectionState(
                    @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True")),
                _ => throw new InvalidOperationException("No lifecycle call expected."),
                _ => throw new InvalidOperationException("No administration call expected."), root);
            var evidence = ((Panel)settings.FindName("DataTruthMastersHost")).Children.OfType<ImportEvidenceView>().Single();

            Assert.False(evidence.CanKeepEarlierSources);
            settings.UpdateAccess(new(true, true));
            Assert.True(evidence.CanKeepEarlierSources);
            settings.UpdateAccess(new(true, false));
            Assert.False(evidence.CanKeepEarlierSources);
        });
    }

    // Review 1.9.3 finding 3: an import in progress makes the screen say so, not hang and show a generic error.
    [Fact]
    public void An_import_in_progress_is_reported_as_busy_for_the_size_and_the_walk()
    {
        RunSta(() =>
        {
            var service = new FakeEvidenceService { Busy = true };
            var view = new ImportEvidenceView(() => "synthetic", () => true, _ => service, () => [@"C:\Synthetic\Package"]);

            view.RefreshAsync().GetAwaiter().GetResult();
            Assert.Equal(ImportEvidenceView.BusySummaryText, view.SizeText);

            view.KeepEarlierSourcesAsync().GetAwaiter().GetResult();
            Assert.Equal(ImportEvidenceView.BusyWalkText, view.StatusText);
            Assert.True(view.CanKeepEarlierSources);
        });
    }

    // Review 1.9.3 finding 4: the walk can be stopped, and it reports what it did before it stopped.
    [Fact]
    public void Owner_can_stop_keeping_source_files_and_sees_the_counts_so_far()
    {
        RunSta(() =>
        {
            var service = new FakeEvidenceService { WaitForStop = true };
            var view = new ImportEvidenceView(() => "synthetic", () => true, _ => service, () => [@"C:\Synthetic\Package"]);
            Assert.False(view.CanStop);
            Assert.NotNull(FindButton(view, ImportEvidenceView.StopText));

            var run = view.KeepEarlierSourcesAsync();
            Assert.False(run.IsCompleted);
            Assert.True(view.CanStop);
            Assert.False(view.CanKeepEarlierSources);

            view.StopKeepingEarlierSources();
            Pump(); // runs the continuation if the dispatcher, not the caller, resumes it
            Assert.True(run.IsCompleted);
            run.GetAwaiter().GetResult();
            Assert.True(service.Stopped);
            Assert.StartsWith("Stopped. 4 files checked; 2 matched an import. 1 source files kept", view.StatusText);
            Assert.Contains("The source files already kept stay", view.StatusText);
            Assert.False(view.CanStop);
            Assert.True(view.CanKeepEarlierSources);
        });
    }

    [Fact]
    public void The_walk_names_each_kind_of_file_it_could_not_keep()
    {
        var text = ImportEvidenceView.Describe(new EarlierImportEvidenceResult(10, 6, 2, 1, 1, 1048576L,
            Busy: 1, DatabaseFailures: 2, FoldersTooDeep: 3));
        Assert.Contains("1 files or archives could not be read and were skipped.", text);
        Assert.Contains("1 matching files were held by an import in progress", text);
        Assert.Contains("2 matching files could not be stored because of a database error", text);
        Assert.Contains("3 folders lie too deep and were not checked", text);
        // A database failure is not reported as a file that could not be read.
        Assert.DoesNotContain("could not be read", ImportEvidenceView.Describe(new EarlierImportEvidenceResult(3, 3, 1, 0, 0, 10, DatabaseFailures: 2)));
        Assert.StartsWith("Stopped: the database connection was lost", ImportEvidenceView.Describe(
            new EarlierImportEvidenceResult(3, 3, 1, 0, 0, 10, DatabaseFailures: 1, Stop: EarlierImportStop.ConnectionLost)));
    }

    // Review 1.9.3 finding 1 (spec 12): the action is on Imports → Problems, for the Owner only.
    [Fact]
    public void Problems_tab_offers_the_owner_keep_source_files_for_earlier_imports()
    {
        RunSta(() =>
        {
            var imports = new Etp.Reporting.Desktop.Modules.Imports.ImportWorkspaceView(
                new Etp.Reporting.Desktop.Modules.Imports.DesktopImportCoordinator(_ => throw new InvalidOperationException("No import expected.")),
                () => "synthetic");
            var service = new FakeEvidenceService();
            var owner = new ImportEvidenceView(() => "synthetic", () => true, _ => service, () => [@"C:\Synthetic\Package"]);
            var problems = new Etp.Reporting.Desktop.Modules.Imports.ImportProblemsView(imports,
                () => Task.FromResult<IReadOnlyList<Etp.Reporting.Desktop.Modules.Imports.ImportProblem>>([]), owner);

            Assert.Same(owner, problems.Evidence);
            Assert.True(problems.KeepEarlierSourcesVisible);
            var action = Descendants(problems).OfType<Button>().First(button => Equals(button.Content, ImportEvidenceView.KeepEarlierSourcesText)
                && !Descendants(owner).Contains(button));
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([@"C:\Synthetic\Package"], Assert.Single(service.Runs));
            Assert.True(owner.IsExpanded);
            Assert.Contains("Nothing was imported.", owner.StatusText);

            var viewer = new ImportEvidenceView(() => "synthetic", () => false, _ => service, () => [@"C:\Synthetic\Package"]);
            var hidden = new Etp.Reporting.Desktop.Modules.Imports.ImportProblemsView(imports,
                () => Task.FromResult<IReadOnlyList<Etp.Reporting.Desktop.Modules.Imports.ImportProblem>>([]), viewer);
            Assert.False(hidden.KeepEarlierSourcesVisible);
            Assert.Equal(Visibility.Collapsed, viewer.Visibility);
        });
    }

    [Fact]
    public void Task_search_opens_keep_source_files_on_the_problems_tab_for_the_owner_only()
    {
        var task = TaskNavigation.Search("keep source files", ShellAccess.Owner).First();
        Assert.Equal("keep-evidence", task.Id);
        Assert.Equal("Import → Problems → " + ImportEvidenceView.KeepEarlierSourcesText, task.Path);
        Assert.True(new ShellNavigationService().Navigate(task.Route, ShellAccess.Owner).IsAllowed);
        foreach (var access in new[] { ShellAccess.StoreManager, ShellAccess.Viewer })
        {
            Assert.DoesNotContain(TaskNavigation.Search("keep source files", access), found => found.Id == "keep-evidence");
            Assert.False(new ShellNavigationService().Navigate(task.Route, access).IsAllowed);
        }
    }

    [Theory]
    [InlineData("conflicts")]
    [InlineData("keep-evidence")]
    public void Problems_navigation_hosts_the_evidence_action(string taskId)
    {
        RunSta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                var navigator = new TaskNavigator(window);
                Assert.True(navigator.DisplayTaskRoute(TaskNavigation.Find(taskId)!.Route));
                var problems = Assert.IsType<Etp.Reporting.Desktop.Modules.Imports.ImportProblemsView>(window.FocusedWorkspaceHost.Content);
                Assert.NotNull(problems.Evidence);
                Assert.True(problems.KeepEarlierSourcesVisible);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); window.Close(); }
        });
    }

    private sealed class FakeEvidenceService : IImportEvidenceService
    {
        public List<IReadOnlyList<string>> Runs { get; } = [];

        public bool Busy { get; init; }
        public bool WaitForStop { get; init; }
        public bool Stopped { get; private set; }

        public Task<ImportEvidenceSummary> LoadSummaryAsync(CancellationToken cancellationToken = default) => Busy
            ? Task.FromException<ImportEvidenceSummary>(new ImportEvidenceBusyException("Synthetic import in progress."))
            : Task.FromResult(new ImportEvidenceSummary(5, 13 * 1048576L, 9, 4, 400 * 1048576L));

        public Task<EarlierImportEvidenceResult> RetainEarlierImportsAsync(IReadOnlyList<string> folders,
            IProgress<int>? filesHashed = null, CancellationToken cancellationToken = default)
        {
            if (Busy) return Task.FromException<EarlierImportEvidenceResult>(new ImportEvidenceBusyException("Synthetic import in progress."));
            Runs.Add(folders);
            if (WaitForStop)
            {
                // Completes on the thread that stops it, as the walk returns its counts when cancelled.
                var stopped = new TaskCompletionSource<EarlierImportEvidenceResult>();
                cancellationToken.Register(() =>
                {
                    Stopped = true;
                    stopped.SetResult(new(4, 2, 1, 1, 0, 1048576L, Stop: EarlierImportStop.Cancelled));
                });
                return stopped.Task;
            }
            return Task.FromResult(new EarlierImportEvidenceResult(12, 3, 2, 1, 0, 2 * 1048576L));
        }
    }

    private static Button FindButton(DependencyObject root, string title) =>
        Descendants(root).OfType<Button>().Single(button => Equals(button.Content, title));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "STA test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
