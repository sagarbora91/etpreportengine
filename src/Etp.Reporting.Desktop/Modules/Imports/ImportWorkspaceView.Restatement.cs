extern alias EtpApplication;
using System.Windows;
using System.Windows.Threading;
using RestatementCandidate = EtpApplication::Etp.Reporting.Application.Imports.RestatementCandidate;
using RestatementTargetChoice = EtpApplication::Etp.Reporting.Application.Imports.RestatementTargetChoice;

namespace Etp.Reporting.Desktop.Modules.Imports;

public partial class ImportWorkspaceView
{
    // Tests answer here instead of the modal dialog.
    internal Func<RestatementTargetChoice, RestatementCandidate?>? RestatementTargetChooser { get; set; }

    // The folder import asks from a worker thread when a restatement's period overlaps several current
    // imports (IF-016); the dialog runs on this view's dispatcher.
    private Task<RestatementCandidate?> ChooseRestatementTargetAsync(RestatementTargetChoice choice, CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(() => (RestatementTargetChooser ?? ShowRestatementTargetDialog)(choice),
            DispatcherPriority.Normal, cancellationToken).Task;

    private RestatementCandidate? ShowRestatementTargetDialog(RestatementTargetChoice choice) =>
        RestatementTargetDialog.Choose(Window.GetWindow(this), choice);
}
