extern alias EtpApplication;
using AccountingService = EtpApplication::Etp.Reporting.Application.Accounting.IAccountingService;
using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed class TallyDestinationSettingsView : StackPanel
{
    private readonly Func<bool> isOwner;
    private readonly Func<AccountingService> serviceFactory;
    private readonly TextBox company = new() { MaxLength = 200 };
    private readonly ComboBox environment = new() { ItemsSource = new[] { "TEST", "PRODUCTION" }, SelectedIndex = 0 };
    private readonly TextBox confirmation = new();
    private readonly TextBox reason = new() { MaxLength = 1000 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private string[]? saved;
    public bool IsBusy { get; private set; }
    private string[] Values => [company.Text, environment.SelectedItem?.ToString() ?? "TEST", confirmation.Text, reason.Text];
    public bool HasDraft => saved is not null && !Values.SequenceEqual(saved);

    /// <summary>The composition root hands the screen its service; the screen never sees a connection string (A1 rule).</summary>
    public TallyDestinationSettingsView(Func<bool> isOwner, Func<AccountingService> serviceFactory)
    {
        this.isOwner = isOwner; this.serviceFactory = serviceFactory;
        Margin = new Thickness(0, 18, 0, 0);
        var heading = new TextBlock { Text = "Tally file destination" };
        heading.SetResourceReference(StyleProperty, "CardTitleText");
        Children.Add(heading);
        var hint = new TextBlock { Text = "Company and TEST label are saved with every exported file. Live export remains unavailable until Tally setup and read-back are approved in Phase 7.", TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(StyleProperty, "CaptionText");
        Children.Add(hint);
        AddField("Tally company name (leave blank until D12 is decided)", company);
        AddField("Environment", environment);
        AddField("Type the exact company name to save PRODUCTION", confirmation);
        AddField("Change reason", reason);
        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(save, "Save Tally destination");
        save.Click += async (_, _) => await SaveDraftAsync(); Children.Add(save); Children.Add(status);
        IsEnabled = false;
    }

    private void AddField(string label, Control control)
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) };
        caption.SetResourceReference(StyleProperty, "FieldLabelText");
        Children.Add(caption);
        System.Windows.Automation.AutomationProperties.SetName(control, label); Children.Add(control);
    }

    public async Task LoadAsync()
    {
        if (!isOwner() || IsBusy || HasDraft) return;
        IsBusy = true; IsEnabled = false;
        try
        {
            var value = await serviceFactory().LoadDestinationAsync();
            company.Text = value.CompanyName ?? ""; environment.SelectedItem = value.EnvironmentLabel;
            confirmation.Clear(); reason.Clear(); saved = Values;
        }
        catch(Exception exception) { status.Text = DesktopDiagnostics.WithReference(DesktopFriendlyError.Describe(exception), DesktopDiagnostics.Record(exception, "Settings.TallyDestination", "TALLY_DESTINATION_LOAD_FAILED", operation: "Tally destination load failed")); }
        finally { IsBusy = false; IsEnabled = isOwner() && saved is not null; }
    }

    public void DiscardDraft()
    {
        if (saved is null) return;
        company.Text = saved[0]; environment.SelectedItem = saved[1]; confirmation.Text = saved[2]; reason.Text = saved[3];
    }

    public async Task<bool> SaveDraftAsync()
    {
        if (!isOwner() || IsBusy) return false;
        IsBusy = true; IsEnabled = false;
        try
        {
            await serviceFactory().SaveDestinationAsync(new(company.Text, environment.SelectedItem?.ToString() ?? "TEST", confirmation.Text, reason.Text));
            confirmation.Clear(); reason.Clear(); saved = Values;
            status.Text = "Tally destination saved. This does not enable live Tally export.";
            return true;
        }
        catch(Exception exception) { status.Text = DesktopDiagnostics.WithReference(DesktopFriendlyError.Describe(exception), DesktopDiagnostics.Record(exception, "Settings.TallyDestination", "TALLY_DESTINATION_SAVE_FAILED", operation: "Tally destination save failed")); return false; }
        finally { IsBusy = false; IsEnabled = isOwner(); }
    }
}
