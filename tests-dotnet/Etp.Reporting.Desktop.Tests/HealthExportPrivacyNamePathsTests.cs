using Etp.Reporting.Desktop.Modules.HealthCheck;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.2 L9 (Security audit S-16): a sample person name, in the shapes it takes in an error row or a free-text
/// field, goes through EVERY guard path and must come out redacted and counted. A bare single word that cannot be
/// told from a code word is the guard's known limit; probes keep names out at the source (tokens never carry row
/// data), which is why every shape below carries something the guard can see. All values are synthetic.
/// </summary>
public sealed class HealthExportPrivacyNamePathsTests
{
    public static TheoryData<string> NameShapes => new()
    {
        "Ramesh Kumar 9876543210",
        "Ramesh Kumar, 98765-43210",
        "ramesh.kumar@example.com",
        "Ramesh/Kumar",
        "O\"Brien Ramesh",
        "Ramesh's \"shop\"",
        "रमेश कुमार",
        "Ramesh & Sons",
        "Kumar\\Ramesh",
        "Ramesh Kumar HE-0012345",
    };

    private static HealthExportPrivacyGuard Guard() => new(["Backup finished for {store}."]);

    [Theory]
    [MemberData(nameof(NameShapes))]
    public void A_name_is_redacted_on_every_path(string name)
    {
        var guard = Guard();
        Assert.Equal(HealthExportPrivacyGuard.Redacted, guard.Token(name));
        Assert.Equal(HealthExportPrivacyGuard.Redacted, guard.Text(name));
        Assert.Equal(HealthExportPrivacyGuard.Redacted, guard.TokenOrText(name));
        Assert.Equal(HealthExportPrivacyGuard.Redacted, guard.FingerprintValue(name));
        Assert.Equal(HealthExportPrivacyGuard.Redacted, guard.ReferenceValue(name));
        Assert.Equal(HealthExportPrivacyGuard.Redacted, guard.Key(name, new HashSet<string>()));
        Assert.Equal(6, guard.Redactions);
    }

    [Theory]
    [MemberData(nameof(NameShapes))]
    public void A_name_inside_a_template_placeholder_is_redacted(string name)
    {
        var guard = Guard();
        Assert.Equal(HealthExportPrivacyGuard.Redacted, guard.Text("Backup finished for " + name + "."));
        Assert.Equal(1, guard.Redactions);
    }
}
