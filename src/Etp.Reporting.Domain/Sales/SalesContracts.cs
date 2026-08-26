using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Domain.Primitives;

namespace Etp.Reporting.Domain.Sales;

public enum SalesTransactionClassification
{
    Unresolved = 0,
    Sale,
    Return,
    Cancellation
}

public sealed record SalesImportCommand
{
    public SalesImportCommand(
        string storeCode,
        string documentNumber,
        string lineIdentifier,
        DateOnly transactionDate,
        string productCode,
        Quantity sourceQuantity,
        Money sourceGrossAmount,
        SourceLineage lineage,
        string? sourceTransactionType = null)
    {
        StoreCode = Required(storeCode, nameof(storeCode));
        DocumentNumber = Required(documentNumber, nameof(documentNumber));
        LineIdentifier = Required(lineIdentifier, nameof(lineIdentifier));
        TransactionDate = transactionDate;
        ProductCode = Required(productCode, nameof(productCode));
        SourceQuantity = sourceQuantity;
        SourceGrossAmount = sourceGrossAmount;
        Lineage = lineage ?? throw new ArgumentNullException(nameof(lineage));
        SourceTransactionType = string.IsNullOrWhiteSpace(sourceTransactionType) ? null : sourceTransactionType.Trim();
    }

    public string StoreCode { get; }
    public string DocumentNumber { get; }
    public string LineIdentifier { get; }
    public DateOnly TransactionDate { get; }
    public string ProductCode { get; }
    public Quantity SourceQuantity { get; }
    public Money SourceGrossAmount { get; }
    public SourceLineage Lineage { get; }
    public string? SourceTransactionType { get; }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("Value is required.", name)
        : value.Trim();
}

public sealed record ClassifiedSalesTransaction(
    SalesImportCommand Source,
    SalesTransactionClassification Classification,
    Quantity ReportingQuantity,
    Money ReportingGrossAmount,
    string PolicyVersion);

public interface ISalesTransactionPolicy
{
    string Version { get; }
    ClassifiedSalesTransaction Classify(SalesImportCommand command);
}
