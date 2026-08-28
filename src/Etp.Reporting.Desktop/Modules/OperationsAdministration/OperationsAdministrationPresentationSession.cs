extern alias EtpApplication;

using System.Globalization;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;
using AdministrationDashboard = EtpApplication::Etp.Reporting.Application.OperationsAdministration.AdministrationDashboard;
using ApplicationUser = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ApplicationUser;
using AutomationRun = EtpApplication::Etp.Reporting.Application.OperationsAdministration.AutomationRun;
using ControlledMaster = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ControlledMaster;
using DataQualityFinding = EtpApplication::Etp.Reporting.Application.OperationsAdministration.DataQualityFinding;
using DataQualityIssue = EtpApplication::Etp.Reporting.Application.OperationsAdministration.DataQualityIssue;
using KpiDefinition = EtpApplication::Etp.Reporting.Application.OperationsAdministration.KpiDefinition;
using ManagementTrendPoint = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ManagementTrendPoint;
using OperationsDashboard = EtpApplication::Etp.Reporting.Application.OperationsAdministration.OperationsDashboard;
using OperationsPeriod = EtpApplication::Etp.Reporting.Application.OperationsAdministration.OperationsPeriod;
using ProductConfiguration = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ProductConfiguration;
using ProductHealth = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ProductHealth;
using ReportSchedule = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ReportSchedule;
using SaveApplicationUser = EtpApplication::Etp.Reporting.Application.OperationsAdministration.SaveApplicationUser;
using SaveControlledMaster = EtpApplication::Etp.Reporting.Application.OperationsAdministration.SaveControlledMaster;
using SaveProductConfiguration = EtpApplication::Etp.Reporting.Application.OperationsAdministration.SaveProductConfiguration;
using SaveReportSchedule = EtpApplication::Etp.Reporting.Application.OperationsAdministration.SaveReportSchedule;
using SaveWatchFolderConfiguration = EtpApplication::Etp.Reporting.Application.OperationsAdministration.SaveWatchFolderConfiguration;
using WatchFolderConfiguration = EtpApplication::Etp.Reporting.Application.OperationsAdministration.WatchFolderConfiguration;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public sealed record OperationsPresentationState(
    WatchFolderConfiguration WatchFolders,
    IReadOnlyList<ManagementTrendPoint> Trend,
    IReadOnlyList<DataQualityFinding> Quality,
    IReadOnlyList<DataQualityIssue> Issues,
    IReadOnlyList<ReportSchedule> Schedules,
    IReadOnlyList<AutomationRun> AutomationRuns,
    string Status);

public sealed record ScheduleEditorState(
    int Id,
    string Time,
    bool IsEnabled,
    bool ExportExcel,
    bool ExportPdf);

public sealed record AdministrationUserPresentation(
    int Id,
    string WindowsIdentity,
    string DisplayName,
    string RoleCode,
    bool IsActive,
    DateTime ModifiedUtc,
    string ModifiedBy);

public sealed record ProductSettingsPresentation(
    string DocumentRepositoryPath,
    string ShareFolderPath,
    string OcrHelperPath,
    string OcrModelPath,
    string SmtpHost,
    string SmtpPort,
    string SmtpFromAddress,
    string MaximumAttachmentMb);

public sealed record AdministrationPresentationState(
    IReadOnlyList<ControlledMaster> Masters,
    IReadOnlyList<AdministrationUserPresentation> Users,
    IReadOnlyList<KpiDefinition> Kpis,
    IReadOnlyList<ProductHealth> ProductHealth,
    ProductSettingsPresentation ProductSettings,
    string Status);

public sealed class OperationsAdministrationPresentationSession
{
    private ReportSchedule? selectedSchedule;

    public OperationsPresentationState? Operations { get; private set; }
    public AdministrationPresentationState? Administration { get; private set; }
    public ScheduleEditorState? SelectedSchedule { get; private set; }

    public OperationsPeriod CreatePeriod(DateTime? from, DateTime? to)
    {
        if (from is null || to is null)
            throw new InvalidOperationException("Select the management trend dates.");
        return new(DateOnly.FromDateTime(from.Value), DateOnly.FromDateTime(to.Value));
    }

    public OperationsPresentationState Capture(OperationsDashboard dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        Operations = new(
            dashboard.WatchFolders,
            dashboard.Trend,
            dashboard.Quality,
            dashboard.Issues,
            dashboard.Schedules,
            dashboard.AutomationRuns,
            $"Loaded {dashboard.Trend.Count:N0} daily store result(s), {dashboard.Issues.Count:N0} governed quality issue(s), and {dashboard.AutomationRuns.Count:N0} recent unattended run(s).");
        return Operations;
    }

