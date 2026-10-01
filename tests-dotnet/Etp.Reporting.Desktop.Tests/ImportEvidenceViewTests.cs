using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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

    private sealed class FakeEvidenceService : IImportEvidenceService
    {
        public List<IReadOnlyList<string>> Runs { get; } = [];

        public Task<ImportEvidenceSummary> LoadSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportEvidenceSummary(5, 13 * 1048576L, 9, 4, 400 * 1048576L));

        public Task<EarlierImportEvidenceResult> RetainEarlierImportsAsync(IReadOnlyList<string> folders,
            IProgress<int>? filesHashed = null, CancellationToken cancellationToken = default)
        {
            Runs.Add(folders);
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

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
