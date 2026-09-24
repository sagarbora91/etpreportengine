using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Desktop.Modules.Archive;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.Registers;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Modules.SourceInbox;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class Phase3ShellTests
{
    [Fact]
    public void Blank_walkins_are_rejected_inline_and_revisited_editor_tracks_header()
    {
        Sta(() =>
        {
            var window=CreateWindow();
            typeof(MainWindow).GetMethod("CompleteWelcomeState",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);
            Assert.Equal(Visibility.Collapsed,window.WelcomeOverlay.Visibility);
            Assert.IsType<DailySalesReportWorkspace>(window.FocusedWorkspaceHost.Content);
            window.OpenSection("Today","Walk-ins");
            window.ShellStoreSelector.SelectedIndex=0;
            var next=new DateTime(2026,8,26);
            window.ShellBusinessDateSelector.SelectedDate=next;
            Assert.Equal(next,window.dailyWorkflowWorkspace.BusinessDate);
            Assert.Equal("WLMHW",window.dailyWorkflowWorkspace.StoreCode);
            window.OpenSection("Import"); window.OpenSection("Today","Walk-ins");
            window.ShellBusinessDateSelector.SelectedDate=next.AddDays(1);
            Assert.Equal(next.AddDays(1),window.dailyWorkflowWorkspace.BusinessDate);
            window.dailyWorkflowWorkspace.SaveManualInputAsync().GetAwaiter().GetResult();
            Assert.Equal("Enter the walk-in count",window.dailyWorkflowWorkspace.StatusText);
            Assert.DoesNotContain("Parameter",window.dailyWorkflowWorkspace.StatusText);
            window.dailyWorkflowWorkspace.PrepareTouchTask("cash-input");
            Assert.Equal(Visibility.Visible,((ComboBox)window.dailyWorkflowWorkspace.FindName("ManualFieldInput")).Visibility);
            window.dailyWorkflowWorkspace.SaveManualInputAsync().GetAwaiter().GetResult();
            Assert.Equal("Enter the amount",window.dailyWorkflowWorkspace.StatusText);
            window.dailyWorkflowWorkspace.PrepareTouchTask("walk-ins");
            Assert.Equal(Visibility.Collapsed,((ComboBox)window.dailyWorkflowWorkspace.FindName("ManualFieldInput")).Visibility);
            window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void Status_changes_raise_the_success_toast_only_for_successful_outcomes()
    {
        Sta(() =>
        {
            var window=CreateWindow();
            window.SuccessToast.Visibility=Visibility.Collapsed;
            window.ApplicationStatus.Text="Import failed: could not save R022.";
            Assert.Equal(Visibility.Collapsed,window.SuccessToast.Visibility);
            window.ApplicationStatus.Text="Settings saved.";
            Assert.Equal(Visibility.Visible,window.SuccessToast.Visibility);
            Assert.Equal("Settings saved.",window.ToastMessage.Text);
            window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Theory]
    [InlineData(1366,728,1)]
    [InlineData(816,440,1)]
    [InlineData(1093,582,1.25)]
    public void Real_shell_screens_render_and_expose_touch_sized_controls(int width,int height,double scale)
    {
        Sta(() =>
        {
            var window=CreateWindow();
            window.WindowState=WindowState.Normal;window.Width=width;window.Height=height;
            window.ShellBusinessDateSelector.SelectedDate=new(2026,8,25);
            var root=(FrameworkElement)window.Content;
            var screenshots=Output();
            foreach(var screen in new[]{"Sales","Walk-ins","Import","Reports","Stock","Settings"})
            {
                if(screen is "Sales" or "Walk-ins") window.OpenSection("Today",screen);
                else window.OpenSection(screen);
                if(screen=="Sales")
                {
                    var workspace=Assert.IsType<DailySalesReportWorkspace>(window.FocusedWorkspaceHost.Content);
                    workspace.SetReport(Document());
                }
                if(screen=="Stock")
                {
                    var workspace=Assert.IsType<ReportWorkspaceControl>(window.FocusedWorkspaceHost.Content);
                    var rows=Enumerable.Range(1,6).Select(i=>new { StoreCode="WLMHW",Sku=$"WATCH-{i:000}",Brand="TITAN",ClosingQuantity=12,ClosingValue=45678.90m }).ToArray();
                    var model=new VisualReportModel(new("stock-closing","Closing stock",new(2026,8,25),new(2026,8,25),"synthetic",DateTimeOffset.UtcNow),
                        [new("Closing units",72,"number")],[],new([],[]),[],[]);
                    workspace.SetPreview(ReportVisualPresenter.BuildFocusedPreview(model,rows),"Synthetic layout fixture: six stock rows.");
                }
                if(screen=="Import")
                {
                    var rows=new[]{new Etp.Reporting.Application.Imports.FolderImportFileResult("synthetic-sales.xlsx","sales","WLMHW",new(2026,8,25),new(2026,8,25),"Imported",25,25)};
                    typeof(ImportWorkspaceView).GetField("latestResults",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window.importWorkspaceView,rows);
                    window.importWorkspaceView.SelectTask("import-files");
                }
                window.ApplicationStatus.Text="Synthetic layout fixture — no live database.";
                root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
                // Finish responsive reflow after SizeChanged updates columns.
                root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
                var image=new RenderTargetBitmap((int)(width*scale),(int)(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(root);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                using(var stream=File.Create(Path.Combine(screenshots,$"{screen}-{width}x{height}-{scale}.png")))encoder.Save(stream);
                var controls=Visuals(root).OfType<Control>().Where(c=>Rendered(c,root) && c is Button or ToggleButton or TextBox or ComboBox or TabItem or DataGridRow).ToArray();
                Assert.NotEmpty(controls);
                foreach(var control in controls)
                    Assert.True(control.ActualHeight>=43.5,$"{screen}: {control.GetType().Name} {control.Name} {control.ActualHeight}");
                if(width==1366)
                {
                    var scrolls=Visuals(window.FocusedWorkspaceHost).OfType<ScrollViewer>().Where(v=>Rendered(v,root) && v.VerticalScrollBarVisibility==ScrollBarVisibility.Auto).ToArray();
                    foreach(var scroll in scrolls) Assert.True(scroll.ScrollableHeight<1,$"{screen} scrolls by {scroll.ScrollableHeight} DIP");
                }
            }
            Assert.Equal(TaskNavigation.Sections,window.RailPanel.Children.OfType<Button>().Where(b=>b.Tag is string).Select(b=>(string)b.Tag));
            window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void Date_change_disables_matrix_and_prevents_stale_document_reopening()
    {
        Sta(()=>
        {
            var workspace=new DailySalesReportWorkspace(); workspace.SetReport(Document());
            workspace.Measure(new Size(1200,650));workspace.Arrange(new Rect(0,0,1200,650));workspace.UpdateLayout();
            var matrix=Visuals(workspace).OfType<Button>().Single(b=>b.Content?.ToString()=="Full matrix");
            Assert.True(matrix.IsEnabled);
            workspace.BusinessDatePicker.SelectedDate=new DateTime(2026,8,26);
            Assert.False(workspace.HasCurrentPreview);Assert.False(matrix.IsEnabled);
            matrix.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(Visuals(workspace).OfType<TodaySalesView>());
            Assert.Empty(Visuals(workspace).OfType<EveningDsrView>());
        });
    }

    [Fact]
    public async Task Cancelled_export_keeps_destination_and_removes_staged_file()
    {
        var directory=Path.Combine(Path.GetTempPath(),"EtpExportCancel",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"report.pdf");await File.WriteAllTextAsync(path,"accepted report");
        using var cancellation=new CancellationTokenSource();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>ExportStaging.WriteAsync(path,async temporary=>
            { await File.WriteAllTextAsync(temporary,"partial new report"); cancellation.Cancel(); },cancellation.Token));
            Assert.Equal("accepted report",await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public void Table_columns_format_dates_amounts_and_identifiers_without_losing_rows()
    {
        Sta(()=>
        {
            var rows=new[]{new { BusinessDate=new DateOnly(2026,8,25),NetValue=1234567.125m,Invoice="00123" }};
            var grid=new DataGrid { ItemsSource=rows,AutoGenerateColumns=true };
            TablePresentation.Configure(grid);
            Assert.False(grid.AutoGenerateColumns);Assert.Equal(3,grid.Columns.Count);Assert.Same(rows,grid.ItemsSource);
            var amount=Assert.IsType<DataGridTextColumn>(grid.Columns[1]);
            var binding=Assert.IsType<System.Windows.Data.Binding>(amount.Binding);
            Assert.Equal("12,34,567.13",binding.Converter.Convert(rows[0].NetValue,typeof(string),null!,PresentationCulture.Indian));
            var date=Assert.IsType<System.Windows.Data.Binding>(((DataGridTextColumn)grid.Columns[0]).Binding);
            Assert.Equal("25 Aug 2026",date.Converter.Convert(rows[0].BusinessDate,typeof(string),null!,PresentationCulture.Indian));
        });
    }

    internal static MainWindow CreateWindow()
    {
        var cases=ExtractedWorkspaceUiSmokeTests.CreateWorkspaces(Path.Combine(Path.GetTempPath(),"EtpPhase3Ui",Guid.NewGuid().ToString("N")));
        T View<T>(string name)=>(T)(object)cases.Single(c=>c.Name==name).View;
        var window=new MainWindow(new ShellViewModel(new ShellNavigationService()),View<DashboardView>("Dashboard"),
            _=>throw new InvalidOperationException("Synthetic dashboard unavailable"),View<SettingsWorkspaceView>("Settings"),new DesktopConnectionState("Server=(local);Database=EtpPhase3Synthetic;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=1"),
            _=>throw new InvalidOperationException("Synthetic access"),View<ArchiveWorkspaceView>("Archive"),View<RegistersWorkspaceView>("Registers"),
            View<DailyWorkflowWorkspaceView>("Daily"),View<SourceInboxWorkspaceView>("SourceInbox"),View<ReportsWorkspaceView>("Reports"),
            View<AccountingWorkspaceView>("Accounting"),View<OperationsWorkspaceView>("Operations"),View<InvestigationApprovalsWorkspaceView>("Investigation"),
            View<AdministrationWorkspaceView>("Administration"),_=>throw new InvalidOperationException("Synthetic audit"),View<ImportWorkspaceView>("Import"));
        typeof(MainWindow).GetField("currentAccess",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,new AccessSession("synthetic","Synthetic owner",AccessRole.Owner,true));
        window.WelcomeOverlay.Visibility=Visibility.Collapsed;
        window.ApplyDensity(UiDensity.Touch,false);
        return window;
    }
    private static DailySalesReportDocument Document()
    {
        var rows=new[]{"VOL","VALUE","AUPT","AVPT","EDGE","RAGA","XYLYS","AUTOMATIC","TITAN","Other / unmapped","RETAIL WALKIN","INVOICE","CONVERSION %","WCC WALKIN","WCC SALES","WDC BILLS"}
            .Select((m,i)=>new EveningMetricRow(m,i==1?34215:15,20000,71.075m,938197,2143453.75m,1200000,i is 0 or 10 or 11 or 13 or 15 ? "number" : "currency")).ToArray();
        return DailySalesReportBuilder.Build(new(2026,8,25),[],[],new Dictionary<string,decimal?>()) with { EveningSheets=[
            new("WLMHW","Titan World",1600000,51612.90m,661803,94543.29m,rows),new("HEMW","Helios",1200000,38709.68m,425131.4m,60733.06m,rows),new("COMBINED","Both stores",2800000,90322.58m,1086934.4m,155276.35m,rows)] };
    }
    private static string Output()
    {
        var path=Environment.GetEnvironmentVariable("ETP_PHASE3_UI_EVIDENCE") ?? Path.Combine(Path.GetTempPath(),"EtpPhase3Review");
        Directory.CreateDirectory(path);return path;
    }
    private static bool Rendered(FrameworkElement element,DependencyObject root)
    {
        for(DependencyObject? current=element;current is not null;current=VisualTreeHelper.GetParent(current))
        { if(current is UIElement ui && ui.Visibility!=Visibility.Visible)return false; if(ReferenceEquals(current,root))return true; }
        return false;
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var next in Visuals(child))yield return next; }
    }
    private static void Sta(Action action)
    {
        Exception? error=null;var thread=new Thread(()=>{try{System.Globalization.CultureInfo.CurrentCulture=PresentationCulture.Indian;action();}catch(Exception ex){error=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        if(error is not null)throw new InvalidOperationException("Phase 3 UI verification failed",error);
    }
}
