using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U1 (frees U2): the Close day tasks choose the parts of DailyWorkflowWorkspaceView's root panel by name. Before U2
/// names the five unnamed wrap panels the positions are exactly what 1.12.0 wave 1 hard-coded; after, a part inserted
/// anywhere in the panel moves nothing.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class TaskNavigatorCloseDayTests
{
    // The arrays TaskNavigator hard-coded at v1.12.0-w1 (body, toolbar) per section.
    public static TheoryData<string, int[], int[]> WaveOneLayouts => new()
    {
        { "manual", [7, 8, 3, 6], [8] },
        { "stock-count", [2, 12, 3, 11], [12] },
        { "staff-target", [2, 14, 3], [14] },
        { "finalisation", [2, 15, 3], [15] },
        { "readiness", [3, 15, 17, 18], [15, 16] },
        { "pack", [2, 3, 17], [16] }
    };

    [Theory]
    [MemberData(nameof(WaveOneLayouts))]
    public void The_view_as_it_is_today_resolves_to_the_wave_one_positions(string section, int[] body, int[] actions)
    {
        RunSta(() =>
        {
            var positions = TaskNavigator.ReadPartPositions(WaveOnePanel());
            var (bodyNames, actionNames) = TaskNavigator.CloseDayLayout(section);

            Assert.Equal(body, TaskNavigator.Resolve(positions, bodyNames));
            Assert.Equal(actions, TaskNavigator.Resolve(positions, actionNames));
        });
    }

    [Fact]
    public void Once_every_part_is_named_an_inserted_part_moves_nothing()
    {
        RunSta(() =>
        {
            var panel = WaveOnePanel();
            NameTheUnnamedParts(panel);
            panel.Children.Insert(4, new Border { Name = "FinaliseVerdictBand" });   // what U2 adds (any position)

            var positions = TaskNavigator.ReadPartPositions(panel);
            var (body, actions) = TaskNavigator.CloseDayLayout("readiness");
            var resolved = TaskNavigator.Resolve(positions, body.Concat(actions));

            Assert.Equal(
                new[] { "VerdictBand", "FinaliseActions", "DailyPackGrid", "DayRegistersPanel", "FinaliseActions", "PackActions" },
                resolved.Select(index => ((FrameworkElement)panel.Children[index]).Name));
        });
    }

    [Fact]
    public void A_name_the_panel_does_not_carry_is_skipped_never_mapped_to_another_part()
    {
        RunSta(() =>
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Name = "VerdictBand" });
            var positions = TaskNavigator.ReadPartPositions(panel);

            Assert.Equal([0], TaskNavigator.Resolve(positions, ["VerdictBand", "PackActions", "DailyPackGrid"]));
        });
    }

    [Fact]
    public void Every_layout_name_is_a_known_part_with_a_wave_one_position()
    {
        foreach (var section in new[] { "manual", "stock-count", "staff-target", "finalisation", "readiness", "pack" })
        {
            var (body, actions) = TaskNavigator.CloseDayLayout(section);
            Assert.All(body.Concat(actions), name => Assert.True(TaskNavigator.LegacyPositions.ContainsKey(name), name));
        }
    }

    /// <summary>The root panel of DailyWorkflowWorkspaceView at v1.12.0-w1: names where the XAML has them, unnamed elsewhere.</summary>
    private static StackPanel WaveOnePanel()
    {
        string?[] names =
        [
            null, null, null, "VerdictBand", "ManualEntryHeading", null, "ManualInputsGrid", "CashQuickFields", null,
            null, null, "StockCountsGrid", null, null, null, null, null, "DailyPackGrid", "DayRegistersPanel"
        ];
        var panel = new StackPanel();
        foreach (var name in names) panel.Children.Add(new WrapPanel { Name = name ?? string.Empty });
        return panel;
    }

    private static void NameTheUnnamedParts(StackPanel panel)
    {
        foreach (var (name, index) in new[] { ("ScopeBar", 2), ("ManualEntryFields", 8), ("StockCountFields", 12), ("StaffTargetFields", 14), ("FinaliseActions", 15), ("PackActions", 16) })
            ((FrameworkElement)panel.Children[index]).Name = name;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
