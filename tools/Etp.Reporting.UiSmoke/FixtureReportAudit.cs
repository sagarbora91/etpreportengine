using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

internal static class FixtureReportAudit
{
    public static void Run(string output,string connection, bool exportsOnly = false)
    {
        if(!connection.Contains("Database=EtpReportingUiSprint_",StringComparison.Ordinal)) throw new InvalidOperationException("Synthetic UI database is required");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window=new DesktopCompositionRoot(AppContext.BaseDirectory,connection,Path.Combine(output,"settings")).CreateMainWindow();
        Program.SetAccess(window,AccessRole.Owner,"Synthetic Owner");
        ((FrameworkElement)window.FindName("WelcomeOverlay")).Visibility=Visibility.Collapsed;
        var workspace=(ReportsWorkspaceView)typeof(MainWindow).GetField("reportsWorkspaceView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var presentation=(ReportPresentationSession)typeof(ReportsWorkspaceView).GetField("presentation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(workspace)!;
        var results=new List<object>();
        var dateFields=new List<object>();
        foreach(var report in ProductReportCatalogue.All)
        foreach(var state in new[]{"populated","missing"})
        {
            var date=state=="populated" ? new DateTime(2026,8,25) : new DateTime(2099,1,1);
            workspace.ApplyScope(date,date,ReportTaskScope.RequiresSingleStore(report.Code) ? "Titan" : "Combined");
            Pump(workspace.RunReportAsync(report.Code));
            ((TaskNavigator)typeof(MainWindow).GetField("taskNavigator",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).RestoreBreadcrumbs();
            var snapshot=presentation.Current;
            if(snapshot.ReportCode!=report.Code || snapshot.ExportMetadata is null || snapshot.ExportData is null)
                throw new InvalidOperationException($"{report.Code}/{state} did not reach its report result: {((System.Windows.Controls.TextBlock)workspace.FindName("ReportResult")).Text}");
            if (exportsOnly)
            {
                var exporter = new ReportExportCoordinator();
                Pump(exporter.ExportReportExcelAsync(Path.Combine(output, $"{report.Code}-{state}.xlsx"), snapshot.ExportMetadata, snapshot.ExportData, snapshot.VisualReport));
                Pump(exporter.ExportReportPdfAsync(Path.Combine(output, $"{report.Code}-{state}.pdf"), snapshot.ExportMetadata, snapshot.ExportData, snapshot.VisualReport, snapshot.DailySalesReport));
            }
            foreach(var size in exportsOnly ? Array.Empty<(int,int)>() : new[]{(1000,600),(800,440)})
            {
                Program.Render(window,Path.Combine(output,$"{report.Code}-{state}-{size.Item1}x{size.Item2}.png"),size.Item1,size.Item2);
                VisualContractAudit.Capture((DependencyObject)window.Content,$"{report.Code}/{state}/{size.Item1}x{size.Item2}");
                if (report.Code == "dsr")
                {
                    var view=(DailySalesReportWorkspace)((System.Windows.Controls.ContentControl)window.FindName("FocusedWorkspaceHost")).Content;
                    var input=(System.Windows.Controls.Primitives.DatePickerTextBox)view.BusinessDatePicker.Template.FindName("PART_TextBox",view.BusinessDatePicker);
                    dateFields.Add(new {state,width=size.Item1,height=size.Item2,selected=view.BusinessDatePicker.SelectedDate,text=view.BusinessDatePicker.Text,input=input.Text,input.IsEnabled,input.Visibility,input.ActualWidth,input.ActualHeight});
                }
            }
            results.Add(new {report.Code,state,date=date.ToString("yyyy-MM-dd"),result="PASS",metadata=snapshot.ExportMetadata,data=snapshot.ExportData,dsr=snapshot.DailySalesReport,method=exportsOnly ? "Live synthetic SQL query into WPF presentation; production export coordinator writes files. Not user interaction." : "Live synthetic SQL query into WPF report view, pumped dispatcher and offscreen capture. Not installed UI or user input."});
        }
        if (!exportsOnly) VisualContractAudit.Write(output);
        File.WriteAllText(Path.Combine(output,"report-state-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"date-field-results.json"),JsonSerializer.Serialize(dateFields,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(exportsOnly ? $"{results.Count} live synthetic report states exported to Excel and PDF." : $"{results.Count} live synthetic report states rendered at two sizes.");
    }
    private static void Pump(Task task)
    {
        if(!task.IsCompleted)
        {
            var dispatcher=Dispatcher.CurrentDispatcher; var frame=new DispatcherFrame();
            task.ContinueWith(_=>dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
