using System.Diagnostics;
using System.Windows.Threading;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// 1.12 wave 2, lane U3, plan Appendix B item 6 (cross-layer): the Tally companies screen's view model saves a TEST profile (port 9000,
// build label EDUCATIONAL, test PC name) through the real profile store; SQL keeps and returns it; the same profile with another PC
// name makes SendAsync refuse before any attempt row exists. Synthetic store code and PC names only; no socket, no Tally.
public sealed class TallyProfileScreenSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Store = "U3SCR";

    [SqlFact]
    public Task The_screen_saves_a_test_profile_with_port_9000_educational_and_a_test_pc_and_a_send_from_another_pc_is_refused_before_any_attempt()
    {
        // One STA thread owns the view for the whole test; every database call is awaited by pumping that thread's dispatcher.
        RunSta(() =>
        {
            Await(database.ExecuteAsync($"INSERT dbo.stores(store_code,store_name) VALUES('{Store}',N'Synthetic U3 screen store')"));
            var service = new SqlServerTallyProfileService(database.ConnectionString);

            var view = new TallyCompaniesView(() => true, () => service, () => "SYNTH-VM");
            view.CodeInput.Text = "u3scr";
            view.CompanyInput.Text = TallyTestCompany.Name;
            view.StoresInput.Text = "u3scr";
            view.DeliveryInput.SelectedItem = TallyCompaniesView.DeliveryHttpWords;
            view.BuildInput.Text = "TallyPrime 5.0 EDUCATIONAL";
            view.TestPcInput.Text = "synth-vm";
            view.ReasonInput.Text = "Synthetic screen test";
            Await(view.SaveAsync());
            Assert.Equal("Tally company saved. Nothing has been sent to Tally.", view.StatusText.Text);

            var saved = Assert.Single(Await(service.LoadAsync()), profile => profile.ProfileCode == "U3SCR");
            Assert.Equal("TEST", saved.Environment);
            Assert.Equal("HTTP", saved.DefaultDeliveryMode);
            Assert.Equal("http://127.0.0.1:9000/", saved.EndpointUrl);
            Assert.Equal("TallyPrime 5.0 EDUCATIONAL", saved.TallyBuildLabel);
            Assert.Equal("SYNTH-VM", saved.TestMachineName);
            Assert.Equal(TallyTestCompany.Name, saved.CompanyName);
            Assert.Null(TallyConnectedMode.Refusal(saved));
            Assert.Null(TallyConnectedMode.TestPcRefusal(saved, "synth-vm"));
            Assert.Equal("SYNTH-VM", Await(database.ExecuteAsync($"SELECT test_machine_name FROM dbo.tally_profiles WHERE tally_profile_id={saved.Id}")));

            // A batch of this company (draft is enough: the PC check comes before the status check and before any attempt row).
            var batchId = Convert.ToInt64(Await(database.ExecuteAsync(
                "INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final) " +
                $"VALUES('{Store}','20260825',1,REPLICATE('a',64),N'{{}}',SUSER_SNAME(),1); " +
                "INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by,tally_profile_id,batch_kind) " +
                $"OUTPUT inserted.accounting_batch_id SELECT '{Store}','20260825',SCOPE_IDENTITY(),1,0,0,'DRAFT',SUSER_SNAME(),{saved.Id},'SALES_VOUCHERS';")));

            // On the named PC the PC check passes, so the send stops at the next rule (the file was never written).
            var onTestPc = new SqlServerTallyConnectedService(database.ConnectionString, _ => throw new InvalidOperationException("no socket"),
                _ => throw new InvalidOperationException("no socket"), Path.Combine(Path.GetTempPath(), "EtpU3Screen"), () => "synth-vm");
            var notWritten = Await(Assert.ThrowsAsync<InvalidOperationException>(() => onTestPc.SendAsync(batchId)));
            Assert.Contains("Write the Tally file first", notWritten.Message, StringComparison.Ordinal);

            // The Owner changes the test PC on the screen; the same machine name now refuses before any attempt row.
            view.TestPcInput.Text = "other-vm";
            view.ReasonInput.Text = "Moved the test to another PC";
            Await(view.SaveAsync());
            Assert.Equal("OTHER-VM", Await(database.ExecuteAsync($"SELECT test_machine_name FROM dbo.tally_profiles WHERE tally_profile_id={saved.Id}")));

            var refused = Await(Assert.ThrowsAsync<InvalidOperationException>(() => onTestPc.SendAsync(batchId)));
            Assert.Equal(TallyConnectedMode.OnlyTheTestPc, refused.Message);
            Assert.Equal(0, Await(database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.tally_attempts WHERE accounting_batch_id={batchId}")));
        });
        return Task.CompletedTask;
    }

    private static T Await<T>(Task<T> task)
    {
        Await((Task)task);
        return task.GetAwaiter().GetResult();
    }

    private static void Await(Task task)
    {
        var elapsed = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30), "Tally companies screen action timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
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
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "Tally companies screen test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
