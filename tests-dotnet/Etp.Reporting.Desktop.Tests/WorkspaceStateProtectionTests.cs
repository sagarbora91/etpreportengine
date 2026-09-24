using System.Windows.Controls;
using System.Windows;
using System.Windows.Automation;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Sharing;
using Etp.Reporting.Desktop.Modules.Archive;
namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class WorkspaceStateProtectionTests
{
    [Fact]
    public void Focused_tables_have_no_outer_page_scroll_and_keep_rows_and_selected_tab_after_return()
    {
        RunSta(() =>
        {
            var rows = new[] { new Record(1), new Record(2) };
            var table = new DataGrid { ItemsSource = rows }; AutomationProperties.SetName(table, "Records");
            var fields = new WrapPanel(); fields.Children.Add(new TextBox { Name = "TaskStore", Text = "WLMHW" });
            var original = new StackPanel(); original.Children.Add(fields); original.Children.Add(table);
            var view = new UserControl { Content = original };
            FocusedTaskLayout.Show(view, "Task", [0,1], []);
            var tabs = Walk(view).OfType<TabControl>().Single(); tabs.SelectedIndex = 1;
            for (DependencyObject? parent = table.Parent; parent is not null; parent = LogicalTreeHelper.GetParent(parent)) Assert.IsNotType<ScrollViewer>(parent);
            FocusedTaskLayout.Show(view, "Other", [0], []);
            FocusedTaskLayout.Show(view, "Task", [0,1], []);
            Assert.Same(rows, table.ItemsSource); Assert.Equal(1, Walk(view).OfType<TabControl>().Single().SelectedIndex);
        });
    }

    [Fact]
    public void Help_topic_and_category_round_trips_do_not_reparent_live_controls_twice()
    {
        RunSta(() =>
        {
            var help = new HelpCentreView();
            for (var round = 0; round < 3; round++)
            {
                Assert.Equal(5, Walk(help).OfType<Button>().Count(button => AutomationProperties.GetName(button).EndsWith("help category", StringComparison.Ordinal)));
                help.OpenTopic("dashboard");
                Walk(help).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Return to all Help Centre topics")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(HelpCentreRegistry.HomeTopicId, help.CurrentTopicId);
            }
        });
    }
    [Fact]
    public void Operation_gate_blocks_reentry_and_restores_original_enabled_state_even_after_failure()
    {
        RunSta(() =>
        {
            var view = new UserControl(); var gate = new WorkspaceOperationGate();
            var lease = gate.TryEnter(view)!;
            Assert.True(gate.IsBusy); Assert.False(view.IsEnabled); Assert.Null(gate.TryEnter(view));
            lease.Dispose(); lease.Dispose(); Assert.False(gate.IsBusy); Assert.True(view.IsEnabled);
            view.IsEnabled = false; using (gate.TryEnter(view)) { Assert.True(gate.IsBusy); }
            Assert.False(view.IsEnabled); Assert.False(gate.IsBusy);
        });
    }

    [Fact]
    public void Review_reasons_follow_exact_record_across_selection_and_refresh()
    {
        RunSta(() =>
        {
            var grid = new DataGrid { ItemsSource = new[] { new Record(1), new Record(2) } }; var input = new TextBox();
            var drafts = new SelectionReasonDrafts(grid, input, row => (row as Record)?.Id);
            Assert.False(input.IsEnabled);
            grid.SelectedIndex = 0; input.Text = "Reason for first";
            grid.SelectedIndex = 1; Assert.Equal("", input.Text); input.Text = "Reason for second";
            grid.ItemsSource = new[] { new Record(1), new Record(2) };
            grid.SelectedIndex = 0; Assert.Equal("Reason for first", input.Text); Assert.True(drafts.HasDrafts);
            input.Clear(); grid.SelectedIndex = 1; Assert.Equal("Reason for second", input.Text);
            drafts.Discard(); Assert.False(drafts.HasDrafts);
        });
    }

    [Fact]
    public void Sharing_contact_drafts_survive_selection_refresh_and_failed_save_without_new_record_retargeting()
    {
        RunSta(() =>
        {
            var contacts = new Contacts();
            var view = new ArchiveWorkspaceView(new ArchiveDistributionPresentationSession(_ => throw new InvalidOperationException("No archive fixture"),
                _ => contacts, _ => throw new InvalidOperationException("No distribution fixture")), () => "synthetic",
                (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, new NoSharing());
            view.AttachHost(() => new("TEST\\owner", "Synthetic", AccessRole.Owner, true), (_, _, _) => Task.CompletedTask, ex => ex.Message);
            view.RefreshAsync().GetAwaiter().GetResult();
            var grid = (DataGrid)view.FindName("SharingContactsGrid"); var name = (TextBox)view.FindName("ContactNameInput");
            grid.SelectedIndex = 0; name.Text = "First edited";
            grid.SelectedIndex = 1; name.Text = "Second edited";
            view.RefreshAsync().GetAwaiter().GetResult(); Assert.Equal("Second edited", name.Text);
            grid.SelectedIndex = 0; Assert.Equal("First edited", name.Text);
            Assert.False(view.SaveContactDraftAsync().GetAwaiter().GetResult());
            Assert.Equal(1, contacts.Submitted!.Id); Assert.Equal(new[] {1, 2}, view.UnsavedContacts);
            view.DiscardContactDraft(1); Assert.Equal("First", name.Text);
            view.DiscardContactDraft(2); Assert.Empty(view.UnsavedContacts);
        });
    }
    private sealed record Record(long Id);
    [Fact]
    public void Import_busy_lease_preserves_inputs_and_cancel_and_blocks_reentry()
    {
        RunSta(() =>
        {
            var coordinator = new Etp.Reporting.Desktop.Modules.Imports.DesktopImportCoordinator(_ => throw new InvalidOperationException("No persistence"), (_,_,_,_,_,_,_) => Task.CompletedTask);
            var view = new Etp.Reporting.Desktop.Modules.Imports.ImportWorkspaceView(coordinator, () => "synthetic");
            var date = (DatePicker)view.FindName("ImportBusinessDateInput"); var store = (ComboBox)view.FindName("ImportStoreInput");
            var cancel = (Button)view.FindName("CancelBatchButton"); var begin = view.GetType().GetMethod("BeginImportOperation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var original = date.SelectedDate; var lease = (IDisposable)begin.Invoke(view,null)!;
            Assert.True(view.IsBusy); Assert.False(date.IsEnabled); Assert.False(store.IsEnabled); Assert.Null(begin.Invoke(view,null));
            cancel.IsEnabled = true; Assert.True(cancel.IsEnabled); Assert.False(view.BrowseWorkbook());
            lease.Dispose(); lease.Dispose(); Assert.False(view.IsBusy); Assert.False(date.IsEnabled); Assert.False(store.IsEnabled); Assert.False(cancel.IsEnabled); Assert.Equal(original,date.SelectedDate);
        });
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var item in Walk(child)) yield return item; }
    }
    private sealed class Contacts : ISharingContactsService
    {
        public SharingContactDraft? Submitted;
        public Task<IReadOnlyList<SharingContact>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SharingContact>>(
            [new(1,"First",null,null,null,null,true,"synthetic",DateTime.UtcNow), new(2,"Second",null,null,null,null,true,"synthetic",DateTime.UtcNow)]);
        public Task<int> SaveAsync(SharingContactDraft contact, string reason, CancellationToken cancellationToken = default)
        { Submitted = contact; return Task.FromException<int>(new InvalidOperationException("Synthetic failure")); }
    }
    private sealed class NoSharing : IArchiveShareLauncher
    {
        public void OpenWhatsApp(string attachmentPath, string message, string? phone) => throw new InvalidOperationException("Not permitted in fixture");
        public void OpenEmailDraft(string shareFolderPath, string attachmentPath, string to, string? cc, string subject, string body) => throw new InvalidOperationException("Not permitted in fixture");
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("State test failed", failure);
    }
}
