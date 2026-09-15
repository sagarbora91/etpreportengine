using Etp.Reporting.Import.Batch;

namespace Etp.Reporting.Desktop.Tests;

public sealed class HandledFailureDiagnosticsTests
{
    [Fact]
    public void Friendly_error_preserves_the_safe_import_source_contract()
    {
        var exception = new ImportSourceException("IMPORT_TYPE_UNSUPPORTED", "Only supported import sources are allowed.");

        Assert.Equal("Only supported import sources are allowed.", DesktopFriendlyError.Describe(exception));
        Assert.Equal(
            "The action could not be completed. Technical details are available in the support package.",
            DesktopFriendlyError.Describe(new Exception("server=C:/sensitive/source.xlsx")));
    }

}
