using Etp.Reporting.Domain.Primitives;

namespace Etp.Reporting.Domain.Stock;

public sealed record StockBalanceInputs(
    Quantity Opening,
    Quantity Inward,
    Quantity Outward,
    Quantity ReportedClosing);

public sealed record StockBalanceEvaluation(
    Quantity ExpectedClosing,
    Quantity Variance,
    bool IsReconciled,
    string PolicyVersion);

public interface IStockBalancePolicy
{
    string Version { get; }
    StockBalanceEvaluation Evaluate(StockBalanceInputs inputs);
}
