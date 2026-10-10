using System.ComponentModel;
using System.Globalization;

namespace Etp.Reporting.Desktop.Modules.Settings;

/// <summary>One target row carried forward by "Copy from previous month". Nothing is saved until the Owner saves the copied rows.</summary>
public sealed class CopiedTargetRow(string storeCode, string? croNumber, decimal previousTarget, decimal? currentTarget, string newTarget) : INotifyPropertyChanged
{
    private string newTargetText = newTarget;

    public string StoreCode { get; } = storeCode;
    public string? CroNumber { get; } = croNumber;
    public decimal PreviousTarget { get; } = previousTarget;
    /// <summary>The target already saved for the month being filled, if any. It is replaced only after the Owner confirms.</summary>
    public decimal? CurrentTarget { get; } = currentTarget;

    /// <summary>The value to save; pre-filled with the previous month's target and editable. Blank skips the row.</summary>
    public string NewTarget
    {
        get => newTargetText;
        set { if (newTargetText == value) return; newTargetText = value ?? ""; PropertyChanged?.Invoke(this, new(nameof(NewTarget))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record TargetCopySource(string StoreCode, string? CroNumber, DateOnly Month, decimal TargetSales);

public sealed record ResolvedTargetCopy(string StoreCode, string? CroNumber, decimal TargetSales, bool ReplacesExisting);

/// <summary>Owner choice when copied rows would replace targets already saved for the month.</summary>
public enum TargetOverwriteChoice { Cancel, ReplaceExisting, KeepExisting }

/// <summary>
/// 1.9.9 targets-copy: plans "Copy from previous month" for monthly store targets and staff (CRO) targets.
/// The plan only pre-fills an editor; saving is a separate Owner action, and a row that would change a target
/// already saved for the month is saved only when the Owner confirms the replacement.
/// </summary>
public static class TargetCopy
{
    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly MonthEnd(DateOnly date) => MonthStart(date).AddMonths(1).AddDays(-1);

    public static DateOnly PreviousMonth(DateOnly month) => MonthStart(month).AddMonths(-1);

    public static string MonthLabel(DateOnly month) => month.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Rows of the month before <paramref name="targetMonth"/>, each with the target already saved for <paramref name="targetMonth"/> (if any).</summary>
    public static IReadOnlyList<CopiedTargetRow> Plan(IEnumerable<TargetCopySource> saved, DateOnly targetMonth, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var month = MonthStart(targetMonth);
        var previous = PreviousMonth(month);
        var rows = saved.ToArray();
        var current = rows.Where(row => MonthStart(row.Month) == month)
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().TargetSales, StringComparer.OrdinalIgnoreCase);
        return rows.Where(row => MonthStart(row.Month) == previous)
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(row => row.StoreCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.CroNumber, StringComparer.OrdinalIgnoreCase)
            .Select(row => new CopiedTargetRow(
                row.StoreCode, row.CroNumber, row.TargetSales,
                current.TryGetValue(Key(row), out var existing) ? existing : null,
                row.TargetSales.ToString("0.##", formatProvider ?? CultureInfo.CurrentCulture)))
            .ToArray();
    }

    /// <summary>
    /// Parses the edited rows. Blank rows and rows equal to the saved target are skipped; a row that would change
    /// a saved target is flagged <see cref="ResolvedTargetCopy.ReplacesExisting"/>.
    /// </summary>
    public static IReadOnlyList<ResolvedTargetCopy> Resolve(IEnumerable<CopiedTargetRow> rows, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var resolved = new List<ResolvedTargetCopy>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.NewTarget)) continue;
            if (!decimal.TryParse(row.NewTarget, NumberStyles.Number, formatProvider ?? CultureInfo.CurrentCulture, out var value) || value < 0)
                throw new ArgumentException($"Enter a non-negative target for {Describe(row.StoreCode, row.CroNumber)}, or clear it to skip that row.");
            if (row.CurrentTarget == value) continue;
            resolved.Add(new(row.StoreCode, row.CroNumber, value, row.CurrentTarget is not null));
        }
        return resolved;
    }

    /// <summary>The rows to save after the Owner's choice; an empty list when the Owner cancels.</summary>
    public static IReadOnlyList<ResolvedTargetCopy> Apply(IReadOnlyList<ResolvedTargetCopy> resolved, TargetOverwriteChoice choice) => choice switch
    {
        TargetOverwriteChoice.ReplaceExisting => resolved,
        TargetOverwriteChoice.KeepExisting => resolved.Where(row => !row.ReplacesExisting).ToArray(),
        _ => [],
    };

    public static string OverwriteQuestion(IReadOnlyList<ResolvedTargetCopy> resolved, DateOnly month)
    {
        var replaced = resolved.Where(row => row.ReplacesExisting).ToArray();
        var names = string.Join(", ", replaced.Take(8).Select(row => Describe(row.StoreCode, row.CroNumber)));
        if (replaced.Length > 8) names += $" and {replaced.Length - 8} more";
        return $"{replaced.Length} target(s) for {MonthLabel(month)} are already saved with a different value ({names}).\n\n" +
               "Yes: replace them with the copied values.\nNo: keep the saved values and save only the new rows.\nCancel: save nothing.";
    }

    public static string Describe(string storeCode, string? croNumber) =>
        string.IsNullOrWhiteSpace(croNumber) ? storeCode : $"{storeCode} CRO {croNumber}";

    private static string Key(TargetCopySource row) => $"{row.StoreCode.Trim()}|{row.CroNumber?.Trim()}";
}

/// <summary>The editable grid that shows copied rows before they are saved; only the New target column can be edited.</summary>
public static class CopiedTargetGrid
{
    public static System.Windows.Controls.DataGrid Create(string automationName, bool withCro)
    {
        var grid = new System.Windows.Controls.DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, IsReadOnly = false,
            MaxHeight = 260, MinHeight = 88, RowHeight = 44, Margin = new(0, 6, 0, 6),
            Visibility = System.Windows.Visibility.Collapsed,
        };
        System.Windows.Automation.AutomationProperties.SetName(grid, automationName);
        grid.Columns.Add(ReadOnly("Store", nameof(CopiedTargetRow.StoreCode)));
        if (withCro) grid.Columns.Add(ReadOnly("CRO", nameof(CopiedTargetRow.CroNumber)));
        grid.Columns.Add(ReadOnly("Previous month", nameof(CopiedTargetRow.PreviousTarget)));
        grid.Columns.Add(ReadOnly("Already saved", nameof(CopiedTargetRow.CurrentTarget)));
        grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
        {
            Header = "New target (clear to skip)",
            Binding = new System.Windows.Data.Binding(nameof(CopiedTargetRow.NewTarget)) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
        });
        return grid;
    }

    private static System.Windows.Controls.DataGridTextColumn ReadOnly(string header, string path) =>
        new() { Header = header, Binding = new System.Windows.Data.Binding(path) { Mode = System.Windows.Data.BindingMode.OneWay }, IsReadOnly = true };
}
