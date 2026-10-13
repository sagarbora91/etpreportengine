using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Accounting;

namespace Etp.Reporting.Desktop.Tests;

// 1.12 U3: the Send to Tally card. Fake services only: no database, no socket, no Tally. Synthetic names and store codes.
public sealed class TallyTransferViewTests
{
    private const string TestPc = "TESTPC-VM";

    private static TallyProfile Profile(string? testPc = TestPc, string delivery = "HTTP", string? endpoint = "http://127.0.0.1:9000/") =>
        TallyProfile.NewTest("GOLDEN", TallyTestCompany.Name, new[] { "SC999" }) with
        { Id = 1, DefaultDeliveryMode = delivery, EndpointUrl = endpoint, TestMachineName = testPc };

    private static TallyTransferBatch Batch(string status) => new(40, "SC999", new DateOnly(2026, 8, 25), status);

    [Theory]
    [InlineData("PLANNED", "Not approved", false, false, false, false, false, null)]
    [InlineData("DRAFT", "Not approved", false, false, false, false, false, null)]
    [InlineData("APPROVED_READY", "Approved", true, false, false, false, false, nameof(TallyTransferView.WriteFileButton))]
    [InlineData("EXPORTED_AWAITING_IMPORT", "File written", false, true, true, true, false, nameof(TallyTransferView.SendButton))]
    [InlineData("RECONCILED", "Matches Tally", false, true, true, false, false, null)]
    public void Buttons_follow_the_batch_state_for_the_owner_on_the_test_pc(string status, string word, bool writeFile, bool readBack, bool readBackFile, bool send, bool compare, string? primary)
    {
        TallyStaRunner.Run(() =>
        {
            var view = Shown(new FakeActions { Batches = [Batch(status)] }, owner: true, Profile());
            Assert.Equal(word, view.BatchChip.Text);
            Assert.Equal(writeFile, view.WriteFileButton.IsEnabled);
            Assert.Equal(readBack, view.ReadBackButton.IsEnabled);
            Assert.Equal(readBackFile, view.ReadBackFileButton.IsEnabled);
            Assert.Equal(send, view.SendButton.IsEnabled);
            Assert.Equal(compare, view.CompareButton.IsEnabled);
            Assert.True(view.WriteMastersButton.IsEnabled);
            Assert.True(view.OpenFolderButton.IsEnabled);
            Assert.Equal(Visibility.Visible, view.SendButton.Visibility);
            Assert.Equal(primary, PrimaryName(view));
        });
    }

    [Fact]
    public void Send_is_a_button_only_on_the_profiles_test_pc_and_a_sentence_elsewhere()
    {
        TallyStaRunner.Run(() =>
        {
            var elsewhere = Shown(new FakeActions { Pc = "WORKPC", Batches = [Batch("EXPORTED_AWAITING_IMPORT")] }, owner: true, Profile());
            Assert.Equal(Visibility.Collapsed, elsewhere.SendButton.Visibility);
            Assert.False(elsewhere.IsTestPc);
            Assert.Equal($"Sending is set up for {TestPc} only. On this PC, write the file and import it in Tally by hand, then read back.", elsewhere.SendCaption.Text);
            Assert.Equal(Visibility.Visible, elsewhere.SendCaption.Visibility);
            // With no Send button the next step is the read-back, and it is the only primary button.
            Assert.Equal(nameof(TallyTransferView.ReadBackButton), PrimaryName(elsewhere));

            var nobody = Shown(new FakeActions { Pc = "WORKPC", Batches = [Batch("EXPORTED_AWAITING_IMPORT")] }, owner: true, Profile(testPc: null));
            Assert.Equal(Visibility.Collapsed, nobody.SendButton.Visibility);
            Assert.Equal(TallyTransferView.SendNowhereSentence, nobody.SendCaption.Text);

            var here = Shown(new FakeActions { Pc = TestPc.ToLowerInvariant(), Batches = [Batch("EXPORTED_AWAITING_IMPORT")] }, owner: true, Profile());
            Assert.True(here.IsTestPc);
            Assert.Equal(Visibility.Visible, here.SendButton.Visibility);
            Assert.Equal("", here.SendCaption.Text);
            Assert.Equal(Visibility.Collapsed, here.SendCaption.Visibility);
        });
    }

