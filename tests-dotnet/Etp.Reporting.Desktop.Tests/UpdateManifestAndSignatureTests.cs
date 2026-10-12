using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Application.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 (B1, plan section B tests): the release.json parser, the detached signature (good, bad, wrong key, unknown keyId),
/// the version rules (never older, minimumFromVersion) and the feed choice. Throwaway keys made in the test only.
/// </summary>
public sealed class UpdateManifestAndSignatureTests
{
    // ------------------------------------------------------------ versions

    [Theory]
    [InlineData("1.12.0", true)]
    [InlineData("0.0.0", true)]
    [InlineData("10.20.300", true)]
    [InlineData("1.12", false)]
    [InlineData("1.12.0.0", false)]
    [InlineData("v1.12.0", false)]
    [InlineData("1.12.0-rc", false)]
    [InlineData("1.012.0", false)]
    [InlineData(" 1.12.0", false)]
    [InlineData("1.-1.0", false)]
    [InlineData("", false)]
    public void A_release_number_is_exactly_three_whole_numbers(string text, bool valid) =>
        Assert.Equal(valid, ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void Versions_order_numerically_and_the_running_build_reads_from_its_informational_version()
    {
        Assert.True(ReleaseVersion.Parse("1.12.0") > ReleaseVersion.Parse("1.9.9"));
        Assert.True(ReleaseVersion.Parse("1.12.10") > ReleaseVersion.Parse("1.12.9"));
        Assert.True(ReleaseVersion.TryParseRunning("1.12.0+9d9ae21abcdef", out var running) && running == new ReleaseVersion(1, 12, 0));
        Assert.True(ReleaseVersion.TryParseRunning("1.12.0.0", out var four) && four == new ReleaseVersion(1, 12, 0));
        Assert.False(ReleaseVersion.TryParseRunning("1.12.0.3", out _));
        Assert.False(ReleaseVersion.TryParseRunning(null, out _));
    }

    // ------------------------------------------------------------ parser

    [Fact]
    public void A_complete_schema_2_manifest_parses_and_a_byte_order_mark_is_allowed()
    {
        var bytes = Manifest("1.12.0", "0123456789abcdef");
        var parsed = ReleaseManifestParser.Parse(bytes);
        var manifest = Assert.IsType<ReleaseManifest>(parsed.Manifest);
        Assert.Equal(new ReleaseVersion(1, 12, 0), manifest.Version);
        Assert.Equal("EtpReportingEngine-Setup-1.12.0-x64.exe", manifest.InstallerFileName);
        Assert.Equal(1234L, manifest.InstallerBytes);
        Assert.Equal(Sha, manifest.InstallerSha256);
        Assert.Equal(new ReleaseVersion(1, 11, 0), manifest.MinimumFromVersion);
        Assert.True(manifest.HasSchemaChange);
        Assert.Equal("0123456789abcdef", manifest.KeyId);

        byte[] withBom = [0xEF, 0xBB, 0xBF, .. bytes];
        Assert.True(ReleaseManifestParser.Parse(withBom).Succeeded);
    }

    public static TheoryData<string, object?, ReleaseManifestProblem> Refusals => new()
    {
        { "schema", 1, ReleaseManifestProblem.UnsupportedSchema },
        { "product", "Other Product", ReleaseManifestProblem.WrongProduct },
        { "version", "1.12", ReleaseManifestProblem.BadVersion },
        { "installer", @"..\..\Windows\evil.exe", ReleaseManifestProblem.BadInstallerName },
        { "installer", "EtpReportingEngine-Setup-1.11.0-x64.exe", ReleaseManifestProblem.BadInstallerName },
        { "installerBytes", 0L, ReleaseManifestProblem.BadInstallerSize },
        { "sha256", "ABC", ReleaseManifestProblem.BadHash },
        { "commit", "not-hex", ReleaseManifestProblem.BadCommit },
        { "publishedUtc", "yesterday", ReleaseManifestProblem.BadDate },
        { "minimumFromVersion", "1.12.0", ReleaseManifestProblem.BadMinimumVersion },
        { "minimumFromVersion", "1.13.0", ReleaseManifestProblem.BadMinimumVersion },
        { "notes", new string('n', 4001), ReleaseManifestProblem.NotesTooLong },
        { "keyId", "0123456789ABCDEF", ReleaseManifestProblem.BadKeyId },
        { "hasSchemaChange", "yes", ReleaseManifestProblem.WrongType },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_manifest_with_a_bad_field_is_refused_and_the_field_is_named(string field, object? value, ReleaseManifestProblem problem)
    {
        var parsed = ReleaseManifestParser.Parse(Manifest("1.12.0", "0123456789abcdef", change: f => f[field] = value));
        Assert.False(parsed.Succeeded);
        Assert.Equal(problem, parsed.Problem);
        Assert.Equal(field, parsed.Field);
    }

    [Fact]
    public void Missing_or_duplicated_fields_and_non_json_are_refused()
    {
        var missing = ReleaseManifestParser.Parse(Manifest("1.12.0", "0123456789abcdef", change: f => f.Remove("sha256")));
        Assert.Equal((ReleaseManifestProblem.MissingField, "sha256"), (missing.Problem, missing.Field));

        var text = Encoding.UTF8.GetString(Manifest("1.12.0", "0123456789abcdef"));
        var duplicated = text.Replace("\"notes\":", "\"version\":\"9.9.9\",\"notes\":", StringComparison.Ordinal);
        Assert.Equal(ReleaseManifestProblem.DuplicateField, ReleaseManifestParser.Parse(Encoding.UTF8.GetBytes(duplicated)).Problem);

        Assert.Equal(ReleaseManifestProblem.NotJson, ReleaseManifestParser.Parse("not json"u8).Problem);
        Assert.Equal(ReleaseManifestProblem.NotJson, ReleaseManifestParser.Parse("[1,2]"u8).Problem);
        Assert.Equal(ReleaseManifestProblem.TooLarge, ReleaseManifestParser.Parse(new byte[ReleaseManifest.MaximumBytes + 1]).Problem);
    }

    // ------------------------------------------------------------ signature

    [Fact]
    public void A_good_signature_verifies_and_one_changed_byte_does_not()
    {
        var (key, trusted) = NewKey();
        using (key)
        {
            var manifest = Manifest("1.12.0", trusted.KeyId);
            var signature = Sign(key, trusted.KeyId, manifest);
            Assert.Equal(ReleaseSignatureOutcome.Verified, ReleaseSignatureVerifier.Verify(manifest, signature, [trusted]).Outcome);

            // The same text with one more space (a re-serialisation) is a different file: no canonicalisation.
            var tampered = manifest.ToArray();
            tampered[^2] ^= 0x01;
            Assert.Equal(ReleaseSignatureOutcome.BadSignature, ReleaseSignatureVerifier.Verify(tampered, signature, [trusted]).Outcome);
            byte[] spaced = [.. manifest, (byte)' '];
            Assert.Equal(ReleaseSignatureOutcome.BadSignature, ReleaseSignatureVerifier.Verify(spaced, signature, [trusted]).Outcome);
        }
    }

    [Fact]
    public void A_signature_by_another_key_under_the_trusted_key_id_fails_and_an_unknown_key_id_is_named()
    {
        var (key, trusted) = NewKey();
        var (other, otherTrusted) = NewKey();
        using (key)
        using (other)
        {
            var manifest = Manifest("1.12.0", trusted.KeyId);
            // Someone with the shared OneDrive account signs with their own key and claims Sagar's key id.
            Assert.Equal(ReleaseSignatureOutcome.BadSignature, ReleaseSignatureVerifier.Verify(manifest, Sign(other, trusted.KeyId, manifest), [trusted]).Outcome);
            // ... or uses their own key id, which this build does not trust.
            var unknown = ReleaseSignatureVerifier.Verify(manifest, Sign(other, otherTrusted.KeyId, manifest), [trusted]);
            Assert.Equal((ReleaseSignatureOutcome.UnknownKey, otherTrusted.KeyId), (unknown.Outcome, unknown.KeyId));
            // Rotation: with both keys trusted, either signature verifies.
            Assert.True(ReleaseSignatureVerifier.Verify(manifest, Sign(other, otherTrusted.KeyId, manifest), [trusted, otherTrusted]).IsVerified);
        }
    }

    [Fact]
    public void No_compiled_in_key_means_not_set_up_and_nothing_verifies()
    {
        var (key, trusted) = NewKey();
        using (key)
        {
            var manifest = Manifest("1.12.0", trusted.KeyId);
            Assert.Equal(ReleaseSignatureOutcome.NotConfigured, ReleaseSignatureVerifier.Verify(manifest, Sign(key, trusted.KeyId, manifest), []).Outcome);
            var choice = UpdateFeedSelector.Choose(new ReleaseVersion(1, 11, 3), [new UpdateFeedEntry("1.12.0", manifest, Sign(key, trusted.KeyId, manifest))], []);
            Assert.Equal(UpdateFeedStatus.NotSetUp, choice.Status);
            Assert.Null(choice.Offer);
        }
    }

    [Fact]
    public void Malformed_signature_files_and_bad_trusted_keys_are_refused()
    {
        var (key, trusted) = NewKey();
        using (key)
        {
            var manifest = Manifest("1.12.0", trusted.KeyId);
            var raw = key.SignData(manifest, HashAlgorithmName.SHA256);
            Assert.Equal(ReleaseSignatureOutcome.SignatureFileMalformed, ReleaseSignatureVerifier.Verify(manifest, "{}"u8, [trusted]).Outcome);
            Assert.Equal(ReleaseSignatureOutcome.SignatureFileMalformed, ReleaseSignatureVerifier.Verify(manifest, [], [trusted]).Outcome);
            // A DER-encoded signature (another tool's default) is not the agreed 64-byte format.
            var der = key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            Assert.Equal(ReleaseSignatureOutcome.SignatureFileMalformed, ReleaseSignatureVerifier.Verify(manifest, SignatureFile(trusted.KeyId, der), [trusted]).Outcome);
            Assert.Equal(ReleaseSignatureOutcome.UnsupportedAlgorithm,
                ReleaseSignatureVerifier.Verify(manifest, SignatureFile(trusted.KeyId, raw, "RSA-SHA256"), [trusted]).Outcome);

            // A pasted key whose id does not match its point is never trusted.
            var mistyped = trusted with { KeyId = "ffffffffffffffff" };
            Assert.Equal(ReleaseSignatureOutcome.TrustedKeyInvalid,
                ReleaseSignatureVerifier.Verify(manifest, SignatureFile(mistyped.KeyId, raw), [mistyped]).Outcome);
        }
    }

    [Fact]
    public void The_compiled_in_keys_are_well_formed_public_points_and_no_key_file_is_in_the_repository()
    {
        foreach (var key in ReleaseSigningKeys.Trusted)
        {
            var point = Convert.FromBase64String(key.PublicKey);
            Assert.Equal(65, point.Length);
            Assert.Equal(key.KeyId, ReleaseSigningKey.ComputeKeyId(point));
        }
        Assert.Equal(ReleaseSigningKeys.Trusted.Count > 0, ReleaseSigningKeys.AreConfigured);

        // The private key lives only on the USB sticks: no file in the repository holds an encrypted key file's body.
        var root = RepositoryRoot();
        var offenders = new[] { "src", "scripts", "tests-dotnet", "installer", "database", "docs", "tools" }
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules"))
            .Where(path => new FileInfo(path).Length < 1_000_000)
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("\"ciphertext\"", StringComparison.Ordinal) && text.Contains("etp-release-signing-key", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();
        Assert.True(offenders.Length == 0, "A release signing key file is in the repository: " + string.Join(", ", offenders));
    }

    // ------------------------------------------------------------ version rules and the feed

    [Fact]
    public void Older_and_equal_releases_are_never_offered_and_minimum_from_version_is_respected()
    {
        var running = new ReleaseVersion(1, 11, 3);
        ReleaseManifest M(string version, string minimum) => ReleaseManifestParser.Parse(Manifest(version, "0123456789abcdef", minimum)).Manifest!;
        Assert.Equal(UpdateVersionVerdict.NotNewer, UpdateVersionRules.Evaluate(running, M("1.11.3", "1.11.0")));
        Assert.Equal(UpdateVersionVerdict.NotNewer, UpdateVersionRules.Evaluate(running, M("1.11.2", "1.11.0")));
        Assert.Equal(UpdateVersionVerdict.Offer, UpdateVersionRules.Evaluate(running, M("1.12.0", "1.11.3")));
        Assert.Equal(UpdateVersionVerdict.NeedsEarlierRelease, UpdateVersionRules.Evaluate(running, M("1.13.0", "1.12.0")));
    }

    [Fact]
    public void The_feed_offers_the_highest_release_that_verifies_and_may_be_installed_from_here()
    {
        var (key, trusted) = NewKey();
        var (stranger, strangerKey) = NewKey();
        using (key)
        using (stranger)
        {
            UpdateFeedEntry Signed(string folder, string version, string minimum = "1.11.0")
            {
                var manifest = Manifest(version, trusted.KeyId, minimum);
                return new(folder, manifest, Sign(key, trusted.KeyId, manifest));
            }

            var forged = Manifest("1.14.0", trusted.KeyId);
            var entries = new List<UpdateFeedEntry>
            {
                Signed("1.11.2", "1.11.2"),                               // older: never offered
                Signed("1.12.0", "1.12.0"),                               // the one to offer
                Signed("1.12.1", "1.12.1", minimum: "1.12.0"),            // needs 1.12.0 first
                new("1.14.0", forged, Sign(stranger, trusted.KeyId, forged)), // dropped by someone with the shared account
                Signed("1.15.0", "1.13.0"),                               // folder renamed
                new("1.16.0", Manifest("1.16.0", trusted.KeyId), null),  // still syncing
                new("notes", null, null),
            };
            var choice = UpdateFeedSelector.Choose(new ReleaseVersion(1, 11, 3), entries, [trusted]);

            Assert.Equal(UpdateFeedStatus.UpdateAvailable, choice.Status);
            Assert.Equal(new ReleaseVersion(1, 12, 0), choice.Offer!.Version);
            Assert.Equal("1.12.0", choice.OfferFolderName);
            Assert.True(choice.HasUntrustedEntries);
            UpdateFeedRejection Of(string folder) => choice.Findings.Single(f => f.FolderName == folder).Rejection;
            Assert.Equal(UpdateFeedRejection.NotNewer, Of("1.11.2"));
            Assert.Equal(UpdateFeedRejection.NeedsEarlierRelease, Of("1.12.1"));
            Assert.Equal(UpdateFeedRejection.SignatureNotValid, Of("1.14.0"));
            Assert.Equal(UpdateFeedRejection.FolderVersionMismatch, Of("1.15.0"));
            Assert.Equal(UpdateFeedRejection.SignatureMissing, Of("1.16.0"));
            Assert.Equal(UpdateFeedRejection.FolderNameNotAVersion, Of("notes"));

            // A manifest whose keyId is not the signature's is not one publish.
            var crossed = Manifest("1.12.0", strangerKey.KeyId);
            var mixed = UpdateFeedSelector.Choose(new ReleaseVersion(1, 11, 3), [new UpdateFeedEntry("1.12.0", crossed, Sign(key, trusted.KeyId, crossed))], [trusted]);
            Assert.Equal(UpdateFeedRejection.KeyIdMismatch, Assert.Single(mixed.Findings).Rejection);
            Assert.Equal(UpdateFeedStatus.UpToDate, mixed.Status);

            Assert.Equal(UpdateFeedStatus.FeedUnavailable, UpdateFeedSelector.Choose(new ReleaseVersion(1, 11, 3), null, [trusted]).Status);
            var current = UpdateFeedSelector.Choose(new ReleaseVersion(1, 12, 0), [Signed("1.12.0", "1.12.0")], [trusted]);
            Assert.Equal((UpdateFeedStatus.UpToDate, false), (current.Status, current.HasUntrustedEntries));
        }
    }
}
