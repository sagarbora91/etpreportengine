extern alias EtpApplication;

using System.Windows;
using MorningSummaryQuery = EtpApplication::Etp.Reporting.Application.Morning.IMorningSummaryQuery;
using MorningFinaliseResult = EtpApplication::Etp.Reporting.Application.Morning.MorningFinaliseResult;
using MorningTone = EtpApplication::Etp.Reporting.Application.Morning.MorningTone;

namespace Etp.Reporting.Desktop.Modules.Morning;

/// <summary>"Publish shop results now": whether the task ran and what to tell the Owner, as one sentence.</summary>
public sealed record MorningPublishResult(bool Succeeded, string Message);

/// <summary>
/// What the Morning page needs, built by <c>DesktopCompositionRoot.Morning.cs</c> from the connection factory (never a
/// connection string): the read model, "Finalise all ready stores" (one transaction per store) and the publish task.
/// </summary>
public sealed record MorningScreenServices(
    Func<MorningSummaryQuery> Summary,
    Func<DateOnly, IReadOnlyList<string>, IProgress<string>?, CancellationToken, Task<IReadOnlyList<MorningFinaliseResult>>> FinaliseAll,
    Func<CancellationToken, Task<MorningPublishResult>> PublishShopResults);

/// <summary>
/// N1's "Mark as no-sale day" and "Withdraw" buttons (plan section 4: <c>NoSaleDayActions.CreateMarkButton</c> /
/// <c>CreateWithdrawButton</c>). U1 places them on the Morning page and the Today checklist; it never builds the dialog.
/// The shell holds one instance in <c>MainWindow.NoSaleDayButtons</c>; while it is null (a build without N1) the pages
/// show the no-sale text and no button. Arguments: store code, business date, what to do after a mark or a withdraw.
/// </summary>
public sealed record NoSaleDayButtons(
    Func<string, DateOnly, Action, UIElement> Mark,
    Func<string, DateOnly, Action, UIElement> Withdraw);

/// <summary>The Morning tones are the design system's tones (section 8), one to one.</summary>
public static class MorningTones
{
    public static StatusTone ToStatusTone(MorningTone tone) => tone switch
    {
        MorningTone.Success => StatusTone.Success,
        MorningTone.Warning => StatusTone.Warning,
        MorningTone.Critical => StatusTone.Critical,
        MorningTone.Information => StatusTone.Information,
        _ => StatusTone.Neutral
    };
}
