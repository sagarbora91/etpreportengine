using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Desktop.Modules.NoSaleDays;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 lane N1 (decision 52): the mark / withdraw buttons and the dialog with a fake service. Owner and Store Manager get a
/// button; a Viewer gets none. A refusal keeps the dialog open with the sentence; a saved change closes it and runs onDone.
/// Synthetic store codes only.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class NoSaleDayActionsTests
{
    private static readonly DateOnly Day = new(2026, 8, 21);

    [Fact]
    public void Owner_and_store_manager_get_the_buttons_and_a_viewer_gets_none() => RunSta(() =>
    {
        foreach (var (access, expected) in new[] { (ShellAccess.Owner, true), (ShellAccess.StoreManager, true), (ShellAccess.Viewer, false), (ShellAccess.DatabaseSetup, false) })
        {
            var actions = new NoSaleDayActions(() => new FakeService(), () => access);
            Assert.Equal(expected, actions.CanChange);
            Assert.Equal(expected, actions.CreateMarkButton("ZZ001", Day) is not null);
            Assert.Equal(expected, actions.CreateWithdrawButton("ZZ001", Day) is not null);
        }
        var owner = new NoSaleDayActions(() => new FakeService(), () => ShellAccess.Owner, code => code == "ZZ001" ? "Shop one" : null);
        var mark = owner.CreateMarkButton("ZZ001", Day)!;
        Assert.Equal(NoSaleDayActions.MarkLabel, mark.Content);
        Assert.Equal("Mark as no-sale day for Shop one", AutomationProperties.GetName(mark));
        Assert.Equal(NoSaleDayActions.WithdrawLabel, owner.CreateWithdrawButton("ZZ001", Day)!.Content);
        Assert.Null(owner.CreateMarkButton(" ", Day));
    });

    [Fact]
    public void Mark_dialog_asks_for_a_reason_choice_and_text_before_it_calls_the_service() => RunSta(() =>
    {
        var service = new FakeService();
        var actions = new NoSaleDayActions(() => service, () => ShellAccess.StoreManager);
        var dialog = new NoSaleDayDialog(NoSaleDayDialogMode.Mark, "Shop one", Day,
            (code, reason) => actions.SubmitAsync(NoSaleDayDialogMode.Mark, "ZZ001", Day, code, reason));

        Assert.Equal(480, dialog.Width);
        Assert.Equal(3, dialog.ReasonChoices.Count);
        Assert.Equal("Keep working", dialog.CancelAction.Content);
        Assert.Equal("Mark", dialog.PrimaryAction.Content);
        Assert.True(dialog.CancelAction.IsCancel);
        // The no-op button comes first and the primary last (L3).
        var row = (WrapPanel)VisualParent(dialog.PrimaryAction);
        Assert.Same(dialog.CancelAction, row.Children[0]);
        Assert.Same(dialog.PrimaryAction, row.Children[^1]);

        Assert.False(dialog.SubmitAsync().GetAwaiter().GetResult());
        Assert.Equal("Not saved: Choose why there were no sales.", dialog.Message.Text);
        dialog.ReasonChoices[0].IsChecked = true;
        Assert.False(dialog.SubmitAsync().GetAwaiter().GetResult());
        Assert.Equal("Not saved: Write a short reason.", dialog.Message.Text);
        Assert.Empty(service.Calls);

        dialog.ReasonInput.Text = "  Festival holiday  ";
        Assert.True(dialog.SubmitAsync().GetAwaiter().GetResult());
        Assert.Equal(["MARK ZZ001 2026-08-21 CLOSED_HOLIDAY Festival holiday"], service.Calls);
        Assert.Equal(Visibility.Collapsed, dialog.Message.Visibility);
    });

    [Fact]
    public void A_refusal_keeps_the_dialog_open_with_the_plain_sentence() => RunSta(() =>
    {
        var service = new FakeService { Refusal = new NoSaleDayRefusedException("ETP already holds sales for this shop on this day, so it cannot be marked as a no-sale day.") };
        var actions = new NoSaleDayActions(() => service, () => ShellAccess.Owner);
        var dialog = new NoSaleDayDialog(NoSaleDayDialogMode.Mark, "Shop one", Day,
            (code, reason) => actions.SubmitAsync(NoSaleDayDialogMode.Mark, "ZZ001", Day, code, reason));
        dialog.ReasonChoices[2].IsChecked = true;
        dialog.ReasonInput.Text = "Nothing sold";

        Assert.False(dialog.SubmitAsync().GetAwaiter().GetResult());
        Assert.Equal("Not saved: ETP already holds sales for this shop on this day, so it cannot be marked as a no-sale day.", dialog.Message.Text);
        Assert.Equal(Visibility.Visible, dialog.Message.Visibility);
        Assert.True(dialog.PrimaryAction.IsEnabled);

        service.Refusal = new UnauthorizedAccessException("Only the Owner or a Store Manager can mark or withdraw a no-sale day.");
        Assert.False(dialog.SubmitAsync().GetAwaiter().GetResult());
        Assert.EndsWith("Only the Owner or a Store Manager can mark or withdraw a no-sale day.", dialog.Message.Text, StringComparison.Ordinal);
    });

    [Fact]
    public void Withdraw_dialog_asks_only_for_a_reason_and_on_done_runs_after_a_saved_change() => RunSta(() =>
    {
        var service = new FakeService();
        var shown = 0;
        var actions = new NoSaleDayActions(() => service, () => ShellAccess.Owner, showDialog: (dialog, _) =>
        {
            shown++;
            Assert.Equal(NoSaleDayDialogMode.Withdraw, dialog.Mode);
            Assert.Empty(dialog.ReasonChoices);
            Assert.Equal("Withdraw", dialog.PrimaryAction.Content);
            dialog.ReasonInput.Text = "Sales file arrived";
            return dialog.SubmitAsync().GetAwaiter().GetResult();
        });
        var done = 0;
        var button = actions.CreateWithdrawButton("ZZ001", Day, () => done++)!;

        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.Equal(1, shown);
        Assert.Equal(1, done);
        Assert.Equal(["WITHDRAW ZZ001 2026-08-21 Sales file arrived"], service.Calls);
    });

    [Fact]
    public void Today_sales_card_offers_the_mark_while_waiting_and_shows_the_line_once_marked() => RunSta(() =>
    {
        var service = new FakeService();
        var previous = NoSaleDayActions.Current;
        try
        {
            NoSaleDayActions.Current = new NoSaleDayActions(() => service, () => ShellAccess.StoreManager);
            var waiting = new TodaySalesView(Document(marked: false));
            Assert.Contains(Texts(waiting), text => text == TodaySalesView.WaitingText(Day));
            Assert.Contains(Buttons(waiting), button => Equals(button.Content, NoSaleDayActions.MarkLabel));

            const string line = "No sales (marked by manager on 21 Aug 2026)";
            var marked = new TodaySalesView(Document(marked: true, line));
            Assert.Contains(Texts(marked), text => text == line);
            Assert.Contains(Buttons(marked), button => Equals(button.Content, NoSaleDayActions.WithdrawLabel));
            Assert.DoesNotContain(Texts(marked), text => text == TodaySalesView.WaitingText(Day));

            NoSaleDayActions.Current = new NoSaleDayActions(() => service, () => ShellAccess.Viewer);
            var viewer = new TodaySalesView(Document(marked: false));
            Assert.Contains(Texts(viewer), text => text == TodaySalesView.WaitingText(Day));
            Assert.DoesNotContain(Buttons(viewer), button => Equals(button.Content, NoSaleDayActions.MarkLabel));
        }
        finally { NoSaleDayActions.Current = previous; }
    });

    [Fact]
    public void No_sale_desktop_files_use_tokens_and_styles_only()
    {
        var root = RepositoryRoot();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "NoSaleDays"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(new Regex(@"#[0-9A-Fa-f]{6}\b|Brushes\.|FontSize\s*=|FontWeight\s*=|MinHeight\s*=|MaxHeight\s*=|Padding\s*=\s*new"), text);
            Assert.DoesNotContain("Microsoft.Data.SqlClient", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ConnectionString", text, StringComparison.Ordinal);
        }
    }

    private static DailySalesReportDocument Document(bool marked, string line = "")
    {
        var document = DailySalesReportBuilder.Build(Day, [], [], new Dictionary<string, decimal?>(), stores: [new("ZZ001", "Shop one")]) with
        {
            EveningSheets =
            [
                new("ZZ001", "Shop one", null, null, null, null, [new("VALUE", null, null, null, null, null, null, "currency")]),
                new("COMBINED", "Combined", null, null, null, null, [new("VALUE", null, null, null, null, null, null, "currency")])
            ]
        };
        return marked ? DailySalesReportBuilder.ApplyNoSaleDays(document, [new("ZZ001", line)]) : document;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static IEnumerable<string> Texts(DependencyObject root) => Descendants<TextBlock>(root).Select(x => x.Text);

    private static IEnumerable<Button> Buttons(DependencyObject root) => Descendants<Button>(root);

    private static DependencyObject VisualParent(FrameworkElement element) => LogicalTreeHelper.GetParent(element);

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Etp.Reporting.slnx"))) return folder.FullName;
        throw new DirectoryNotFoundException("Etp.Reporting.slnx");
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

    private sealed class FakeService : INoSaleDayService
    {
        public List<string> Calls { get; } = [];
        public Exception? Refusal { get; set; }

        public Task<IReadOnlyList<NoSaleDay>> LoadAsync(DateOnly businessDate, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NoSaleDay>>([]);

        public Task MarkAsync(string storeCode, DateOnly businessDate, string reasonCode, string reason, CancellationToken ct = default)
        {
            if (Refusal is not null) return Task.FromException(Refusal);
            Calls.Add($"MARK {storeCode} {businessDate:yyyy-MM-dd} {reasonCode} {reason}");
            return Task.CompletedTask;
        }

        public Task WithdrawAsync(string storeCode, DateOnly businessDate, string reason, CancellationToken ct = default)
        {
            if (Refusal is not null) return Task.FromException(Refusal);
            Calls.Add($"WITHDRAW {storeCode} {businessDate:yyyy-MM-dd} {reason}");
            return Task.CompletedTask;
        }
    }
}
