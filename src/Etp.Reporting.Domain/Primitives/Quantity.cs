namespace Etp.Reporting.Domain.Primitives;

public readonly record struct Quantity
{
    public Quantity(decimal value, string unitCode)
    {
        Value = value;
        UnitCode = Required(unitCode, nameof(unitCode)).ToUpperInvariant();
    }

    public decimal Value { get; }
    public string UnitCode { get; }

    public Quantity Add(Quantity other)
    {
        if (!StringComparer.Ordinal.Equals(UnitCode, other.UnitCode))
            throw new InvalidOperationException("Quantities with different units cannot be combined.");
        return new Quantity(checked(Value + other.Value), UnitCode);
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", name)
            : value.Trim();
}
