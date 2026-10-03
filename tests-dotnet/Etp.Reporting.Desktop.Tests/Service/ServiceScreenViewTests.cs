using System.IO.Compression;
using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// The four read-only Service centre screens (Service interim, lane L5) against a fake
/// IServiceReportQuery. All values are synthetic: job numbers JOAW330SYN…, "Sample Customer NN".
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServiceScreenViewTests
{
    private static readonly string[] ForbiddenHeaderWords = ["phone", "mobile", "landline", "e-mail", "email", "address"];

    [Fact]
    public void Jobs_screen_shows_rows_the_as_at_line_and_the_chosen_status_list()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobsView(() => query, NoExport);
            view.SelectedStatus = ServiceScreens.StatusLists.Single(choice => choice.Code == "S017");

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal("S017", query.LastStatusView);
            Assert.Equal(2, view.Rows.Count);
            Assert.Equal("Service data as at 05 Oct 2026 (refreshed " + FakeServiceQuery.ImportedLocal.ToString("dd MMM yyyy") + ")", view.AsAtText);
            Assert.Equal("2 jobs · RWR (S017).", view.StatusText);
            Assert.Contains("Customer name", view.ColumnHeaders);
            Assert.Contains("In other lists", view.ColumnHeaders);
            Assert.Same(view.Rows, view.Table.ItemsSource);
            Assert.Contains(Descendants<TextBlock>(view), block => block.Text.StartsWith(ServiceScreenView.ServiceCentreLabel, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void All_lists_passes_no_status_filter()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobsView(() => query, NoExport);
            view.ActivateAsync().GetAwaiter().GetResult();
            Assert.Null(query.LastStatusView);
            Assert.Equal(11, ServiceScreens.StatusLists.Count);
        });
    }

    [Theory]
    [InlineData("jobs")]
    [InlineData("pending")]
    [InlineData("history")]
    [InlineData("money")]
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
    public void Pending_screen_sorts_by_age_descending_with_unknown_ages_last()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServicePendingView(() => query, NoExport);
            view.SelectedList = ServiceScreens.PendingLists.Single(choice => choice.Code == "S010");

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal("S010", query.LastPendingList);
            Assert.Equal(["JOAW330SYN0003", "JOAW330SYN0001", "JOAW330SYN0002"], view.Rows.Select(row => (string)row.Cells[0]!));
            Assert.Equal("3 jobs · Pending delivery (S010) · oldest first.", view.StatusText);
        });
    }

    [Fact]
    public void Job_history_trims_the_job_number_and_uses_information_wording()
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            var view = new ServiceJobHistoryView(() => query, NoExport) { JobNumber = "  JOAW330SYN0007 \t" };

            view.ActivateAsync().GetAwaiter().GetResult();

            Assert.Equal("JOAW330SYN0007", query.LastJob);
            Assert.Equal("JOAW330SYN0007", view.SearchedJobNumber);
            Assert.Equal(2, view.Rows.Count);
            var pending = view.Rows.Single(row => (string?)row.Cells[1] == "S009");
            Assert.Equal(new DateOnly(2026, 9, 28), pending.Cells[2]);
            Assert.Equal(new DateOnly(2026, 9, 28), pending.Cells[3]);
            Assert.Equal("Left the list on or before 05 Oct 2026", pending.Cells[4]);
            var delivered = view.Rows.Single(row => (string?)row.Cells[1] == "S018");
            Assert.Equal(new DateOnly(2026, 10, 5), delivered.Cells[2]);
            Assert.Equal("Job JOAW330SYN0007 was in 2 lists.", view.StatusText);
            Assert.DoesNotContain(view.Rows.SelectMany(row => row.Cells).OfType<string>(),
                text => text.Contains("problem", StringComparison.OrdinalIgnoreCase) || text.Contains("error", StringComparison.OrdinalIgnoreCase));
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
        });
    }

    [Fact]
    public void Reappearing_job_shows_left_and_back_on_the_list()
    {
        var events = new ServiceJobEvent[]
        {
            new("JOAW330SYN0009", "S009", "Pending repair", ServiceJobEventKind.FirstSeen, new(2026, 9, 21), null),
            new("JOAW330SYN0009", "S009", "Pending repair", ServiceJobEventKind.LeftList, new(2026, 9, 28), new(2026, 9, 21)),
            new("JOAW330SYN0009", "S009", "Pending repair", ServiceJobEventKind.Reappeared, new(2026, 10, 5), new(2026, 9, 21))
        };
        var row = Assert.Single(ServiceJobHistoryView.SummariseEvents(events));
        Assert.Equal(new DateOnly(2026, 9, 21), row.Cells[2]);
        Assert.Equal(new DateOnly(2026, 10, 5), row.Cells[3]);
        Assert.Equal("Left the list on or before 28 Sep 2026", row.Cells[4]);
        Assert.Equal("Back on 05 Oct 2026", row.Cells[5]);
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
            Assert.Equal("HEMW, WLMHW", cash.Cells[5]);
            Assert.StartsWith("Manual entries from: HEMW, WLMHW.", view.ManualSourcesText, StringComparison.Ordinal);
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
    [InlineData("pending")]
    [InlineData("history")]
    [InlineData("money")]
    public void Export_writes_the_visible_columns_and_no_phone_email_or_address_column(string screen)
    {
        RunSta(() =>
        {
            var query = new FakeServiceQuery();
            (string Path, ExcelReportMetadata Metadata, ExcelReportData Data)? captured = null;
            var view = Create(screen, query, (path, metadata, data) => { captured = (path, metadata, data); return Task.CompletedTask; });
            if (view is ServiceJobHistoryView history) history.JobNumber = "JOAW330SYN0007";
            if (view is ServiceMoneyView money) { money.From = new(2026, 9, 26); money.To = new(2026, 9, 28); }
            view.ActivateAsync().GetAwaiter().GetResult();

            view.ExportToPathAsync("synthetic.xlsx").GetAwaiter().GetResult();

            var export = Assert.NotNull(captured);
            Assert.Equal(view.Table.Columns.Select(column => (string)column.Header), export.Data.Columns.Select(column => column.Header));
            Assert.Equal(view.Rows.Count, export.Data.Rows.Count);
            Assert.All(export.Data.Rows, row => Assert.Equal(export.Data.Columns.Count, row.Count));
            Assert.DoesNotContain(export.Data.Columns, column => ForbiddenHeaderWords.Any(word => column.Header.Contains(word, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(ServiceScreenView.ServiceCentreLabel, export.Metadata.AppliedScope);
            if (screen is "jobs" or "pending") Assert.Contains("Customer name", export.Data.Columns.Select(column => column.Header));
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
                Assert.Contains("JOAW330SYN0004", xml);
                Assert.DoesNotContain("Phone", xml, StringComparison.OrdinalIgnoreCase);
            }
            finally { File.Delete(path); }
        });
    }

    [Fact]
    public void No_contract_record_carries_a_phone_email_or_address_field()
    {
        var types = new[] { typeof(ServiceRefresh), typeof(ServiceJobRow), typeof(ServicePendingRow), typeof(ServiceJobEvent), typeof(ServiceMoneyDay), typeof(ServiceMoneyChange) };
        Assert.All(types.SelectMany(type => type.GetProperties()), property =>
            Assert.DoesNotContain(ForbiddenHeaderWords, word => property.Name.Contains(word.Replace("-", ""), StringComparison.OrdinalIgnoreCase)));
    }

    private static ServiceScreenView Create(string screen, IServiceReportQuery query, ServiceExcelExport? export = null) => screen switch
    {
        "jobs" => new ServiceJobsView(() => query, export ?? NoExport),
        "pending" => new ServicePendingView(() => query, export ?? NoExport),
        "history" => new ServiceJobHistoryView(() => query, export ?? NoExport),
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
                new(new(2026, 9, 27), "CASH", 850m, 1000m, -150m, ["WLMHW", "HEMW"]),
                new(new(2026, 9, 26), "CARD", 400m, null, 400m, [])
            ];
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ServiceMoneyChange> rows = [new(new(2026, 9, 27), "S004", new(2026, 9, 28), 600m, new(2026, 10, 5), 850m)];
            return Task.FromResult(rows);
        }
    }
}
