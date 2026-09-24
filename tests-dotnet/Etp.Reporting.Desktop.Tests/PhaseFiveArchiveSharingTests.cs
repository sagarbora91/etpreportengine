using Etp.Reporting.Desktop.Modules.Archive;

namespace Etp.Reporting.Desktop.Tests;

public sealed class PhaseFiveArchiveSharingTests
{
    [Fact]
    public void WhatsApp_handoff_targets_desktop_protocol_with_escaped_text_and_validated_number()
    {
        var uri = ArchiveShareLauncher.BuildWhatsAppUri("Attach the PDF & review", "+91 (000) 001-2345");
        Assert.StartsWith("whatsapp://send?", uri);
        Assert.Contains("text=Attach%20the%20PDF%20%26%20review", uri);
        Assert.EndsWith("&phone=910000012345", uri);
        Assert.DoesNotContain("phone=", ArchiveShareLauncher.BuildWhatsAppUri("Choose recipient", null));
        Assert.Throws<ArgumentException>(() => ArchiveShareLauncher.BuildWhatsAppUri("test", "123"));
        Assert.Throws<ArgumentException>(() => ArchiveShareLauncher.BuildWhatsAppUri("test", "1234567&text=wrong"));
    }
}
