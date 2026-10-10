using System.IO.Compression;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// The Service Pending board (1.10.0, design review 3.3, Q3, Q4, Q8) against a fake IServiceReportQuery built on lane
/// sql's contract, and the pure rules of ServicePendingBoardRules and ServiceAgeing. All values are synthetic:
/// job numbers JOAW330SYN01NN, "Sample Brand A/B".
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServicePendingBoardViewTests
{
    private static readonly string[] ForbiddenHeaderWords = ["phone", "mobile", "landline", "e-mail", "email", "address", "customer name"];
    private static readonly DateOnly AsAt = new(2026, 10, 5);
    private static readonly string[] BoardLabels =
        ["Booked, no status yet", "On the bench", "Indent raised, parts awaited", "SRN out for repair", "Repaired, awaiting delivery",
         "Sent back after repair, in transit", "DC issued", "RA issued"];

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
    public void Age_bands_are_0_7_8_15_16_30_31_60_and_over_60(int? days, string? band)
    {
        Assert.Equal(band, ServiceAgeing.Band(days));
        Assert.Equal(["0-7", "8-15", "16-30", "31-60", "60+"], ServiceAgeing.Bands);
    }

    [Fact]
    public void Board_groups_are_the_eight_open_stages_in_order_with_the_Q4_limits()
    {
        Assert.Equal([ServiceStages.Booked, ServiceStages.OnBench, ServiceStages.IndentRaised, ServiceStages.SrnOut,
                ServiceStages.ReadyForDelivery, ServiceStages.InTransitBack, ServiceStages.DcIssued, ServiceStages.RaIssued],
            ServicePendingBoardRules.BoardStages);
        Assert.Equal(BoardLabels, ServicePendingBoardRules.BoardStages.Select(ServiceStages.Label));
        Assert.Equal([null, 7, 15, 30, 7, 15, null, null], ServicePendingBoardRules.BoardStages.Select(ServiceAgeing.StageLimit));
        Assert.DoesNotContain(ServiceStages.Delivered, ServicePendingBoardRules.BoardStages);
        Assert.DoesNotContain(ServiceStages.Rwr, ServicePendingBoardRules.BoardStages);
    }

    [Fact]
    public void Overdue_with_an_EDD_is_days_past_the_EDD_whatever_the_stage()
    {
        Assert.Equal(4, ServiceAgeing.OverdueBy(ServiceStages.OnBench, new(2026, 10, 1), 2, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.OnBench, new(2026, 10, 5), 40, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.OnBench, new(2026, 10, 9), 40, AsAt));
        Assert.Equal(15, ServiceAgeing.OverdueBy(ServiceStages.RaIssued, new(2026, 9, 20), 30, AsAt));
    }

    [Fact]
    public void Overdue_without_an_EDD_is_days_in_stage_past_the_stage_limit()
    {
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.OnBench, null, 7, AsAt));
        Assert.Equal(1, ServiceAgeing.OverdueBy(ServiceStages.OnBench, null, 8, AsAt));
        Assert.Equal(5, ServiceAgeing.OverdueBy(ServiceStages.IndentRaised, null, 20, AsAt));
        Assert.Equal(60, ServiceAgeing.OverdueBy(ServiceStages.SrnOut, null, 90, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.InTransitBack, null, 15, AsAt));
        Assert.Equal(1, ServiceAgeing.OverdueBy(ServiceStages.ReadyForDelivery, null, 8, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.Booked, null, 500, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.DcIssued, null, 500, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.OnBench, null, null, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.Delivered, new(2026, 1, 1), 500, AsAt));
    }

    [Fact]
    public void Delivered_returned_and_claimed_DC_RA_jobs_are_never_on_the_board()
    {
        Assert.False(ServicePendingBoardRules.IsOnBoard(Row("A", ServiceStages.Delivered)));
        Assert.False(ServicePendingBoardRules.IsOnBoard(Row("B", ServiceStages.Rwr)));
        Assert.False(ServicePendingBoardRules.IsOnBoard(Row("C", ServiceStages.DcIssued, claimRaised: true)));
        Assert.False(ServicePendingBoardRules.IsOnBoard(Row("D", ServiceStages.RaIssued, claimRaised: true)));
        Assert.False(ServicePendingBoardRules.IsOnBoard(Row("E", "SOMETHING_NEW")));
        Assert.True(ServicePendingBoardRules.IsOnBoard(Row("F", ServiceStages.DcIssued, claimRaised: false)));
        Assert.True(ServicePendingBoardRules.IsOnBoard(Row("G", ServiceStages.RaIssued, claimRaised: false)));
        Assert.True(ServicePendingBoardRules.IsOnBoard(Row("H", ServiceStages.OnBench, claimRaised: true)));
        Assert.True(ServicePendingBoardRules.IsOnBoard(Row("I", ServiceStages.Booked)));
    }

    [Fact]
    public void Rows_are_sorted_by_stage_order_then_days_in_stage_descending_with_unknown_last()
    {
        var rows = ServicePendingBoardRules.Rows(SampleRows().Reverse());
        Assert.Equal(["JOAW330SYN0101", "JOAW330SYN0102", "JOAW330SYN0103", "JOAW330SYN0114", "JOAW330SYN0104", "JOAW330SYN0105",
                "JOAW330SYN0106", "JOAW330SYN0108", "JOAW330SYN0107", "JOAW330SYN0109", "JOAW330SYN0111"],
            rows.Select(row => row.JobOrderNumber));
        var groups = ServicePendingBoardRules.Groups(rows);
        Assert.Equal(BoardLabels, groups.Select(group => group.Label));
        Assert.Equal(ServicePendingBoardRules.BoardStages, groups.Select(group => group.Stage));
        Assert.Equal([1, 3, 2, 1, 1, 1, 1, 1], groups.Select(group => group.Rows.Count));
    }

    [Fact]
    public void The_five_numbers_count_the_whole_board_as_the_contract_does()
    {
        var rows = ServicePendingBoardRules.Rows(SampleRows());
        Assert.Equal(new ServicePendingNumbers(OpenJobs: 11, Overdue: 6, Over30Days: 4, InTransit: 1, PartsAwaited: 2), ServicePendingBoardRules.Numbers(rows));
        Assert.Equal(new ServicePendingNumbers(0, 0, 0, 0, 0), ServicePendingBoardRules.Numbers([]));
    }

    [Fact]
    public void Filters_narrow_by_stage_band_overdue_brand_guarantee_and_job_type()
    {
        var rows = ServicePendingBoardRules.Rows(SampleRows());
        string[] Jobs(ServicePendingFilter filter) => ServicePendingBoardRules.Apply(rows, filter).Select(row => row.JobOrderNumber).ToArray();

        Assert.Equal(11, Jobs(ServicePendingFilter.None).Length);
        Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0103", "JOAW330SYN0114", "JOAW330SYN0104", "JOAW330SYN0105"],
            Jobs(new(Stages: [ServiceStages.OnBench, ServiceStages.IndentRaised])));
        // Age band = days since booking (decision 25, design 4.3).
        Assert.Equal(["JOAW330SYN0109"], Jobs(new(AgeBand: "16-30")));
        Assert.Equal(["JOAW330SYN0104", "JOAW330SYN0107", "JOAW330SYN0111"], Jobs(new(AgeBand: "31-60")));
        Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0108"], Jobs(new(AgeBand: "8-15")));
        // R-UI-04: the "Over 15 days" choice is what the Service Today card counts (days since booking > 15).
        Assert.Equal(["JOAW330SYN0104", "JOAW330SYN0106", "JOAW330SYN0107", "JOAW330SYN0109", "JOAW330SYN0111"],
            Jobs(new(AgeBand: ServicePendingBoardRules.Over15Days)));
        Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0104", "JOAW330SYN0105", "JOAW330SYN0106", "JOAW330SYN0108", "JOAW330SYN0111"],
            Jobs(new(OverdueOnly: true)));
        Assert.Equal(["JOAW330SYN0103", "JOAW330SYN0107"], Jobs(new(Brand: "sample brand b")));
        Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0106"], Jobs(new(Guarantee: "Out of guarantee")));
        Assert.Equal(["JOAW330SYN0103", "JOAW330SYN0105"], Jobs(new(JoType: ServiceJobTypes.QuickBilling)));
        Assert.Equal(9, Jobs(new(JoType: ServiceJobTypes.Booking)).Length);
        Assert.Equal(["JOAW330SYN0104"], Jobs(new(Stages: [ServiceStages.IndentRaised], OverdueOnly: true, AgeBand: "31-60")));
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
            Assert.Equal(AsAt, view.AsAt);
            Assert.Equal("Service data as at 05 Oct 2026 (refreshed " + FakeBoardQuery.ImportedLocal.ToString("dd MMM yyyy") + ")", view.AsAtText);
            Assert.Equal("11 open jobs in 8 stage groups · stage order, longest in stage first.", view.StatusText);
            Assert.Equal(["Job number", "Stage", "Booked on", "Days since booking", "Days in stage", "EDD", "Overdue by (days)", "Age band", "Brand",
                "Model", "Product", "Guarantee", "Customer type", "Job type", "Pending at", "Spare required", "Last reading"], view.ColumnHeaders);
            Assert.Equal(["JOAW330SYN0101", "JOAW330SYN0102", "JOAW330SYN0103", "JOAW330SYN0114", "JOAW330SYN0104", "JOAW330SYN0105",
                "JOAW330SYN0106", "JOAW330SYN0108", "JOAW330SYN0107", "JOAW330SYN0109", "JOAW330SYN0111"], view.Rows.Select(row => (string)row.Cells[0]!));
            var indent = view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0104");
            Assert.Equal(["JOAW330SYN0104", "Indent raised, parts awaited", new DateOnly(2026, 8, 20), 46, 20, new DateOnly(2026, 10, 1), 4, "31-60",
                "Sample Brand A", "Model 4", "Watch", "In guarantee", "Retail", "Booking", "AW330", "Crown", new DateOnly(2026, 10, 5)], indent.Cells);
            var srn = view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0106");
            Assert.Equal(60, srn.Cells[6]);
            Assert.Equal("60+", srn.Cells[7]);
            Assert.Equal("Titan Bangalore", srn.Cells[14]);
            Assert.Null(view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0109").Cells[6]);
            Assert.Equal(new ServicePendingNumbers(11, 6, 4, 1, 2), view.Numbers);
            Assert.Equal(BoardLabels, view.Groups.Select(group => group.Label));
            var cards = Descendants<KpiCard>(view).ToArray();
            Assert.Equal(5, cards.Length);
            Assert.Equal(["Open jobs: 11. on the board", "Overdue: 6. EDD passed or over the stage limit", "Over 30 days: 4. since booking",
                "In transit: 1. sent back after repair", "Parts awaited: 2. indent raised"], cards.Select(System.Windows.Automation.AutomationProperties.GetName));
            // The grid groups by stage, in stage order.
            var grouped = Assert.IsAssignableFrom<IEnumerable<object>>(view.Table.Items.Groups).Cast<CollectionViewGroup>().ToArray();
            Assert.Equal(BoardLabels, grouped.Select(group => (string)group.Name));
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
                SelectedStages = [ServiceStages.OnBench, ServiceStages.IndentRaised],
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
            view.SelectedJoType = ServiceJobTypes.QuickBilling;
            view.SelectedAgeBand = "0-7";
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["JOAW330SYN0103"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("Sample Brand B", view.SelectedBrand);
            Assert.Equal("In guarantee", view.SelectedGuarantee);
            Assert.Equal("1 open job in 1 stage group · stage order, longest in stage first.", view.StatusText);

            view.SelectedAgeBand = "60+";
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
            var ages = Descendants<ComboBox>(view).Single(combo => System.Windows.Automation.AutomationProperties.GetName(combo) == "Age band (days since booking)");
            Assert.Equal([ServicePendingBoardView.AllAgesLabel, "0-7 days", "8-15 days", "16-30 days", "31-60 days", "60+ days", "Over 15 days"], ages.Items.Cast<ServiceListChoice>().Select(choice => choice.Label));
            Assert.Equal(8, Descendants<CheckBox>(view).Count(box => System.Windows.Automation.AutomationProperties.GetName(box).StartsWith("Stage ", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void A_brand_and_guarantee_picked_in_the_lists_filter_the_board_and_survive_the_reload()
    {
        RunSta(() =>
        {
            // R-UI-02: a user picks in the combo (not through the property); the reload must keep and apply that pick.
            var view = new ServicePendingBoardView(() => new FakeBoardQuery(), NoExport, NoPackExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            var brands = Descendants<ComboBox>(view).Single(combo => System.Windows.Automation.AutomationProperties.GetName(combo) == "Brand");
            brands.SelectedItem = brands.Items.Cast<ServiceListChoice>().Single(choice => choice.Code == "Sample Brand B");
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["JOAW330SYN0103", "JOAW330SYN0107"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("Sample Brand B", view.SelectedBrand);

            var guarantee = Descendants<ComboBox>(view).Single(combo => System.Windows.Automation.AutomationProperties.GetName(combo) == "Guarantee");
            brands.SelectedIndex = 0;
            guarantee.SelectedItem = guarantee.Items.Cast<ServiceListChoice>().Single(choice => choice.Code == "Out of guarantee");
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(["JOAW330SYN0102", "JOAW330SYN0106"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Null(view.SelectedBrand);
            Assert.Equal("Out of guarantee", view.SelectedGuarantee);
        });
    }

    [Fact]
    public void A_row_opens_Job_history_with_its_job_number()
    {
        RunSta(() =>
        {
            // Lane history's recipe: ServiceScreens.Create passes the shell's opener (NavigateServiceJob) as openJob.
            var opened = new List<string>();
            var view = (ServicePendingBoardView)ServiceScreens.Create(ServiceScreens.PendingTask, () => new FakeBoardQuery(), NoExport,
                null, opened.Add);
            SpinUntil(() => !view.IsLoading);
            Assert.True(view.CanOpenJob);
            Assert.False(view.OpenSelected());
            Assert.Empty(opened);

            view.Table.SelectedItem = view.Rows.Single(row => (string)row.Cells[0]! == "JOAW330SYN0106");
            Assert.True(view.OpenSelected());
            Assert.Equal(["JOAW330SYN0106"], opened);
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
    public void Export_writes_one_sheet_per_stage_shown_with_the_grid_columns()
    {
        RunSta(() =>
        {
            ReportPackDocument? captured = null;
            var view = new ServicePendingBoardView(() => new FakeBoardQuery(), NoExport, (path, document) => { captured = document; return Task.CompletedTask; });
            view.ActivateAsync().GetAwaiter().GetResult();

            view.ExportToPathAsync("synthetic.xlsx").GetAwaiter().GetResult();

            Assert.NotNull(captured);
            var document = captured!;
            Assert.Equal("Service pending board", document.Title);
            Assert.Equal((AsAt, AsAt), (document.DateFrom, document.DateTo));
            Assert.Equal(view.AsAtText, document.Message);
            Assert.Equal(BoardLabels, document.Tables.Select(table => table.Name));
            Assert.Equal(view.Groups.Select(group => group.Rows.Count), document.Tables.Select(table => table.Data.Rows.Count));
            Assert.Equal(view.Rows.Count, document.Tables.Sum(table => table.Data.Rows.Count));
            Assert.Equal("3 jobs", document.Tables[1].Status);
            Assert.All(document.Tables, table =>
            {
                Assert.Equal(view.Table.Columns.Select(column => (string)column.Header), table.Data.Columns.Select(column => column.Header));
                Assert.All(table.Data.Rows, row => Assert.Equal(table.Data.Columns.Count, row.Count));
                Assert.DoesNotContain(table.Data.Columns, column => ForbiddenHeaderWords.Any(word => column.Header.Contains(word, StringComparison.OrdinalIgnoreCase)));
            });

            // The stage filter chooses the visible group: one sheet.
            view.SelectedStages = [ServiceStages.SrnOut];
            view.ActivateAsync().GetAwaiter().GetResult();
            view.ExportToPathAsync("synthetic.xlsx").GetAwaiter().GetResult();
            Assert.NotNull(captured);
            var one = Assert.Single(captured!.Tables);
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
            var view = new ServicePendingBoardView(() => new FakeBoardQuery { Rows = [Row("JOAW330SYN0112", ServiceStages.Delivered), Row("JOAW330SYN0110", ServiceStages.DcIssued, claimRaised: true)] }, NoExport, NoPackExport);
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
    public void A_query_without_the_1_10_0_model_gives_the_contract_default_empty_board()
    {
        RunSta(() =>
        {
            var view = new ServicePendingBoardView(() => new PreModelQuery(), NoExport, NoPackExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Empty(view.Rows);
            Assert.Null(view.AsAt);
            Assert.Equal("No open jobs. Every job the Service Centre exported is delivered, returned or closed by claim.", view.StatusText);
            Assert.False(view.IsLoading);
        });
    }

    [Fact]
    public void A_load_failure_is_described_by_DesktopFriendlyError()
    {
        RunSta(() =>
        {
            var failure = new UnauthorizedAccessException("raw technical text");
            var view = new ServicePendingBoardView(() => new FakeBoardQuery { Failure = failure }, NoExport, NoPackExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal("Service pending board could not be loaded. " + DesktopFriendlyError.Describe(failure), view.StatusText);
            Assert.DoesNotContain("raw technical text", view.StatusText);
            Assert.Null(view.Numbers);
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

    /// <summary>A contract row; OverdueBy comes from the contract rule (ServiceAgeing.OverdueBy), as SqlServerServiceReportQuery sets it.</summary>
    private static ServicePendingBoardRow Row(string job, string stage, DateOnly? booking = null, int? daysSinceBooking = null, int? daysInStage = null,
        DateOnly? edd = null, string? brand = "Sample Brand A", string? guarantee = "In guarantee", string joType = ServiceJobTypes.Booking,
        string? pendingAt = "AW330", string? spare = null, bool claimRaised = false, string? model = null) =>
        new(job, stage, booking, daysSinceBooking, daysInStage, edd, ServiceAgeing.OverdueBy(stage, edd, daysInStage, AsAt), brand, model ?? "Model " + job[^1],
            "Watch", guarantee, "Retail", pendingAt, spare, joType, ServiceJobTypes.Normalise(joType) == ServiceJobTypes.QuickBilling, claimRaised, AsAt);

    private static IReadOnlyList<ServicePendingBoardRow> SampleRows() =>
    [
        Row("JOAW330SYN0101", ServiceStages.Booked, new(2026, 10, 3), 2, 2),
        Row("JOAW330SYN0102", ServiceStages.OnBench, new(2026, 9, 25), 10, 8, guarantee: "Out of guarantee"),
        Row("JOAW330SYN0103", ServiceStages.OnBench, new(2026, 9, 30), 5, 5, edd: new(2026, 10, 10), brand: "Sample Brand B", joType: ServiceJobTypes.QuickBilling),
        Row("JOAW330SYN0104", ServiceStages.IndentRaised, new(2026, 8, 20), 46, 20, edd: new(2026, 10, 1), spare: "Crown", model: "Model 4"),
        Row("JOAW330SYN0105", ServiceStages.IndentRaised, new(2026, 9, 28), 7, 3, edd: new(2026, 10, 4), spare: "Strap", joType: ServiceJobTypes.QuickBilling),
        Row("JOAW330SYN0106", ServiceStages.SrnOut, new(2026, 7, 1), 96, 90, guarantee: "Out of guarantee", pendingAt: "Titan Bangalore"),
        Row("JOAW330SYN0107", ServiceStages.InTransitBack, new(2026, 9, 1), 34, 10, brand: "Sample Brand B", pendingAt: "In_Transit"),
        Row("JOAW330SYN0108", ServiceStages.ReadyForDelivery, new(2026, 9, 20), 15, 8),
        Row("JOAW330SYN0109", ServiceStages.DcIssued, new(2026, 9, 10), 25, 20),
        Row("JOAW330SYN0110", ServiceStages.DcIssued, new(2026, 9, 10), 25, 20, claimRaised: true),
        Row("JOAW330SYN0111", ServiceStages.RaIssued, new(2026, 9, 1), 34, 30, edd: new(2026, 9, 20)),
        Row("JOAW330SYN0112", ServiceStages.Delivered, new(2026, 9, 1), 34, 1),
        Row("JOAW330SYN0113", ServiceStages.Rwr, new(2026, 9, 1), 34, 1),
        Row("JOAW330SYN0114", ServiceStages.OnBench, brand: null, guarantee: null)
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

    /// <summary>A query that implements the 1.10.0 board (any row order; the numbers as ServiceBoard.Build counts them); the other members are not used.</summary>
    private sealed class FakeBoardQuery : IServiceReportQuery
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
        public Exception? Failure { get; init; }
        public int BoardCalls { get; private set; }

        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) =>
            Failure is null ? Task.FromResult(Refreshes) : Task.FromException<IReadOnlyList<ServiceRefresh>>(Failure);

        public Task<ServicePendingBoard> LoadPendingBoardAsync(CancellationToken cancellationToken = default)
        {
            BoardCalls++;
            var open = Rows.Where(row => !ServiceStages.Closed.Contains(row.Stage) && !(row.ClaimRaised && row.Stage is ServiceStages.DcIssued or ServiceStages.RaIssued)).ToArray();
            var numbers = ServicePendingBoardRules.Numbers(open);
            return Task.FromResult(new ServicePendingBoard(Rows, numbers.OpenJobs, numbers.Overdue, numbers.Over30Days, numbers.InTransit, numbers.PartsAwaited, AsAt));
        }

        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A query written before 1.10.0: the contract's default LoadPendingBoardAsync gives an empty board.</summary>
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
