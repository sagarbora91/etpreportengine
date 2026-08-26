using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class ProductisationServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "EtpProductisationTests", Guid.NewGuid().ToString("N"));

    public ProductisationServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public void Accounting_composer_requires_mappings_and_balances_signed_events()
    {
        var events = new[]
        {
            new AccountingBusinessEvent("NET_SALES", 100m, "WLMHW/20260825", "Net sales"),
            new AccountingBusinessEvent("RETURN", -10m, "WLMHW/20260825", "Sales return")
        };
        var incomplete = new AccountingBatchComposer().Compose(events,
            [new("NET_SALES", "Tender Control", "Sales", "{description} {reference}")]);
        Assert.False(incomplete.IsBalanced);
        Assert.Equal(["RETURN"], incomplete.MissingMappings);

        var complete = new AccountingBatchComposer().Compose(events,
            [new("NET_SALES", "Tender Control", "Sales", "{description} {reference}"), new("RETURN", "Sales Return", "Tender Control", "{description}")]);
        Assert.True(complete.IsBalanced);
        Assert.Equal(110m, complete.DebitTotal);
        Assert.Equal(110m, complete.CreditTotal);
        Assert.Equal(4, complete.Entries.Count);
    }

    [Fact]
    public async Task Tally_export_rejects_unbalanced_batch_and_emits_controlled_xml()
    {
        var exporter = new TallyXmlExportService();
        var path = Path.Combine(root, "batch.xml");
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(path, "Company", new(2026,8,25), new([], 10m, 9m, false, [])));

        var balanced = new AccountingBatchDraft([
            new(1,"NET_SALES","Bank",100m,0,"Net sales",null,"scope"),
            new(2,"NET_SALES","Sales",0,100m,"Net sales",null,"scope")],100m,100m,true,[]);
        var hash = await exporter.ExportAsync(path, "Saagar & Traders", new(2026,8,25), balanced);
        Assert.Equal(64, hash.Length);
        var xml = await File.ReadAllTextAsync(path);
        Assert.Contains("Saagar &amp; Traders", xml, StringComparison.Ordinal);
        Assert.Contains("20260825", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Managed_document_repository_deduplicates_by_content_and_preserves_original()
    {
        var source = Path.Combine(root, "invoice.pdf");
        await File.WriteAllTextAsync(source, "%PDF-1.4\n(Invoice ABC123) Tj\n%%EOF");
        var repository = Path.Combine(root, "managed");
        var first = await ManagedDocumentRepository.StoreAsync(source, repository);
        var second = await ManagedDocumentRepository.StoreAsync(source, repository);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.ManagedPath, second.ManagedPath);
        Assert.Equal("%PDF-1.4\n(Invoice ABC123) Tj\n%%EOF", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task Native_pdf_extraction_uses_text_layer_without_ocr()
    {
        var source = Path.Combine(root, "text.pdf");
        await File.WriteAllTextAsync(source, "%PDF-1.4\n(Invoice ABC123 supplier value 10000 GST included) Tj\n%%EOF");
        var result = await new NativePdfTextExtractor().ExtractAsync(source);
        Assert.Equal("NATIVE_PDF", result.Method);
        Assert.Contains("ABC123", result.Text, StringComparison.Ordinal);
        Assert.Equal("REVIEW_REQUIRED", result.ReviewStatus);
    }

    [Fact]
    public void WhatsApp_link_is_official_and_does_not_claim_file_attachment()
    {
        var uri = SafeShareLauncher.CreateWhatsAppUri("Report ready", "+91 98765 43210");
        Assert.Equal("wa.me", uri.Host);
        Assert.Contains("919876543210", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("Report%20ready", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }
}
