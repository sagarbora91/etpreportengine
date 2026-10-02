using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;
using static Etp.Reporting.Import.Tests.SyntheticConsolidatedWorkbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class ConsolidationContractReaderTests
{
    private static readonly string[] Stores = ["WLMHW", "HEMW"];

    [Fact]
    public void Contract_layout_is_read_from_Info_and_ETP_Excluded()
    {
        var result = new ConsolidationContractReader().Read(ContractBinWise());

        Assert.True(result.IsContract);
        Assert.Empty(result.Diagnostics);
        var contract = result.Contract!;
        Assert.Equal(1, contract.Header.Version);
        Assert.Equal("R010", contract.Header.FamilyCode);
        Assert.Equal("WLMHW", contract.Header.StoreCode);
        Assert.Equal(ContractRule.Snapshot, contract.Header.Rule);
        Assert.Equal(8, contract.Header.DataRows);
        Assert.Equal(ConsolidationContractLayout.BlockTableColumns, contract.BlockTableHeader);
        Assert.Equal(14, contract.BlockTableHeaderRow);
        Assert.Equal([1, 2, 3], contract.Blocks.Select(block => block.Block!.Value));
        Assert.Equal((5, 7, new DateOnly(2026, 8, 7)), (contract.Blocks[1].FirstRow, contract.Blocks[1].LastRow, contract.Blocks[1].SnapshotDate));
        Assert.Equal(ExportTime.AtMinute(new(2026, 9, 29, 14, 33, 0)), contract.Blocks[2].ExportTime);
        Assert.Equal(["block", "sheet", "row"], contract.ExcludedHeader);
        Assert.Equal([(2, 3), (3, 6)], contract.Excluded.Select(row => (row.Block!.Value, row.Row!.Value)));
    }

    [Fact]
    public void Workbook_without_the_marker_is_not_a_contract()
    {
        Assert.False(new ConsolidationContractReader().Read(LegacyStackedBinWise()).IsContract);
        Assert.False(new ConsolidationContractReader().Read(new WorkbookSnapshot("x.xlsx", 1, Hash, [BinWiseData(1)])).IsContract);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("keys")]
    [InlineData("table")]
    [InlineData("header")]
    public void Unreadable_contract_refuses_the_file_with_CONTRACT_UNREADABLE(string fault)
    {
        var workbook = ContractBinWise();
        var info = workbook.Sheets[1];
        info = fault switch
        {
            "version" => info with { Headers = [ConsolidationContractLayout.Marker, "one"] },
            "keys" => info with { Rows = info.Rows.Where(row => row.RowNumber > 13).ToArray() },
            "table" => info with { Rows = info.Rows.Where(row => row.RowNumber < 13).ToArray() },
            _ => info with { Rows = info.Rows.Select(row => row.RowNumber == 14
                ? Row(14, [.. ConsolidationContractLayout.BlockTableColumns.Select(name => name == "sheet" ? "block" : name)]) : row).ToArray() }
        };
        workbook = workbook with { Sheets = [workbook.Sheets[0], info, workbook.Sheets[2]] };

        var read = new ConsolidationContractReader().Read(workbook);
        var inspection = new MatchedImportEnvelopeFactory(Stores).Inspect(workbook);

        Assert.True(read.IsContract);
        Assert.Null(read.Contract);
        Assert.Equal(ImportCodes.Contract.Unreadable, Assert.Single(read.Diagnostics).Code);
        Assert.False(inspection.Accepted);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.Contract.Unreadable &&
            diagnostic.Severity == ImportDiagnosticSeverity.Blocker);
    }

    [Fact]
    public void Preflight_skips_Info_ETP_Excluded_and_Snapshot_History_as_data_sheets()
    {
        var workbook = ContractBinWise();
        var history = BinWiseData(2, name: ConsolidationContractLayout.HistorySheet);
        history = history with { Headers = [.. history.Headers, "Snapshot_As_Of", "SourceFile"] };
        workbook = workbook with { Sheets = [.. workbook.Sheets, history, BinWiseData(1, name: " snapshot history ")] };

        var result = new ImportPreflight().Inspect(workbook, ApprovedImportProfileRegistry.All);

        Assert.True(result.CanImport, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Code)));
        Assert.Equal("Data", result.Sheet!.Name);
        Assert.Equal("R010", result.Profile!.ReportCode);
        Assert.True(result.Contract.IsContract);
        Assert.True(ImportPreflight.IsNonDataSheet("etp_excluded"));
        Assert.False(ImportPreflight.IsNonDataSheet("Data"));
    }

    [Theory]
    [InlineData("R022", "R022")]
    [InlineData("R001", "R001")]
    public void Contract_family_code_settles_a_header_tie_before_the_file_name(string familyCode, string expected)
    {
        // The file and sheet names say R022; the contract's family code decides.
        var headers = RetailSalesProfiles.R022Headers;
        var info = ContractInfo(SnapshotKeys(familyCode, "HEMW", 0, 0) , []);
        var workbook = new WorkbookSnapshot("R022_Revenue_Report.xlsx", 1, Hash, [new("Revenue Report", 1, headers, []), info]);

        var accepted = new MatchedImportEnvelopeFactory(Stores).RequireAccepted(workbook);

        Assert.Equal(expected, accepted.Profile.ReportCode);
    }

    [Fact]
    public void Contract_store_code_is_used_when_the_data_has_none()
    {
        var accepted = new MatchedImportEnvelopeFactory([]).RequireAccepted(ContractBinWise(dataStore: null, contractStore: "hemw"));

        Assert.Equal("HEMW", accepted.Scope.StoreCode);
        Assert.Equal(8, accepted.Staging.Rows.Count);
    }

    [Fact]
    public void Data_store_wins_over_the_contract_store()
    {
        var accepted = new MatchedImportEnvelopeFactory(Stores).RequireAccepted(ContractBinWise(dataStore: "WLMHW", contractStore: "HEMW"));

        Assert.Equal("WLMHW", accepted.Scope.StoreCode);
    }
}
