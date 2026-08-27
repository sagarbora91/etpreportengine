using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class HelpWorkspaceSessionTests
{
    [Fact]
    public void Opening_help_captures_the_workspace_return_state()
    {
        var content = new object();
        var snapshot = new HelpWorkspaceSnapshot(content, "report", "Daily Sales", "Report description", "Reports / Sales", true);
        var session = new HelpWorkspaceSession();

        Assert.True(session.Open(snapshot));

        Assert.True(session.IsOpen);
        Assert.Same(snapshot, session.ReturnState);
        Assert.True(Assert.IsType<HelpWorkspaceSnapshot>(session.ReturnState).CanRestoreFocusedWorkspace);
    }

    [Fact]
    public void Changing_help_topics_does_not_replace_the_original_return_state()
    {
        var original = new HelpWorkspaceSnapshot(new object(), "report", "Daily Sales", "Description", "Reports", true);
        var replacement = new HelpWorkspaceSnapshot(null, "help", "Help Centre", "Guidance", "Help", false);
        var session = new HelpWorkspaceSession();
        session.Open(original);

        Assert.False(session.Open(replacement));

        Assert.Same(original, session.ReturnState);
    }

    [Fact]
    public void Closing_help_returns_the_snapshot_and_ends_the_session()
    {
        var snapshot = new HelpWorkspaceSnapshot(new object(), "report", "Stock", "Description", "Reports / Stock", true);
        var session = new HelpWorkspaceSession();
        session.Open(snapshot);

        var restored = session.Close();

        Assert.Same(snapshot, restored);
        Assert.False(session.IsOpen);
        Assert.Null(session.ReturnState);
    }

    [Fact]
    public void Closing_when_help_is_not_open_has_no_return_state()
    {
        var session = new HelpWorkspaceSession();

        Assert.Null(session.Close());
        Assert.False(session.IsOpen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dashboard")]
    [InlineData("help")]
    public void Only_a_report_with_content_can_restore_the_focused_workspace(string? workspaceKind)
    {
        var snapshot = new HelpWorkspaceSnapshot(new object(), workspaceKind, "Title", "Description", "Breadcrumb", false);

        Assert.False(snapshot.CanRestoreFocusedWorkspace);
    }

    [Fact]
    public void Report_without_content_falls_back_to_the_legacy_workspace()
    {
        var snapshot = new HelpWorkspaceSnapshot(null, "report", "Reports", "Description", "Reports", false);

        Assert.False(snapshot.CanRestoreFocusedWorkspace);
    }

    [Fact]
    public void Abandon_clears_stale_return_state_when_help_navigates_elsewhere()
    {
        var session = new HelpWorkspaceSession();
        session.Open(new(new object(), "report", "Report", "Description", "Reports", true));

        session.Abandon();

        Assert.False(session.IsOpen);
        Assert.Null(session.ReturnState);
    }
}
