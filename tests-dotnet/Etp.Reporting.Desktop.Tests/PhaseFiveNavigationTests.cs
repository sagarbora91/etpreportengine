using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Distribution;

namespace Etp.Reporting.Desktop.Tests;

public sealed class PhaseFiveNavigationTests
{
    [Fact]
    public void Unified_routes_keep_archive_contacts_separate_and_show_close_day_registers_and_task_status()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow(); var navigator = new TaskNavigator(window);
            navigator.DisplayTaskRoute(TaskNavigation.Find("generations")!.Route);
            var archive = Descendants(window.FocusedWorkspaceHost).ToArray();
            Assert.Contains(archive, x => x is DataGrid { Name: "ShareHistoryGrid" });
            Assert.DoesNotContain(archive, x => x is TextBox { Name: "ContactReasonInput" });
            window.archiveWorkspaceView.RevealShareRecipient();
            var tabs = archive.OfType<TabControl>().Single();
            Assert.Contains(Descendants((DependencyObject)Assert.IsType<TabItem>(tabs.SelectedItem).Content), x => x is TextBox { Name: "ShareEmailToInput" });
            Assert.Contains(archive, x => x is PasswordBox { Name: "SmtpPasswordInput" });
            navigator.DisplayTaskRoute(TaskNavigation.Find("sharing-contacts")!.Route);
            Assert.Contains(Descendants(window.FocusedWorkspaceHost), x => x is TextBox { Name: "ContactReasonInput" });
            navigator.DisplayTaskRoute(TaskNavigation.Find("generations")!.Route);
            Assert.DoesNotContain(Descendants(window.FocusedWorkspaceHost), x => x is TextBox { Name: "ContactReasonInput" });
            navigator.DisplayTaskRoute(TaskNavigation.Find("readiness")!.Route);
            Assert.Contains(Descendants(window.FocusedWorkspaceHost), x => x is DataGrid { Name: "DayRegistersGrid" });
            navigator.DisplayTaskRoute(TaskNavigation.Find("watch-folder")!.Route);
            var automatic = Descendants(window.FocusedWorkspaceHost).ToArray();
            Assert.Contains(automatic, x => x is DataGrid { Name: "ReportSchedulesGrid" });
            Assert.Contains(automatic, x => x is DataGrid { Name: "AutomationRunsGrid" });
            navigator.DisplayTaskRoute(TaskNavigation.Find("prepare-batch")!.Route);
            var scroll = Assert.IsType<ScrollViewer>(window.accountingWorkspaceView.Content);
            scroll.Measure(new Size(680,330)); scroll.Arrange(new Rect(0,0,680,330)); scroll.UpdateLayout();
            Assert.True(scroll.ScrollableHeight > 0, "Accounting decisions/history must remain reachable on the compact screen.");
            scroll.ScrollToEnd(); scroll.UpdateLayout(); Assert.True(scroll.VerticalOffset > 0);
            window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void Refresh_retains_custom_store_subset_and_an_explicit_store_choice_replaces_it()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            window.ApplyStoreCatalog([new("A","Shop A",true),new("B","Shop B",true),new("C","Shop C",true)]);
            var source = window.reportsWorkspaceView;
            ((TextBox)source.FindName("StoreFilterInput")).Text = "A,B";
            var workspace = new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("sales-item"));
            workspace.SetStores(window.StoreScopes, source.StoreScope);
            Assert.Equal("Custom: A,B", workspace.ScopeSelector.SelectedItem);
            var run = typeof(MainWindow).GetMethod("RunFocusedReport", BindingFlags.NonPublic|BindingFlags.Instance)!;
            run.Invoke(window, ["sales-item",workspace]);
            Assert.Equal("A,B", ((TextBox)source.FindName("StoreFilterInput")).Text);
            workspace.ScopeSelector.SelectedItem = "Shop C (C)"; run.Invoke(window,["sales-item",workspace]);
            Assert.Equal("C", ((TextBox)source.FindName("StoreFilterInput")).Text);
            window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void Investigation_preserves_store_date_and_blocks_viewer_register_navigation()
    {
        Sta(() =>
        {
            var window=Phase3ShellTests.CreateWindow(); var navigator=(TaskNavigator)typeof(MainWindow).GetField("taskNavigator",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
            var hit=new InvestigationHit("Register","COURIER-42","HEMW",new(2026,8,25),"Courier","") { TargetTaskId="register-courier", StoreCode="HEMW" };
            navigator.NavigateInvestigation(hit);
            Assert.Equal("register-courier",window.shell.CurrentRoute.TaskId);
            Assert.Equal("HEMW",window.registersWorkspaceView.StoreCode);
            Assert.Equal(new DateTime(2026,8,25),window.registersWorkspaceView.BusinessDate);
            Assert.Equal("COURIER-42",((TextBox)window.registersWorkspaceView.FindName("RegisterSearchInput")).Text);
            navigator.NavigateInvestigation(hit with { StoreCode="INACTIVE" });
            Assert.Contains("Store INACTIVE is inactive",window.ApplicationStatus.Text);
            Assert.Equal("HEMW",window.registersWorkspaceView.StoreCode);
            typeof(MainWindow).GetField("currentAccess",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,new AccessSession("viewer","Viewer",AccessRole.Viewer,true));
            navigator.NavigateInvestigation(hit);
            Assert.Equal("This result is not available to your role.",window.ApplicationStatus.Text);
            window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach(var descendant in Descendants(child)) yield return descendant; } }
    private static void Sta(Action action)
    {
        Exception? error=null; var thread=new Thread(()=> { try { action(); } catch(Exception ex) { error=ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        if(error is not null) throw new InvalidOperationException("Phase 5 navigation failed",error);
    }
}
