using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// The Parts and purchases screen (1.10.0, design 3.6, lane parts) against a fake IServiceReportQuery that returns the
/// lane-sql contract's ServiceParts. All values are synthetic: invoices PISYN…, GRNs GRNSYN…, jobs JOAW330SYN…, items PART-SYN-NN.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServicePartsViewTests
{
    private static readonly string[] ForbiddenHeaderWords = ["phone", "mobile", "landline", "e-mail", "email", "address", "customer"];
    private static readonly DateOnly Snapshot = new(2026, 10, 5);

    [Fact]
    public void Open_and_closed_invoices_show_the_status_and_days_open_the_contract_carries()
    {
        var open = Invoice("PISYN0001", new(2026, 9, 20), daysOpen: 15);
        var received = Invoice("PISYN0002", new(2026, 9, 1), grn: "GRNSYN0002", grnDate: new(2026, 9, 9), received: new(2026, 9, 10), daysOpen: 9);

        Assert.True(open.IsOpen);
        Assert.Equal("Open", ServicePartsView.StatusLabel(open));
        Assert.False(received.IsOpen);
        Assert.Equal("Received", received.Status);
        Assert.Equal("Closed", ServicePartsView.StatusLabel(received));
    }

    [Fact]
    public void Invoices_sort_open_first_then_oldest_first_with_undated_last()
    {
        var sorted = ServicePartsView.Sort(
        [
            Invoice("PISYN0010", new(2026, 9, 2), received: new(2026, 9, 5)),
            Invoice("PISYN0011", new(2026, 9, 30)),
            Invoice("PISYN0012", null),
            Invoice("PISYN0013", new(2026, 8, 1)),
            Invoice("PISYN0014", new(2026, 9, 1), received: new(2026, 9, 9))
        ]);
        Assert.Equal(["PISYN0013", "PISYN0011", "PISYN0012", "PISYN0014", "PISYN0010"], sorted.Select(invoice => invoice.InvoiceNumber));
    }

    [Fact]
    public void Filters_apply_open_only_month_and_item_text_over_the_invoice_lines()
    {
        var invoices = new[]
        {
            Invoice("PISYN0020", new(2026, 9, 3), lines: [Line("PART-SYN-A-CROWN", 1, 0, 100m)]),
            Invoice("PISYN0021", new(2026, 9, 12), received: new(2026, 9, 20), lines: [Line("PART-SYN-B-STRAP", 2, 2, 200m)]),
            Invoice("PISYN0022", new(2026, 8, 28), lines: [Line("PART-SYN-C-CROWN", 1, 0, 50m)]),
            Invoice("PISYN0023", null)
        };

        Assert.Equal(["PISYN0020", "PISYN0022", "PISYN0023"], ServicePartsView.Filter(invoices, openOnly: true, month: null, itemText: null).Select(invoice => invoice.InvoiceNumber));
        Assert.Equal(["PISYN0020", "PISYN0021"], ServicePartsView.Filter(invoices, openOnly: false, month: new(2026, 9, 1), itemText: "").Select(invoice => invoice.InvoiceNumber));
        Assert.Equal(["PISYN0020", "PISYN0022"], ServicePartsView.Filter(invoices, openOnly: false, month: null, itemText: " crown ").Select(invoice => invoice.InvoiceNumber));
        Assert.Equal(["PISYN0021"], ServicePartsView.Filter(invoices, openOnly: false, month: null, itemText: "part-syn-b").Select(invoice => invoice.InvoiceNumber));
        Assert.Equal(["PISYN0020"], ServicePartsView.Filter(invoices, openOnly: true, month: new(2026, 9, 1), itemText: "crown").Select(invoice => invoice.InvoiceNumber));
    }

    [Fact]
    public void The_numbers_take_the_contract_counts_and_name_the_oldest_open_invoice()
    {
        var parts = FakePartsQuery.SampleParts();
        var numbers = ServicePartsView.Summarise(parts, Snapshot);

        Assert.Equal(2, numbers.OpenInvoices);
        Assert.Equal(1250m, numbers.OpenValue);
        Assert.Equal(49, numbers.OldestOpenDays);
        Assert.Equal("PISYN0102", numbers.OldestOpenInvoice);
        Assert.Equal(1, numbers.ReceivedThisMonth);
        Assert.Equal(2, numbers.JobsWaitingForParts);
        Assert.Equal(2, numbers.GitLinesLast30Days);
        Assert.Equal(Snapshot, numbers.AsOf);
        // A query that leaves OldestOpenDays null still gets it from the invoices.
        Assert.Equal(49, ServicePartsView.Summarise(parts with { OldestOpenDays = null }, Snapshot).OldestOpenDays);
        Assert.Equal(["STMSYN0002", "STMSYN0001"], ServicePartsView.RecentGit(parts.Git, Snapshot).Select(line => line.StmNumber));
    }

    [Fact]
    public void The_GIT_grid_keeps_the_same_30_days_as_the_GIT_card()
    {
        // R-UI-12: a line exactly 30 days before the as-of date is outside both (card: date > asOf - 30).
        var edge = new ServiceGitLine("STMSYN0030", Snapshot.AddDays(-30), "PART-SYN-08", 1, "CCPT", "AW330", 10m, Snapshot);
        var inside = new ServiceGitLine("STMSYN0029", Snapshot.AddDays(-29), "PART-SYN-09", 1, "CCPT", "AW330", 10m, Snapshot);
        Assert.Equal(["STMSYN0029"], ServicePartsView.RecentGit([edge, inside], Snapshot).Select(line => line.StmNumber));
    }

    [Fact]
    public void As_of_is_the_contract_as_at_then_the_latest_reading_then_today()
    {
        var today = new DateOnly(2026, 10, 10);
        IReadOnlyList<ServiceRefresh> refreshes = [new("S009", new(2026, 10, 3), 1, 1, DateTime.UtcNow)];
        Assert.Equal(Snapshot, ServicePartsView.AsOf(FakePartsQuery.SampleParts(), refreshes, today));
        Assert.Equal(new DateOnly(2026, 10, 3), ServicePartsView.AsOf(ServicePartsView.EmptyParts, refreshes, today));
        Assert.Equal(today, ServicePartsView.AsOf(ServicePartsView.EmptyParts, [], today));
    }

    [Fact]
    public void Parts_screen_shows_invoices_numbers_both_panels_git_and_closing_stock()
    {
        RunSta(() =>
        {
            var query = new FakePartsQuery();
            var view = new ServicePartsView(() => query, NoExport);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(1, query.PartsCalls);
            Assert.Equal(["PISYN0102", "PISYN0101", "PISYN0103", "PISYN0104"], view.Rows.Select(row => (string)row.Cells[0]!));
            var open = view.Rows[0];
            Assert.Equal("Open", open.Cells[8]);
            Assert.Equal(49, open.Cells[9]);
            Assert.Null(open.Cells[2]);
            var closed = view.Rows[2];
            Assert.Equal("Closed", closed.Cells[8]);
            Assert.Equal("GRNSYN0103", closed.Cells[2]);
            Assert.Equal(9, closed.Cells[9]);
            Assert.Equal("CCPT", closed.Cells[10]);
            Assert.Equal("4 invoices · open first, oldest first.", view.StatusText);
            Assert.Equal(["Invoice", "Invoice date", "GRN", "GRN date", "Items", "Shipped qty", "Received qty", "Net amount", "Status", "Days open", "From location"], view.ColumnHeaders);

            Assert.NotNull(view.NumbersShown);
            var numbers = view.NumbersShown!;
            Assert.Equal(2, numbers.OpenInvoices);
            Assert.Equal(5, Descendants<KpiCard>(view).Count());

            Assert.Equal(["JOAW330SYN0302", "JOAW330SYN0301"], view.WaitingRows.Select(row => (string)row.Cells[0]!));
            Assert.Equal(21, view.WaitingRows[0].Cells[3]);
            Assert.Same(view.WaitingRows, view.Waiting.ItemsSource);
            Assert.StartsWith("2 jobs waiting for parts", view.WaitingStatusText, StringComparison.Ordinal);
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text == ServicePartsView.NoLinkText);
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text == ServicePartsView.WaitingTitle);

            Assert.Equal(2, view.GitRows.Count);
            Assert.Equal("STMSYN0002", view.GitRows[0].Cells[1]);
            Assert.Equal("2 lines in the 30 days to 05 Oct 2026.", view.GitStatusText);
            Assert.Equal("Latest closing stock (S006) as at 03 Oct 2026: 412 items, quantity 1,180, value 95,400.00.", view.ClosingStockText);
            Assert.Equal(ServicePartsView.NoLinesText, view.LinesStatusText);
            Assert.Empty(view.LineRows);
            Assert.Equal(["All months", "Sep 2026", "Aug 2026"], view.MonthChoices.Select(choice => choice.Label));
        });
    }

    [Fact]
    public void Selecting_an_invoice_shows_its_lines()
    {
        RunSta(() =>
        {
            var view = new ServicePartsView(() => new FakePartsQuery(), NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();

            view.SelectInvoice("PISYN0101");

            Assert.Equal(2, view.LineRows.Count);
            Assert.Equal(["PART-SYN-01", "PART-SYN-02"], view.LineRows.Select(row => (string?)row.Cells[0]));
            Assert.Same(view.LineRows, view.Lines.ItemsSource);
            Assert.Equal("Invoice PISYN0101: 2 lines.", view.LinesStatusText);
            Assert.Equal("PISYN0101", ((ServicePartsInvoice)((ServiceGridRow)view.Table.SelectedItem).Source).InvoiceNumber);

            view.SelectInvoice("PISYN0104");
            Assert.Empty(view.LineRows);
            Assert.Equal("Invoice PISYN0104: no lines in the export.", view.LinesStatusText);
        });
    }

    [Fact]
    public void Open_only_month_and_item_filters_narrow_the_grid_and_the_status_line()
    {
        RunSta(() =>
        {
            var query = new FakePartsQuery();
            var view = new ServicePartsView(() => query, NoExport) { OpenOnly = true };
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["PISYN0102", "PISYN0101"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("2 invoices · open first, oldest first · open only.", view.StatusText);

            view.OpenOnly = false; view.SelectedMonth = new(2026, 9, 1);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["PISYN0101", "PISYN0103", "PISYN0104"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("Sep 2026", view.MonthChoices.First(choice => choice.Month == new DateOnly(2026, 9, 1)).Label);
            Assert.Equal("3 invoices · open first, oldest first · Sep 2026.", view.StatusText);

            view.SelectedMonth = null; view.ItemFilter = "part-syn-03";
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["PISYN0102"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("1 invoice · open first, oldest first · item \"part-syn-03\".", view.StatusText);

            view.ItemFilter = "no such part";
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Empty(view.Rows);
            Assert.Equal("No purchase invoice matches these filters.", view.StatusText);
            // The numbers and the other panels never follow the invoice filters.
            Assert.Equal(2, view.NumbersShown!.OpenInvoices);
            Assert.Equal(2, view.WaitingRows.Count);
        });
    }

    [Fact]
    public void A_waiting_job_opens_Job_history_through_the_shell_opener()
    {
        RunSta(() =>
        {
            var opened = new List<string>();
            var view = new ServicePartsView(() => new FakePartsQuery(), NoExport, opened.Add);
            view.ActivateAsync().GetAwaiter().GetResult();

            view.OpenSelectedJobHistory();
            Assert.Empty(opened);

            view.Waiting.SelectedItem = view.WaitingRows[1];
            view.OpenSelectedJobHistory();
            Assert.Equal("JOAW330SYN0301", Assert.Single(opened));

            view.OpenJobHistory("JOAW330SYN0302");
            Assert.Equal("JOAW330SYN0302", opened[^1]);

            // Without an opener (a screen built outside the shell) the row action does nothing.
            var alone = new ServicePartsView(() => new FakePartsQuery(), NoExport);
            alone.OpenJobHistory("JOAW330SYN0302");
        });
    }

    [Fact]
    public void ServiceScreens_creates_the_Parts_screen_with_the_opener_and_the_history_route_carries_the_job()
    {
        RunSta(() =>
        {
            var query = new FakePartsQuery();
            var opened = new List<string>();
            var parts = Assert.IsType<ServicePartsView>(ServiceScreens.Create(ServiceScreens.PartsTask, () => query, NoExport, null, opened.Add));
            SpinUntil(() => !parts.IsLoading);
            Assert.Equal(4, parts.Rows.Count);
            parts.OpenJobHistory("JOAW330SYN0302");
            Assert.Equal(["JOAW330SYN0302"], opened);
            Assert.Equal("service-parts", ServiceScreens.PartsTask);

            var route = ServiceScreens.JobHistoryRoute(" JOAW330SYN0302 ");
            Assert.Equal((ServiceScreens.JobHistoryTask, "JOAW330SYN0302"), (route.TaskId, route.Argument));
            var history = Assert.IsType<ServiceJobHistoryView>(ServiceScreens.Create(route.TaskId!, () => query, NoExport, route.Argument, _ => { }));
            SpinUntil(() => !history.IsLoading);
            Assert.Equal("JOAW330SYN0302", history.JobNumber);
            Assert.Equal("JOAW330SYN0302", query.LastJob);
        });
    }

    [Fact]
    public void Both_panels_export_their_grid_columns_and_rows_without_privacy_headers()
    {
        RunSta(() =>
        {
            var captured = new List<(string Path, ExcelReportMetadata Metadata, ExcelReportData Data)>();
            var view = new ServicePartsView(() => new FakePartsQuery(), (path, metadata, data) => { captured.Add((path, metadata, data)); return Task.CompletedTask; });
            view.ActivateAsync().GetAwaiter().GetResult();

            view.ExportToPathAsync("synthetic-invoices.xlsx").GetAwaiter().GetResult();
            view.ExportWaitingToPathAsync("synthetic-waiting.xlsx").GetAwaiter().GetResult();

            Assert.Equal(2, captured.Count);
            var invoices = captured[0];
            Assert.Equal(view.Table.Columns.Select(column => (string)column.Header), invoices.Data.Columns.Select(column => column.Header));
            Assert.Equal(view.Rows.Count, invoices.Data.Rows.Count);
            Assert.All(invoices.Data.Rows, row => Assert.Equal(invoices.Data.Columns.Count, row.Count));
            Assert.Equal("Service parts and purchases", invoices.Metadata.ReportName);
            Assert.Equal((new DateOnly(2026, 8, 17), Snapshot), (invoices.Metadata.DateFrom, invoices.Metadata.DateTo));
            Assert.Equal(ServiceScreenView.ServiceCentreLabel, invoices.Metadata.AppliedScope);

            var waiting = captured[1];
            Assert.Equal(view.WaitingColumnHeaders, waiting.Data.Columns.Select(column => column.Header));
            Assert.Equal(view.WaitingRows.Count, waiting.Data.Rows.Count);
            Assert.All(waiting.Data.Rows, row => Assert.Equal(waiting.Data.Columns.Count, row.Count));
            Assert.Equal("Service jobs waiting for parts", waiting.Metadata.ReportName);
            Assert.Equal("#,##0", waiting.Data.Columns.Single(column => column.Header == "Days waiting").NumberFormat);

            foreach (var export in captured)
                Assert.DoesNotContain(export.Data.Columns, column => ForbiddenHeaderWords.Any(word => column.Header.Contains(word, StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(view.Git.Columns.Select(column => (string)column.Header).Concat(view.Lines.Columns.Select(column => (string)column.Header)),
                header => ForbiddenHeaderWords.Any(word => header.Contains(word, StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Fact]
    public void Empty_parts_data_says_so_in_every_panel()
    {
        RunSta(() =>
        {
            var view = new ServicePartsView(() => new FakePartsQuery { Parts = ServicePartsView.EmptyParts }, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Empty(view.Rows);
            Assert.Equal("No purchase invoices (S007/S008) imported yet.", view.StatusText);
            Assert.Equal("No job on the latest Pending repair list is waiting for a part.", view.WaitingStatusText);
            Assert.Equal("No goods in transit lines in the 30 days to 05 Oct 2026.", view.GitStatusText);
            Assert.Equal("No S006 closing stock reading imported yet.", view.ClosingStockText);
            Assert.Equal("", view.LinesStatusText);
            Assert.NotNull(view.NumbersShown);
            var numbers = view.NumbersShown!;
            Assert.Equal((0, 0m, (int?)null, (string?)null, 0, 0, 0), (numbers.OpenInvoices, numbers.OpenValue, numbers.OldestOpenDays, numbers.OldestOpenInvoice, numbers.ReceivedThisMonth, numbers.JobsWaitingForParts, numbers.GitLinesLast30Days));
            Assert.Equal(["All months"], view.MonthChoices.Select(choice => choice.Label));
        });
    }

    [Fact]
    public void Parts_screen_says_no_Service_data_imported_yet_when_nothing_is_imported()
    {
        RunSta(() =>
        {
            var query = new FakePartsQuery { Refreshes = [] };
            var view = new ServicePartsView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(ServiceScreenView.NoDataText, view.AsAtText);
            Assert.StartsWith(ServiceScreenView.NoDataText, view.StatusText, StringComparison.Ordinal);
            Assert.Equal(0, query.PartsCalls);
            Assert.Null(view.NumbersShown);
            Assert.Empty(Descendants<KpiCard>(view));
        });
    }

    [Fact]
    public void A_failing_parts_query_is_described_not_thrown()
    {
        RunSta(() =>
        {
            var view = new ServicePartsView(() => new FakePartsQuery { Failure = new InvalidOperationException("The Service parts read model is not available in this build.") }, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal("Service parts and purchases could not be loaded. The Service parts read model is not available in this build.", view.StatusText);
            Assert.False(view.IsLoading);
        });
    }

    [Fact]
    public void The_screen_status_is_raised_for_the_application_status_line()
    {
        RunSta(() =>
        {
            var raised = new List<string>();
            var view = new ServicePartsView(() => new FakePartsQuery(), NoExport);
            view.StatusChanged += (_, text) => raised.Add(text);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal("Loading Service data…", raised[0]);
            Assert.Equal(view.StatusText, raised[^1]);
        });
    }

    [Fact]
    public void No_parts_record_carries_a_customer_phone_email_or_address_field()
    {
        var types = new[] { typeof(ServicePartsInvoice), typeof(ServicePartsLine), typeof(ServiceWaitingJob), typeof(ServiceGitLine), typeof(ServiceStockSummary), typeof(ServiceParts), typeof(ServicePartsNumbers) };
        Assert.All(types.SelectMany(type => type.GetProperties()), property =>
            Assert.DoesNotContain(ForbiddenHeaderWords, word => property.Name.Contains(word.Replace("-", ""), StringComparison.OrdinalIgnoreCase)));
    }

    private static ServicePartsLine Line(string item, decimal shipped, decimal received, decimal net, string? grn = null, DateOnly? grnDate = null) =>
        new(item, shipped, received, net, grn, grnDate);

    private static ServicePartsInvoice Invoice(string number, DateOnly? date, string? grn = null, DateOnly? grnDate = null, DateOnly? received = null,
        decimal? net = 100m, int? daysOpen = null, IReadOnlyList<ServicePartsLine>? lines = null)
    {
        var open = received is null && grnDate is null;
        return new(number, date, grn, grnDate, received, lines?.Count ?? 1, 1, open ? 0 : 1, net, open ? "Open" : "Received", daysOpen, "CCPT", Snapshot, lines ?? []);
    }

    private static Task NoExport(string path, ExcelReportMetadata metadata, ExcelReportData data) =>
        throw new InvalidOperationException("This test does not export.");

    private static void SpinUntil(Func<bool> condition)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            frame.Continue = true;
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        Assert.True(condition(), "The screen did not finish loading.");
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed class FakePartsQuery : IServiceReportQuery
    {
        private static readonly DateTime ImportedUtc = new(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc);

        public IReadOnlyList<ServiceRefresh> Refreshes { get; init; } =
        [
            new("S007", new(2026, 10, 5), 4, 150, ImportedUtc),
            new("S008", new(2026, 10, 5), 2, 151, ImportedUtc),
            new("S009", new(2026, 10, 5), 2, 152, ImportedUtc),
            new("S006", new(2026, 10, 3), 412, 149, ImportedUtc.AddDays(-2))
        ];
        public ServiceParts Parts { get; init; } = SampleParts();
        public Exception? Failure { get; init; }
        public int PartsCalls { get; private set; }
        public string? LastJob { get; private set; }

        /// <summary>Two open invoices (one 49 days old), two received (one in Oct), two jobs waiting, three GIT lines (one older than 30 days), as the query would compute them.</summary>
        public static ServiceParts SampleParts() => new(
            [
                new("PISYN0101", new(2026, 9, 10), null, null, null, 2, 3, 0, 450m, "Open", 25, "CCPT", Snapshot,
                    [Line("PART-SYN-02", 2, 0, 300m), Line("PART-SYN-01", 1, 0, 150m)]),
                new("PISYN0102", new(2026, 8, 17), null, null, null, 1, 1, 0, 800m, "Open", 49, "CCPT", Snapshot, [Line("PART-SYN-03", 1, 0, 800m)]),
                new("PISYN0103", new(2026, 9, 1), "GRNSYN0103", new(2026, 9, 9), new(2026, 9, 10), 1, 2, 2, 300m, "Received", 9, "CCPT", Snapshot,
                    [Line("PART-SYN-04", 2, 2, 300m, "GRNSYN0103", new(2026, 9, 9))]),
                new("PISYN0104", new(2026, 9, 25), "GRNSYN0104", new(2026, 10, 2), new(2026, 10, 2), 1, 1, 1, 120m, "Received", 7, "CCPT", Snapshot, [])
            ],
            [
                new("STMSYN0001", new(2026, 9, 28), "PART-SYN-05", 4, "CCPT", "AW330", 160m, Snapshot),
                new("STMSYN0002", new(2026, 10, 2), "PART-SYN-06", 1, "CCPT", "AW330", 90m, Snapshot),
                new("STMSYN0000", new(2026, 8, 20), "PART-SYN-07", 2, "CCPT", "AW330", 100m, Snapshot)
            ],
            new(new(2026, 10, 3), 412, 1180m, 95400m),
            [
                new("JOAW330SYN0302", "Sample part 03", new(2026, 9, 14), 21, "Sample Brand", "Model 2", "AW330"),
                new("JOAW330SYN0301", "Sample part 01", new(2026, 9, 30), 5, "Sample Brand", "Model 1", "AW330")
            ],
            OpenInvoices: 2, OpenValue: 1250m, OldestOpenDays: 49, ReceivedThisMonth: 1, JobsWaiting: 2, GitLinesLast30Days: 2, AsAt: Snapshot);

        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Refreshes);

        public Task<ServiceParts> LoadPartsAsync(CancellationToken cancellationToken = default)
        {
            PartsCalls++;
            return Failure is null ? Task.FromResult(Parts) : Task.FromException<ServiceParts>(Failure);
        }

        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceJobRow>>([]);

        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServicePendingRow>>([]);

        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default)
        {
            LastJob = jobOrderNumber;
            return Task.FromResult<IReadOnlyList<ServiceJobEvent>>([new(jobOrderNumber, "S009", "Pending repair", ServiceJobEventKind.FirstSeen, Snapshot, null)]);
        }

        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceMoneyDay>>([]);

        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceMoneyChange>>([]);
    }
}
