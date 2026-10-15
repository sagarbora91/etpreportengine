extern alias EtpApplication;
using System.IO;

using System.Text.RegularExpressions;
using EtpApplication::Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>
/// 1.12 wave 2, lane U4: the four Collections screens (task ids frozen in the wave-2 plan, section 4) and the words and
/// role rules they share. The screens read only through the four Application interfaces of lane D2 and never see a
/// connection; the composition root (DesktopCompositionRoot.Collections.cs) builds the services.
/// </summary>
public static class CollectionsScreens
{
    public const string SetupTask = "collections-setup";
    public const string BankFilesTask = "bank-files";
    public const string StatementTask = "collections-statement";
    public const string DepositsTask = "deposits-in-transit";

    public static IReadOnlyList<string> TaskIds { get; } = [SetupTask, BankFilesTask, StatementTask, DepositsTask];

    public const string SetupOwnerOnlyText = "Collections setup is for the Owner. Ask the Owner to set up bank accounts, providers and file layouts.";
    public const string BankFilesRefusedText = "Importing bank and settlement files is for the Owner or a Store Manager.";
    public const string DepositsRefusedText = "Deposits in transit are for the Owner or a Store Manager.";
    public const string NotSignedInText = "Your Windows account has no ETP role. Ask the Owner to add you in Settings > Users.";
    public const string MissingValue = "—";
    public const string ProviderNotSetUpChip = "Provider not set up";

    public static bool CanSetUp(ShellAccess access) => access is { HasAssignedRole: true, CanAdminister: true };
    public static bool CanImportFiles(ShellAccess access) => access is { HasAssignedRole: true, CanImport: true };
    public static bool CanSeeDeposits(ShellAccess access) => CanImportFiles(access);
    /// <summary>Accepting a proposal, recording a credit by hand and withdrawing one are Owner-only (ICashDepositService).</summary>
    public static bool CanConfirmDeposits(ShellAccess access) => CanSetUp(access);
    public static bool CanReadStatement(ShellAccess access) => access is { HasAssignedRole: true, CanView: true };

    /// <summary>A bank statement's file name can carry a whole account number. Every run of five or more digits keeps its
    /// last four ("XXXXXXXXXX5678") on screen, as the import list and the database do.</summary>
    public static string MaskDigits(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Regex.Replace(text, "[0-9]{5,}", match => new string('X', match.Length - 4) + match.Value[^4..],
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>A refusal or failure of a service call as the one plain sentence a person reads.</summary>
    public static string Sentence(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            UnauthorizedAccessException refusal => refusal.Message,
            FileNotFoundException => "The selected file no longer exists. Choose it again.",
            IOException io => io.Message,
            ArgumentException or InvalidOperationException => exception.Message,
            _ => "ETP could not finish this. Press Refresh and try again; if it happens again, tell the Owner."
        };
    }
}

/// <summary>The services of the four screens. A factory runs on every visit, so a connection changed in Settings is the one used.</summary>
public sealed record CollectionsServices(
    Func<ICollectionsSetupService> Setup,
    Func<ICollectionsImportService> Import,
    Func<ICashDepositService> Deposits,
    Func<ICollectionsStatementQuery> Statement,
    CollectionsExcelExport? Export = null);

/// <summary>The questions and file choices the screens ask of a person. The default shows Windows dialogs; tests inject answers.</summary>
public interface ICollectionsDialogs
{
    /// <summary>One or more statement files (.csv or .xlsx). Empty when cancelled.</summary>
    IReadOnlyList<string> ChooseStatementFiles();

    /// <summary>One sample file for a layout. Null when cancelled.</summary>
    string? ChooseSampleFile();

    /// <summary>A reason a person types (a duplicate choice, a bank credit), with an optional suggestion filled in. Null when cancelled.</summary>
    string? AskReason(string title, string prompt, string? suggestion = null);

    /// <summary>A yes to a change that is hard to undo. False when cancelled.</summary>
    bool Confirm(string action, string detail);

    /// <summary>1.12.1 P2: "Record by hand" - the credited date, the account, the amount, the bank reference and the reason,
    /// starting from <paramref name="suggestion"/>. Null when cancelled. The model checks what comes back.</summary>
    ManualCreditEntry? AskManualCredit(string title, string prompt, ManualCreditEntry suggestion, IReadOnlyList<DepositAccount> accounts);
}

/// <summary>1.12.1 P2: writes the statement to an Excel workbook. Production uses the report exporter (ReportExportCoordinator).</summary>
public delegate Task CollectionsExcelExport(string path, Etp.Reporting.Reporting.ExcelReportMetadata metadata, Etp.Reporting.Reporting.ExcelReportData data);
