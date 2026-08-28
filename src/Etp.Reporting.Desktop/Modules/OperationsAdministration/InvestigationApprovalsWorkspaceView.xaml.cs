extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    public InvestigationApprovalsWorkspaceView(
        Func<string> connectionStringProvider,
        Func<string, IOperationsAdministrationService> operationsServiceFactory,
        Func<string, IInvestigationQuery> investigationQueryFactory)
    {
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        this.operationsServiceFactory = operationsServiceFactory ?? throw new ArgumentNullException(nameof(operationsServiceFactory));
        this.investigationQueryFactory = investigationQueryFactory ?? throw new ArgumentNullException(nameof(investigationQueryFactory));
        InitializeComponent();
        AdjustmentDateInput.SelectedDate = DateTime.Today.AddDays(-1);
    }

    public long? LinkedSourceDocumentId { get; set; }
    public string StatusText => InvestigationStatus.Text;
    public int ApprovalRowCount => ApprovalGrid.Items.Count;

    public void UpdateAccess(OperationsAdministrationWorkspaceAccess value) => access = value;
    public void FocusSearch() { GlobalSearchInput.Focus(); GlobalSearchInput.SelectAll(); }

    public async Task RefreshApprovalsAsync()
    {
        try
        {
            RequireViewAccess();
            var rows = await OperationsService.LoadApprovalsAsync();
            ApprovalGrid.ItemsSource = rows;
            InvestigationStatus.Text = $"{rows.Count:N0} approval(s) pending.";
        }
        catch (Exception ex) { InvestigationStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
    }

    private IOperationsAdministrationService OperationsService => operationsServiceFactory(connectionStringProvider());
    private async void RunGlobalSearch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess();
            var rows = await investigationQueryFactory(connectionStringProvider()).SearchAsync(GlobalSearchInput.Text);
            InvestigationGrid.ItemsSource = rows;
            InvestigationStatus.Text = $"{rows.Count:N0} result(s) across canonical transactions, sources, reports and registers.";
        }
        catch (Exception ex) { InvestigationStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
    }

    private async void SubmitAdjustment_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
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
            InvestigationStatus.Text = $"Adjustment {id:N0} is pending Owner approval. Canonical ETP facts were not changed.";
            await RefreshApprovalsAsync();
        }
        catch (Exception ex) { InvestigationStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
    }

    private async void RefreshApprovals_Click(object sender, RoutedEventArgs e) => await RefreshApprovalsAsync();
    private async void ApproveSelected_Click(object sender, RoutedEventArgs e) => await DecideApprovalAsync(true);
    private async void RejectSelected_Click(object sender, RoutedEventArgs e) => await DecideApprovalAsync(false);

    private async Task DecideApprovalAsync(bool approve)
    {
        try
        {
            RequireOwnerAccess();
            if (ApprovalGrid.SelectedItem is not ApprovalRequest row) throw new InvalidOperationException("Select one pending approval.");
            await OperationsService.DecideApprovalAsync(new DecideApproval(row.Id, approve, ApprovalReasonInput.Text));
            ApprovalReasonInput.Clear();
            await RefreshApprovalsAsync();
        }
        catch (Exception ex) { InvestigationStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
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
