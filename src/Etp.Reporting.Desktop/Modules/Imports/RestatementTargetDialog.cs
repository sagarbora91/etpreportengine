extern alias EtpApplication;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using RestatementCandidate = EtpApplication::Etp.Reporting.Application.Imports.RestatementCandidate;
using RestatementTargetChoice = EtpApplication::Etp.Reporting.Application.Imports.RestatementTargetChoice;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>
/// Asks which current import a restatement replaces when the new file's period overlaps several (IF-016
/// interim, planner 1). The others are not restated: they go through superset promotion as before.
/// </summary>
public sealed class RestatementTargetDialog : Window
{
    public RestatementCandidate? SelectedCandidate { get; private set; }
    internal DataGrid CandidateGrid { get; }
    internal Button RestateButton { get; }

    public RestatementTargetDialog(Window? owner, RestatementTargetChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (owner?.IsLoaded == true) Owner = owner;
        Title = "Choose the import to restate"; Width = 720; SizeToContent = SizeToContent.Height;
        MinWidth = 420; MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetName(this, "Choose the import to restate");

        var root = new DockPanel { Margin = new Thickness(20) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Do not import", IsCancel = true, MinHeight = 44, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0) };
        RestateButton = new Button { Content = "Restate selected import", IsDefault = true, IsEnabled = false, MinHeight = 44, Padding = new Thickness(12, 8, 12, 8) };
        RestateButton.SetResourceReference(StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(cancel, "Do not import this file");
        AutomationProperties.SetName(RestateButton, "Restate the selected import");
        actions.Children.Add(cancel); actions.Children.Add(RestateButton);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Which import does this file replace?", FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = $"{choice.FileName} ({choice.ReportCode}, {TablePresentation.StoreLabel(choice.StoreCode)}, {choice.Period}) overlaps {choice.Candidates.Count} current imports. " +
                "Choose the one to restate; its figures are archived and replaced once the Owner approves. " +
                "Each other import is superseded only if this file covers its period and holds every row it holds; otherwise nothing is imported.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12)
        });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        CandidateGrid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 120, MaxHeight = 320, ItemsSource = choice.Candidates
        };
        CandidateGrid.Columns.Add(new DataGridTextColumn { Header = "File id", Binding = new Binding(nameof(RestatementCandidate.ImportFileId)), Width = 80 });
        CandidateGrid.Columns.Add(new DataGridTextColumn { Header = "File name", Binding = new Binding(nameof(RestatementCandidate.FileName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        CandidateGrid.Columns.Add(new DataGridTextColumn { Header = "Period", Binding = new Binding(nameof(RestatementCandidate.Period)), Width = 215 });
        CandidateGrid.Columns.Add(new DataGridTextColumn { Header = "Rows", Binding = new Binding(nameof(RestatementCandidate.Rows)) { StringFormat = "N0" }, Width = 80 });
        AutomationProperties.SetName(CandidateGrid, "Current imports this file overlaps");
        CandidateGrid.SelectionChanged += (_, _) => RestateButton.IsEnabled = CandidateGrid.SelectedItem is RestatementCandidate;
        root.Children.Add(CandidateGrid);

        RestateButton.Click += (_, _) =>
        {
            if (CandidateGrid.SelectedItem is not RestatementCandidate selected) return;
            SelectedCandidate = selected;
            DialogResult = true;
        };
        Content = root;
    }

    /// <summary>Shows the dialog; returns the chosen import, or null when the Owner chose none.</summary>
    public static RestatementCandidate? Choose(Window? owner, RestatementTargetChoice choice)
    {
        var dialog = new RestatementTargetDialog(owner, choice);
        return dialog.ShowDialog() == true ? dialog.SelectedCandidate : null;
    }
}
