using System.Reflection;
using System.Windows.Controls;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Desktop.Modules.Accounting;

namespace Etp.Reporting.Desktop.Tests;

// 1.12 U3 (B3/B4): "1 Prepare / 2 Approve / 3 Write" are three plain buttons and the next one is the primary.
[Collection(WpfViewCollection.Name)]
public sealed class AccountingStepButtonsTests
{
    [Fact]
    public void Step_buttons_are_plain_verbs_with_no_numbers_or_ellipsis()
    {
        TallyStaRunner.Run(() =>
        {
            var view = Owner(out _);
            Assert.Equal(new[] { "Prepare", "Save", "Approve", "Reject", "Write file" },
                new[] { "PreviewTaskButton", "SaveTaskButton", "ApproveTaskButton", "RejectTaskButton", "TallyTaskButton" }
                    .Select(name => (string)((Button)view.FindName(name)).Content));
            foreach (var name in new[] { "PreviewTaskButton", "SaveTaskButton", "ApproveTaskButton", "RejectTaskButton", "TallyTaskButton" })
                Assert.DoesNotMatch(@"\d|[→…]", (string)((Button)view.FindName(name)).Content);
        });
    }

    [Theory]
    [InlineData(null, "PreviewTaskButton")]
    [InlineData("DRAFT", "ApproveTaskButton")]
    [InlineData("BLOCKED", null)]
    [InlineData("APPROVED_READY", "TallyTaskButton")]
    [InlineData("EXPORTED_AWAITING_IMPORT", null)]
    [InlineData("REJECTED", null)]
    public void Exactly_the_next_step_is_primary(string? status, string? primary)
    {
        TallyStaRunner.Run(() =>
        {
            var view = Owner(out _);
            var grid = (DataGrid)view.FindName("AccountingBatchGrid");
            if (status is not null)
            {
                grid.ItemsSource = new[] { new AccountingBatchSummary(1, "SC999", new(2026, 8, 25), 1, 1, 25, 25, status, null, null, DateTime.UtcNow, "Missing mapping") };
                grid.SelectedIndex = 0;
            }
            Assert.Equal(primary, view.PrimaryStepButton?.Name);
        });
    }

    [Fact]
    public void A_manager_has_no_primary_step_because_every_step_is_Owner_only()
    {
        TallyStaRunner.Run(() =>
        {
            var view = Owner(out var access);
            access.Role = AccessRole.StoreManager;
            view.SelectTask("prepare-batch");
            Assert.Null(view.PrimaryStepButton);
            Assert.False(((Button)view.FindName("PreviewTaskButton")).IsEnabled);
        });
    }

    private static AccountingWorkspaceView Owner(out AccessHolder holder)
    {
        var service = DispatchProxy.Create<IAccountingService, StepProxy>();
        var view = new AccountingWorkspaceView(new(() => service));
        var access = new AccessHolder();
        view.AttachHost(() => new AccessSession("fixture", "Fixture", access.Role, true), error => error.Message);
        holder = access;
        return view;
    }

    private sealed class AccessHolder { public AccessRole Role { get; set; } = AccessRole.Owner; }

    public class StepProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IAccountingService.LoadDestinationAsync) => Task.FromResult(new AccountingDestination("TEST Fixture", "TEST")),
            nameof(IAccountingService.LoadEntriesAsync) => Task.FromResult<IReadOnlyList<AccountingEntry>>([]),
            _ => throw new InvalidOperationException("Unexpected fixture call: " + method?.Name)
        };
    }
}
