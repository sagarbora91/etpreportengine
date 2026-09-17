using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFiveAccountingTests
{
    [Theory]
    [InlineData(125.75)]
    [InlineData(-125.75)]
    public async Task Owner_can_map_an_approved_adjustment_and_prepare_its_balanced_batch(decimal amount)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await database.ExecuteAsync("""
                INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
                VALUES('PHASE5','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1);
                """);
            var adjustment = Convert.ToInt64(await database.ExecuteAsync(
                $"EXEC dbo.submit_controlled_adjustment 'PHASE5','20260825','CORRECTION',{amount.ToString(CultureInfo.InvariantCulture)},N'Synthetic accounting evidence';"));
            var request = Convert.ToInt64(await database.ExecuteAsync(
                $"SELECT approval_request_id FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
            var service = new SqlServerAccountingService(database.ConnectionString);
            var scope = new AccountingScope("PHASE5", new(2026, 8, 25));
            Assert.Empty((await service.LoadSourceAsync(scope)).Events);
            await database.ExecuteAsync($"EXEC dbo.decide_approval_request {request},1,N'Synthetic evidence checked';");
            Assert.Equal(new[] { "ADJUSTMENT" }, (await service.PreviewAsync(scope)).Batch.MissingMappings);

            RunSta(() =>
            {
                var view = new AccountingWorkspaceView(new AccountingPresentationSession(_ => service), () => database.ConnectionString);
                view.AttachHost(() => new AccessSession("synthetic", "Fixture Owner", AccessRole.Owner, true), exception => exception.Message);
                ((TextBox)view.FindName("AccountingStoreInput")).Text = scope.StoreCode;
                view.BusinessDate = scope.BusinessDate.ToDateTime(TimeOnly.MinValue);
                var choices = (ComboBox)view.FindName("AccountingEventInput");
                choices.SelectedItem = Assert.Single(choices.Items.Cast<ComboBoxItem>(), item => Equals(item.Content, "ADJUSTMENT"));
                ((TextBox)view.FindName("DebitLedgerInput")).Text = "Adjustment expense";
                ((TextBox)view.FindName("CreditLedgerInput")).Text = "Adjustment clearing";
                ((TextBox)view.FindName("AccountingMappingReasonInput")).Text = "Synthetic mapping approved by Owner";
                var mappingButton = LogicalChildren(view).OfType<Button>().Single(button => Equals(button.Content, "Approve mapping"));
                mappingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var status = (TextBlock)view.FindName("AccountingStatus");
                PumpUntil(() => view.IsEnabled && status.Text.Length > 0, status);
                Assert.StartsWith("Approved ADJUSTMENT ledger mapping", status.Text);
                status.Text = "";
                ((Button)view.FindName("PreviewTaskButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => view.IsEnabled && status.Text.Length > 0, status);
                Assert.StartsWith("Balanced preview", status.Text);
                var entries = ((DataGrid)view.FindName("AccountingEntryGrid")).ItemsSource.Cast<AccountingEntry>().ToArray();
                Assert.Equal(Math.Abs(amount), entries.Sum(entry => entry.DebitAmount));
                Assert.Equal(Math.Abs(amount), entries.Sum(entry => entry.CreditAmount));
                Assert.Equal(amount > 0 ? "Adjustment expense" : "Adjustment clearing", Assert.Single(entries, entry => entry.DebitAmount > 0).LedgerName);
                Assert.True(((Button)view.FindName("SaveTaskButton")).IsEnabled);
                Capture(view, amount);
                status.Text = "";
                ((Button)view.FindName("SaveTaskButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => view.IsEnabled && status.Text.Length > 0, status);
                Assert.Contains("saved for Owner review", status.Text);
            });

            var batch = Assert.Single(await service.LoadBatchesAsync());
            Assert.Equal("REVIEW", batch.Status);
            Assert.Equal(Math.Abs(amount), batch.DebitTotal);
            Assert.Equal(batch.DebitTotal, batch.CreditTotal);
            Assert.All(await service.LoadEntriesAsync(batch.Id), entry => Assert.Equal("ADJUSTMENT", entry.BusinessEvent));
            Assert.Equal(amount, await database.ExecuteAsync($"SELECT amount FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ApproveAsync(new(batch.Id, " ")));
            Assert.Equal(DBNull.Value, await database.ExecuteAsync($"SELECT approval_reason FROM dbo.accounting_batches WHERE accounting_batch_id={batch.Id}"));
            await service.ApproveAsync(new(batch.Id, "  Checked source and balanced entries  "));
            Assert.Equal("APPROVED", Assert.Single(await new SqlServerAccountingService(database.ConnectionString).LoadBatchesAsync()).Status);
            Assert.Equal("Checked source and balanced entries", await database.ExecuteAsync($"SELECT approval_reason FROM dbo.accounting_batches WHERE accounting_batch_id={batch.Id}"));
            await Assert.ThrowsAsync<ArgumentException>(()=>service.RejectAsync(new(batch.Id," ")));
            var approved=Assert.Single(await service.LoadBatchesAsync());
            RunSta(()=>
            {
                var view=new AccountingWorkspaceView(new AccountingPresentationSession(_=>service),()=>database.ConnectionString);
                view.AttachHost(()=>new AccessSession("synthetic","Owner",AccessRole.Owner,true),error=>error.Message);
                var grid=(DataGrid)view.FindName("AccountingBatchGrid");grid.ItemsSource=new[]{approved};grid.SelectedIndex=0;
                ((TextBox)view.FindName("BatchApprovalReasonInput")).Text="Correction needed before export";
                ((Button)view.FindName("RejectTaskButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var status=(TextBlock)view.FindName("AccountingStatus");
                PumpUntil(()=>view.IsEnabled && status.Text.Length>0,status);
                Assert.Contains("rejected",status.Text);
            });
            Assert.Equal("REJECTED",Assert.Single(await service.LoadBatchesAsync()).Status);
            Assert.Equal("Correction needed before export",await database.ExecuteAsync($"SELECT rejection_reason FROM dbo.accounting_batches WHERE accounting_batch_id={batch.Id}"));
            Assert.Equal("Checked source and balanced entries",await database.ExecuteAsync($"SELECT approval_reason FROM dbo.accounting_batches WHERE accounting_batch_id={batch.Id}"));
            foreach(var state in new[]{"REJECTED","EXPORTED"})
            {
                await database.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='{state}' WHERE accounting_batch_id={batch.Id}");
                var denied=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>service.RejectAsync(new(batch.Id,"Second decision")));
                Assert.Equal(51432,denied.Number);
            }
            foreach(var role in new[]{"etp_viewer","etp_store_manager"})
            {
                await database.ExecuteAsync($"CREATE USER reject_probe WITHOUT LOGIN; ALTER ROLE {role} ADD MEMBER reject_probe;");
                var denied=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>database.ExecuteAsync($"EXECUTE AS USER='reject_probe'; BEGIN TRY EXEC dbo.reject_accounting_batch {batch.Id},N'Denied'; REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;"));
                Assert.Equal(229,denied.Number);
                await database.ExecuteAsync("DROP USER reject_probe;");
            }
        }
        finally { await database.DisposeAsync(); }
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
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "Accounting UI test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PumpUntil(Func<bool> complete, TextBlock status)
    {
        var timer = Stopwatch.StartNew();
        while (!complete())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(20), "Accounting action timed out: " + status.Text);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        }
    }

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalChildren(child)) yield return descendant;
        }
    }

    private static void Capture(AccountingWorkspaceView view, decimal amount)
    {
        var folder = Environment.GetEnvironmentVariable("ETP_PHASE5_UI_EVIDENCE");
        if (string.IsNullOrWhiteSpace(folder) || amount < 0) return;
        Directory.CreateDirectory(folder);
        var host = new ScrollViewer { Content = view, Background = Brushes.White, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        foreach (var (width, height) in new[] { (1366, 768), (816, 480) })
        {
            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, $"Accounting-adjustment-{width}x{height}-wpf.png"));
            encoder.Save(stream);
        }
        host.Content = null;
    }
}
