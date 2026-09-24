using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class ImportRetryClosureTests
{
    [Theory]
    [InlineData(AccessRole.Owner)]
    [InlineData(AccessRole.StoreManager)]
    [InlineData(AccessRole.Viewer)]
    public async Task Problems_button_retries_only_failed_workbook_and_respects_role(AccessRole role)
    {
        var database = new SqlDatabaseFixture();
        var folder = Path.Combine(Path.GetTempPath(), "EtpRetryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await database.InitializeAsync();
            var good = Path.Combine(folder, "sales.xlsx");
            var bad = Path.Combine(folder, "R022_revenue.xlsx");
            File.Copy(Fixture("R025"), good);
            await File.WriteAllTextAsync(bad, "Deliberately corrupted synthetic workbook");
            var reader = new CountingReader();
            RunSta(() =>
            {
                var coordinator = new DesktopImportCoordinator(connection => new SqlServerImportPersistenceUseCase(connection),
                    (_, _, _, _, _, _, _) => Task.CompletedTask, reader);
                var imports = new ImportWorkspaceView(coordinator, () => database.ConnectionString);
                var access = new AccessSession("synthetic", "Synthetic Owner", AccessRole.Owner, true);
                imports.AttachHost(() => new(access.CanImport, access.CanAdminister), (_, _, _) => Task.CompletedTask, () => Task.CompletedTask);
                try
                {
                    ((TextBox)imports.FindName("WorkbookPathInput")).Text = folder;
                    Await(imports.ImportSelectedSourceAsync());
                    var first = ((DataGrid)imports.FindName("BatchResultsGrid")).ItemsSource.Cast<FolderImportFileResult>().ToArray();
                    var successful = Assert.Single(first, result => result.Status == "Imported");
                    Assert.Equal("Failed", Assert.Single(first, result => result.FileName == "R022_revenue.xlsx").Status);
                    Assert.True(successful.NewRows > 0);
                    var beforeFacts = Scalar(database, "SELECT COUNT(*) FROM dbo.sales_lines");
                    var beforeOutcomes = Scalar(database, "SELECT COUNT(*) FROM dbo.import_row_outcomes o JOIN dbo.import_files f ON f.import_file_id=o.import_file_id WHERE f.report_code='R025'");
                    var beforeFiles = Scalar(database, "SELECT COUNT(*) FROM dbo.import_files WHERE report_code='R025'");
                    access = access with { Role = role };
                    var problems = new ImportProblemsView(imports, () => Task.FromResult(imports.Problems));
                    problems.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    var retry = Children(problems).OfType<Button>().Single(button => Equals(button.Content, "Retry failed"));
                    Assert.Equal(access.CanView, TaskNavigation.Find("conflicts")!.IsAllowed(new(access.HasAssignedRole, access.CanView, access.CanImport, access.CanAdminister)));
                    Assert.Equal(imports.CanRetry, retry.IsEnabled);
                    Assert.Equal(access.CanImport, retry.IsEnabled);
                    Assert.Equal(ShellCommand.RetryImport, ShellShortcutRegistry.Resolve(Key.R, Key.None, ModifierKeys.Control));
                    reader.Reads.Clear();
                    File.Copy(Fixture("R022"), bad, overwrite: true);
                    // Editing the source chooser must not retarget the retry to the successful file.
                    ((TextBox)imports.FindName("WorkbookPathInput")).Text = good;
                    retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (access.CanImport)
                    {
                        Assert.True(imports.IsBusy);
                        Assert.False(retry.IsEnabled);
                        PumpUntil(() => !imports.IsBusy);
                        Assert.Equal(new[] { bad }, reader.Reads.ToArray());
                        var after = ((DataGrid)imports.FindName("BatchResultsGrid")).ItemsSource.Cast<FolderImportFileResult>().ToArray();
                        Assert.Equal(successful, Assert.Single(after, result => result.FileName == "sales.xlsx"));
                        Assert.Equal("Imported", Assert.Single(after, result => result.FileName == "R022_revenue.xlsx").Status);
                        Assert.False(imports.CanRetry);
                        Assert.False(retry.IsEnabled);
                        Assert.Empty(imports.Problems);
                        Assert.Equal(1, Scalar(database, "SELECT COUNT(*) FROM dbo.import_files WHERE report_code='R022'"));
                    }
                    else
                    {
                        Await(imports.RetryFailedBatchAsync());
                        Assert.Empty(reader.Reads);
                        Assert.Equal("Failed", Assert.Single(imports.Problems).Status);
                        Assert.Equal(0, Scalar(database, "SELECT COUNT(*) FROM dbo.import_files WHERE report_code='R022'"));
                    }
                    Assert.Equal(beforeFacts, Scalar(database, "SELECT COUNT(*) FROM dbo.sales_lines"));
                    Assert.Equal(beforeOutcomes, Scalar(database, "SELECT COUNT(*) FROM dbo.import_row_outcomes o JOIN dbo.import_files f ON f.import_file_id=o.import_file_id WHERE f.report_code='R025'"));
                    Assert.Equal(beforeFiles, Scalar(database, "SELECT COUNT(*) FROM dbo.import_files WHERE report_code='R025'"));
                    problems.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                }
                finally { Await(imports.DisposeAsync().AsTask()); }
            });
            if (role == AccessRole.Viewer)
            {
                await database.ExecuteAsync("CREATE USER retry_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER retry_viewer;");
                var denied = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("EXECUTE AS USER='retry_viewer'; EXEC dbo.append_etp_r025;"));
                Assert.Equal(229, denied.Number);
            }
        }
        finally
        {
            await database.DisposeAsync();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Fixture(string code) => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), code + "_*.xlsx").Single();

    private sealed class CountingReader : IWorkbookReader
    {
        public ConcurrentQueue<string> Reads { get; } = new();
        public Task<WorkbookSnapshot> ReadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Reads.Enqueue(filePath);
            return new OpenXmlWorkbookReader().ReadAsync(filePath, cancellationToken);
        }
    }

    private static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static object? Scalar(SqlDatabaseFixture database, string sql)
    {
        var task = database.ExecuteAsync(sql);
        Await(task);
        return task.GetAwaiter().GetResult();
    }
    private static void PumpUntil(Func<bool> completed)
    {
        var elapsed = Stopwatch.StartNew();
        while (!completed())
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30), "Import UI action timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        }
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); } catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Import UI test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
}
