using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// The Service claims screen (1.10.0 design 3.5, lane claims) against a fake IServiceReportQuery. All values are
/// synthetic: job numbers JOAW330SYN…, document numbers …SYN…. Claims are raised-only (decision 25, Q9 = A).
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServiceClaimsViewTests
{
    private static readonly string[] ForbiddenHeaderWords = ["phone", "mobile", "landline", "e-mail", "email", "address"];
    private static readonly string[] ForbiddenWording = ["outstanding", "unsettled"];

    [Fact]
    public void Summary_totals_month_by_claim_type_newest_month_first()
    {
        RunSta(() =>
        {
            var query = new FakeClaimsQuery();
            var view = new ServiceClaimsView(() => query, NoExport);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(1, query.ClaimsCalls);
            Assert.Equal(ServiceClaimsView.SummaryMode, view.ShownMode);
            Assert.Equal(["Month", "Claim type", "Documents", "Lines", "Jobs", "Net incl. tax", "UCP value"], view.ColumnHeaders);
            Assert.Equal(6, view.Rows.Count);
            Assert.Equal([new DateOnly(2026, 10, 1), new(2026, 10, 1), new(2026, 9, 1), new(2026, 9, 1), new(2026, 8, 1), new(2026, 7, 1)],
                view.Rows.Select(row => (DateOnly)row.Cells[0]!));
            Assert.Equal(["GPRC cell", "WDC", "GPRC cell", "Module Bank", "WRA", "WDC"], view.Rows.Select(row => (string)row.Cells[1]!));
            // Oct GPRC: one document, two lines, one job, net 100 + 50, UCP 1000 + 500.
            Assert.Equal([new DateOnly(2026, 10, 1), "GPRC cell", 1, 2, 1, 150m, 1500m], view.Rows[0].Cells);
            // Aug WRA: one document whose line has no job number -> zero jobs, never a blank job counted.
            Assert.Equal([new DateOnly(2026, 8, 1), "WRA", 1, 1, 0, 50m, 0m], view.Rows[4].Cells);
            Assert.Equal("6 month and claim type rows · All claim types · claims raised only.", view.StatusText);
            Assert.Same(view.Rows, view.Table.ItemsSource);
        });
    }

    [Fact]
    public void Not_yet_claimed_lists_DC_and_RA_jobs_longest_waiting_first_with_days_since_issued()
    {
        RunSta(() =>
        {
            var query = new FakeClaimsQuery();
            var view = new ServiceClaimsView(() => query, NoExport) { Mode = ServiceClaimsView.NotYetClaimedMode };

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(["Job number", "Issued as", "Issued on", "Days since issued", "Claim type due", "DC/RA number", "Brand", "Model", "As at"], view.ColumnHeaders);
            Assert.Equal(["JOAW330SYN0201", "JOAW330SYN0202", "JOAW330SYN0203"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal(["JOAW330SYN0201", "DC", new DateOnly(2026, 9, 1), 34, "WDC", "WDCSYN0001", "Sample Brand", "Model 1", new DateOnly(2026, 10, 5)], view.Rows[0].Cells);
            Assert.Equal(10, view.Rows[1].Cells[3]);
            Assert.Null(view.Rows[2].Cells[3]);
            Assert.Equal("3 DC/RA jobs with no claim document yet · longest waiting first.", view.StatusText);
        });
    }

    [Fact]
    public void Numbers_show_claims_raised_this_month_not_yet_claimed_and_the_oldest()
    {
        RunSta(() =>
        {
            var view = new ServiceClaimsView(() => new FakeClaimsQuery(), NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(["Claims raised this month", "DC/RA jobs not yet claimed", "Oldest not yet claimed", "GPRC claim export"], view.Numbers.Select(number => number.Label));
            // The latest claims reading is 02 Oct 2026, so "this month" is Oct 2026: two documents, net 150 + 300.
            Assert.Equal("2", view.Numbers[0].Value);
            Assert.Equal($"{450m:N2} net incl. tax · Oct 2026 (latest claims reading)", view.Numbers[0].Secondary);
            Assert.Equal("3", view.Numbers[1].Value);
            Assert.Equal("WDC due 2 · WRA due 1", view.Numbers[1].Secondary);
            Assert.Equal("34 days", view.Numbers[2].Value);
            Assert.StartsWith("JOAW330SYN0201 · DC on 01 Sep 2026", view.Numbers[2].Secondary, StringComparison.Ordinal);
            Assert.Equal(4, Descendants<KpiCard>(view).Count());
        });
    }

    [Fact]
    public void GPRC_gap_warning_shows_when_the_GPRC_claim_export_is_older_than_the_DC_RA_lists()
    {
        RunSta(() =>
        {
            var view = new ServiceClaimsView(() => new FakeClaimsQuery(), NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Contains("latest GPRC CLAIM export (S041) is dated 02 Oct 2026", view.GapWarningText);
            Assert.Contains("latest DC/RA list (S014/S016) is dated 05 Oct 2026", view.GapWarningText);
            Assert.Contains("Export GPRC CLAIM", view.GapWarningText);
            Assert.Equal("02 Oct 2026", view.Numbers[3].Value);
            Assert.Equal("DC/RA lists to 05 Oct 2026", view.Numbers[3].Secondary);
        });
    }

    [Fact]
    public void GPRC_gap_warning_is_hidden_when_the_GPRC_claim_export_is_current()
    {
        RunSta(() =>
        {
            var query = new FakeClaimsQuery
            {
                Refreshes =
                [
                    new("S041", new(2026, 10, 5), 25, 150, FakeClaimsQuery.ImportedUtc),
                    new("S014", new(2026, 10, 5), 108, 151, FakeClaimsQuery.ImportedUtc),
                    new("S016", new(2026, 9, 29), 23, 152, FakeClaimsQuery.ImportedUtc)
                ]
            };
            var view = new ServiceClaimsView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal("", view.GapWarningText);
        });
    }

    [Fact]
    public void GPRC_gap_rule_treats_a_missing_GPRC_export_as_behind_and_no_DC_RA_list_as_nothing_to_compare()
    {
        var dcOnly = ServiceClaimsView.GprcGap([new("S014", new(2026, 10, 5), 1, 1, FakeClaimsQuery.ImportedUtc)]);
        Assert.True(dcOnly.IsBehind);
        Assert.Null(dcOnly.LatestGprcReading);
        var gprcOnly = ServiceClaimsView.GprcGap([new("S041", new(2026, 10, 2), 1, 1, FakeClaimsQuery.ImportedUtc)]);
        Assert.False(gprcOnly.IsBehind);
        Assert.False(ServiceClaimsView.GprcGap([]).IsBehind);
        var equal = ServiceClaimsView.GprcGap(
        [
            new("S041", new(2026, 10, 5), 1, 1, FakeClaimsQuery.ImportedUtc), new("S016", new(2026, 10, 5), 1, 2, FakeClaimsQuery.ImportedUtc)
        ]);
        Assert.False(equal.IsBehind);
    }

    [Fact]
    public void Claim_type_and_month_filters_narrow_the_lines()
    {
        RunSta(() =>
        {
            var view = new ServiceClaimsView(() => new FakeClaimsQuery(), NoExport)
            {
                Mode = ServiceClaimsView.DetailMode,
                FromMonth = new(2026, 9, 1),
                ToMonth = new(2026, 10, 15)
            };
            view.SelectedClaimType = ServiceClaimsView.ClaimTypes.Single(choice => choice.Code == ServiceClaimTypes.GprcCell);

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal(["Date", "Document number", "Job number", "Item", "Quantity", "Net incl. tax", "Source family", "Claim type"], view.ColumnHeaders);
            // Any day of a month selects the whole month: Sep and Oct GPRC lines, the Jul/Aug ones and other types left out.
            Assert.Equal(["GPAW3SYN0001", "GPAW3SYN0001", "GPAW3SYN0002"], view.Rows.Select(row => (string)row.Cells[1]!));
            Assert.All(view.Rows, row => Assert.Equal("GPRC cell", row.Cells[7]));
            Assert.Equal(["S041", "S041", "S023"], view.Rows.Select(row => (string)row.Cells[6]!));
            Assert.Equal("3 claim lines · GPRC cell · claims raised only.", view.StatusText);
            // The numbers are the screen's, not the filter's.
            Assert.Equal("3", view.Numbers[1].Value);
        });
    }

    [Fact]
    public void A_reversed_month_range_is_refused_without_querying()
    {
        RunSta(() =>
        {
            var query = new FakeClaimsQuery();
            var view = new ServiceClaimsView(() => query, NoExport) { FromMonth = new(2026, 10, 1), ToMonth = new(2026, 7, 1) };
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(0, query.ClaimsCalls);
            Assert.Contains("From month is after the To month", view.StatusText);
        });
    }

    [Fact]
    public void Not_yet_claimed_honours_the_claim_type_filter()
    {
        RunSta(() =>
        {
            var view = new ServiceClaimsView(() => new FakeClaimsQuery(), NoExport) { Mode = ServiceClaimsView.NotYetClaimedMode };
            view.SelectedClaimType = ServiceClaimsView.ClaimTypes.Single(choice => choice.Code == ServiceClaimTypes.Wra);
            view.ActivateAsync().GetAwaiter().GetResult();
            var row = Assert.Single(view.Rows);
            Assert.Equal("JOAW330SYN0202", row.Cells[0]);
            Assert.Equal("RA", row.Cells[1]);
        });
    }

    [Fact]
    public void Opening_a_month_row_shows_that_month_and_type_as_claim_lines()
    {
        RunSta(() =>
        {
            var query = new FakeClaimsQuery();
            var view = new ServiceClaimsView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            var julWdc = view.Rows.Single(row => (DateOnly)row.Cells[0]! == new DateOnly(2026, 7, 1));

            view.DrillDown(julWdc);
            SpinUntil(() => !view.IsLoading && view.ShownMode == ServiceClaimsView.DetailMode);

            Assert.Equal(ServiceClaimsView.DetailMode, view.Mode);
            Assert.Equal(new DateOnly(2026, 7, 1), view.FromMonth);
            Assert.Equal(new DateOnly(2026, 7, 1), view.ToMonth);
            Assert.Equal(ServiceClaimTypes.Wdc, view.SelectedClaimType.Code);
            var line = Assert.Single(view.Rows);
            Assert.Equal("DSAW3SYN0004", line.Cells[1]);
            Assert.Equal(2, query.ClaimsCalls);
        });
    }

    [Fact]
    public void Opening_a_claim_line_or_a_waiting_job_navigates_to_job_history_with_the_job_number()
    {
        RunSta(() =>
        {
            var opened = new List<(string Task, string Argument)>();
            var view = new ServiceClaimsView(() => new FakeClaimsQuery(), NoExport, (task, argument) => opened.Add((task, argument)))
            {
                Mode = ServiceClaimsView.DetailMode
            };
            view.ActivateAsync().GetAwaiter().GetResult();

            view.DrillDown(view.Rows.Single(row => (string)row.Cells[1]! == "DSAW3SYN0004"));
            Assert.Equal([(ServiceScreens.JobHistoryTask, "JOAW330SYN0104")], opened);
            Assert.Equal("Opening Service job history for job JOAW330SYN0104.", view.DrillDownText);

            // A line without a job number (WRA old header, 1.7) cannot open a history.
            view.DrillDown(view.Rows.Single(row => (string)row.Cells[1]! == "RSAW3SYN0005"));
            Assert.Single(opened);
            Assert.Equal("This claim line carries no job number.", view.DrillDownText);

            view.Mode = ServiceClaimsView.NotYetClaimedMode;
            view.ActivateAsync().GetAwaiter().GetResult();
            view.DrillDown(view.Rows[1]);
            Assert.Equal((ServiceScreens.JobHistoryTask, "JOAW330SYN0202"), opened[^1]);
        });
    }

    [Fact]
    public void Without_a_navigation_hook_the_screen_tells_the_user_which_job_to_look_up()
    {
        RunSta(() =>
        {
            var view = new ServiceClaimsView(() => new FakeClaimsQuery(), NoExport) { Mode = ServiceClaimsView.NotYetClaimedMode };
            view.ActivateAsync().GetAwaiter().GetResult();
            view.DrillDown(view.Rows[0]);
            Assert.Equal("Open Service job history and enter job JOAW330SYN0201.", view.DrillDownText);
        });
    }

    [Theory]
    [InlineData(ServiceClaimsView.SummaryMode, "Service claims by month")]
    [InlineData(ServiceClaimsView.DetailMode, "Service claim lines")]
    [InlineData(ServiceClaimsView.NotYetClaimedMode, "Service claims not yet raised")]
    public void Export_writes_the_visible_columns_and_no_phone_email_or_address_column(string mode, string exportName)
    {
        RunSta(() =>
        {
            (string Path, ExcelReportMetadata Metadata, ExcelReportData Data)? captured = null;
            var view = new ServiceClaimsView(() => new FakeClaimsQuery(), (path, metadata, data) => { captured = (path, metadata, data); return Task.CompletedTask; }) { Mode = mode };
            view.ActivateAsync().GetAwaiter().GetResult();

            view.ExportToPathAsync("synthetic.xlsx").GetAwaiter().GetResult();

            var export = Assert.NotNull(captured);
            Assert.Equal(exportName, export.Metadata.ReportName);
            Assert.Equal(view.Table.Columns.Select(column => (string)column.Header), export.Data.Columns.Select(column => column.Header));
            Assert.Equal(view.Rows.Count, export.Data.Rows.Count);
            Assert.All(export.Data.Rows, row => Assert.Equal(export.Data.Columns.Count, row.Count));
            Assert.DoesNotContain(export.Data.Columns, column => ForbiddenHeaderWords.Any(word => column.Header.Contains(word, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(ServiceScreenView.ServiceCentreLabel, export.Metadata.AppliedScope);
            Assert.True(export.Metadata.DateFrom <= export.Metadata.DateTo);
            if (mode != ServiceClaimsView.NotYetClaimedMode) Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 1)), (export.Metadata.DateFrom, export.Metadata.DateTo));
        });
    }

    [Theory]
    [InlineData(ServiceClaimsView.SummaryMode)]
    [InlineData(ServiceClaimsView.DetailMode)]
    [InlineData(ServiceClaimsView.NotYetClaimedMode)]
    public void Wording_says_raised_and_never_outstanding_or_settled(string mode)
    {
        RunSta(() =>
        {
            var view = new ServiceClaimsView(() => new FakeClaimsQuery(), NoExport) { Mode = mode };
            view.ActivateAsync().GetAwaiter().GetResult();

            var texts = Descendants<TextBlock>(view).Select(block => block.Text)
                .Concat(view.ColumnHeaders).Append(view.StatusText).Append(view.GapWarningText).Append(view.DrillDownText)
                .Concat(view.Numbers.SelectMany(number => new[] { number.Label, number.Value, number.Secondary }))
                .Concat(view.Rows.SelectMany(row => row.Cells).OfType<string>())
                .Concat(ServiceClaimsView.Modes).Append(ServiceClaimsView.SettlementNote)
                .ToArray();
            Assert.Contains(texts, text => text.Contains("raised", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text == ServiceClaimsView.SettlementNote);
            Assert.All(texts, text => Assert.DoesNotContain(ForbiddenWording, word => text.Contains(word, StringComparison.OrdinalIgnoreCase)));
            // The only place settlement is mentioned is the note saying no export carries it.
            Assert.All(texts.Where(text => text.Contains("settle", StringComparison.OrdinalIgnoreCase)), text => Assert.Equal(ServiceClaimsView.SettlementNote, text));
        });
    }

    [Fact]
    public void Nothing_imported_says_so_and_does_not_query_the_claims()
    {
        RunSta(() =>
        {
            var query = new FakeClaimsQuery { Refreshes = [] };
            var view = new ServiceClaimsView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal(ServiceScreenView.NoDataText, view.AsAtText);
            Assert.StartsWith(ServiceScreenView.NoDataText, view.StatusText, StringComparison.Ordinal);
            Assert.Empty(view.Rows);
            Assert.Empty(view.Numbers);
            Assert.Equal("", view.GapWarningText);
            Assert.Equal(0, query.ClaimsCalls);
        });
    }

    [Fact]
    public void No_claim_lines_shows_the_empty_state_with_zero_numbers()
    {
        RunSta(() =>
        {
            var query = new FakeClaimsQuery { Claims = new([], []) };
            var view = new ServiceClaimsView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal("No claims raised for this choice.", view.StatusText);
            Assert.Equal("0", view.Numbers[0].Value);
            Assert.Equal("No claim lines imported", view.Numbers[0].Secondary);
            Assert.Equal("0", view.Numbers[1].Value);
            Assert.Equal("—", view.Numbers[2].Value);

            view.Mode = ServiceClaimsView.NotYetClaimedMode;
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Equal("Every DC/RA job in this choice has a claim document.", view.StatusText);
        });
    }

    [Fact]
    public void A_build_whose_query_predates_claims_says_so_through_DesktopFriendlyError()
    {
        RunSta(() =>
        {
            var view = new ServiceClaimsView(() => new LegacyQuery(), NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.StartsWith("Service claims could not be loaded. ", view.StatusText, StringComparison.Ordinal);
            Assert.False(view.IsLoading);
        });
    }

    [Fact]
    public void The_claims_task_creates_the_claims_screen()
    {
        RunSta(() =>
        {
            Assert.Equal("service-claims", ServiceScreens.ClaimsTask);
            var view = ServiceScreens.Create(ServiceScreens.ClaimsTask, () => new FakeClaimsQuery(), NoExport);
            Assert.IsType<ServiceClaimsView>(view);
            Assert.Equal("Service claims", view.GetValue(System.Windows.Automation.AutomationProperties.NameProperty));
        });
    }

    [Fact]
    public void Claim_records_carry_no_phone_email_or_address_field_and_no_settlement_state()
    {
        var types = new[] { typeof(ServiceClaimLine), typeof(ServiceUnclaimedJob), typeof(ServiceClaims) };
        Assert.All(types.SelectMany(type => type.GetProperties()), property =>
        {
            Assert.DoesNotContain(ForbiddenHeaderWords, word => property.Name.Contains(word.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(ForbiddenWording, word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
        });
        Assert.Equal(["GPRC", "MODULE_BANK", "WDC", "WRA"], ServiceClaimTypes.All);
        Assert.Equal(["All claim types", "GPRC cell", "Module Bank", "WDC", "WRA"], ServiceClaimsView.ClaimTypes.Select(choice => choice.Label));
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

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
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

    /// <summary>A query written before 1.10.0: it does not override LoadClaimsAsync, so the contract's default refuses.</summary>
    private class LegacyQuery : IServiceReportQuery
    {
        public static readonly DateTime ImportedUtc = new(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc);

        public virtual IReadOnlyList<ServiceRefresh> Refreshes { get; init; } =
        [
            new("S009", new(2026, 10, 5), 7, 140, ImportedUtc),
            new("S041", new(2026, 10, 2), 25, 141, ImportedUtc.AddMinutes(-2)),
            new("S014", new(2026, 10, 5), 108, 142, ImportedUtc),
            new("S016", new(2026, 9, 29), 23, 143, ImportedUtc.AddDays(-7))
        ];

        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Refreshes);
        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeClaimsQuery : LegacyQuery, IServiceReportQuery
    {
        public int ClaimsCalls { get; private set; }

        /// <summary>
        /// Six documents across four months and the four claim types; the GPRC Oct document has two lines (one job);
        /// the WRA Aug line has no job number (old header, design 1.7); S041/S039 are new-header families.
        /// </summary>
        public ServiceClaims Claims { get; init; } = new(
        [
            new(ServiceClaimTypes.GprcCell, new(2026, 10, 1), "GPAW3SYN0001", "JOAW330SYN0101", "ITEM-A", 1m, 100m, 1000m, "S041", new(2026, 10, 2)),
            new(ServiceClaimTypes.GprcCell, new(2026, 10, 1), "GPAW3SYN0001", "JOAW330SYN0101", "ITEM-B", 2m, 50m, 500m, "S041", new(2026, 10, 2)),
            new(ServiceClaimTypes.GprcCell, new(2026, 9, 15), "GPAW3SYN0002", "JOAW330SYN0102", "ITEM-C", 1m, 200m, 800m, "S023", new(2026, 9, 29)),
            new(ServiceClaimTypes.Wdc, new(2026, 10, 1), "DSAW3SYN0003", "JOAW330SYN0103", "ITEM-D", 1m, 300m, 1200m, "S039", new(2026, 10, 2)),
            new(ServiceClaimTypes.Wdc, new(2026, 7, 1), "DSAW3SYN0004", "JOAW330SYN0104", "ITEM-E", 1m, 400m, null, "S039", new(2026, 10, 2)),
            new(ServiceClaimTypes.Wra, new(2026, 8, 10), "RSAW3SYN0005", null, "ITEM-F", 1m, 50m, null, "S026", new(2026, 9, 29)),
            new(ServiceClaimTypes.ModuleBank, new(2026, 9, 20), "MCAW3SYN0006", "JOAW330SYN0106", "ITEM-G", 3m, 75m, 300m, "S024", new(2026, 9, 29))
        ],
        [
            new("JOAW330SYN0202", "S016", "RA", ServiceClaimTypes.Wra, new(2026, 9, 25), "RADCSYN0002", "Sample Brand", "Model 2", new(2026, 10, 5)),
            new("JOAW330SYN0203", "S014", "DC", ServiceClaimTypes.Wdc, null, "WDCSYN0003", "Sample Brand", "Model 3", new(2026, 10, 5)),
            new("JOAW330SYN0201", "S014", "DC", ServiceClaimTypes.Wdc, new(2026, 9, 1), "WDCSYN0001", "Sample Brand", "Model 1", new(2026, 10, 5))
        ]);

        public Task<ServiceClaims> LoadClaimsAsync(CancellationToken cancellationToken = default)
        {
            ClaimsCalls++;
            return Task.FromResult(Claims);
        }
    }
}
