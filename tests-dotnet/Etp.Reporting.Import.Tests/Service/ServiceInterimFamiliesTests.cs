using System.Text.Json;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Import.Service;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>
/// Service interim (S-2, decision 15): the family sets every Service lane builds against. They must be disjoint,
/// cover S001-S040, and give each importable family exactly one read rule whose column exists in the frozen
/// scripts/service-centre/families.spec.json (lane L4 builds its views on those names).
/// </summary>
public sealed class ServiceInterimFamiliesTests
{
    private static readonly string[] AllCodes = [.. Enumerable.Range(1, 40).Select(n => $"S{n:000}")];

    [Fact]
    public void The_sets_are_disjoint_and_cover_S001_to_S040()
    {
        IReadOnlySet<string>[] sets = [ServiceInterimFamilies.Importable, ServiceInterimFamilies.Derived,
            ServiceInterimFamilies.NotNeeded, ServiceInterimFamilies.Deferred];
        var all = sets.SelectMany(set => set).ToArray();

        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(AllCodes, all.Order(StringComparer.Ordinal));
        Assert.Equal(35, ServiceInterimFamilies.Importable.Count);
        Assert.Equal(["S001"], ServiceInterimFamilies.Derived);
        Assert.Equal(["S005", "S038"], ServiceInterimFamilies.NotNeeded.Order(StringComparer.Ordinal));
        Assert.Equal(["S027", "S028"], ServiceInterimFamilies.Deferred.Order(StringComparer.Ordinal));
        Assert.All(AllCodes, code => Assert.True(ServiceInterimFamilies.IsServiceCode(code)));
        Assert.False(ServiceInterimFamilies.IsServiceCode("R022"));
    }

    [Theory]
    [InlineData("S001", ServiceInterimFamilies.Codes.FamilyDerived)]
    [InlineData("S005", ServiceInterimFamilies.Codes.ServiceFamilyNotNeeded)]
    [InlineData("S038", ServiceInterimFamilies.Codes.ServiceFamilyNotNeeded)]
    [InlineData("S027", ServiceInterimFamilies.Codes.ServiceFamilyDeferred)]
    [InlineData("S028", ServiceInterimFamilies.Codes.ServiceFamilyDeferred)]
    [InlineData("S009", null)]
    [InlineData("R022", null)]
    [InlineData(null, null)]
    public void Each_family_that_is_not_imported_has_its_reason(string? code, string? expected)
        => Assert.Equal(expected, ServiceInterimFamilies.NotImportedCode(code));

    [Fact]
    public void Every_importable_family_has_exactly_one_read_rule_with_a_column_of_the_spec()
    {
        var spec = LoadSpec();
        Assert.Equal(ServiceInterimFamilies.Importable.Order(StringComparer.Ordinal), ServiceInterimFamilies.ReadRules.Keys.Order(StringComparer.Ordinal));

        foreach (var (code, rule) in ServiceInterimFamilies.ReadRules)
        {
            Assert.Equal(code, rule.ReportCode);
            Assert.True(spec.ContainsKey(code), $"{code} is missing from families.spec.json.");
            var columns = spec[code];
            switch (rule.Kind)
            {
                case ServiceReadRuleKind.DateLog:
                    Assert.NotNull(rule.DateColumn);
                    Assert.Null(rule.JobColumn);
                    AssertColumn(code, columns, rule.DateColumn!, "Date");
                    break;
                case ServiceReadRuleKind.JobList:
                    Assert.Null(rule.DateColumn);
                    Assert.NotNull(rule.JobColumn);
                    AssertColumn(code, columns, rule.JobColumn!, null);
                    break;
                case ServiceReadRuleKind.StateSnapshot:
                    Assert.Null(rule.DateColumn);
                    if (rule.JobColumn is not null) AssertColumn(code, columns, rule.JobColumn, null);
                    break;
            }
        }

        Assert.Equal(["S006", "S009", "S010"], RulesOf(ServiceReadRuleKind.StateSnapshot));
        Assert.Equal(["S003", "S004", "S007", "S008", "S013", "S019", "S022", "S023", "S024", "S025", "S026", "S029", "S039", "S040"],
            RulesOf(ServiceReadRuleKind.DateLog));
        Assert.Equal(["S002", "S011", "S012", "S014", "S015", "S016", "S017", "S018", "S020", "S021", "S030",
            "S031", "S032", "S033", "S034", "S035", "S036", "S037"], RulesOf(ServiceReadRuleKind.JobList));
    }

    [Fact]
    public void The_status_views_pending_lists_and_money_logs_are_importable_and_ranked_once()
    {
        Assert.Equal(["S014", "S015", "S016", "S017", "S018", "S031", "S032", "S033", "S034", "S035"],
            ServiceInterimFamilies.StatusViews.Select(view => view.ReportCode));
        Assert.Equal(["PR", "IR", "SRN", "SRNINV", "DC", "RA", "REPAIRED", "RWR", "PD", "DELIVERED"],
            ServiceInterimFamilies.StatusViews.OrderBy(view => view.LifecycleRank).Select(view => view.StatusLabel));
        Assert.Equal(Enumerable.Range(1, 10), ServiceInterimFamilies.StatusViews.Select(view => view.LifecycleRank).Order());
        Assert.All(ServiceInterimFamilies.StatusViews, view =>
            Assert.Equal(ServiceReadRuleKind.JobList, ServiceInterimFamilies.ReadRules[view.ReportCode].Kind));

        Assert.Equal(["S009", "S010", "S011"], ServiceInterimFamilies.PendingLists.Select(list => list.ReportCode));
        Assert.Equal(ServicePendingLists.All, ServiceInterimFamilies.PendingLists.Select(list => list.ListKey));

        Assert.Equal(["S003", "S004", "S019", "S023", "S024", "S025", "S026", "S039", "S040"],
            ServiceInterimFamilies.MoneyLogs.Order(StringComparer.Ordinal));
        Assert.All(ServiceInterimFamilies.MoneyLogs, code => Assert.Contains(code, ServiceInterimFamilies.Importable));
        Assert.Equal("AW330", ServiceInterimFamilies.ServiceStoreCode);
    }

    private static string[] RulesOf(ServiceReadRuleKind kind) =>
        [.. ServiceInterimFamilies.ReadRules.Values.Where(rule => rule.Kind == kind).Select(rule => rule.ReportCode).Order(StringComparer.Ordinal)];

    private static void AssertColumn(string code, IReadOnlyList<(string Header, string Canonical, string Type)> columns, ServiceColumn column, string? type)
    {
        var matches = columns.Where(c => c.Canonical == column.CanonicalField).ToArray();
        Assert.True(matches.Length == 1, $"{code}: canonical field '{column.CanonicalField}' occurs {matches.Length} times in families.spec.json.");
        Assert.Equal(column.SourceHeader, matches[0].Header);
        if (type is not null) Assert.Equal(type, matches[0].Type);
    }

    private static Dictionary<string, IReadOnlyList<(string Header, string Canonical, string Type)>> LoadSpec()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "service-centre", "families.spec.json")));
        return document.RootElement.EnumerateArray().ToDictionary(
            family => family.GetProperty("FamilyCode").GetString()!,
            family => (IReadOnlyList<(string, string, string)>)[.. family.GetProperty("Columns").EnumerateArray().Select(column => (
                column.GetProperty("SourceHeader").GetString()!,
                column.GetProperty("CanonicalField").GetString()!,
                column.GetProperty("DataType").GetString()!))],
            StringComparer.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }
}
