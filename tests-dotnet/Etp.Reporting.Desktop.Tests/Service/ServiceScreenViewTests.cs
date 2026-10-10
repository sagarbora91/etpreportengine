using System.IO.Compression;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// The read-only Service centre screens against a fake IServiceReportQuery: the interim pending and money screens
/// (lane L5) and the 1.10.0 Jobs list and Job history (design 3.4, lane history). All values are synthetic:
/// job numbers JOAW330SYN…, "Sample Customer NN", no phone, e-mail or address anywhere.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServiceScreenViewTests
{
    private static readonly string[] ForbiddenHeaderWords = ["phone", "mobile", "landline", "e-mail", "email", "address"];

    [Fact]
    public void Jobs_screen_defaults_to_closed_in_the_last_30_days_with_stage_type_and_TAT_columns()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobsView(() => query, NoExport);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(ServiceJobsView.RecentClosedCode, view.SelectedChoice.Code);
            Assert.False(view.ShowAll);
            Assert.Equal(1, query.JobListCalls);
            // Closed within 30 days of the as-at date (5 Oct): delivered 4 Oct and 20 Sep, RWR 30 Sep, DC claimed 15 Sep.
            // Out: delivered 1 Sep (older than 30 days), DC not claimed, the open jobs.
            Assert.Equal(["JOAW330SYN0104", "JOAW330SYN0101", "JOAW330SYN0103", "JOAW330SYN0102", "JOAW330SYN0106"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("Service data as at 05 Oct 2026 (refreshed " + FakeServiceQuery.ImportedLocal.ToString("dd MMM yyyy") + ")", view.AsAtText);
            Assert.Equal(["Job number", "Stage", "Job type", "Booked on", "Stage date", "TAT (days)", "Days open", "EDD", "Brand", "Model", "Product", "Guarantee", "Customer name", "Spare value", "Labour", "As at"],
                view.ColumnHeaders);
            var quick = view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0104");
            Assert.Equal("Delivered", quick.Cells[1]);
            Assert.Equal("Quick Billing", quick.Cells[2]);
            Assert.Equal(0, quick.Cells[5]);
            Assert.Null(quick.Cells[6]);
            var rwr = view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0103");
            Assert.Equal("Returned without repair", rwr.Cells[1]);
            Assert.Equal("Booking", rwr.Cells[2]);
            Assert.Equal(8, rwr.Cells[5]);
            Assert.Equal("Out of Warranty", rwr.Cells[11]);
            Assert.Equal("Sample Customer 03", rwr.Cells[12]);
            Assert.Same(view.Rows, view.Table.ItemsSource);
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text.StartsWith(ServiceScreenView.ServiceCentreLabel, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Every_screen_shows_the_freshness_strip_under_the_as_at_line()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobsView(() => query, NoExport) { FreshnessToday = () => new DateOnly(2026, 10, 9) };

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(1, query.FreshnessCalls);
            Assert.Equal(new DateOnly(2026, 10, 9), query.LastFreshnessAsOf);
            Assert.Equal(ServiceFreshness.Groups.Select(group => group.Group), view.Freshness.Select(chip => chip.Group));
            // S009 is at 5 Oct but S010 was never exported, so the Pending lists chip is partial; Money has S004 only.
            Assert.Equal("Pending lists: some families never exported", ServiceFreshnessStrip.TextFor(view.Freshness.Single(chip => chip.Group == "Pending lists")));
            Assert.Equal(ServiceFreshnessColour.NoData, view.Freshness.Single(chip => chip.Group == "Jobs").Colour);
            Assert.Equal("Jobs: no export yet", ServiceFreshnessStrip.TextFor(view.Freshness.Single(chip => chip.Group == "Jobs")));
            var strip = Descendants<WrapPanel>(view).Single(panel => AutomationProperties.GetName(panel) == "Service data freshness");
            Assert.Equal(view.Freshness.Count, strip.Children.Count);
            Assert.Equal(view.Freshness, strip.Children.OfType<Border>().Select(border => (ServiceFreshnessChip)border.Tag));
        });
    }

    [Fact]
    public void Jobs_status_line_headlines_the_Booking_TAT_median_and_shows_Quick_Billing_separately()
    {
        RunSta(() =>
        {
            var view = new ServiceJobsView(() => new FakeServiceQuery(), NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            // Delivered in the window: Booking 12 days (SYN0101, SYN0102), Quick Billing 0 days (SYN0104); RWR and DC rows are not TAT rows.
            Assert.Equal("5 jobs · Closed in the last 30 days. TAT booking to delivered: Booking median 12 days (2 delivered, 0 over 15 days) · Quick Billing median 0 days (1 delivered).", view.StatusText);
        });
    }

    [Fact]
    public void Without_Service_data_the_strip_is_empty_and_freshness_is_not_queried()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery { Refreshes = [] };
            var view = new ServiceJobsView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(0, query.FreshnessCalls);
            Assert.Empty(view.Freshness);
        });
    }

    [Theory]
    [InlineData("claims")]
    [InlineData("parts")]
    public void A_placeholder_screen_keeps_the_frame_and_points_at_the_interim_lists(string screen)
    {
        RunSta(() =>
        {
            var opened = new List<ServiceDrillDown>();
            var definition = screen == "claims" ? ServicePlaceholderView.Claims : ServicePlaceholderView.Parts;
            var view = new ServicePlaceholderView(definition, () => new FakeServiceQuery(), NoExport, opened.Add);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.StartsWith(ServicePlaceholderView.NotYetText, view.StatusText, StringComparison.Ordinal);
            Assert.Empty(view.Rows);
            Assert.NotEmpty(view.Freshness);
            var buttons = Descendants<Button>(view).Where(button => definition.Links.Any(link => link.Label == (string)button.Content)).ToArray();
            Assert.Equal(definition.Links.Count, buttons.Length);
            foreach (var button in buttons) button.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(definition.Links.Select(link => link.Target), opened);
            Assert.All(opened, target => Assert.Contains(target.TaskId, ServiceScreens.Tasks));
        });
    }

    [Fact]
    public void Jobs_show_all_lists_every_job_and_open_jobs_lists_the_open_ones()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobsView(() => query, NoExport) { ShowAll = true };
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(ServiceJobsView.AllJobsCode, view.SelectedChoice.Code);
            Assert.Equal(FakeServiceQuery.Jobs.Count, view.Rows.Count);
            Assert.StartsWith("9 jobs · All jobs.", view.StatusText, StringComparison.Ordinal);

            view.SelectedChoice = ServiceJobsView.Choices.Single(choice => choice.Code == ServiceJobsView.OpenJobsCode);
            view.ActivateAsync().GetAwaiter().GetResult();
            // Open, newest stage date first: on the bench 1 Oct, DC issued without a claim 29 Sep (Q3), indent raised 25 Sep.
            Assert.Equal(["JOAW330SYN0107", "JOAW330SYN0108", "JOAW330SYN0105"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("3 jobs · Open jobs.", view.StatusText);

            view.SelectedChoice = ServiceJobsView.Choices.Single(choice => choice.Code == ServiceJobStages.OnBench);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["JOAW330SYN0107"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal(13, ServiceJobsView.Choices.Count);
        });
    }

    [Fact]
    public void Jobs_with_nothing_closed_recently_say_how_to_show_all()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery { JobList = FakeServiceQuery.Jobs.Where(job => !ServiceJobStages.IsClosed(job.Stage, job.ClaimRaised)).ToArray() };
            var view = new ServiceJobsView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Empty(view.Rows);
            Assert.Equal("No job closed in the last 30 days. Choose \"All jobs\" to see every job.", view.StatusText);
        });
    }

    [Fact]
    public void A_job_row_opens_Service_job_history_through_the_opener()
    {
        RunSta(() =>
        {
            string? opened = null;
            var view = new ServiceJobsView(() => new FakeServiceQuery(), NoExport, job => opened = job);
            view.ActivateAsync().GetAwaiter().GetResult();
            view.OpenSelectedJob();
            Assert.Null(opened);
            view.Table.SelectedItem = view.Rows[2];
            view.OpenSelectedJob();
            Assert.Equal("JOAW330SYN0103", opened);
        });
    }

    [Fact]
    public void A_job_number_passed_as_the_route_argument_opens_the_history_without_typing()
    {
        RunSta(() =>
        {
            var route = ServiceScreens.JobHistoryRoute("  JOAW330SYN0101 ");
            Assert.Equal(ServiceScreens.JobHistoryTask, route.TaskId);
            Assert.Equal(ServiceScreens.Destination, route.Destination);
            Assert.Equal("JOAW330SYN0101", route.Argument);
            Assert.Equal(TaskNavigation.Find(ServiceScreens.JobHistoryTask)!.Route, route with { Argument = null });
            Assert.NotEqual(ServiceScreens.JobHistoryRoute("JOAW330SYN0102"), route);
            var navigation = new ShellNavigationService();
            Assert.True(navigation.Navigate(route, ShellAccess.Viewer).IsAllowed);
            Assert.Equal(route, navigation.Current);
            Assert.True(navigation.Navigate(ServiceScreens.JobHistoryRoute("JOAW330SYN0102"), ShellAccess.Viewer).IsAllowed);
            Assert.True(navigation.CanGoBack);

            var query = new FakeServiceQuery();
            var view = (ServiceJobHistoryView)ServiceScreens.Create(ServiceScreens.JobHistoryTask, () => query, NoExport, route.Argument, _ => { });
            SpinUntil(() => !view.IsLoading);
            Assert.Equal("JOAW330SYN0101", view.JobNumber);
            Assert.Equal("JOAW330SYN0101", query.LastJob);
            Assert.True(view.IsHeaderVisible);
        });
    }

    [Theory]
    [InlineData("jobs")]
    [InlineData("pending")]
    [InlineData("history")]
    [InlineData("money")]
    [InlineData("today")]
    [InlineData("claims")]
    [InlineData("parts")]
    public void Every_screen_says_no_Service_data_imported_yet_when_nothing_is_imported(string screen)
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery { Refreshes = [] };
            var view = Create(screen, query);
            if (view is ServiceJobHistoryView history) history.JobNumber = "JOAW330SYN0001";

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(ServiceScreenView.NoDataText, view.AsAtText);
            Assert.StartsWith(ServiceScreenView.NoDataText, view.StatusText, StringComparison.Ordinal);
            Assert.Empty(view.Rows);
            Assert.False(view.HasData);
            Assert.Equal(0, query.BodyCalls);
        });
    }

    [Theory]
    [InlineData("jobs")]
    [InlineData("pending")]
    [InlineData("history")]
    [InlineData("money")]
    [InlineData("today")]
    [InlineData("claims")]
    [InlineData("parts")]
    public void A_load_failure_is_described_by_DesktopFriendlyError(string screen)
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery { Failure = new UnauthorizedAccessException("raw technical text") };
            var view = Create(screen, query);
            if (view is ServiceJobHistoryView history) history.JobNumber = "JOAW330SYN0001";

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.EndsWith("could not be loaded. " + DesktopFriendlyError.Describe(query.Failure), view.StatusText, StringComparison.Ordinal);
            Assert.DoesNotContain("raw technical text", view.StatusText);
            Assert.False(view.IsLoading);
        });
    }

    [Fact]
    public void A_build_without_the_read_model_says_so_instead_of_failing_to_open()
    {
        RunSta(() =>
        {
            var view = (ServiceScreenView)ServiceScreens.Create(ServiceScreens.JobsTask, ServiceScreens.Unavailable, NoExport);
            SpinUntil(() => !view.IsLoading);
            Assert.Contains("The Service read model is not available in this build.", view.StatusText);
        });
    }

    [Fact]
    public void Job_history_shows_the_header_card_and_the_timeline_newest_first()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobHistoryView(() => query, NoExport) { JobNumber = "  JOAW330SYN0101 \t" };

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal("JOAW330SYN0101", query.LastJob);
            Assert.Equal("JOAW330SYN0101", view.SearchedJobNumber);
            Assert.True(view.IsHeaderVisible);
            Assert.Equal(["Job number", "Booked on", "Brand / model / product", "Guarantee", "Customer type", "Customer name", "Current stage", "TAT", "EDD", "Labour / spares (S003)"],
                view.HeaderItems.Select(item => item.Label));
            Assert.Equal("JOAW330SYN0101", view.HeaderItems[0].Value);
            Assert.Equal("20 Sep 2026 · Booking", view.HeaderItems[1].Value);
            Assert.Equal("Sample Brand, Model 1, Watch", view.HeaderItems[2].Value);
            Assert.Equal("Under Warranty", view.HeaderItems[3].Value);
            Assert.Equal("B2C", view.HeaderItems[4].Value);
            Assert.Equal("Sample Customer 01", view.HeaderItems[5].Value);
            Assert.Equal("Delivered · 02 Oct 2026", view.HeaderItems[6].Value);
            Assert.Equal("TAT 12 days (booked to delivered)", view.HeaderItems[7].Value);
            Assert.Equal("30 Sep 2026", view.HeaderItems[8].Value);
            Assert.Equal("Labour 80.00 · Spares 120.50", view.HeaderItems[9].Value);
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text == "TAT 12 days (booked to delivered)");

            Assert.Equal(["Snapshot", "Family", "Report", "Event date", "Status", "Pending at", "Document", "Amount", "Source"], view.ColumnHeaders);
            // Snapshot date desc, then event date desc (unknown last), then report code.
            Assert.Equal(["S003", "S018", "S002", "S009", "S009"], view.Rows.Select(row => (string)row.Cells[2]!));
            Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), new DateOnly(2026, 9, 28)],
                view.Rows.Select(row => (DateOnly)row.Cells[0]!));
            Assert.Equal("consolidated", view.Rows[0].Cells[8]);
            Assert.Equal("raw", view.Rows[3].Cells[8]);
            Assert.Equal("INV-SYN-1", view.Rows[0].Cells[6]);
            Assert.Equal(200.50m, view.Rows[0].Cells[7]);
            Assert.Equal("Job JOAW330SYN0101: 5 readings across 4 families · newest first.", view.StatusText);
            Assert.Same(view.Rows, view.Table.ItemsSource);
        });
    }

    [Fact]
    public void Job_history_header_describes_an_open_job_with_days_open_and_stage_facts()
    {
        var items = ServiceJobHistoryView.DescribeHeader(FakeServiceQuery.Jobs.Single(job => job.JobOrderNumber == "JOAW330SYN0105"));
        Assert.Equal("Indent raised, parts awaited · 25 Sep 2026 · at AW330", items.Single(item => item.Label == "Current stage").Value);
        Assert.Equal("Open 20 days · 10 in this stage · EDD passed", items.Single(item => item.Label == "Days open").Value);
        Assert.Equal("Crown", items.Single(item => item.Label == "Spare required").Value);
        Assert.Equal("Labour — · Spares —", items.Single(item => item.Label == "Labour / spares (S003)").Value);
        Assert.DoesNotContain(items, item => item.Label == "TAT");
        var dc = ServiceJobHistoryView.DescribeHeader(FakeServiceQuery.Jobs.Single(job => job.JobOrderNumber == "JOAW330SYN0108"));
        Assert.Equal("DC issued · 29 Sep 2026 · claim not raised · at AW330", dc.Single(item => item.Label == "Current stage").Value);
        var claimed = ServiceJobHistoryView.DescribeHeader(FakeServiceQuery.Jobs.Single(job => job.JobOrderNumber == "JOAW330SYN0106"));
        Assert.Equal("DC issued · 15 Sep 2026 · claim raised", claimed.Single(item => item.Label == "Current stage").Value);
        Assert.Equal("TAT not known", claimed.Single(item => item.Label == "TAT").Value);
    }

    [Fact]
    public void Job_history_says_when_no_family_holds_the_job_and_clears_the_header()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobHistoryView(() => query, NoExport) { JobNumber = "JOAW330SYN0101" };
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.True(view.IsHeaderVisible);

            view.JobNumber = "JOAW330SYN9999";
            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal("JOAW330SYN9999", query.LastJob);
            Assert.Empty(view.Rows);
            Assert.Null(view.Header);
            Assert.False(view.IsHeaderVisible);
            Assert.Empty(view.HeaderItems);
            Assert.Equal("No Service family holds job JOAW330SYN9999. Check the number.", view.StatusText);
        });
    }

    [Fact]
    public void Job_history_with_a_blank_number_asks_for_one_and_does_not_query()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobHistoryView(() => query, NoExport) { JobNumber = "   " };
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Null(query.LastJob);
            Assert.Equal("Enter a job number and select Show history.", view.StatusText);
            Assert.False(view.IsHeaderVisible);
        });
    }

    [Fact]
    public void Timeline_rows_sort_by_snapshot_then_event_date_descending_regardless_of_input_order()
    {
        var rows = new ServiceJobTimelineRow[]
        {
            new("JOAW330SYN0111", new(2026, 9, 28), "S009", "Pending repair", new(2026, 9, 20), "Pending_Repair", "AW330", null, null, "CONSOLIDATED", 101),
            new("JOAW330SYN0111", new(2026, 10, 5), "S002", "Job list", null, "Delivered", null, null, null, "RAW", 140),
            new("JOAW330SYN0111", new(2026, 10, 5), "S018", "DELIVERED", new(2026, 10, 2), null, null, null, null, "CONSOLIDATED", 142),
            new("JOAW330SYN0111", new(2026, 10, 5), "S003", "Invoice lines", new(2026, 10, 2), "Delivered", null, "INV-SYN-2", 50m, "CONSOLIDATED", 143)
        };
        var grid = ServiceJobHistoryView.TimelineRows(rows);
        Assert.Equal(["S003", "S018", "S002", "S009"], grid.Select(row => (string)row.Cells[2]!));
        Assert.Equal(["consolidated", "consolidated", "raw", "consolidated"], grid.Select(row => (string)row.Cells[8]!));
        Assert.Equal("Invoice lines", grid[0].Cells[1]);
    }

    [Fact]
    public void Jobs_filter_and_TAT_summary_are_pure_over_the_contract_rows()
    {
        var recent = ServiceJobsView.Filter(FakeServiceQuery.Jobs, ServiceJobsView.RecentClosedCode);
        Assert.Equal(["JOAW330SYN0104", "JOAW330SYN0101", "JOAW330SYN0103", "JOAW330SYN0102", "JOAW330SYN0106"], recent.Select(job => job.JobOrderNumber));
        Assert.Equal("", ServiceJobsView.DescribeTat([]));
        Assert.Equal("TAT booking to delivered: Booking median 12 days (2 delivered, 0 over 15 days) · Quick Billing median 0 days (1 delivered).", ServiceJobsView.DescribeTat(recent));
        Assert.Equal(ServiceJobTypes.QuickBilling, ServiceJobTypes.Normalise("Quick_Billing"));
        Assert.Equal(ServiceJobTypes.Booking, ServiceJobTypes.Normalise(null));
        Assert.Equal(3, ServiceJobTat.Median([1, 3, 9, 20]));
        Assert.Null(ServiceJobTat.Median([]));
        Assert.True(ServiceJobStages.IsClosed(ServiceJobStages.DcIssued, claimRaised: true));
        Assert.False(ServiceJobStages.IsClosed(ServiceJobStages.DcIssued, claimRaised: false));
    }

    [Fact]
    public void Money_screen_shows_the_difference_the_manual_sources_and_the_change_list()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceMoneyView(() => query, NoExport) { From = new(2026, 9, 26), To = new(2026, 9, 28) };

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal((new DateOnly(2026, 9, 26), new DateOnly(2026, 9, 28)), query.LastMoneyRange);
            Assert.Contains("Difference", view.ColumnHeaders);
            var cash = view.Rows.Single(row => (string?)row.Cells[1] == "CASH" && (DateOnly?)row.Cells[0] == new DateOnly(2026, 9, 27));
            Assert.Equal(-150m, cash.Cells[4]);
            Assert.Equal("WLMHW", cash.Cells[5]);
            Assert.Equal("Manual entries from: WLMHW. Only the Titan World shop's Service cash, card and UPI entries are compared (decision 16); Service WDC is not compared.",
                view.ManualSourcesText);
            // Decision 16: another shop's Service entry is listed in its own grid and never reaches a money-check row.
            Assert.Equal((new DateOnly(2026, 9, 26), new DateOnly(2026, 9, 28)), query.LastUnmatchedRange);
            var apart = Assert.Single(view.UnmatchedRows);
            Assert.Equal([new DateOnly(2026, 9, 27), "HEMW", "SERVICE_CASH", 150m, "Service entry at Helios: not matched to AW330."], apart.Cells);
            Assert.Same(view.UnmatchedRows, view.Unmatched.ItemsSource);
            Assert.All(view.Rows, row => Assert.DoesNotContain("HEMW", (string?)row.Cells[5] ?? "", StringComparison.Ordinal));
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text == ServiceMoneyView.UnmatchedTitle);
            Assert.Equal("Service entries at other shops (not added)", ServiceMoneyView.UnmatchedTitle);
            Assert.StartsWith("1 Service entry at other shops.", view.UnmatchedStatusText, StringComparison.Ordinal);
            var change = Assert.Single(view.ChangeRows);
            Assert.Equal(new DateOnly(2026, 9, 27), change.Cells[0]);
            Assert.Equal(250m, change.Cells[6]);
            Assert.Same(view.ChangeRows, view.Changes.ItemsSource);
            Assert.Equal("1 date changed since the previous refresh.", view.ChangesStatusText);
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text == "Money changed since the previous refresh");
        });
    }

    [Fact]
    public void Money_screen_refuses_a_reversed_range_without_querying()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceMoneyView(() => query, NoExport) { From = new(2026, 9, 28), To = new(2026, 9, 1) };
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Null(query.LastMoneyRange);
            Assert.Contains("From date is after the To date", view.StatusText);
        });
    }

    [Theory]
    [InlineData("jobs")]
    [InlineData("history")]
    [InlineData("money")]
    [InlineData("today")]
    public void Export_writes_the_visible_columns_and_no_phone_email_or_address_column(string screen)
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            (string Path, ExcelReportMetadata Metadata, ExcelReportData Data)? captured = null;
            var view = Create(screen, query, (path, metadata, data) => { captured = (path, metadata, data); return Task.CompletedTask; });
            if (view is ServiceJobHistoryView history) history.JobNumber = "JOAW330SYN0101";
            if (view is ServiceMoneyView money) { money.From = new(2026, 9, 26); money.To = new(2026, 9, 28); }
            view.ActivateAsync().GetAwaiter().GetResult();

            view.ExportToPathAsync("synthetic.xlsx").GetAwaiter().GetResult();

            var export = Assert.NotNull(captured);
            Assert.Equal(view.Table.Columns.Select(column => (string)column.Header), export.Data.Columns.Select(column => column.Header));
            Assert.Equal(view.Rows.Count, export.Data.Rows.Count);
            Assert.All(export.Data.Rows, row => Assert.Equal(export.Data.Columns.Count, row.Count));
            Assert.DoesNotContain(export.Data.Columns, column => ForbiddenHeaderWords.Any(word => column.Header.Contains(word, StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(ForbiddenHeaderWords, word => export.Metadata.Message.Contains(word, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ServiceScreenView.ServiceCentreLabel, export.Metadata.AppliedScope);
            Assert.StartsWith(view.AsAtText, export.Metadata.Message, StringComparison.Ordinal);
            if (screen is "jobs") Assert.Contains("Customer name", export.Data.Columns.Select(column => column.Header));
            // SD-09: the export period is the screen's range, not today; history also carries the header card in the message line.
            if (screen == "history")
            {
                Assert.Equal((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5)), (export.Metadata.DateFrom, export.Metadata.DateTo));
                Assert.Contains("Job number: JOAW330SYN0101", export.Metadata.Message);
                Assert.Contains("TAT: TAT 12 days (booked to delivered)", export.Metadata.Message);
            }
            if (screen == "jobs")
            {
                Assert.Equal((new DateOnly(2026, 9, 5), new DateOnly(2026, 10, 5)), (export.Metadata.DateFrom, export.Metadata.DateTo));
                Assert.Contains("TAT booking to delivered: Booking median 12 days", export.Metadata.Message);
            }
        });
    }

    [Fact]
    public void Export_through_the_report_exporter_produces_a_workbook_with_the_headers()
    {
        RunSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"etp-service-jobs-{Guid.NewGuid():N}.xlsx");
            try
            {
                var view = new ServiceJobsView(() => new FakeServiceQuery(),
                    (file, metadata, data) => new Modules.Reports.ReportExportCoordinator().ExportReportExcelAsync(file, metadata, data, null));
                view.ActivateAsync().GetAwaiter().GetResult();
                view.ExportToPathAsync(path).GetAwaiter().GetResult();

                using var archive = ZipFile.OpenRead(path);
                var xml = string.Concat(archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .Select(entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }));
                Assert.Contains("Customer name", xml);
                Assert.Contains("JOAW330SYN0102", xml);
                Assert.DoesNotContain("Phone", xml, StringComparison.OrdinalIgnoreCase);
            }
            finally { File.Delete(path); }
        });
    }

    [Fact]
    public void No_contract_record_carries_a_phone_email_or_address_field()
    {
        var types = new[] { typeof(ServiceRefresh), typeof(ServiceJobRow), typeof(ServicePendingRow), typeof(ServiceJobEvent), typeof(ServiceMoneyDay), typeof(ServiceMoneyChange), typeof(ServiceUnmatchedMoneyEntry), typeof(ServiceJobHeader), typeof(ServiceJobTimelineRow), typeof(ServiceJobDetail), typeof(ServiceFreshnessChip), typeof(ServiceToday), typeof(ServicePendingBoardRow), typeof(ServiceClaimLine), typeof(ServicePartsInvoice) };
        Assert.All(types.SelectMany(type => type.GetProperties()), property =>
            Assert.DoesNotContain(ForbiddenHeaderWords, word => property.Name.Contains(word.Replace("-", ""), StringComparison.OrdinalIgnoreCase)));
    }

    private static ServiceScreenView Create(string screen, IServiceReportQuery query, ServiceExcelExport? export = null) => screen switch
    {
        "jobs" => new ServiceJobsView(() => query, export ?? NoExport),
        "pending" => new ServicePendingBoardView(() => query, export ?? NoExport),
        "history" => new ServiceJobHistoryView(() => query, export ?? NoExport),
        "today" => new ServiceTodayView(() => query, export ?? NoExport),
        "claims" => new ServicePlaceholderView(ServicePlaceholderView.Claims, () => query, export ?? NoExport, null),
        "parts" => new ServicePlaceholderView(ServicePlaceholderView.Parts, () => query, export ?? NoExport, null),
        _ => new ServiceMoneyView(() => query, export ?? NoExport)
    };

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

    private sealed class FakeServiceQuery : IServiceReportQuery
    {
        public static readonly DateTime ImportedUtc = new(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc);
        public static DateTime ImportedLocal => ImportedUtc.ToLocalTime();

        public IReadOnlyList<ServiceRefresh> Refreshes { get; init; } =
        [
            new("S009", new(2026, 9, 28), 6, 101, ImportedUtc.AddDays(-7)),
            new("S009", new(2026, 10, 5), 7, 140, ImportedUtc),
            new("S004", new(2026, 10, 5), 9, 141, ImportedUtc.AddMinutes(-2))
        ];
        public Exception? Failure { get; init; }
        public string? LastStatusView { get; private set; }
        public string? LastPendingList { get; private set; }
        public string? LastJob { get; private set; }
        public (DateOnly From, DateOnly To)? LastMoneyRange { get; private set; }
        public (DateOnly From, DateOnly To)? LastUnmatchedRange { get; private set; }
        public int BodyCalls { get; private set; }

        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) =>
            Failure is null ? Task.FromResult(Refreshes) : Task.FromException<IReadOnlyList<ServiceRefresh>>(Failure);

        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default)
        {
            BodyCalls++; LastStatusView = statusView;
            IReadOnlyList<ServiceJobRow> rows =
            [
                new("JOAW330SYN0004", "S017", "RWR", new(2026, 9, 20), new(2026, 10, 2), "Sample Brand", "Model 4", "Watch", "Sample Customer 04", 120.50m, 80m, 3, new(2026, 10, 5), 2),
                new("JOAW330SYN0005", "S017", "RWR", new(2026, 9, 22), null, "Sample Brand", "Model 5", "Watch", "Sample Customer 05", null, 0m, 1, new(2026, 10, 5), 0)
            ];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default)
        {
            BodyCalls++; LastPendingList = list;
            if (!ServicePendingLists.All.Contains(list))
                throw new ArgumentOutOfRangeException(nameof(list), list, "Not a ServicePendingLists key.");
            IReadOnlyList<ServicePendingRow> rows =
            [
                new(list, "JOAW330SYN0002", null, null, "Sample Brand", "Model 2", "Sample Customer 02", "AW330", new(2026, 10, 5)),
                new(list, "JOAW330SYN0001", new(2026, 9, 25), 10, "Sample Brand", "Model 1", "Sample Customer 01", "AW330", new(2026, 10, 5)),
                new(list, "JOAW330SYN0003", new(2026, 9, 1), 34, "Sample Brand", "Model 3", "Sample Customer 03", "AW330", new(2026, 10, 5))
            ];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default)
        {
            BodyCalls++; LastJob = jobOrderNumber;
            IReadOnlyList<ServiceJobEvent> rows =
            [
                new(jobOrderNumber, "S009", "Pending repair", ServiceJobEventKind.FirstSeen, new(2026, 9, 28), null),
                new(jobOrderNumber, "S009", "Pending repair", ServiceJobEventKind.LeftList, new(2026, 10, 5), new(2026, 9, 28)),
                new(jobOrderNumber, "S018", "DELIVERED", ServiceJobEventKind.FirstSeen, new(2026, 10, 5), null)
            ];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            BodyCalls++; LastMoneyRange = (from, to);
            IReadOnlyList<ServiceMoneyDay> rows =
            [
                new(new(2026, 9, 27), "UPI", 300m, 300m, 0m, ["WLMHW"]),
                new(new(2026, 9, 27), "CASH", 850m, 1000m, -150m, ["WLMHW"]),
                new(new(2026, 9, 26), "CARD", 400m, null, 400m, [])
            ];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<ServiceUnmatchedMoneyEntry>> LoadUnmatchedServiceEntriesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            LastUnmatchedRange = (from, to);
            IReadOnlyList<ServiceUnmatchedMoneyEntry> rows = [new(new(2026, 9, 27), "HEMW", "SERVICE_CASH", 150m, "Service entry at Helios: not matched to AW330.")];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ServiceMoneyChange> rows = [new(new(2026, 9, 27), "S004", new(2026, 9, 28), 600m, new(2026, 10, 5), 850m)];
            return Task.FromResult(rows);
        }

        public int JobListCalls { get; private set; }
        public IReadOnlyList<ServiceJobHeader> JobList { get; init; } = Jobs;

        /// <summary>Nine synthetic v_service_job rows as at 5 Oct 2026: five closed in the window, one closed earlier, three open.</summary>
        public static IReadOnlyList<ServiceJobHeader> Jobs { get; } =
        [
            // Delivered 2 Oct, booked 20 Sep: Booking, TAT 12.
            new("JOAW330SYN0101", new(2026, 9, 20), "Booking", "Delivered", "Sample Brand", "Model 1", "Watch", "Under_Warranty", "B2C", "Sample Customer 01",
                new(2026, 9, 30), ServiceJobStages.Delivered, new(2026, 10, 2), "AW330", null, false, 120.50m, 80m, 12, null, null, false, new(2026, 10, 5)),
            // Delivered 20 Sep, booked 8 Sep: Booking, TAT 12 (inside the 30-day window).
            new("JOAW330SYN0102", new(2026, 9, 8), "Booking", "Delivered", "Sample Brand", "Model 2", "Watch", "Out_of_Warranty", "B2C", "Sample Customer 02",
                null, ServiceJobStages.Delivered, new(2026, 9, 20), "AW330", null, false, 40m, 60m, 12, null, null, false, new(2026, 10, 5)),
            // Returned without repair 30 Sep, booked 22 Sep: TAT 8, not a delivered TAT row.
            new("JOAW330SYN0103", new(2026, 9, 22), "Booking", "Returned_Without_Repair", "Sample Brand", "Model 3", "Watch", "Out_of_Warranty", "B2C", "Sample Customer 03",
                null, ServiceJobStages.Rwr, new(2026, 9, 30), "AW330", null, false, null, 0m, 8, null, null, false, new(2026, 10, 5)),
            // Quick Billing delivered the day it was booked (4 Oct): TAT 0.
            new("JOAW330SYN0104", new(2026, 10, 4), "Quick_Billing", "Delivered", "Sample Brand", "Model 4", "Strap", "Out_of_Warranty", "B2C", "Sample Customer 04",
                null, ServiceJobStages.Delivered, new(2026, 10, 4), "AW330", null, false, 15m, 10m, 0, null, null, false, new(2026, 10, 5)),
            // Indent raised 25 Sep, booked 15 Sep, EDD 1 Oct passed: open 20 days, 10 in stage.
            new("JOAW330SYN0105", new(2026, 9, 15), "Booking", "Indent_Raised", "Sample Brand", "Model 5", "Watch", "Under_Warranty", "B2C", "Sample Customer 05",
                new(2026, 10, 1), ServiceJobStages.IndentRaised, new(2026, 9, 25), "AW330", "Crown", false, null, null, null, 20, 10, true, new(2026, 10, 5)),
            // DC issued 15 Sep and claimed: closed by claim (Q3), inside the window.
            new("JOAW330SYN0106", new(2026, 9, 1), "Booking", "DC_Issued", "Sample Brand", "Model 6", "Watch", "Under_Warranty", "B2C", "Sample Customer 06",
                null, ServiceJobStages.DcIssued, new(2026, 9, 15), "AW330", null, true, null, null, null, 34, 20, false, new(2026, 10, 5)),
            // On the bench since 1 Oct.
            new("JOAW330SYN0107", new(2026, 10, 1), "Booking", "Pending_Repair", "Sample Brand", "Model 7", "Watch", "Out_of_Warranty", "B2C", "Sample Customer 07",
                new(2026, 10, 8), ServiceJobStages.OnBench, new(2026, 10, 1), "AW330", null, false, null, null, null, 4, 4, false, new(2026, 10, 5)),
            // DC issued 29 Sep, claim not raised: open.
            new("JOAW330SYN0108", new(2026, 9, 5), "Booking", "DC_Issued", "Sample Brand", "Model 8", "Watch", "Under_Warranty", "B2C", "Sample Customer 08",
                null, ServiceJobStages.DcIssued, new(2026, 9, 29), "AW330", null, false, null, null, null, 30, 6, false, new(2026, 10, 5)),
            // Delivered 1 Sep: closed, older than the window.
            new("JOAW330SYN0109", new(2026, 8, 25), "Booking", "Delivered", "Sample Brand", "Model 9", "Watch", "Out_of_Warranty", "B2C", "Sample Customer 09",
                null, ServiceJobStages.Delivered, new(2026, 9, 1), "AW330", null, false, 10m, 20m, 7, null, null, false, new(2026, 10, 5))
        ];

        public Task<IReadOnlyList<ServiceJobHeader>> LoadJobListAsync(CancellationToken cancellationToken = default)
        {
            BodyCalls++; JobListCalls++;
            return Task.FromResult(JobList);
        }

        public Task<ServiceJobDetail?> LoadJobAsync(string jobOrderNumber, CancellationToken cancellationToken = default)
        {
            BodyCalls++; LastJob = jobOrderNumber;
            var header = Jobs.FirstOrDefault(job => job.JobOrderNumber == jobOrderNumber);
            if (header is null) return Task.FromResult<ServiceJobDetail?>(null);
            // Deliberately out of order: the screen sorts snapshot desc, event date desc, report code.
            IReadOnlyList<ServiceJobTimelineRow> timeline =
            [
                new(jobOrderNumber, new(2026, 9, 28), "S009", "Pending repair", new(2026, 9, 20), "Pending_Repair", "AW330", null, null, "CONSOLIDATED", 101),
                new(jobOrderNumber, new(2026, 10, 5), "S002", "Job list", new(2026, 9, 20), "Delivered", null, null, null, "CONSOLIDATED", 139),
                new(jobOrderNumber, new(2026, 10, 5), "S009", "Pending repair", null, "Indent_Raised", "AW330", null, null, "RAW", 140),
                new(jobOrderNumber, new(2026, 10, 5), "S003", "Invoice lines", new(2026, 10, 2), "Delivered", null, "INV-SYN-1", 200.50m, "CONSOLIDATED", 142),
                new(jobOrderNumber, new(2026, 10, 5), "S018", "DELIVERED", new(2026, 10, 2), null, null, null, null, "CONSOLIDATED", 141)
            ];
            return Task.FromResult<ServiceJobDetail?>(new ServiceJobDetail(header, timeline));
        }

        public int FreshnessCalls { get; private set; }
        public DateOnly? LastFreshnessAsOf { get; private set; }

        /// <summary>The contract rule (ServiceFreshness.Build) over this fake's refresh log, with S009/S004 as raw exports.</summary>
        public Task<IReadOnlyList<ServiceFreshnessChip>> LoadFreshnessAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default)
        {
            FreshnessCalls++; LastFreshnessAsOf = asOf;
            return Task.FromResult(ServiceFreshness.Build(Refreshes, asOf ?? new DateOnly(2026, 10, 9),
                new Dictionary<string, string> { ["S009"] = "RAW", ["S004"] = "RAW" }));
        }

        public DateOnly? LastTodayDate { get; private set; }

        public Task<ServiceToday> LoadTodayAsync(DateOnly? businessDate = null, CancellationToken cancellationToken = default)
        {
            BodyCalls++; LastTodayDate = businessDate;
            var date = businessDate ?? new DateOnly(2026, 10, 5);
            return Task.FromResult(new ServiceToday(date, new(2026, 10, 5), 6, 4, 2, 61, 40, 21, 4, 58, 1, 46, 25, 11, 97, 12, 12_500m, 8_000m, 2_500m, 2_000m, true, 12_500m, 9, 7, 41_000m));
        }
    }
}
