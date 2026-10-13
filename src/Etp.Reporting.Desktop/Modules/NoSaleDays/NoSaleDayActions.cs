extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using INoSaleDayService = EtpApplication::Etp.Reporting.Application.NoSaleDays.INoSaleDayService;
using NoSaleDay = EtpApplication::Etp.Reporting.Application.NoSaleDays.NoSaleDay;
using NoSaleDayRefusedException = EtpApplication::Etp.Reporting.Application.NoSaleDays.NoSaleDayRefusedException;

namespace Etp.Reporting.Desktop.Modules.NoSaleDays;

/// <summary>
/// 1.12 lane N1 (decision 52; frozen contract 112-W2-PLAN section 4): the "Mark as no-sale day" and "Withdraw no-sale day"
/// buttons, for the screens that place them (U1: Today checklist and Morning page; U2: Close day; N1: Today sales). Nobody
/// else builds the dialog. Owner and Store Manager get a button; a Viewer (and a start with no role) gets null, so the
/// caller shows nothing. The SQL procedures check the role again.
/// <para><see cref="Current"/> is set once by <c>DesktopCompositionRoot.NoSaleDays.cs</c>; the shell exposes it as
/// <c>MainWindow.NoSaleDayButtons</c> (U1; wired in <c>DesktopCompositionRoot.NoSaleDays.cs</c>).</para>
/// </summary>
public sealed class NoSaleDayActions
{
    public const string MarkLabel = "Mark as no-sale day";
    public const string WithdrawLabel = "Withdraw no-sale day";

    private readonly Func<INoSaleDayService> service;
    private readonly Func<ShellAccess> access;
    private readonly Func<string, string?> shopName;
    private readonly Func<NoSaleDayDialog, FrameworkElement?, bool> showDialog;

    /// <param name="service">The service of the window's current connection.</param>
    /// <param name="access">The signed-in role.</param>
    /// <param name="shopName">The shop's name for a store code (null: the code is shown).</param>
    /// <param name="showDialog">Shows the dialog modally (tests replace it); true when the change was saved.</param>
    public NoSaleDayActions(
        Func<INoSaleDayService> service,
        Func<ShellAccess> access,
        Func<string, string?>? shopName = null,
        Func<NoSaleDayDialog, FrameworkElement?, bool>? showDialog = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.shopName = shopName ?? (_ => null);
        this.showDialog = showDialog ?? ShowModal;
    }

    /// <summary>The actions of the running window, or null before the composition root set them (tests, setup start).</summary>
    public static NoSaleDayActions? Current { get; set; }

    /// <summary>Owner and Store Manager may mark and withdraw (decision 52); a Viewer may not.</summary>
    public bool CanChange
    {
        get
        {
            var role = access();
            return role.HasAssignedRole && role.CanImport;
        }
    }

    /// <summary>The marks of the date (every store), for a checklist line or a card.</summary>
    public Task<IReadOnlyList<NoSaleDay>> LoadAsync(DateOnly businessDate, CancellationToken ct = default) =>
        service().LoadAsync(businessDate, ct);

    /// <summary>"Mark as no-sale day" for the store-day, or null for a role that may not mark. <paramref name="onDone"/> runs after a saved mark.</summary>
    public Button? CreateMarkButton(string storeCode, DateOnly businessDate, Action? onDone = null) =>
        Create(NoSaleDayDialogMode.Mark, storeCode, businessDate, onDone);

    /// <summary>"Withdraw no-sale day" for the store-day, or null for a role that may not withdraw.</summary>
    public Button? CreateWithdrawButton(string storeCode, DateOnly businessDate, Action? onDone = null) =>
        Create(NoSaleDayDialogMode.Withdraw, storeCode, businessDate, onDone);

    private Button? Create(NoSaleDayDialogMode mode, string storeCode, DateOnly businessDate, Action? onDone)
    {
        if (string.IsNullOrWhiteSpace(storeCode) || !CanChange) return null;
        var store = storeCode.Trim();
        var label = mode == NoSaleDayDialogMode.Mark ? MarkLabel : WithdrawLabel;
        var button = new Button { Content = label };
        AutomationProperties.SetName(button, $"{label} for {Shop(store)}");
        button.Click += (_, _) =>
        {
            if (Open(mode, store, businessDate, button)) onDone?.Invoke();
        };
        return button;
    }

    /// <summary>Opens the dialog for the store-day; true when the change was saved.</summary>
    internal bool Open(NoSaleDayDialogMode mode, string storeCode, DateOnly businessDate, FrameworkElement? source)
    {
        var dialog = new NoSaleDayDialog(mode, Shop(storeCode), businessDate,
            (reasonCode, reason) => SubmitAsync(mode, storeCode, businessDate, reasonCode, reason));
        return showDialog(dialog, source);
    }

    /// <summary>Calls the service; null when saved, else the sentence the dialog shows.</summary>
    internal async Task<string?> SubmitAsync(NoSaleDayDialogMode mode, string storeCode, DateOnly businessDate, string? reasonCode, string reason)
    {
        try
        {
            if (mode == NoSaleDayDialogMode.Mark)
                await service().MarkAsync(storeCode, businessDate, reasonCode ?? string.Empty, reason);
            else
                await service().WithdrawAsync(storeCode, businessDate, reason);
            return null;
        }
        catch (NoSaleDayRefusedException refusal) { return refusal.Message; }
        catch (UnauthorizedAccessException denied) { return denied.Message; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DesktopDiagnostics.Record(exception, "NoSaleDay", mode == NoSaleDayDialogMode.Mark ? "NO_SALE_MARK_FAILED" : "NO_SALE_WITHDRAW_FAILED", DesktopDiagnosticSeverity.Error);
            return "ETP could not save this change. Try again; if it happens again, open the Error register.";
        }
    }

    private string Shop(string storeCode) => shopName(storeCode) is { Length: > 0 } name ? name : storeCode;

    private static bool ShowModal(NoSaleDayDialog dialog, FrameworkElement? source)
    {
        var owner = source is null ? Application.Current?.MainWindow : Window.GetWindow(source);
        if (owner?.IsVisible == true) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
