namespace Etp.Reporting.Domain.Primitives;

public readonly record struct Money
{
    public Money(decimal amount, string currencyCode)
    {
        Amount = amount;
        CurrencyCode = NormalizeCurrency(currencyCode);
    }

    public decimal Amount { get; }
    public string CurrencyCode { get; }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(checked(Amount + other.Amount), CurrencyCode);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(checked(Amount - other.Amount), CurrencyCode);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!StringComparer.Ordinal.Equals(CurrencyCode, other.CurrencyCode))
            throw new InvalidOperationException("Money values with different currencies cannot be combined.");
    }

    private static string NormalizeCurrency(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Currency code is required.", nameof(value));
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency code must contain three ASCII letters.", nameof(value));
        return normalized;
    }
}
