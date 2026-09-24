using System.Windows.Controls;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Registers;
using Etp.Reporting.Desktop.Modules.Registers;

namespace Etp.Reporting.Desktop.Tests;

public sealed class RegisterDraftTests
{
    [Fact]
    public void Failed_save_retains_draft_and_success_clears_dirty_state_without_duplicate_write()
    {
        RunSta(() =>
        {
            var service = new RegistersStub();
            var view = Create(service);
            view.SelectTask("register-inward");
            Assert.False(view.HasUnsavedChanges);
            Input(view, "RegisterDocumentNumberInput").Text = "TEST-01";
            Input(view, "RegisterStoreInput").Text = "WLMHW";
            Input(view, "RegisterReasonInput").Text = "Synthetic draft";
            Assert.True(view.HasUnsavedChanges);
            Assert.False(view.SaveDraftAsync().GetAwaiter().GetResult());
            Assert.True(view.HasUnsavedChanges);
            Assert.Equal("TEST-01", Input(view, "RegisterDocumentNumberInput").Text);
            service.Fail = false;
            Assert.True(view.SaveDraftAsync().GetAwaiter().GetResult());
            Assert.False(view.HasUnsavedChanges);
            Assert.Equal(1, service.Writes);
            Assert.False(view.SaveDraftAsync().GetAwaiter().GetResult());
            Assert.Equal(1, service.Writes);
        });
    }

    [Fact]
    public void Register_type_round_trip_preserves_draft_store_date_and_document_link()
    {
        RunSta(() =>
        {
            var view = Create(new RegistersStub()); view.SelectTask("register-inward");
            Input(view, "RegisterDocumentNumberInput").Text = "IN-01";
            Input(view, "RegisterStoreInput").Text = "HEMW";
            view.BusinessDate = new DateTime(2026,8,25); view.LinkedSourceDocumentId = 42;
            view.SelectTask("register-outward");
            Input(view, "RegisterDocumentNumberInput").Text = "OUT-01";
            view.LinkedSourceDocumentId = 71; view.BusinessDate = new DateTime(2026,8,26);
            view.SelectTask("register-inward");
            Assert.Equal("IN-01", Input(view, "RegisterDocumentNumberInput").Text);
            Assert.Equal("HEMW", Input(view, "RegisterStoreInput").Text);
            Assert.Equal(new DateTime(2026,8,25), view.BusinessDate); Assert.Equal(42, view.LinkedSourceDocumentId);
            view.DiscardDraft(); Assert.False(view.HasUnsavedChanges);
        });
    }

    [Fact]
    public void Courier_edit_and_owner_verification_preserve_document_binding_and_manager_cannot_verify()
    {
        RunSta(() =>
        {
            var day = new DateOnly(2026,8,25);
            var row = new DigitalRegisterEntry(12,"COURIER",42,"HEMW",day,"COURIER-1",day,"Carrier",1,25,"TRACK-1","Manager","DRAFT","Received","Manager",DateTime.UtcNow);
            var service = new RegistersStub { Fail = false, Entries = [row] };
            var view = Create(service); view.SelectTask("register-courier");
            var grid = (DataGrid)view.FindName("RegisterGrid");
            Assert.Single(grid.Items); grid.SelectedIndex = 0;
            Assert.Equal("COURIER-1",Input(view,"RegisterDocumentNumberInput").Text);
            Assert.Equal(42,view.LinkedSourceDocumentId);
            Input(view,"RegisterReasonInput").Text = "Checked delivery evidence";
            Assert.True(view.SaveEntryAsync(true).GetAwaiter().GetResult());
            Assert.Equal("VERIFIED",service.LastEntry!.VerificationStatus);
            Assert.Equal(day,service.LastEntry.DocumentDate);
            Assert.Equal("COURIER",service.LastEntry.RegisterType);
            grid.SelectedIndex = 0;
            view.AttachHost(() => new AccessSession("TEST\\manager","Manager",AccessRole.StoreManager,true),ex => ex.Message);
            Assert.False(view.SaveEntryAsync(true).GetAwaiter().GetResult());
            Assert.Equal(1,service.Writes);
        });
    }

    [Fact]
    public void Changing_selected_entry_cannot_discard_an_edited_register_draft()
    {
        RunSta(() =>
        {
            var row = new DigitalRegisterEntry(12,"INWARD",null,"HEMW",new(2026,8,25),"IN-1",null,"Vendor",1,25,null,"Manager","DRAFT",null,"Manager",DateTime.UtcNow);
            var service = new RegistersStub { Entries = [row, row with { Id=13, DocumentNumber="IN-2" }] };
            var view = Create(service); view.SelectTask("register-inward");
            var grid = (DataGrid)view.FindName("RegisterGrid"); grid.SelectedIndex = 0;
            Input(view,"RegisterRemarksInput").Text = "Unsaved receiving notes";
            grid.SelectedIndex = 1;
            Assert.Equal(row,grid.SelectedItem);
            Assert.Equal("Unsaved receiving notes",Input(view,"RegisterRemarksInput").Text);
            Assert.True(view.HasUnsavedChanges);
        });
    }

    private static RegistersWorkspaceView Create(RegistersStub service)
    {
        var view = new RegistersWorkspaceView(new RegistersPresentationSession(_ => service), () => "synthetic");
        view.AttachHost(() => new AccessSession("TEST\\user", "Synthetic", AccessRole.Owner, true), ex => ex.Message); return view;
    }
    private static TextBox Input(RegistersWorkspaceView view, string name) => (TextBox)view.FindName(name);
    private static void RunSta(Action action)
    {
        Exception? failure = null; var thread = new Thread(() => { try { action(); } catch(Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Draft test failed", failure);
    }
    private sealed class RegistersStub : IDigitalRegisterService
    {
        public bool Fail = true; public int Writes;
        public IReadOnlyList<DigitalRegisterEntry> Entries = [];
        public DigitalRegisterEntryDraft? LastEntry;
        public Task<IReadOnlyList<DigitalRegisterEntry>> LoadAsync(string? search = null, int limit = 500, CancellationToken cancellationToken = default) => Task.FromResult(Entries);
        public Task<long> SaveAsync(DigitalRegisterEntryDraft entry, string reason, CancellationToken cancellationToken = default)
        {
            if (Fail) return Task.FromException<long>(new InvalidOperationException("Synthetic service failure"));
            Writes++; LastEntry = entry; return Task.FromResult(1L);
        }
    }
}
