using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>Optional lane U1: the Authenticode thumbprint rule, with throwaway self-signed certificates made in the test.</summary>
public sealed class InstallerAuthenticodeCheckTests
{
    private sealed class FakeVerifier(AuthenticodeInspection? result, bool throws = false) : IAuthenticodeVerifier
    {
        public int Calls { get; private set; }
        public AuthenticodeInspection Inspect(string filePath)
        {
            Calls++;
            if (throws) throw new InvalidOperationException("api");
            return result!;
        }
    }

    private static string NewThumbprint()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Throwaway ETP Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return cert.Thumbprint;
    }

    [Fact]
    public void Empty_list_is_not_set_up_and_does_not_even_look_at_the_file()
    {
        var verifier = new FakeVerifier(new(AuthenticodeSignatureState.NotSigned, null));
        var result = InstallerAuthenticodeCheck.Evaluate("setup.exe", verifier, []);
        Assert.Equal(InstallerAuthenticodeOutcome.NotConfigured, result.Outcome);
        Assert.True(result.MayLaunch);
        Assert.Null(result.RefusalMessage);
        Assert.Equal(0, verifier.Calls);
        Assert.False(ReleasePublisherCertificates.AreConfigured);
    }

    [Fact]
    public void Valid_signature_from_the_pinned_certificate_passes_ignoring_case_and_spaces()
    {
        var thumbprint = NewThumbprint();
        var spaced = string.Join(' ', Enumerable.Range(0, thumbprint.Length / 2).Select(i => thumbprint.Substring(i * 2, 2))).ToLowerInvariant();
        var result = InstallerAuthenticodeCheck.Evaluate("setup.exe", new FakeVerifier(new(AuthenticodeSignatureState.Valid, spaced)), [thumbprint]);
        Assert.Equal(InstallerAuthenticodeOutcome.Verified, result.Outcome);
        Assert.True(result.MayLaunch);
    }

    [Fact]
    public void Valid_signature_from_another_certificate_is_refused()
    {
        var result = InstallerAuthenticodeCheck.Evaluate("setup.exe", new FakeVerifier(new(AuthenticodeSignatureState.Valid, NewThumbprint())), [NewThumbprint()]);
        Assert.Equal(InstallerAuthenticodeOutcome.WrongPublisher, result.Outcome);
        Assert.False(result.MayLaunch);
        Assert.False(string.IsNullOrWhiteSpace(result.RefusalMessage));
    }

    [Theory]
    [InlineData(AuthenticodeSignatureState.NotSigned, InstallerAuthenticodeOutcome.NotSigned)]
    [InlineData(AuthenticodeSignatureState.Tampered, InstallerAuthenticodeOutcome.Tampered)]
    [InlineData(AuthenticodeSignatureState.NotTrusted, InstallerAuthenticodeOutcome.NotTrusted)]
    [InlineData(AuthenticodeSignatureState.Unknown, InstallerAuthenticodeOutcome.CouldNotCheck)]
    public void Bad_states_are_refused_with_a_message_even_if_the_thumbprint_matches(AuthenticodeSignatureState state, InstallerAuthenticodeOutcome expected)
    {
        var thumbprint = NewThumbprint();
        var result = InstallerAuthenticodeCheck.Evaluate("setup.exe", new FakeVerifier(new(state, thumbprint)), [thumbprint]);
        Assert.Equal(expected, result.Outcome);
        Assert.False(result.MayLaunch);
        Assert.False(string.IsNullOrWhiteSpace(result.RefusalMessage));
    }

    [Fact]
    public void A_verifier_that_throws_refuses_rather_than_passes()
    {
        var result = InstallerAuthenticodeCheck.Evaluate("setup.exe", new FakeVerifier(null, throws: true), [NewThumbprint()]);
        Assert.Equal(InstallerAuthenticodeOutcome.CouldNotCheck, result.Outcome);
        Assert.False(result.MayLaunch);
    }

    [Fact]
    public void Missing_signer_thumbprint_on_a_valid_state_is_refused()
    {
        var result = InstallerAuthenticodeCheck.Evaluate("setup.exe", new FakeVerifier(new(AuthenticodeSignatureState.Valid, null)), [NewThumbprint()]);
        Assert.Equal(InstallerAuthenticodeOutcome.WrongPublisher, result.Outcome);
    }
}
