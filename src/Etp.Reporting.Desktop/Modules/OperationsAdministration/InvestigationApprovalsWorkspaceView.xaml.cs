extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using InvestigationHit = EtpApplication::Etp.Reporting.Application.Distribution.InvestigationHit;
using ApprovalRequest = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ApprovalRequest;
using DecideApproval = EtpApplication::Etp.Reporting.Application.OperationsAdministration.DecideApproval;
using IInvestigationQuery = EtpApplication::Etp.Reporting.Application.Distribution.IInvestigationQuery;
using IOperationsAdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IOperationsAdministrationService;
using SubmitAdjustment = EtpApplication::Etp.Reporting.Application.OperationsAdministration.SubmitAdjustment;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public partial class InvestigationApprovalsWorkspaceView : UserControl
{
    private readonly Func<string> connectionStringProvider;
    private readonly Func<string, IOperationsAdministrationService> operationsServiceFactory;
    private readonly Func<string, IInvestigationQuery> investigationQueryFactory;
    private OperationsAdministrationWorkspaceAccess access = new(false, false, false);
    private readonly WorkspaceOperationGate operationGate = new();
    private readonly SelectionReasonDrafts approvalReasons;
    public bool IsBusy => operationGate.IsBusy;
    public bool HasRetainedDraft => approvalReasons.HasDrafts || new[] { AdjustmentTypeInput, AdjustmentAmountInput, AdjustmentReasonInput }.Any(input => input.Text.Length > 0);
    public void DiscardRetainedDraft() { approvalReasons.Discard(); AdjustmentTypeInput.Clear(); AdjustmentAmountInput.Clear(); AdjustmentReasonInput.Clear(); }

    public InvestigationApprovalsWorkspaceView(
        Func<string> connectionStringProvider,
        Func<string, IOperationsAdministrationService> operationsServiceFactory,
        Func<string, IInvestigationQuery> investigationQueryFactory)
    {
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        this.operationsServiceFactory = operationsServiceFactory ?? throw new ArgumentNullException(nameof(operationsServiceFactory));
        this.investigationQueryFactory = investigationQueryFactory ?? throw new ArgumentNullException(nameof(investigationQueryFactory));
        InitializeComponent();
        approvalReasons = new(ApprovalGrid, ApprovalReasonInput, row => (row as ApprovalRequest)?.Id);
        AdjustmentDateInput.SelectedDate = DateTime.Today.AddDays(-1);
    }

    public long? LinkedSourceDocumentId { get; set; }
    public string StatusText => InvestigationStatus.Text;
    public int ApprovalRowCount => ApprovalGrid.Items.Count;

    public event EventHandler<InvestigationHit>? InvestigationNavigationRequested;
    public string StoreCode { get => AdjustmentStoreInput.Text; set => AdjustmentStoreInput.Text = value; }
    public void UpdateAccess(OperationsAdministrationWorkspaceAccess value)
    {
        access = value;
        AdjustmentEditor.IsEnabled = ApprovalActions.IsEnabled = RefreshApprovalsButton.IsEnabled = value.CanAdminister;
    }
    public void FocusSearch() { GlobalSearchInput.Focus(); GlobalSearchInput.SelectAll(); }

    public async Task RefreshApprovalsAsync()
    {
        try
        {
            RequireOwnerAccess();
            var filter = (ApprovalStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var rows = await OperationsService.LoadApprovalsAsync(filter == "All requests" ? null : filter?.ToUpperInvariant());
            ApprovalGrid.ItemsSource = rows;
            InvestigationStatus.Text = $"{rows.Count:N0} request(s), {rows.Count(x => x.Status == "PENDING"):N0} pending. Select a pending request to decide.";
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Investigation", "APPROVAL_REFRESH_FAILED"); InvestigationStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
    }

    private IOperationsAdministrationService OperationsService => operationsServiceFactory(connectionStringProvider());
    private async void RunGlobalSearch_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireViewAccess();
            var rows = await investigationQueryFactory(connectionStringProvider()).SearchAsync(GlobalSearchInput.Text);
            InvestigationGrid.ItemsSource = rows;
            InvestigationStatus.Text = $"{rows.Count:N0} result(s) across recorded transactions, sources, reports and registers.";
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Investigation", "INVESTIGATION_SEARCH_FAILED"); InvestigationStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
    }

    private async void SubmitAdjustment_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            if (AdjustmentDateInput.SelectedDate is null) throw new InvalidOperationException("Select the adjustment business date.");
            if (string.IsNullOrWhiteSpace(AdjustmentStoreInput.Text) || string.IsNullOrWhiteSpace(AdjustmentTypeInput.Text))
                throw new InvalidOperationException("Enter the store and adjustment type.");
            if (!decimal.TryParse(AdjustmentAmountInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount == 0)
                throw new InvalidOperationException("Enter a non-zero signed adjustment amount.");
            var id = await OperationsService.SubmitAdjustmentAsync(new SubmitAdjustment(
                AdjustmentStoreInput.Text,
                DateOnly.FromDateTime(AdjustmentDateInput.SelectedDate.Value),
                AdjustmentTypeInput.Text,
                amount,
                AdjustmentReasonInput.Text,
                LinkedSourceDocumentId));
            AdjustmentAmountInput.Clear();
            AdjustmentReasonInput.Clear();
            AdjustmentTypeInput.Clear();
            InvestigationStatus.Text = $"Adjustment {id:N0} is pending Owner approval. recorded ETP facts were not changed.";
            await RefreshApprovalsAsync();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Investigation", "ADJUSTMENT_SUBMIT_FAILED"); InvestigationStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
    }

    private async void RefreshApprovals_Click(object sender, RoutedEventArgs e) => await RefreshApprovalsAsync();
    private async void ApproveSelected_Click(object sender, RoutedEventArgs e) => await DecideApprovalAsync(true);
    private async void RejectSelected_Click(object sender, RoutedEventArgs e) { if (ConfirmationSheet.Show(this, "Reject request", "Reject the selected request with the entered reason?")) await DecideApprovalAsync(false); }

    private async Task DecideApprovalAsync(bool approve)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            if (ApprovalGrid.SelectedItem is not ApprovalRequest row || row.Status != "PENDING") throw new InvalidOperationException("Select one pending approval.");
            await OperationsService.DecideApprovalAsync(new DecideApproval(row.Id, approve, ApprovalReasonInput.Text));
            ApprovalReasonInput.Clear();
            await RefreshApprovalsAsync();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Investigation", "APPROVAL_DECISION_FAILED"); InvestigationStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
    }

    private async void ApprovalFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (approvalReasons is not null && access.CanAdminister) await RefreshApprovalsAsync();
    }
    private void OpenInvestigation_Click(object sender, RoutedEventArgs e) => OpenSelectedInvestigation();
    private void Investigation_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenSelectedInvestigation();
    public void OpenSelectedInvestigation()
    {
        if (!access.CanView || InvestigationGrid.SelectedItem is not InvestigationHit hit) return;
        if (hit.TargetTaskId is null) { InvestigationStatus.Text = "This result has no available destination."; return; }
        if (hit.TargetTaskId.StartsWith("register-", StringComparison.Ordinal) && !access.CanImport)
        { InvestigationStatus.Text = "Owner or Store Manager permission is required to open registers."; return; }
        InvestigationNavigationRequested?.Invoke(this, hit);
    }

    private void RequireViewAccess()
    {
        if (!access.CanView) throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private void RequireImportAccess()
    {
        if (!access.CanImport) throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private void RequireOwnerAccess()
    {
        if (!access.CanAdminister) throw new UnauthorizedAccessException("Owner permission is required.");
    }
}
