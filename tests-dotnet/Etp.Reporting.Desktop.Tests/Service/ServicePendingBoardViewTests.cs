using System.IO.Compression;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// The Service Pending board (1.10.0, design review 3.3, Q3, Q4, Q8) against a fake IServiceReportQuery, and the pure
/// rules of ServicePendingBoard. All values are synthetic: job numbers JOAW330SYN01NN, "Sample Brand A/B".
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServicePendingBoardViewTests
{
    private static readonly string[] ForbiddenHeaderWords = ["phone", "mobile", "landline", "e-mail", "email", "address", "customer name"];
    private static readonly DateOnly AsAt = new(2026, 10, 5);

    // ----- pure rules -----

    [Theory]
    [InlineData(null, null)]
    [InlineData(-1, "0-7")]
    [InlineData(0, "0-7")]
    [InlineData(7, "0-7")]
    [InlineData(8, "8-15")]
    [InlineData(15, "8-15")]
    [InlineData(16, "16-30")]
    [InlineData(30, "16-30")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "60+")]
    [InlineData(400, "60+")]
    public void Age_bands_are_0_7_8_15_16_30_31_60_and_over_60(int? ageDays, string? band)
    {
        Assert.Equal(band, ServiceAgeBands.Of(ageDays));
        Assert.Equal(["0-7", "8-15", "16-30", "31-60", "60+"], ServiceAgeBands.All);
    }

    [Fact]
    public void Board_groups_are_in_stage_order_with_the_Q4_limits()
    {
        Assert.Equal([ServiceJobStages.Booked, ServiceJobStages.OnBench, ServiceJobStages.IndentRaised, ServiceJobStages.SrnOut,
                ServiceJobStages.InTransitBack, ServiceJobStages.ReadyForDelivery, ServiceJobStages.DcIssued, ServiceJobStages.RaIssued],
            ServicePendingStages.Board.Select(stage => stage.Code));
        Assert.Equal([null, 7, 15, 30, 15, 7, null, null], ServicePendingStages.Board.Select(stage => stage.LimitDays));
        Assert.Equal(Enumerable.Range(0, 8), ServicePendingStages.Board.Select(stage => stage.Order));
        Assert.Null(ServicePendingStages.Find(ServiceJobStages.Delivered));
        Assert.Null(ServicePendingStages.Find(ServiceJobStages.Rwr));
        Assert.Null(ServicePendingStages.Find(null));
        Assert.Same(ServicePendingStages.OnBench, ServicePendingStages.Find("on_bench"));
    }

    [Fact]
    public void Overdue_with_an_EDD_is_days_past_the_EDD_whatever_the_stage()
    {
        var bench = ServicePendingStages.OnBench;
        Assert.Equal(4, ServicePendingBoard.OverdueBy(Row("A", ServiceJobStages.OnBench, edd: new(2026, 10, 1), daysInStage: 2), bench));
        Assert.Null(ServicePendingBoard.OverdueBy(Row("B", ServiceJobStages.OnBench, edd: new(2026, 10, 5), daysInStage: 40), bench));
        Assert.Null(ServicePendingBoard.OverdueBy(Row("C", ServiceJobStages.OnBench, edd: new(2026, 10, 9), daysInStage: 40), bench));
    }

    [Fact]
    public void Overdue_without_an_EDD_is_days_in_stage_past_the_stage_limit()
    {
        Assert.Null(ServicePendingBoard.OverdueBy(Row("A", ServiceJobStages.OnBench, daysInStage: 7), ServicePendingStages.OnBench));
        Assert.Equal(1, ServicePendingBoard.OverdueBy(Row("B", ServiceJobStages.OnBench, daysInStage: 8), ServicePendingStages.OnBench));
        Assert.Equal(5, ServicePendingBoard.OverdueBy(Row("C", ServiceJobStages.IndentRaised, daysInStage: 20), ServicePendingStages.IndentRaised));
        Assert.Equal(60, ServicePendingBoard.OverdueBy(Row("D", ServiceJobStages.SrnOut, daysInStage: 90), ServicePendingStages.SrnOut));
        Assert.Null(ServicePendingBoard.OverdueBy(Row("E", ServiceJobStages.InTransitBack, daysInStage: 15), ServicePendingStages.InTransitBack));
        Assert.Equal(1, ServicePendingBoard.OverdueBy(Row("F", ServiceJobStages.ReadyForDelivery, daysInStage: 8), ServicePendingStages.ReadyForDelivery));
        Assert.Null(ServicePendingBoard.OverdueBy(Row("G", ServiceJobStages.Booked, daysInStage: 500), ServicePendingStages.Booked));
        Assert.Null(ServicePendingBoard.OverdueBy(Row("H", ServiceJobStages.DcIssued, daysInStage: 500), ServicePendingStages.DcIssued));
        Assert.Null(ServicePendingBoard.OverdueBy(Row("I", ServiceJobStages.OnBench, daysInStage: null), ServicePendingStages.OnBench));
    }

    [Fact]
    public void Delivered_returned_and_claimed_DC_RA_jobs_are_never_on_the_board()
    {
        Assert.False(ServicePendingBoard.IsOnBoard(Row("A", ServiceJobStages.Delivered)));
        Assert.False(ServicePendingBoard.IsOnBoard(Row("B", ServiceJobStages.Rwr)));
        Assert.False(ServicePendingBoard.IsOnBoard(Row("C", ServiceJobStages.DcIssued, claimRaised: true)));
        Assert.False(ServicePendingBoard.IsOnBoard(Row("D", ServiceJobStages.RaIssued, claimRaised: true)));
        Assert.False(ServicePendingBoard.IsOnBoard(Row("E", "SOMETHING_NEW")));
        Assert.True(ServicePendingBoard.IsOnBoard(Row("F", ServiceJobStages.DcIssued, claimRaised: false)));
        Assert.True(ServicePendingBoard.IsOnBoard(Row("G", ServiceJobStages.RaIssued, claimRaised: false)));
        Assert.True(ServicePendingBoard.IsOnBoard(Row("H", ServiceJobStages.OnBench, claimRaised: true)));
        Assert.True(ServicePendingBoard.IsOnBoard(Row("I", ServiceJobStages.Booked)));
    }

    [Fact]
    public void Entries_are_sorted_by_stage_order_then_days_in_stage_descending_with_unknown_last()
    {
        var entries = ServicePendingBoard.Entries(SampleRows());
        Assert.Equal(["JOAW330SYN0101", "JOAW330SYN0102", "JOAW330SYN0103", "JOAW330SYN0114", "JOAW330SYN0104", "JOAW330SYN0105",
                "JOAW330SYN0106", "JOAW330SYN0107", "JOAW330SYN0108", "JOAW330SYN0109", "JOAW330SYN0111"],
            entries.Select(entry => entry.Row.JobOrderNumber));
        var groups = ServicePendingBoard.Groups(entries);
        Assert.Equal(ServicePendingStages.Board.Select(stage => stage.Label), groups.Select(group => group.Stage.Label));
        Assert.Equal([1, 3, 2, 1, 1, 1, 1, 1], groups.Select(group => group.Entries.Count));
    }

    [Fact]
    public void The_five_numbers_count_the_whole_board()
    {
        var numbers = ServicePendingBoard.Numbers(ServicePendingBoard.Entries(SampleRows()));
        Assert.Equal(new ServicePendingNumbers(OpenJobs: 11, Overdue: 6, Over30Days: 4, InTransit: 1, PartsAwaited: 2), numbers);
    }

    [Fact]
    public void Filters_narrow_by_stage_band_overdue_brand_guarantee_and_job_type()
    {
        var entries = ServicePendingBoard.Entries(SampleRows());
        string[] Jobs(ServicePendingFilter filter) => ServicePendingBoard.Apply(entries, filter).Select(entry => entry.Row.JobOrderNumber).ToArray();

        Assert.Equal(11, Jobs(ServicePendingFilter.None).Length);
        Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0103", "JOAW330SYN0114", "JOAW330SYN0104", "JOAW330SYN0105"],
            Jobs(new(Stages: [ServiceJobStages.OnBench, ServiceJobStages.IndentRaised])));
        Assert.Equal(["JOAW330SYN0104", "JOAW330SYN0107", "JOAW330SYN0111"], Jobs(new(AgeBand: ServiceAgeBands.UpTo60)));
        Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0104", "JOAW330SYN0105", "JOAW330SYN0106", "JOAW330SYN0108", "JOAW330SYN0111"],
            Jobs(new(OverdueOnly: true)));
        Assert.Equal(["JOAW330SYN0103", "JOAW330SYN0107"], Jobs(new(Brand: "sample brand b")));
        Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0106"], Jobs(new(Guarantee: "Out of guarantee")));
        Assert.Equal(["JOAW330SYN0103", "JOAW330SYN0105"], Jobs(new(JoType: ServicePendingBoard.QuickBilling)));
        Assert.Equal(["JOAW330SYN0104"], Jobs(new(Stages: [ServiceJobStages.IndentRaised], OverdueOnly: true, AgeBand: ServiceAgeBands.UpTo60)));
    }

    // ----- the screen -----

    [Fact]
    public void Board_shows_the_groups_in_stage_order_the_columns_of_3_3_and_the_five_numbers()
    {
        RunSta(() =>
        {
            var query = new FakeBoardQuery();
            var view = new ServicePendingBoardView(() => query, NoExport, NoPackExport);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(1, query.BoardCalls);
            Assert.Equal(11, view.Rows.Count);
            Assert.Equal("Service data as at 05 Oct 2026 (refreshed " + FakeBoardQuery.ImportedLocal.ToString("dd MMM yyyy") + ")", view.AsAtText);
            Assert.Equal("11 open jobs in 8 stage groups · stage order, longest in stage first.", view.StatusText);
            Assert.Equal(["Job number", "Stage", "Booked on", "Days since booking", "Days in stage", "EDD", "Overdue by (days)", "Age band", "Brand",
                "Model", "Product", "Guarantee", "Customer type", "Job type", "Pending at", "Spare required", "Last reading"], view.ColumnHeaders);
            Assert.Equal(["JOAW330SYN0101", "JOAW330SYN0102", "JOAW330SYN0103", "JOAW330SYN0114", "JOAW330SYN0104", "JOAW330SYN0105",
                "JOAW330SYN0106", "JOAW330SYN0107", "JOAW330SYN0108", "JOAW330SYN0109", "JOAW330SYN0111"], view.Rows.Select(row => (string)row.Cells[0]!));
            var indent = view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0104");
            Assert.Equal(["JOAW330SYN0104", "Indent raised, parts awaited", new DateOnly(2026, 8, 20), 46, 20, new DateOnly(2026, 10, 1), 4, "31-60",
                "Sample Brand A", "Model 4", "Watch", "In guarantee", "Retail", "Booking", "AW330", "Crown", new DateOnly(2026, 10, 5)], indent.Cells);
            var srn = view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0106");
            Assert.Equal(60, srn.Cells[6]);
            Assert.Equal("Titan Bangalore", srn.Cells[14]);
            Assert.Null(view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0109").Cells[6]);
            Assert.Equal(new ServicePendingNumbers(11, 6, 4, 1, 2), view.Numbers);
            Assert.Equal(ServicePendingStages.Board.Select(stage => stage.Label), view.Groups.Select(group => group.Stage.Label));
            var cards = Descendants<KpiCard>(view).ToArray();
            Assert.Equal(5, cards.Length);
            Assert.Equal(["Open jobs: 11. on the board", "Overdue: 6. EDD passed or over the stage limit", "Over 30 days: 4. since booking",
                "In transit: 1. sent back after repair", "Parts awaited: 2. indent raised"], cards.Select(System.Windows.Automation.AutomationProperties.GetName));
            // The grid groups by stage, in stage order.
            var grouped = Assert.IsAssignableFrom<IEnumerable<object>>(view.Table.Items.Groups).Cast<CollectionViewGroup>().ToArray();
            Assert.Equal(ServicePendingStages.Board.Select(stage => stage.Label), grouped.Select(group => (string)group.Name));
            Assert.Same(view.Rows, view.Table.ItemsSource);
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text.StartsWith(ServiceScreenView.ServiceCentreLabel, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Filters_on_the_screen_narrow_the_rows_but_not_the_numbers()
    {
        RunSta(() =>
        {
            var query = new FakeBoardQuery();
            var view = new ServicePendingBoardView(() => query, NoExport, NoPackExport)
            {
                SelectedStages = [ServiceJobStages.OnBench, ServiceJobStages.IndentRaised],
                OverdueOnly = true
            };
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0104", "JOAW330SYN0105"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal(2, view.Groups.Count);
            Assert.Equal(new ServicePendingNumbers(11, 6, 4, 1, 2), view.Numbers);
            Assert.Equal("3 open jobs in 2 stage groups · stage order, longest in stage first.", view.StatusText);

            view.SelectedStages = [];
            view.OverdueOnly = false;
            view.SelectedBrand = "Sample Brand B";
            view.SelectedGuarantee = "In guarantee";
            view.SelectedJoType = ServicePendingBoard.QuickBilling;
            view.SelectedAgeBand = ServiceAgeBands.UpTo7;
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["JOAW330SYN0103"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("Sample Brand B", view.SelectedBrand);
            Assert.Equal("In guarantee", view.SelectedGuarantee);
            Assert.Equal("1 open job in 1 stage group · stage order, longest in stage first.", view.StatusText);

            view.SelectedAgeBand = ServiceAgeBands.Over60;
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Empty(view.Rows);
            Assert.Equal("No open jobs match these filters.", view.StatusText);
            Assert.Equal(new ServicePendingNumbers(11, 6, 4, 1, 2), view.Numbers);
        });
    }

    [Fact]
    public void Brand_and_guarantee_choices_come_from_the_board()
    {
        RunSta(() =>
        {
            var view = new ServicePendingBoardView(() => new FakeBoardQuery(), NoExport, NoPackExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            var brands = Descendants<ComboBox>(view).Single(combo => System.Windows.Automation.AutomationProperties.GetName(combo) == "Brand");
            Assert.Equal([ServicePendingBoardView.AllBrandsLabel, "Sample Brand A", "Sample Brand B"], brands.Items.Cast<ServiceListChoice>().Select(choice => choice.Label));
            var guarantee = Descendants<ComboBox>(view).Single(combo => System.Windows.Automation.AutomationProperties.GetName(combo) == "Guarantee");
            Assert.Equal([ServicePendingBoardView.AllGuaranteesLabel, "In guarantee", "Out of guarantee"], guarantee.Items.Cast<ServiceListChoice>().Select(choice => choice.Label));
            Assert.Null(view.SelectedBrand);
            Assert.Null(view.SelectedGuarantee);
        });
    }

    [Fact]
    public void A_row_opens_Job_history_with_its_job_number()
    {
        RunSta(() =>
        {
            var opened = new List<(string Task, string? Job)>();
            var view = (ServicePendingBoardView)ServiceScreens.Create(ServiceScreens.PendingTask, () => new FakeBoardQuery(), NoExport,
                (task, job) => opened.Add((task, job)));
            SpinUntil(() => !view.IsLoading);
            Assert.True(view.CanOpenJob);
            Assert.False(view.OpenSelected());
            Assert.Empty(opened);

            view.Table.SelectedItem = view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0106");
            Assert.True(view.OpenSelected());
            Assert.Equal([(ServiceScreens.JobHistoryTask, "JOAW330SYN0106")], opened);
            Assert.Contains(Descendants<Button>(view), button => (string)button.Content == ServicePendingBoardView.OpenJobText && button.IsEnabled);
        });
    }

    [Fact]
    public void Without_navigation_the_board_has_no_drill_down()
    {
        RunSta(() =>
        {
            var view = new ServicePendingBoardView(() => new FakeBoardQuery(), NoExport, NoPackExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            view.Table.SelectedItem = view.Rows[0];
            Assert.False(view.CanOpenJob);
            Assert.False(view.OpenSelected());
            Assert.Contains(Descendants<Button>(view), button => (string)button.Content == ServicePendingBoardView.OpenJobText && button.Visibility == System.Windows.Visibility.Collapsed);
        });
    }

    [Fact]
    public void ShowJob_sets_the_job_number_on_the_history_screen_and_ignores_other_views()
    {
        RunSta(() =>
        {
            var query = new FakeBoardQuery();
            var history = new ServiceJobHistoryView(() => query, NoExport);
            ServiceScreens.ShowJob(history, "JOAW330SYN0106");
            SpinUntil(() => !history.IsLoading && history.SearchedJobNumber.Length > 0);
            Assert.Equal("JOAW330SYN0106", history.JobNumber);
            Assert.Equal("JOAW330SYN0106", query.LastJob);
            ServiceScreens.ShowJob(new TextBlock(), "JOAW330SYN0106");
            ServiceScreens.ShowJob(null, "JOAW330SYN0106");
        });
    }

    [Fact]
    public void Export_writes_one_sheet_per_stage_shown_with_the_grid_columns()
    {
        RunSta(() =>
        {
            ReportPackDocument? captured = null;
            var view = new ServicePendingBoardView(() => new FakeBoardQuery(), NoExport, (path, document) => { captured = document; return Task.CompletedTask; });
            view.ActivateAsync().GetAwaiter().GetResult();

            view.ExportToPathAsync("synthetic.xlsx").GetAwaiter().GetResult();

            var document = Assert.NotNull(captured);
            Assert.Equal("Service pending board", document.Title);
            Assert.Equal((AsAt, AsAt), (document.DateFrom, document.DateTo));
            Assert.Equal(view.AsAtText, document.Message);
            Assert.Equal(ServicePendingStages.Board.Select(stage => stage.Label), document.Tables.Select(table => table.Name));
            Assert.Equal(view.Groups.Select(group => group.Entries.Count), document.Tables.Select(table => table.Data.Rows.Count));
            Assert.Equal(view.Rows.Count, document.Tables.Sum(table => table.Data.Rows.Count));
            Assert.Equal("3 jobs", document.Tables[1].Status);
            Assert.All(document.Tables, table =>
            {
                Assert.Equal(view.Table.Columns.Select(column => (string)column.Header), table.Data.Columns.Select(column => column.Header));
                Assert.All(table.Data.Rows, row => Assert.Equal(table.Data.Columns.Count, row.Count));
                Assert.DoesNotContain(table.Data.Columns, column => ForbiddenHeaderWords.Any(word => column.Header.Contains(word, StringComparison.OrdinalIgnoreCase)));
            });

            // The stage filter chooses the visible group: one sheet.
            view.SelectedStages = [ServiceJobStages.SrnOut];
            view.ActivateAsync().GetAwaiter().GetResult();
            view.ExportToPathAsync("synthetic.xlsx").GetAwaiter().GetResult();
            var one = Assert.Single(Assert.NotNull(captured).Tables);
            Assert.Equal("SRN out for repair", one.Name);
            Assert.Equal("JOAW330SYN0106", Assert.Single(one.Data.Rows)[0]);
        });
    }

    [Fact]
    public void Export_through_the_report_exporter_produces_a_workbook_with_a_sheet_per_stage()
    {
        RunSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"etp-service-pending-board-{Guid.NewGuid():N}.xlsx");
            try
            {
                var view = new ServicePendingBoardView(() => new FakeBoardQuery(), NoExport);
                view.ActivateAsync().GetAwaiter().GetResult();
                view.ExportToPathAsync(path).GetAwaiter().GetResult();

                using var archive = ZipFile.OpenRead(path);
                Assert.Equal(8, archive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)));
                var xml = string.Concat(archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .Select(entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }));
                Assert.Contains("JOAW330SYN0106", xml);
                Assert.Contains("On the bench", xml);
                Assert.DoesNotContain("Phone", xml, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Sample Customer", xml, StringComparison.OrdinalIgnoreCase);
            }
            finally { File.Delete(path); }
        });
    }

    [Fact]
    public void An_empty_board_says_every_job_is_closed()
    {
        RunSta(() =>
        {
            var view = new ServicePendingBoardView(() => new FakeBoardQuery { Rows = [Row("JOAW330SYN0112", ServiceJobStages.Delivered), Row("JOAW330SYN0110", ServiceJobStages.DcIssued, claimRaised: true)] }, NoExport, NoPackExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Empty(view.Rows);
            Assert.Empty(view.Board);
            Assert.Equal(new ServicePendingNumbers(0, 0, 0, 0, 0), view.Numbers);
            Assert.Equal("No open jobs. Every job the Service Centre exported is delivered, returned or closed by claim.", view.StatusText);
        });
    }

    [Fact]
    public void No_Service_data_imported_yet_is_said_before_the_board_is_read()
    {
        RunSta(() =>
        {
            var query = new FakeBoardQuery { Refreshes = [] };
            var view = new ServicePendingBoardView(() => query, NoExport, NoPackExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(ServiceScreenView.NoDataText, view.AsAtText);
            Assert.Equal(0, query.BoardCalls);
            Assert.Null(view.Numbers);
            Assert.Empty(Descendants<KpiCard>(view));
        });
    }

    [Fact]
    public void A_query_without_the_1_10_0_model_is_described_by_DesktopFriendlyError()
    {
        RunSta(() =>
        {
            var view = new ServicePendingBoardView(() => new PreModelQuery(), NoExport, NoPackExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.StartsWith("Service pending board could not be loaded. ", view.StatusText, StringComparison.Ordinal);
            Assert.False(view.IsLoading);
        });
    }

    [Fact]
    public void No_column_header_or_contract_field_names_a_phone_email_address_or_customer_name()
    {
        RunSta(() =>
        {
            var view = new ServicePendingBoardView(() => new FakeBoardQuery(), NoExport, NoPackExport);
            Assert.DoesNotContain(view.ColumnHeaders, header => ForbiddenHeaderWords.Any(word => header.Contains(word, StringComparison.OrdinalIgnoreCase)));
        });
        Assert.All(typeof(ServicePendingBoardRow).GetProperties(), property =>
            Assert.DoesNotContain(ForbiddenHeaderWords, word => property.Name.Contains(word.Replace("-", "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));
    }

    // ----- fixtures -----

    private static ServicePendingBoardRow Row(string job, string stage, DateOnly? booking = null, int? ageDays = null, int? daysInStage = null,
        DateOnly? edd = null, string? brand = "Sample Brand A", string? guarantee = "In guarantee", string joType = ServicePendingBoard.Booking,
        string? pendingAt = "AW330", string? spare = null, bool claimRaised = false, string? model = null) =>
        new(job, stage, daysInStage is { } d ? AsAt.AddDays(-d) : null, booking, joType, edd, brand, model ?? "Model " + job[^1],
            "Watch", guarantee, "Retail", pendingAt, spare, claimRaised, ageDays, daysInStage, AsAt, AsAt);

    private static IReadOnlyList<ServicePendingBoardRow> SampleRows() =>
    [
        Row("JOAW330SYN0101", ServiceJobStages.Booked, new(2026, 10, 3), 2, 2),
        Row("JOAW330SYN0102", ServiceJobStages.OnBench, new(2026, 9, 25), 10, 8, guarantee: "Out of guarantee"),
        Row("JOAW330SYN0103", ServiceJobStages.OnBench, new(2026, 9, 30), 5, 5, edd: new(2026, 10, 10), brand: "Sample Brand B", joType: ServicePendingBoard.QuickBilling),
        Row("JOAW330SYN0104", ServiceJobStages.IndentRaised, new(2026, 8, 20), 46, 20, edd: new(2026, 10, 1), spare: "Crown", model: "Model 4"),
        Row("JOAW330SYN0105", ServiceJobStages.IndentRaised, new(2026, 9, 28), 7, 3, edd: new(2026, 10, 4), spare: "Strap", joType: ServicePendingBoard.QuickBilling),
        Row("JOAW330SYN0106", ServiceJobStages.SrnOut, new(2026, 7, 1), 96, 90, guarantee: "Out of guarantee", pendingAt: "Titan Bangalore"),
        Row("JOAW330SYN0107", ServiceJobStages.InTransitBack, new(2026, 9, 1), 34, 10, brand: "Sample Brand B", pendingAt: "In_Transit"),
        Row("JOAW330SYN0108", ServiceJobStages.ReadyForDelivery, new(2026, 9, 20), 15, 8),
        Row("JOAW330SYN0109", ServiceJobStages.DcIssued, new(2026, 9, 10), 25, 20),
        Row("JOAW330SYN0110", ServiceJobStages.DcIssued, new(2026, 9, 10), 25, 20, claimRaised: true),
        Row("JOAW330SYN0111", ServiceJobStages.RaIssued, new(2026, 9, 1), 34, 30, edd: new(2026, 9, 20)),
        Row("JOAW330SYN0112", ServiceJobStages.Delivered, new(2026, 9, 1), 34, 1),
        Row("JOAW330SYN0113", ServiceJobStages.Rwr, new(2026, 9, 1), 34, 1),
        Row("JOAW330SYN0114", ServiceJobStages.OnBench, brand: null, guarantee: null)
    ];

    private static Task NoExport(string path, ExcelReportMetadata metadata, ExcelReportData data) =>
        throw new InvalidOperationException("The board exports through the pack exporter, never one sheet.");

    private static Task NoPackExport(string path, ReportPackDocument document) =>
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

    /// <summary>A query that implements the 1.10.0 board; the other members are not used by these tests.</summary>
    private class FakeBoardQuery : IServiceReportQuery
    {
        public static readonly DateTime ImportedUtc = new(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc);
        public static DateTime ImportedLocal => ImportedUtc.ToLocalTime();

        public IReadOnlyList<ServiceRefresh> Refreshes { get; init; } =
        [
            new("S009", new(2026, 9, 28), 6, 101, ImportedUtc.AddDays(-7)),
            new("S009", new(2026, 10, 5), 7, 140, ImportedUtc),
            new("S002", new(2026, 10, 5), 90, 141, ImportedUtc.AddMinutes(-2))
        ];
        public IReadOnlyList<ServicePendingBoardRow> Rows { get; init; } = SampleRows();
        public int BoardCalls { get; private set; }
        public string? LastJob { get; private set; }

        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Refreshes);

        public virtual Task<IReadOnlyList<ServicePendingBoardRow>> LoadPendingBoardAsync(CancellationToken cancellationToken = default)
        {
            BoardCalls++;
            return Task.FromResult(Rows);
        }

        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default)
        {
            LastJob = jobOrderNumber;
            IReadOnlyList<ServiceJobEvent> rows = [new(jobOrderNumber, "S011", "SRN status", ServiceJobEventKind.FirstSeen, new(2026, 10, 5), null)];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A query written before 1.10.0: the contract's default LoadPendingBoardAsync throws.</summary>
    private sealed class PreModelQuery : IServiceReportQuery
    {
        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceRefresh>>([new("S009", new(2026, 10, 5), 7, 140, FakeBoardQuery.ImportedUtc)]);
        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