    public ScheduleEditorState? SelectSchedule(ReportSchedule? schedule)
    {
        selectedSchedule = schedule;
        SelectedSchedule = schedule is null
            ? null
            : new(schedule.Id, schedule.LocalRunTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                schedule.IsEnabled, schedule.ExportExcel, schedule.ExportPdf);
        return SelectedSchedule;
    }

    public SaveReportSchedule CreateScheduleCommand(
        string? timeText,
        bool isEnabled,
        bool exportExcel,
        bool exportPdf,
        string reason)
    {
        if (selectedSchedule is null)
            throw new InvalidOperationException("Select the morning or evening schedule first.");
        if (!TimeOnly.TryParseExact(timeText?.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
            throw new InvalidOperationException("Enter schedule time in 24-hour HH:mm format.");
        return new(selectedSchedule.Id, time, isEnabled, exportExcel, exportPdf, reason);
    }

    public static SaveWatchFolderConfiguration CreateWatchFolderCommand(
        string inboundPath,
        string processedPath,
        string failedPath,
        string reportOutputPath,
        bool isEnabled,
        string reason) =>
        new(inboundPath, processedPath, failedPath, reportOutputPath, 5, isEnabled, reason);

    public AdministrationPresentationState Capture(AdministrationDashboard dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        Administration = new(
            dashboard.Masters,
            dashboard.Users.Select(PresentUser).ToArray(),
            dashboard.Kpis,
            dashboard.ProductHealth,
            PresentProductSettings(dashboard.ProductConfiguration),
            "Controlled masters and Windows-integrated access are ready for Owner administration.");
        return Administration;
    }

    public static SaveControlledMaster CreateMasterCommand(
        string masterType,
        string code,
        string displayName,
        string approvalStatus,
        bool isActive,
        string reason) =>
        new(masterType, code, displayName, approvalStatus, isActive, reason);

    public static SaveApplicationUser CreateUserCommand(
        string windowsIdentity,
        string displayName,
        string roleLabel,
        bool isActive,
        string reason) =>
        new(windowsIdentity, displayName, ParseRole(roleLabel), isActive, reason);

    public static SaveProductConfiguration CreateProductConfiguration(
        string documentRepositoryPath,
        string shareFolderPath,
        string ocrHelperPath,
        string ocrModelPath,
        string smtpHost,
        string smtpPortText,
        string smtpFromAddress,
        string maximumAttachmentText,
        string reason)
    {
        if (!int.TryParse(maximumAttachmentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximum))
            throw new InvalidOperationException("Enter a valid maximum attachment size in MB.");

        int? port = null;
        if (!string.IsNullOrWhiteSpace(smtpPortText))
        {
            if (!int.TryParse(smtpPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
                throw new InvalidOperationException("Enter a valid SMTP port.");
            port = parsedPort;
        }

        return new(documentRepositoryPath, shareFolderPath, ocrHelperPath, ocrModelPath,
            smtpHost, port, true, smtpFromAddress, maximum, reason);
    }

    private static AdministrationUserPresentation PresentUser(ApplicationUser user) =>
        new(user.Id, user.WindowsIdentity, user.DisplayName, RoleCode(user.Role), user.IsActive,
            user.ModifiedUtc, user.ModifiedBy);

    private static ProductSettingsPresentation PresentProductSettings(ProductConfiguration settings) =>
        new(settings.DocumentRepositoryPath, settings.ShareFolderPath,
            settings.OcrHelperPath ?? string.Empty, settings.OcrModelPath ?? string.Empty,
            settings.SmtpHost ?? string.Empty,
            settings.SmtpPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            settings.SmtpFromAddress ?? string.Empty,
            settings.MaximumAttachmentMb.ToString(CultureInfo.InvariantCulture));

    private static AccessRole ParseRole(string roleLabel) => roleLabel switch
    {
        "Owner" => AccessRole.Owner,
        "Store Manager" => AccessRole.StoreManager,
        "Viewer" => AccessRole.Viewer,
        _ => AccessRole.None
    };

    private static string RoleCode(AccessRole role) => role switch
    {
        AccessRole.Owner => "OWNER",
        AccessRole.StoreManager => "STORE_MANAGER",
        AccessRole.Viewer => "VIEWER",
        _ => "NONE"
    };
}
