namespace Etp.Reporting.Reporting;

public sealed record CashBalanceInputs(
    decimal? OpeningCash,
    decimal RetailCash,
    decimal? ServiceCash,
    decimal? Expenses,
    decimal? CashDeposit,
    decimal? Adjustment,
    decimal? CountedClosingCash);

public sealed record CashBalanceEvaluation(
    decimal? CalculatedClosingCash,
    decimal? Variance,
    ReconciliationStatus Status,
    IReadOnlyList<string> MissingInputs,
    string Formula);

public sealed class CashBalanceReconciliationService
{
    public const string Formula = "Opening cash + retail cash + service cash - expenses - cash deposit + adjustment = calculated closing cash";

    public CashBalanceEvaluation Reconcile(CashBalanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var missing = new List<string>();
        AddIfMissing(inputs.OpeningCash, "OPENING_CASH", missing);
        AddIfMissing(inputs.ServiceCash, "SERVICE_CASH", missing);
        AddIfMissing(inputs.Expenses, "EXPENSES", missing);
        AddIfMissing(inputs.CashDeposit, "CASH_DEPOSIT", missing);
        AddIfMissing(inputs.Adjustment, "CASH_ADJUSTMENT", missing);
        AddIfMissing(inputs.CountedClosingCash, "CLOSING_CASH_COUNTED", missing);
        if (missing.Count > 0) return new(null, null, ReconciliationStatus.Blocked, missing, Formula);

        var calculated = inputs.OpeningCash!.Value + inputs.RetailCash + inputs.ServiceCash!.Value
            - inputs.Expenses!.Value - inputs.CashDeposit!.Value + inputs.Adjustment!.Value;
        var variance = inputs.CountedClosingCash!.Value - calculated;
        return new(calculated, variance, variance == 0 ? ReconciliationStatus.Passed : ReconciliationStatus.Failed, [], Formula);
    }

    private static void AddIfMissing(decimal? value, string field, ICollection<string> missing)
    {
        if (value is null) missing.Add(field);
    }
}
