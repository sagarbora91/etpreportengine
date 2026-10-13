using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>
/// 1.12 U1 (frees U2): the Today &gt; Close day, Walk-ins, Cash, Physical count and Staff target tasks pick the parts of
/// <c>DailyWorkflowWorkspaceView</c>'s root panel by <b>name</b>, not by counting its children, so U2 can add a part (the
/// Finalise verdict band, N1's no-sale button) without breaking every task layout. A name the panel does not carry yet
/// falls back to the position it had in 1.12.0 wave 1 (<see cref="LegacyPositions"/>), so this works before and after U2
/// gives the unnamed parts their <c>x:Name</c>; once every part is named the fallback is never used.
/// </summary>
public sealed partial class TaskNavigator
{
    /// <summary>The parts of the Close day panel by name. U2 names the five unnamed wrap panels with these names.</summary>
    public static class CloseDayParts
    {
        public const string Scope = "ScopeBar";
        public const string Verdict = "VerdictBand";
        public const string ManualEntryGrid = "ManualInputsGrid";
        public const string CashFields = "CashQuickFields";
        public const string ManualEntryFields = "ManualEntryFields";
        public const string StockCountGrid = "StockCountsGrid";
        public const string StockCountFields = "StockCountFields";
        public const string StaffTargetFields = "StaffTargetFields";
        public const string FinaliseActions = "FinaliseActions";
        public const string PackActions = "PackActions";
        public const string PackGrid = "DailyPackGrid";
        public const string DayRegisters = "DayRegistersPanel";
    }

    /// <summary>Where each part sat in the root panel at v1.12.0-w1 (the indices this navigator used to hard-code).</summary>
    internal static IReadOnlyDictionary<string, int> LegacyPositions { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [CloseDayParts.Scope] = 2,
        [CloseDayParts.Verdict] = 3,
        [CloseDayParts.ManualEntryGrid] = 6,
        [CloseDayParts.CashFields] = 7,
        [CloseDayParts.ManualEntryFields] = 8,
        [CloseDayParts.StockCountGrid] = 11,
        [CloseDayParts.StockCountFields] = 12,
        [CloseDayParts.StaffTargetFields] = 14,
        [CloseDayParts.FinaliseActions] = 15,
        [CloseDayParts.PackActions] = 16,
        [CloseDayParts.PackGrid] = 17,
        [CloseDayParts.DayRegisters] = 18,
    };

    /// <summary>The body and toolbar parts of each daily task, by name (same choice as the indices before 1.12 U1).</summary>
    internal static (string[] Body, string[] Actions) CloseDayLayout(string section) => section switch
    {
        "manual" => ([CloseDayParts.CashFields, CloseDayParts.ManualEntryFields, CloseDayParts.Verdict, CloseDayParts.ManualEntryGrid], [CloseDayParts.ManualEntryFields]),
        "stock-count" => ([CloseDayParts.Scope, CloseDayParts.StockCountFields, CloseDayParts.Verdict, CloseDayParts.StockCountGrid], [CloseDayParts.StockCountFields]),
        "staff-target" => ([CloseDayParts.Scope, CloseDayParts.StaffTargetFields, CloseDayParts.Verdict], [CloseDayParts.StaffTargetFields]),
        "finalisation" => ([CloseDayParts.Scope, CloseDayParts.FinaliseActions, CloseDayParts.Verdict], [CloseDayParts.FinaliseActions]),
        "readiness" => ([CloseDayParts.Verdict, CloseDayParts.FinaliseActions, CloseDayParts.PackGrid, CloseDayParts.DayRegisters], [CloseDayParts.FinaliseActions, CloseDayParts.PackActions]),
        _ => ([CloseDayParts.Scope, CloseDayParts.Verdict, CloseDayParts.PackGrid], [CloseDayParts.PackActions])
    };

    private IReadOnlyDictionary<string, int>? closeDayPositions;

    /// <summary>
    /// The positions of the named parts in the panel as it was first read (FocusedTaskLayout keeps that original order for
    /// the life of the view). Read once, before the first layout takes the children out of the panel.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> ReadPartPositions(Panel root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < root.Children.Count; index++)
            if (root.Children[index] is FrameworkElement { Name: { Length: > 0 } name }) positions.TryAdd(name, index);
        foreach (var (name, legacy) in LegacyPositions)
            if (!positions.ContainsKey(name) && legacy < root.Children.Count && root.Children[legacy] is FrameworkElement { Name: { Length: 0 } })
                positions[name] = legacy;
        return positions;
    }

    /// <summary>The panel positions of the named parts; a name that is not in the panel is skipped (never a wrong part).</summary>
    internal static int[] Resolve(IReadOnlyDictionary<string, int> positions, IEnumerable<string> names) =>
        names.Select(name => positions.TryGetValue(name, out var index) ? index : -1).Where(index => index >= 0).ToArray();

    private (int[] Body, int[] Actions) CloseDayIndices(UserControl view, string section)
    {
        if (closeDayPositions is null)
        {
            var root = (view.Content is Border border ? border.Child : view.Content) as Panel
                ?? throw new InvalidOperationException("The Close day view must be laid out before its first task opens.");
            closeDayPositions = ReadPartPositions(root);
        }
        var (body, actions) = CloseDayLayout(section);
        return (Resolve(closeDayPositions, body), Resolve(closeDayPositions, actions));
    }
}