    [Fact]
    public void A_send_on_another_pc_is_refused_before_any_service_call()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Pc = "WORKPC", Batches = [Batch("EXPORTED_AWAITING_IMPORT")] };
            var view = Shown(fake, owner: true, Profile());
            var refused = Assert.ThrowsAsync<InvalidOperationException>(view.SendAsync).GetAwaiter().GetResult();
            Assert.Equal(TallyConnectedMode.OnlyTheTestPc, refused.Message);
            Assert.DoesNotContain(fake.Calls, call => call.StartsWith("Send", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Delivery_by_file_or_a_missing_address_keeps_Send_disabled_with_a_reason()
    {
        TallyStaRunner.Run(() =>
        {
            var byFile = Shown(new FakeActions { Batches = [Batch("EXPORTED_AWAITING_IMPORT")] }, owner: true, Profile(delivery: "FILE"));
            Assert.False(byFile.SendButton.IsEnabled);
            Assert.Contains("delivers by file", (string)byFile.SendButton.ToolTip, StringComparison.Ordinal);
            var noAddress = Shown(new FakeActions { Batches = [Batch("EXPORTED_AWAITING_IMPORT")] }, owner: true, Profile(endpoint: null));
            Assert.False(noAddress.SendButton.IsEnabled);
            Assert.False(noAddress.ReadBackButton.IsEnabled);
            Assert.True(noAddress.ReadBackFileButton.IsEnabled);
        });
    }

    [Theory]
    [InlineData("APPROVED_READY")]
    [InlineData("EXPORTED_AWAITING_IMPORT")]
    public void Without_owner_permission_nothing_loads_nothing_is_enabled_and_Send_is_hidden(string status)
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Batches = [Batch(status)] };
            var view = Shown(fake, owner: false, Profile());
            Assert.DoesNotContain(fake.Calls, call => call.StartsWith("LoadBatches", StringComparison.Ordinal));
            foreach (var button in All(view)) Assert.False(button.IsEnabled);
            Assert.Equal(Visibility.Collapsed, view.SendButton.Visibility);
            Assert.Null(view.NextStepButton);
            Assert.Contains("Owner permission", view.GuidanceText.Text, StringComparison.Ordinal);
            view.WriteFileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain(fake.Calls, call => call.StartsWith("WriteFile", StringComparison.Ordinal));
            Assert.Equal("Owner permission is required.", view.ResultNotice.Title);
        });
    }

    [Fact]
    public void Books_and_batch_state_are_chips_with_their_words()
    {
        TallyStaRunner.Run(() =>
        {
            var test = Shown(new FakeActions(), owner: true, Profile());
            Assert.Equal("TEST books", test.BooksChip.Text);
            Assert.Equal(StatusTone.Information, test.BooksChip.Tone);
            var live = Shown(new FakeActions(), owner: true, Profile() with { Environment = "PRODUCTION", TestMachineName = null });
            Assert.Equal("LIVE books", live.BooksChip.Text);
            Assert.Equal(StatusTone.Warning, live.BooksChip.Tone);

            Assert.Equal(("Matches Tally", StatusTone.Success), TallyTransferView.BatchStatusWord("RECONCILED"));
            Assert.Equal(("Differences found", StatusTone.Critical), TallyTransferView.BatchStatusWord("FAILED_RECONCILIATION"));
            Assert.Equal(("Not compared yet", StatusTone.Warning), TallyTransferView.BatchStatusWord("IMPORT_REPORTED_AWAITING_RECONCILIATION"));
            foreach (var status in TallyBatchStatus.All)
            {
                var (shown, _) = TallyTransferView.BatchStatusWord(status);
                Assert.False(string.IsNullOrWhiteSpace(shown));
                // Nothing before Compare may say the vouchers are in Tally.
                if (status is not ("RECONCILED" or "RECONCILED_WITH_ACCEPTED_WARNINGS" or "PARTIALLY_APPLIED"))
                    Assert.DoesNotContain("in Tally", shown, StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public void Button_labels_are_plain_verbs_without_step_numbers_or_glyphs()
    {
        TallyStaRunner.Run(() =>
        {
            var view = Shown(new FakeActions(), owner: true, Profile());
            foreach (var button in All(view))
            {
                var label = (string)button.Content;
                Assert.DoesNotMatch(@"\d|[→←…▾›]", label);
                Assert.Equal(label, System.Windows.Automation.AutomationProperties.GetName(button));
            }
            Assert.Equal(new[] { "Write masters", "Write file", "Read back", "Read back from file", "Send", "Compare", "Open evidence folder" },
                All(view).Select(button => (string)button.Content));
        });
    }

    [Fact]
    public void Write_file_then_read_back_then_compare_moves_the_primary_button_along()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Pc = "WORKPC", Batches = [Batch("APPROVED_READY")] };
            var view = Shown(fake, owner: true, Profile());
            Assert.Equal(nameof(TallyTransferView.WriteFileButton), PrimaryName(view));

            fake.Batches = [Batch("EXPORTED_AWAITING_IMPORT")];
            Click(view.WriteFileButton);
            Assert.Contains("WriteFile:40", fake.Calls);
            Assert.Equal("File written", view.ResultNotice.Title);
            Assert.Contains("Nothing has been sent to Tally", view.ResultNotice.Message, StringComparison.Ordinal);
            Assert.Equal("File written", view.BatchChip.Text);
            Assert.Equal(nameof(TallyTransferView.ReadBackButton), PrimaryName(view));
            Assert.False(view.CompareButton.IsEnabled);

            Click(view.ReadBackButton);
            Assert.Contains("ReadBack:40", fake.Calls);
            Assert.Equal("Read back recorded", view.ResultNotice.Title);
            Assert.True(view.CompareButton.IsEnabled);
            Assert.Equal(nameof(TallyTransferView.CompareButton), PrimaryName(view));

            fake.Batches = [Batch("RECONCILED")];
            Click(view.CompareButton);
            Assert.Contains("Compare:40:77", fake.Calls);
            Assert.Equal("Matches Tally", view.ResultNotice.Title);
            Assert.Equal(StatusTone.Success, view.ResultNotice.Tone);
            Assert.Null(view.NextStepButton);
        });
    }

    [Fact]
    public void An_incomplete_read_back_is_a_warning_and_does_not_allow_Compare()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Batches = [Batch("EXPORTED_AWAITING_IMPORT")], Readback = new(78, false, "COMPANY_NOT_OPEN", 0, "Open the company in Tally.") };
            var view = Shown(fake, owner: true, Profile());
            Click(view.ReadBackButton);
            Assert.Equal("Read back incomplete", view.ResultNotice.Title);
            Assert.Equal(StatusTone.Warning, view.ResultNotice.Tone);
            Assert.False(view.CompareButton.IsEnabled);
            var refused = Assert.ThrowsAsync<InvalidOperationException>(view.CompareAsync).GetAwaiter().GetResult();
            Assert.Contains("Read back from Tally first", refused.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Send_shows_Tallys_answer_without_claiming_the_vouchers_are_in_Tally()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Pc = TestPc, Batches = [Batch("EXPORTED_AWAITING_IMPORT")] };
            var view = Shown(fake, owner: true, Profile());
            Click(view.SendButton);
            Assert.Contains("Send:40", fake.Calls);
            Assert.Equal("Tally answered: created", view.ResultNotice.Title);
            Assert.Contains("Read back from Tally and compare", view.ResultNotice.Message, StringComparison.Ordinal);

            fake.Send = new(5, "TIMED_OUT", 3, false, "Tally did not answer in time. Read back before sending again.");
            Click(view.SendButton);
            Assert.Equal("Tally did not answer in time", view.ResultNotice.Title);
            Assert.Equal(StatusTone.Warning, view.ResultNotice.Tone);
        });
    }

    [Fact]
    public void A_refused_service_call_shows_its_sentence_in_the_notice_and_keeps_the_screen_usable()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Batches = [Batch("APPROVED_READY")], Failure = new InvalidOperationException("Approve the batch before writing its Tally file.") };
            var view = Shown(fake, owner: true, Profile());
            Click(view.WriteFileButton);
            Assert.Equal(StatusTone.Critical, view.ResultNotice.Tone);
            Assert.Contains("Approve the batch", view.ResultNotice.Message, StringComparison.Ordinal);
            Assert.True(view.IsEnabled);
        });
    }

    [Fact]
    public void Write_masters_asks_for_a_folder_and_lists_the_ledger_names_to_approve()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Batches = [] };
            var folder = (string?)null;
            var view = Shown(fake, owner: true, Profile(), chooseFolder: () => folder);
            Click(view.WriteMastersButton);
            Assert.DoesNotContain(fake.Calls, call => call.StartsWith("WriteMasters", StringComparison.Ordinal));

            folder = @"C:\Synthetic\masters";
            Click(view.WriteMastersButton);
            Assert.Contains(@"WriteMasters:1:C:\Synthetic\masters", fake.Calls);
            Assert.Equal("Masters file written", view.ResultNotice.Title);
            Assert.Equal(StatusTone.Warning, view.ResultNotice.Tone);
            Assert.Contains("SC999 SALES_REVENUE: Sales", view.ResultNotice.Message, StringComparison.Ordinal);
            Assert.Contains("golden-review.txt", view.ResultNotice.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Read_back_from_a_file_passes_the_chosen_file_and_the_batch_day()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Batches = [Batch("EXPORTED_AWAITING_IMPORT")] };
            var view = Shown(fake, owner: true, Profile(), chooseFile: () => @"C:\Synthetic\daybook.xml");
            Assert.Equal(new DateTime(2026, 8, 25), view.ReadBackFromInput.SelectedDate);
            Click(view.ReadBackFileButton);
            Assert.Contains(@"ReadBackFile:40:2026-08-25:2026-08-25:C:\Synthetic\daybook.xml", fake.Calls);
            Assert.True(view.CompareButton.IsEnabled);
        });
    }

    [Fact]
    public void Open_evidence_folder_opens_the_batch_folder_and_says_so_when_there_is_none()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Batches = [Batch("EXPORTED_AWAITING_IMPORT")] };
            var opened = new List<string>();
            var view = Shown(fake, owner: true, Profile(), openFolder: opened.Add);
            Click(view.OpenFolderButton);
            Assert.Equal(new[] { @"C:\Synthetic\evidence\batch-40" }, opened);
            fake.Folder = null;
            Click(view.OpenFolderButton);
            Assert.Single(opened);
            Assert.Contains("no evidence folder", view.ResultNotice.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_transfer_card_follows_the_company_chosen_on_the_vouchers_screen()
    {
        TallyStaRunner.Run(() =>
        {
            var fake = new FakeActions { Batches = [Batch("APPROVED_READY")] };
            var view = new TallyVouchersView(() => true, () => new TallyVouchersViewTests.FakeProfiles(), () => throw new InvalidOperationException("not used"), () => fake);
            Assert.NotNull(view.Transfer);
            view.LoadCompaniesAsync().GetAwaiter().GetResult();
            Assert.Contains("LoadBatches:1", fake.Calls);
            Assert.Equal("Approved", view.Transfer!.BatchChip.Text);
        });
    }

    private static TallyTransferView Shown(FakeActions fake, bool owner, TallyProfile profile,
        Func<string?>? chooseFolder = null, Func<string?>? chooseFile = null, Action<string>? openFolder = null)
    {
        var view = new TallyTransferView(() => owner, () => fake, chooseFolder, chooseFile, openFolder);
        view.ShowCompanyAsync(profile).GetAwaiter().GetResult();
        return view;
    }

    private static Button[] All(TallyTransferView view) =>
        [view.WriteMastersButton, view.WriteFileButton, view.ReadBackButton, view.ReadBackFileButton, view.SendButton, view.CompareButton, view.OpenFolderButton];

    private static string? PrimaryName(TallyTransferView view) =>
        view.NextStepButton is null ? null
        : ReferenceEquals(view.NextStepButton, view.WriteFileButton) ? nameof(TallyTransferView.WriteFileButton)
        : ReferenceEquals(view.NextStepButton, view.ReadBackButton) ? nameof(TallyTransferView.ReadBackButton)
        : ReferenceEquals(view.NextStepButton, view.ReadBackFileButton) ? nameof(TallyTransferView.ReadBackFileButton)
        : ReferenceEquals(view.NextStepButton, view.SendButton) ? nameof(TallyTransferView.SendButton)
        : ReferenceEquals(view.NextStepButton, view.CompareButton) ? nameof(TallyTransferView.CompareButton)
        : "other";

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal sealed class FakeActions : ITallyTransferActions
    {
        public string Pc { get; init; } = TestPc;
        public string ThisPc => Pc;
        public List<string> Calls { get; } = [];
        public IReadOnlyList<TallyTransferBatch> Batches { get; set; } = [];
        public Exception? Failure { get; init; }
        public string? Folder { get; set; } = @"C:\Synthetic\evidence\batch-40";
        public TallyReadbackSummary Readback { get; init; } = new(77, true, null, 3, null);
        public TallySendSummary Send { get; set; } = new(4, "RESPONDED", 3, true, "Tally says 3 of 3 voucher(s) created. Read back from Tally and compare to prove what it holds.");

        private Task<T> Done<T>(string call, T value)
        {
            Calls.Add(call);
            return Failure is null ? Task.FromResult(value) : Task.FromException<T>(Failure);
        }

        public Task<IReadOnlyList<TallyTransferBatch>> LoadBatchesAsync(int profileId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"LoadBatches:{profileId}");
            return Task.FromResult(Batches);
        }
        public Task<TallyMastersSummary> WriteMastersAsync(int profileId, string folder, CancellationToken cancellationToken = default) =>
            Done($"WriteMasters:{profileId}:{folder}", new TallyMastersSummary(folder + @"\tally-masters-golden.xml", folder + @"\tally-masters-golden-review.txt", 2, 1, 6, ["SC999 SALES_REVENUE: Sales"]));
        public Task<TallyFileSummary> WriteFileAsync(long batchId, CancellationToken cancellationToken = default) =>
            Done($"WriteFile:{batchId}", new TallyFileSummary(@"GOLDEN\SC999\2026-08\batch-40\payload.xml", new string('a', 64), 3, 3540m));
        public Task<TallyReadbackSummary> ReadBackAsync(long batchId, CancellationToken cancellationToken = default) => Done($"ReadBack:{batchId}", Readback);
        public Task<TallyReadbackSummary> ReadBackFileAsync(long batchId, DateOnly from, DateOnly to, string path, CancellationToken cancellationToken = default) =>
            Done($"ReadBackFile:{batchId}:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{path}", Readback);
        public Task<TallySendSummary> SendAsync(long batchId, CancellationToken cancellationToken = default) => Done($"Send:{batchId}", Send);
        public Task<TallyCompareSummary> CompareAsync(long batchId, long readbackId, CancellationToken cancellationToken = default) =>
            Done($"Compare:{batchId}:{readbackId}", new TallyCompareSummary("RECONCILED", "RECONCILED", 3, 3, 0, 0));
        public Task<string?> EvidenceFolderAsync(long batchId, CancellationToken cancellationToken = default) => Done($"EvidenceFolder:{batchId}", Folder);
    }
}

/// <summary>Runs a WPF test body on an STA thread and rethrows what it threw.</summary>
internal static class TallyStaRunner
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
