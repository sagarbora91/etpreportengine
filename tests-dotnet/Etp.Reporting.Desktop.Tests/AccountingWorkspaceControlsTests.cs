using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

public sealed class AccountingWorkspaceControlsTests
{
    [Fact]
    public void Unified_workspace_keeps_all_steps_visible_and_actions_follow_role_and_status()
    {
        RunSta(()=>
        {
            var service=DispatchProxy.Create<IAccountingService,AccountingProxy>();
            var view=new AccountingWorkspaceView(new(_=>service),()=>"fixture");
            var owner=new AccessSession("fixture","Owner",AccessRole.Owner,true);
            view.AttachHost(()=>owner,error=>error.Message);
            view.SelectTask("prepare-batch");
            var grid=(DataGrid)view.FindName("AccountingBatchGrid");
            foreach(var state in new[]{"DRAFT","BLOCKED","APPROVED_READY","EXPORTED_AWAITING_IMPORT","REJECTED"})
            {
                grid.ItemsSource=new[]{new AccountingBatchSummary(1,"FIXTURE",new(2026,8,25),1,1,25,25,state,null,null,null,DateTime.UtcNow,"Missing mapping")};
                grid.SelectedIndex=0;
                Assert.Equal(state=="DRAFT",((Button)view.FindName("ApproveTaskButton")).IsEnabled);
                Assert.Equal(state=="APPROVED_READY",((Button)view.FindName("TallyTaskButton")).IsEnabled);
                Assert.Equal(state is "DRAFT" or "BLOCKED" or "APPROVED_READY",((Button)view.FindName("RejectTaskButton")).IsEnabled);
                foreach(var name in new[]{"PreviewTaskButton","SaveTaskButton","ApproveTaskButton","RejectTaskButton","TallyTaskButton"})
                    Assert.Equal(Visibility.Visible,((Button)view.FindName(name)).Visibility);
            }
            owner=new("fixture","Manager",AccessRole.StoreManager,true); view.SelectTask("prepare-batch");
            Assert.False(((Button)view.FindName("PreviewTaskButton")).IsEnabled);
            Assert.Contains("Owner permission",((TextBlock)view.FindName("ActionGuidance")).Text);
        });
    }

    [Fact]
    public void Tally_settings_retain_and_discard_drafts_and_save_through_injected_service()
    {
        RunSta(()=>
        {
            var service=DispatchProxy.Create<IAccountingService,AccountingProxy>(); var proxy=(AccountingProxy)(object)service;
            var view=new TallyDestinationSettingsView(()=>"fixture",()=>true,_=>service);
            view.LoadAsync().GetAwaiter().GetResult();
            var inputs=view.Children.OfType<TextBox>().ToArray();
            Assert.Equal("TEST Fixture",inputs[0].Text);
            inputs[0].Text="TEST New"; Assert.True(view.HasDraft);
            view.DiscardDraft(); Assert.Equal("TEST Fixture",inputs[0].Text); Assert.False(view.HasDraft);
            inputs[0].Text="TEST New"; inputs[2].Text="Accountant-approved test destination";
            view.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("TEST New",proxy.Saved!.CompanyName);
            Assert.Equal("TEST",proxy.Saved.EnvironmentLabel); Assert.False(view.HasDraft);
        });
    }

    public class AccountingProxy : DispatchProxy
    {
        public SaveAccountingDestination? Saved;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IAccountingService.LoadDestinationAsync)=>Task.FromResult(new AccountingDestination("TEST Fixture","TEST")),
            nameof(IAccountingService.LoadEntriesAsync)=>Task.FromResult<IReadOnlyList<AccountingEntry>>([]),
            nameof(IAccountingService.SaveDestinationAsync)=>Save((SaveAccountingDestination)args![0]!),
            _=>throw new InvalidOperationException("Unexpected fixture call: "+method?.Name)
        };
        private Task Save(SaveAccountingDestination value) { Saved=value; return Task.CompletedTask; }
    }

    private static void RunSta(Action action)
    {
        Exception? error=null; var thread=new Thread(()=>{try { action(); } catch(Exception exception) { error=exception; }});
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if(error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
