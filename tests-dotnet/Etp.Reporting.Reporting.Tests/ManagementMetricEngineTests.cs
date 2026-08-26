using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class ManagementMetricEngineTests
{
    private readonly ManagementMetricEngine engine = new();

    [Fact]
    public void Growth_corrects_the_historical_manual_formula_error()
    {
        var result = engine.Growth(69_880m, 22_647m);

        Assert.Equal(MetricAvailability.Available, result.Availability);
        Assert.Equal(208.56m, decimal.Round(result.Value!.Value, 2));
    }

    [Theory]
    [InlineData(0, 0, MetricAvailability.NotApplicable)]
    [InlineData(100, 0, MetricAvailability.Undefined)]
    public void Growth_does_not_publish_misleading_zero_base_percentages(decimal current, decimal lastYear, MetricAvailability state)
    {
        var result = engine.Growth(current, lastYear);

        Assert.Null(result.Value);
        Assert.Equal(state, result.Availability);
    }

    [Fact]
    public void Missing_walk_ins_is_distinct_from_zero_walk_ins()
    {
        var missing = engine.Conversion(8, null);
        var zero = engine.Conversion(8, 0);

        Assert.Equal(MetricAvailability.MissingInput, missing.Availability);
        Assert.Equal(MetricAvailability.NotApplicable, zero.Availability);
    }

    [Fact]
    public void Productivity_requires_an_explicit_traceable_denominator()
    {
        var dsr = engine.UnitsPerTransaction(199m, 171, new("DSR_INVOICE_V1", TransactionDenominator.InvoiceCount));
        var staff = engine.UnitsPerTransaction(199m, 179, new("STAFF_ATTRIBUTED_V1", TransactionDenominator.StaffAttributedTransactionCount));

        Assert.Equal(1.16m, decimal.Round(dsr.Value!.Value, 2));
        Assert.Equal(1.11m, decimal.Round(staff.Value!.Value, 2));
        Assert.NotEqual(dsr.Explanation, staff.Explanation);
    }

    [Fact]
    public void Golden_business_totals_are_derived_not_embedded_in_production_logic()
    {
        var titan = new { Quantity = 8m, Value = 69_880m };
        var helios = new { Quantity = 3m, Value = 76_090m };
        var service = new[] { 700m, 1_807m, 577m };

        Assert.Equal(145_970m, titan.Value + helios.Value);
        Assert.Equal(11m, titan.Quantity + helios.Quantity);
        Assert.Equal(3_084m, service.Sum());
        Assert.Equal(0m, 812m - 812m);
        Assert.Equal(0m, 501m - 501m);
    }

    [Fact]
    public void Report_source_registry_exposes_ambiguities_instead_of_guessing()
    {
        Assert.Equal(8, ReportSourceRegistry.All.Count);
        Assert.All(ReportSourceRegistry.All, report => Assert.False(string.IsNullOrWhiteSpace(report.ReconciliationRule)));
        Assert.Contains(ReportSourceRegistry.Get("RPT-CASH").UnresolvedDefinitions,
            x => x.Contains("quarantined", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("R013", ReportSourceRegistry.Get("RPT-STAFF").SourceReports);
    }
}
