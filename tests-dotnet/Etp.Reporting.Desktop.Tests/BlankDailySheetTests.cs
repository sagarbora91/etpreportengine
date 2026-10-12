using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class BlankDailySheetTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    [Fact]
    public void The_options_are_the_three_sheets_with_service_money_under_titan_world()
    {
        Assert.Equal(3, BlankDailySheet.Options.Count);
        Assert.Contains(BlankDailySheet.Options, o => o.StoreCode == "WLMHW" && o.Kind == DailyInputsKind.Retail);
        Assert.Contains(BlankDailySheet.Options, o => o.StoreCode == "HEMW" && o.Kind == DailyInputsKind.Retail);
        Assert.Contains(BlankDailySheet.Options, o => o.StoreCode == "WLMHW" && o.Kind == DailyInputsKind.Service);
        Assert.Equal(BlankDailySheet.Options.Count, BlankDailySheet.Options.Select(o => (o.StoreCode, o.Kind)).Distinct().Count());
    }

    [Theory]
    [InlineData(0, "WLMHW", "RETAIL")]
    [InlineData(1, "HEMW", "RETAIL")]
    [InlineData(2, "WLMHW", "SERVICE")]
    public void The_suggested_file_name_is_the_one_the_importer_expects(int index, string store, string kind)
    {
        var name = BlankDailySheet.SuggestedFileName(new(BlankDailySheet.Options[index], Day));
        Assert.Equal($"ETP-Daily-Inputs-{store}-{kind}-2026-10-12.xlsx", name);
    }

    [Fact]
    public void The_date_must_be_today_or_within_31_days_back()
    {
        Assert.True(BlankDailySheet.IsDateAllowed(Day, Day));
        Assert.True(BlankDailySheet.IsDateAllowed(Day.AddDays(-31), Day));
        Assert.False(BlankDailySheet.IsDateAllowed(Day.AddDays(-32), Day));
        Assert.False(BlankDailySheet.IsDateAllowed(Day.AddDays(1), Day));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Save_writes_a_recognised_blank_daily_inputs_workbook(int index)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBlankSheet_" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var request = new BlankDailySheetRequest(BlankDailySheet.Options[index], Day);
            var path = Path.Combine(folder, BlankDailySheet.SuggestedFileName(request));
            BlankDailySheet.Save(path, request);

            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
            Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(snapshot));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_failed_write_leaves_no_half_written_file()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBlankSheet_" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(folder, "blank.xlsx");
            var bad = new BlankDailySheetRequest(new("Synthetic", " ", "Synthetic", DailyInputsKind.Retail), Day);
            Assert.ThrowsAny<ArgumentException>(() => BlankDailySheet.Save(path, bad));
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void The_button_saves_the_chosen_sheet_through_the_save_dialog_for_a_manager()
    {
        RunSta(() =>
        {
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBlankSheet_" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var view = CreateView(canImport: true);
                string? suggested = null;
                view.BlankSheetChooser = () => new(BlankDailySheet.Options[1], Day);
                view.BlankSheetSavePath = name => { suggested = name; return Path.Combine(folder, name); };

                Assert.True(view.GetBlankDailySheet());

                Assert.Equal("ETP-Daily-Inputs-HEMW-RETAIL-2026-10-12.xlsx", suggested);
                Assert.True(File.Exists(Path.Combine(folder, suggested!)));
                Assert.Contains("Blank daily sheet saved", Status(view));
                Assert.NotNull(view.FindName("GetBlankSheetButton"));
            }
            finally { Directory.Delete(folder, true); }
        });
    }

    [Fact]
    public void A_viewer_cannot_get_the_sheet_and_the_dialogs_are_not_shown()
    {
        RunSta(() =>
        {
            var view = CreateView(canImport: false);
            view.BlankSheetChooser = () => throw new InvalidOperationException("must not be asked");
            Assert.False(view.GetBlankDailySheet());
            Assert.Contains("permission", Status(view));
        });
    }

    [Fact]
    public void Cancelling_either_dialog_saves_nothing_and_says_nothing()
    {
        RunSta(() =>
        {
            var view = CreateView(canImport: true);
            var before = Status(view);
            view.BlankSheetChooser = () => null;
            Assert.False(view.GetBlankDailySheet());

            view.BlankSheetChooser = () => new(BlankDailySheet.Options[0], Day);
            view.BlankSheetSavePath = _ => null;
            Assert.False(view.GetBlankDailySheet());
            Assert.Equal(before, Status(view));
        });
    }

    [Fact]
    public void A_save_failure_shows_a_friendly_message_with_a_reference()
    {
        RunSta(() =>
        {
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBlankSheet_" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var view = CreateView(canImport: true);
                view.BlankSheetChooser = () => new(BlankDailySheet.Options[0], Day);
                // A folder cannot be created as a file.
                view.BlankSheetSavePath = _ => folder;

                Assert.False(view.GetBlankDailySheet());

                var text = Status(view);
                Assert.StartsWith("The blank daily sheet could not be saved.", text);
                Assert.Matches(@"Ref: [0-9A-Za-z]+$", text);
            }
            finally { Directory.Delete(folder, true); }
        });
    }

    [Fact]
    public void The_dialog_defaults_to_the_first_sheet_and_today_and_returns_the_choice()
    {
        RunSta(() =>
        {
            var dialog = new BlankDailySheetDialog(null, Day);
            Assert.Equal(BlankDailySheet.Options[0], dialog.SheetInput.SelectedItem);
            Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), dialog.DateInput.SelectedDate);
            Assert.Equal(Day.AddDays(-31).ToDateTime(TimeOnly.MinValue), dialog.DateInput.DisplayDateStart);
            Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), dialog.DateInput.DisplayDateEnd);
        });
    }

    private static ImportWorkspaceView CreateView(bool canImport)
    {
        var coordinator = new DesktopImportCoordinator(_ => throw new InvalidOperationException("No database in this test."));
        var view = new ImportWorkspaceView(coordinator, () => "synthetic");
        view.AttachHost(() => new(canImport, false), () => Task.FromResult(new ImportWorkspaceAccess(canImport, false)),
            (_, _, _) => Task.CompletedTask, () => Task.CompletedTask);
        return view;
    }

    private static string Status(ImportWorkspaceView view) => ((TextBlock)view.FindName("ValidationResult")).Text;

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "STA test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
