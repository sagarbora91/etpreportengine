using System.IO.Compression;
using System.Text.Json;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class ReportPackageServiceTests
{
    [Fact]
    public async Task Package_contains_excel_pdf_and_hashed_manifest_without_overwriting_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpPackageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "ETP_ReportPack_2026-08-25_Gen03.zip");
        try
        {
            var data = new ExcelReportData([new("Metric"),new("Value","#,##0.00")], [["Net Sales",100m]], ["Total",100m]);
            var document = new ReportPackDocument("Daily Pack",new(2026,8,25),new(2026,8,25),"Passed","v-test","Complete",DateTimeOffset.UtcNow,[new("Sales","Passed","Canonical",data)]);
            var result = await new ReportPackageService().CreateAsync(path,document,3,"WLMHW",true,"Owner");
            Assert.Equal(64,result.Sha256.Length);
            Assert.Equal(2,result.Files.Count);
            using var archive=ZipFile.OpenRead(path);
            Assert.Contains(archive.Entries,x=>x.FullName.EndsWith("Report-Pack.xlsx",StringComparison.Ordinal));
            Assert.Contains(archive.Entries,x=>x.FullName.EndsWith("Report-Pack.pdf",StringComparison.Ordinal));
            var manifestEntry=Assert.Single(archive.Entries,x=>x.FullName.EndsWith("manifest.json",StringComparison.Ordinal));
            using var reader=new StreamReader(manifestEntry.Open());var manifest=JsonDocument.Parse(await reader.ReadToEndAsync());
            Assert.Equal(3,manifest.RootElement.GetProperty("Generation").GetInt32());
            Assert.Equal("Final",manifest.RootElement.GetProperty("Status").GetString());
        }
        finally { try { Directory.Delete(root,true); } catch(IOException) { } }
    }
}
