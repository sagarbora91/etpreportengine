namespace Etp.Reporting.Reporting;

public enum MetricAvailability { Available, MissingSource, MissingInput, NotApplicable, Undefined }

public sealed record CalculatedMetric(decimal? Value, MetricAvailability Availability, string Explanation)
{
    public static CalculatedMetric Available(decimal value, string explanation) =>
        new(value, MetricAvailability.Available, explanation);
}

public enum TransactionDenominator { InvoiceCount, StaffAttributedTransactionCount }

public sealed record ProductivityRule(string Version, TransactionDenominator Denominator)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version)) throw new ArgumentException("A productivity-rule version is required.");
    }
}

public sealed class ManagementMetricEngine
{
    public CalculatedMetric Growth(decimal? current, decimal? lastYear)
    {
        if (current is null || lastYear is null)
            return new(null, MetricAvailability.MissingSource, "Both current and equivalent prior-year values are required.");
        if (lastYear == 0)
            return current == 0
                ? new(null, MetricAvailability.NotApplicable, "Growth is not applicable when both periods are zero.")
                : new(null, MetricAvailability.Undefined, "Growth is undefined because the prior-year value is zero.");
        return CalculatedMetric.Available(((current.Value / lastYear.Value) - 1m) * 100m,
            "((current / equivalent prior year) - 1) × 100");
    }

    public CalculatedMetric UnitsPerTransaction(decimal? units, int? denominator, ProductivityRule rule) =>
        Divide(units, denominator, rule, "units");

    public CalculatedMetric AverageTransactionValue(decimal? sales, int? denominator, ProductivityRule rule) =>
        Divide(sales, denominator, rule, "sales value");

    public CalculatedMetric Conversion(int? applicableInvoices, decimal? walkIns)
    {
        if (applicableInvoices is null) return new(null, MetricAvailability.MissingSource, "Applicable invoice count is missing.");
        if (walkIns is null) return new(null, MetricAvailability.MissingInput, "Walk-ins have not been entered.");
        if (walkIns == 0) return new(null, MetricAvailability.NotApplicable, "Conversion is not applicable when walk-ins are zero.");
        return CalculatedMetric.Available(applicableInvoices.Value / walkIns.Value * 100m,
            "Applicable invoices / walk-ins × 100");
    }

    private static CalculatedMetric Divide(decimal? numerator, int? denominator, ProductivityRule rule, string numeratorName)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();
        if (numerator is null || denominator is null)
            return new(null, MetricAvailability.MissingSource, $"Both {numeratorName} and the approved denominator are required.");
        if (denominator == 0)
            return new(null, MetricAvailability.NotApplicable, "The metric is not applicable when its approved denominator is zero.");
        return CalculatedMetric.Available(numerator.Value / denominator.Value,
            $"{numeratorName} / {rule.Denominator} ({rule.Version})");
    }
}
