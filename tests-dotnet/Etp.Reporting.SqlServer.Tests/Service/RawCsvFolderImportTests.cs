using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// Lane L9: the folder import reads .csv exports with its default reader and imports them like workbooks. A Retail R025
/// shape is used because it is catalogued on every branch; Service raw exports take the same path (no SQL here).
/// </summary>
public sealed class RawCsvFolderImportTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EtpRawFolder-" + Guid.NewGuid().ToString("N"));

    public RawCsvFolderImportTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string SalesCsv(params string[] invoices)
    {
        string Cell(string header, string invoice) => header switch
        {
            "TRANS_TYPE" => "INV", "STORE CODE" => "HEMW", "ITEMNUMBER" => "TEST-001", "INVNUMBER" => invoice,
            "INVDATE" => "20260825", "QTY" => "1", "NETAMOUNT" => "118.00", "NETVALUE" => "100.00", "TAX" => "18.00", _ => ""
        };
        var headers = RetailSalesProfiles.R025Headers;
        return string.Join("\r\n", [string.Join(",", headers.Select(header => $"\"{header}\"")),
            .. invoices.Select(invoice => string.Join(",", headers.Select(header => $"\"{Cell(header, invoice)}\"")))]) + "\r\n";
    }

    [Fact]
    public async Task A_csv_in_a_folder_is_read_matched_and_persisted_with_its_bytes_as_evidence()
    {
        var path = Path.Combine(folder, "R025_SDB_VariantwiseSales.csv");
        await File.WriteAllTextAsync(path, SalesCsv("100000001", "100000002"));
        var persistence = new CapturePersistence();

        var summary = await new FolderImportService(persistence).RunAsync(folder, new("raw-csv-test"));

        var file = Assert.Single(summary.Files);
        Assert.Equal("R025_SDB_VariantwiseSales.csv", file.FileName);
        Assert.Equal("Imported", file.Status);
        Assert.Equal("R025", file.ReportCode);
        Assert.Equal("HEMW", file.StoreCode);
        Assert.Equal(new DateOnly(2026, 8, 25), file.PeriodEnd);
        var request = Assert.Single(persistence.Requests);
        Assert.Equal(2, request.AcceptedImport.Staging.Rows.Count);
        Assert.Equal(await File.ReadAllBytesAsync(path), request.AcceptedImport.Workbook.EvidenceBytes.ToArray());
    }

    [Fact]
    public async Task A_csv_whose_header_matches_nothing_is_an_unknown_layout_not_a_failure()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "GPRC CLAIM 06.10.2026 TO 09.10.2026.csv"),
            "\"Synthetic A\",\"Synthetic B\"\r\n\"1\",\"2\"\r\n");

        var summary = await new FolderImportService(new CapturePersistence()).RunAsync(folder, new("raw-csv-test"));

        Assert.Equal("Unknown layout", Assert.Single(summary.Files).Status);
        Assert.Equal(0, summary.Failed);
    }

    [Fact]
    public async Task An_undated_csv_that_matches_nothing_is_skipped_not_an_unknown_layout()
    {
        // The shape of F:/ETP/ETP Source Data/HEMW/golden-monthly-HEMW-R025.csv: a check file in a Retail folder, not an export.
        await File.WriteAllTextAsync(Path.Combine(folder, "golden-monthly-HEMW-R025.csv"),
            "store,year,month,invoices\r\nHEMW,2026,8,12\r\n");

        var summary = await new FolderImportService(new CapturePersistence()).RunAsync(folder, new("raw-csv-test"));

        Assert.Equal("Not needed", Assert.Single(summary.Files).Status);
        Assert.Equal(0, summary.UnknownLayouts);
        Assert.Equal(0, summary.Failed);
    }

    private sealed class CapturePersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public List<ImportPersistenceRequest<MatchedImportEnvelope>> Requests { get; } = [];
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(null);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, request.AcceptedImport.Staging.Rows.Count));
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
    }
}
