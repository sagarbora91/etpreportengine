using System.Threading;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U1: the task rows of every wave-2 screen (wave plan Appendix B), their roles, the Owner's Morning landing, and
/// the shell seams the screens use (registry back-reference, argument navigation, the placeholder before registration).
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ShellWave2TaskRowsTests
{
    [Theory]
    [InlineData("morning", "Today → Morning → Morning", "Owner")]
    [InlineData("collections-setup", "Settings → Integrations → Collections setup", "Owner")]
    [InlineData("update-settings", "Settings → Software update → Software update", "Owner")]
    [InlineData("bank-files", "Import → Bank files → Bank and settlement files", "StoreManager")]
    [InlineData("deposits-in-transit", "Reports → Collections → Deposits in transit", "StoreManager")]
    [InlineData("collections-statement", "Reports → Collections → Collections: expected vs evidence", "Viewer")]
    public void Every_wave_two_screen_has_one_row_with_its_role(string id, string path, string lowest)
    {
        var task = Assert.Single(TaskNavigation.All, row => row.Id == id);
        Assert.Equal(path, task.Path);
        Assert.Contains(id, TaskNavigation.RegisteredOnly);
        var roles = new[] { ("Viewer", ShellAccess.Viewer), ("StoreManager", ShellAccess.StoreManager), ("Owner", ShellAccess.Owner) };
        var allowedFrom = Array.FindIndex(roles, role => role.Item1 == lowest);
        for (var index = 0; index < roles.Length; index++)
        {
            var expected = index >= allowedFrom;
            Assert.Equal(expected, task.IsAllowed(roles[index].Item2));
            Assert.Equal(expected, new ShellNavigationService().Navigate(task.Route, roles[index].Item2).IsAllowed);
        }
    }

    [Fact]
    public void The_Owner_lands_on_Morning_and_a_Store_Manager_or_Viewer_never_sees_it()
    {
        Assert.Equal("morning", TaskNavigation.InSection("Today", ShellAccess.Owner)[0].Id);
        foreach (var access in new[] { ShellAccess.StoreManager, ShellAccess.Viewer })
        {
            Assert.Equal("report-dsr", TaskNavigation.InSection("Today", access)[0].Id);
            Assert.DoesNotContain(TaskNavigation.Search("", access), task => task.Id == "morning");
            Assert.DoesNotContain(TaskNavigation.Search("morning", access), task => task.Id == "morning");
        }
        Assert.Contains(TaskNavigation.Search("finalise all", ShellAccess.Owner), task => task.Id == "morning");
    }

    [Fact]
    public void Every_rail_still_fits_the_shop_panel_and_the_new_routes_are_registered()
    {
        foreach (var rail in TaskNavigation.Sections)
        {
            var tabs = TaskNavigation.InSection(rail, ShellAccess.Owner).Select(task => task.Tab).Distinct().Count();
            Assert.True(MainWindow.DropDownHeightFor(tabs, 768) >= tabs * 44, $"{rail} has {tabs} sections.");
        }
        Assert.NotNull(ShellRouteRegistry.Find(TaskNavigation.MorningDestination));
        Assert.NotNull(ShellRouteRegistry.Find(TaskNavigation.CollectionsDestination));
        Assert.Equal(TaskNavigation.All.Count, TaskNavigation.All.Select(task => task.Route).Distinct().Count());
    }

    [Fact]
    public void A_screen_request_carries_argument_navigation_and_a_registry_knows_its_window_only_when_given()
    {
        var opened = new List<(string, string?)>();
        var request = new ScreenRequest("morning", null, new DateOnly(2026, 10, 9), null, ShellAccess.Owner, _ => { })
        {
            NavigateWithArgument = (task, argument) => opened.Add((task, argument))
        };
        request.NavigateWithArgument("import-problems", "attempt:41");
        Assert.Equal([("import-problems", (string?)"attempt:41")], opened);
        // The default does nothing (a registry used on its own, the tests).
        new ScreenRequest("morning", null, new DateOnly(2026, 10, 9), null, ShellAccess.Owner, _ => { }).NavigateWithArgument("x", "y");
        Assert.Null(new ScreenRegistry().Window);
    }

    [Fact]
    public void A_wave_two_task_without_its_screen_says_so_instead_of_opening_another_workspace()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = TaskNavigator.NotInThisBuild(TaskNavigation.Find("bank-files")!);
                var frame = Assert.IsType<PageFrame>(view.Content);
                Assert.Equal("Bank and settlement files is not part of this build", frame.Notice.Title);
                Assert.Equal(StatusTone.Information, frame.Notice.Tone);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void The_fix_task_falls_back_to_Problems_until_U2_adds_its_row()
    {
        Assert.Equal("import-problems", TaskNavigator.ImportFixFallbackFrom);
        Assert.NotNull(TaskNavigation.Find(TaskNavigator.ImportFixFallbackTo));
        Assert.Equal("import-problems", Etp.Reporting.Application.Morning.MorningSummaryBuilder.ImportFixTask);
    }
}
