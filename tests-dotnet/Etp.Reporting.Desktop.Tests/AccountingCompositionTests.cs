namespace Etp.Reporting.Desktop.Tests;

public sealed class AccountingCompositionTests
{
    [Fact]
    public void Accounting_handlers_use_the_injected_application_service()
    {
        var root = FindRepositoryRoot();
        var mainWindow = ReadMainWindowCode(root);
        var workspace = ReadSource(root, "Modules", "Accounting", "AccountingWorkspaceView.xaml.cs");
        var composition = ReadSource(root, "Composition", "DesktopCompositionRoot.cs");

        Assert.Contains("session.RefreshAsync(connectionStringProvider())", workspace, StringComparison.Ordinal);
        Assert.Contains("session.PreviewAsync(connectionStringProvider()", workspace, StringComparison.Ordinal);
        Assert.Contains("session.SaveCurrentAsync(connectionStringProvider(), CurrentScope())", workspace, StringComparison.Ordinal);
        Assert.Contains("session.ApproveAsync(connectionStringProvider()", workspace, StringComparison.Ordinal);
        Assert.Contains("session.ExportAsync(connectionStringProvider()", workspace, StringComparison.Ordinal);
        Assert.Contains("session.ApproveMappingAsync(connectionStringProvider()", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("currentAccountingDraft", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("currentAccountingReportGenerationId", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountingBatchGrid", mainWindow, StringComparison.Ordinal);
        Assert.Contains("AccountingWorkspaceView accountingWorkspaceView", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<string, AccountingService>", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new SqlServerAccountingService(value)", composition, StringComparison.Ordinal);
        Assert.Contains("new AccountingPresentationSession(accountingServiceFactory)", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void Accounting_handlers_do_not_construct_infrastructure_workflow_components()
    {
        var accountingHandlers = ReadSource(FindRepositoryRoot(), "Modules", "Accounting", "AccountingWorkspaceView.xaml.cs");
        Assert.DoesNotContain("new ProductisationRepository", accountingHandlers, StringComparison.Ordinal);
        Assert.DoesNotContain("new AccountingBatchComposer", accountingHandlers, StringComparison.Ordinal);
        Assert.DoesNotContain("new TallyXmlExportService", accountingHandlers, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountingBatchRow", accountingHandlers, StringComparison.Ordinal);
    }

    [Fact]
    public void Accounting_handlers_retain_ui_authorization_and_operator_guidance()
    {
        var accountingHandlers = ReadSource(FindRepositoryRoot(), "Modules", "Accounting", "AccountingWorkspaceView.xaml.cs");
        var accountingSession = ReadSource(FindRepositoryRoot(), "Modules", "Accounting", "AccountingPresentationSession.cs");

        Assert.Contains("RequireViewAccess();", accountingHandlers, StringComparison.Ordinal);
        Assert.Contains("RequireImportAccess();", accountingHandlers, StringComparison.Ordinal);
        Assert.Contains("RequireOwnerAccess();", accountingHandlers, StringComparison.Ordinal);
        Assert.Contains("Preview a balanced accounting batch first.", accountingSession, StringComparison.Ordinal);
        Assert.Contains("Approve the accounting batch before exporting it.", accountingSession, StringComparison.Ordinal);
        Assert.Contains("Tally XML (*.xml)|*.xml", accountingHandlers, StringComparison.Ordinal);
        Assert.Contains("Saagar Traders", accountingHandlers, StringComparison.Ordinal);
        Assert.Contains("Approved Tally XML exported with SHA-256", accountingHandlers, StringComparison.Ordinal);
    }

    private static string ReadSource(string root, params string[] path) =>
        File.ReadAllText(Path.Combine([root, "src", "Etp.Reporting.Desktop", .. path]));

    private static string ReadMainWindowCode(string root)
    {
        var desktopRoot = Path.Combine(root, "src", "Etp.Reporting.Desktop");
        return string.Join(Environment.NewLine,
            Directory.GetFiles(desktopRoot, "MainWindow*.cs").Select(File.ReadAllText));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
