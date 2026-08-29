using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Application.Archive;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.DatabaseLifecycle;
using Etp.Reporting.Application.Distribution;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Application.Registers;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Application.Sharing;
using Etp.Reporting.Application.SourceInbox;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Desktop.Modules.Archive;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.Registers;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Modules.SourceInbox;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ExtractedWorkspaceUiSmokeTests
{
    [Fact]
    public void Extracted_workspaces_render_focus_and_expose_accessible_interactive_controls()
    {
        RunSta(() =>
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "EtpWorkspaceUiSmoke", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            try
            {
                var workspaces = CreateWorkspaces(testRoot);
                Assert.Equal(
                    ["Settings", "Daily", "Archive", "Registers", "Accounting", "Operations", "Investigation", "Administration", "Import", "SourceInbox", "Reports", "Dashboard", "Help"],
                    workspaces.Select(x => x.Name).ToArray());

                foreach (var workspace in workspaces)
                {
                    RenderHeadlessly(workspace.Name, workspace.View);
                    AssertAccessibleInteractiveControls(workspace.Name, workspace.View);
                    ExerciseFocus(workspace.Name, workspace.View);
                    AssertDuplicateHostRejected(workspace.Name, workspace.View);
                }

                foreach (var disposable in workspaces.Select(x => x.View).OfType<IAsyncDisposable>())
                    disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
            }
        });
    }

    [Fact]
    public void Production_composition_constructs_every_extracted_workspace_at_the_boundary()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var constructionBoundary = composition + main;

        foreach (var typeName in new[]
                 {
                     "SettingsWorkspaceView", "DailyWorkflowWorkspaceView", "ArchiveWorkspaceView",
                     "RegistersWorkspaceView", "AccountingWorkspaceView", "OperationsWorkspaceView",
                     "InvestigationApprovalsWorkspaceView", "AdministrationWorkspaceView",
                     "ImportWorkspaceView", "SourceInboxWorkspaceView", "ReportsWorkspaceView"
                 })
            Assert.Contains($"new {typeName}", constructionBoundary, StringComparison.Ordinal);

        Assert.Contains("DashboardView", composition, StringComparison.Ordinal);
        Assert.Contains("ReportPresentationControl", File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Reports", "ReportsWorkspaceView.xaml")), StringComparison.Ordinal);
        var mainWindowSources = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(Path.Combine(root, "src", "Etp.Reporting.Desktop"), "MainWindow*.cs")
                .Select(File.ReadAllText));
        Assert.Contains("HelpCentreView", mainWindowSources, StringComparison.Ordinal);
    }

    private static IReadOnlyList<WorkspaceCase> CreateWorkspaces(string testRoot)
    {
        const string connection = DesktopCompositionRoot.DefaultConnectionString;
        var operationsSession = new OperationsAdministrationPresentationSession();
        var importCoordinator = new DesktopImportCoordinator(
            _ => Proxy<IImportPersistenceUseCase<Etp.Reporting.Import.Preflight.MatchedImportEnvelope>>(),
            (_, _, _, _, _, _, _) => Task.CompletedTask);

        return
        [
            new("Settings", new SettingsWorkspaceView(
                new DesktopSettingsPresentationSession(
                    new DesktopSettingsStore(Path.Combine(testRoot, "settings")),
                    new DesktopConnectionState(connection)),
                _ => Proxy<IDatabaseLifecycleService>(),
                _ => Proxy<IAdministrationService>(),
                Path.Combine(testRoot, "migrations"))),
            new("Daily", new DailyWorkflowWorkspaceView(
                new DailyWorkflowPresentationSession(), () => connection,
                _ => Proxy<IDailyWorkflowQuery>(), _ => Proxy<IDailyWorkflowCommands>(),
                _ => Proxy<IDailyReportPackGenerator<ReportPackDocument>>(),
                () => new(true, true, true), () => true,
                (_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask)),
            new("Archive", new ArchiveWorkspaceView(
                new ArchiveDistributionPresentationSession(
                    _ => Proxy<IReportArchiveQuery<ReportPackDocument>>(),
                    _ => Proxy<ISharingContactsService>(),
                    _ => Proxy<IReportDistributionService<ReportPackDocument>>()),
                () => connection, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, Proxy<IArchiveShareLauncher>())),
            new("Registers", new RegistersWorkspaceView(
                new RegistersPresentationSession(_ => Proxy<IDigitalRegisterService>()), () => connection)),
            new("Accounting", new AccountingWorkspaceView(
                new AccountingPresentationSession(_ => Proxy<IAccountingService>()), () => connection)),
            new("Operations", new OperationsWorkspaceView(
                operationsSession, () => connection, _ => Proxy<IOperationsAdministrationService>(),
                (_, _) => Task.FromResult(new MaintenanceOperationResult(true, "Complete")))),
            new("Investigation", new InvestigationApprovalsWorkspaceView(
                () => connection, _ => Proxy<IOperationsAdministrationService>(), _ => Proxy<IInvestigationQuery>())),
            new("Administration", new AdministrationWorkspaceView(
                operationsSession, () => connection, _ => Proxy<IAdministrationService>())),
            new("Import", new ImportWorkspaceView(importCoordinator, () => connection)),
            new("SourceInbox", new SourceInboxWorkspaceView(
                _ => Proxy<ISourceInboxService>(), () => connection, Proxy<ISourceDocumentLauncher>())),
            new("Reports", new ReportsWorkspaceView(
                () => connection,
                _ => Proxy<IControlledReportQuery>(),
                _ => Proxy<IOperationalReportQuery<DailySalesReportDocument>>(),
                _ => Proxy<IManagementTrendQuery>(),
                new ReportExportCoordinator(),
                Proxy<ITenderVarianceDiagnostic>())),
            new("Dashboard", new DashboardView()),
            new("Help", new HelpCentreView())
        ];
    }

    private static void RenderHeadlessly(string name, FrameworkElement view)
    {
        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1280, 900, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        Assert.True(view.ActualWidth > 0 && view.ActualHeight > 0, $"{name} did not complete headless layout.");
    }

    private static void AssertAccessibleInteractiveControls(string name, DependencyObject view)
    {
        var interactive = Descendants(view).Where(IsInteractive).ToArray();
        Assert.True(interactive.Length > 0,
            $"{name} exposes no interactive controls to the accessibility smoke test.");
        var unnamed = interactive.Where(control => string.IsNullOrWhiteSpace(AccessibleName(control)))
            .Select(control => control.GetType().Name).ToArray();
        Assert.True(unnamed.Length == 0,
            $"{name} has unnamed interactive controls: {string.Join(", ", unnamed)}");
    }

    private static void ExerciseFocus(string name, FrameworkElement view)
    {
        var target = Descendants(view).OfType<Control>()
            .FirstOrDefault(control => control.Focusable && control.IsEnabled) ?? view as Control;
        Assert.NotNull(target);
        target!.Focusable = true;
        var exception = Record.Exception(() =>
        {
            view.Focus();
            target.Focus();
            target.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        });
        Assert.Null(exception);
    }

    private static void AssertDuplicateHostRejected(string name, FrameworkElement view)
    {
        var first = new Grid();
        var second = new Grid();
        first.Children.Add(view);
        var exception = Record.Exception(() => second.Children.Add(view));
        Assert.True(exception is ArgumentException or InvalidOperationException,
            $"{name} unexpectedly attached to two visual hosts.");
        Assert.Contains(view, first.Children.Cast<UIElement>());
        Assert.DoesNotContain(view, second.Children.Cast<UIElement>());
        first.Children.Remove(view);
    }

    private static bool IsInteractive(DependencyObject control)
    {
        if (control is FrameworkElement { TemplatedParent: not null }) return false;
        return control is ButtonBase or TextBoxBase or Selector or DatePicker;
    }

    private static string? AccessibleName(DependencyObject control)
    {
        var explicitName = AutomationProperties.GetName(control);
        if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
        if (control is ContentControl { Content: string content } && !string.IsNullOrWhiteSpace(content)) return content;
        if (control is HeaderedContentControl { Header: string header } && !string.IsNullOrWhiteSpace(header)) return header;
        if (control is FrameworkElement element && element.ToolTip is string tooltip && !string.IsNullOrWhiteSpace(tooltip)) return tooltip;
        return null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (!ReferenceEquals(current, root)) yield return current;
            try
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                    pending.Push(VisualTreeHelper.GetChild(current, index));
            }
            catch (InvalidOperationException)
            {
                // Logical-only objects such as RowDefinition are not Visuals.
            }
            foreach (var logicalChild in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                pending.Push(logicalChild);
        }
    }

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, SafeDefaultProxy>();

    private class SafeDefaultProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType ?? typeof(void);
            if (returnType == typeof(void)) return null;
            if (returnType == typeof(Task)) return Task.CompletedTask;
            if (returnType == typeof(ValueTask)) return ValueTask.CompletedTask;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var value = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [value]);
            }
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
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
        if (failure is not null) throw new InvalidOperationException("STA UI smoke test failed.", failure);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }

    private sealed record WorkspaceCase(string Name, FrameworkElement View);
}
