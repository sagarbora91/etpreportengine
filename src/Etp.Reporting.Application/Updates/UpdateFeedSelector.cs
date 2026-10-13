namespace Etp.Reporting.Application.Updates;

/// <summary>
/// One version folder of the update feed (<c>ETP-Updates\&lt;version&gt;\</c>) as the Desktop reader found it. A file that
/// is missing, unreadable, too large or still a OneDrive placeholder is null.
/// </summary>
public sealed record UpdateFeedEntry(string FolderName, byte[]? ManifestBytes, byte[]? SignatureBytes);

public enum UpdateFeedRejection
{
    FolderNameNotAVersion,
    ManifestMissing,
    SignatureMissing,
    SignatureNotValid,
    ManifestNotValid,
    /// <summary>The verified manifest names a different version from its folder (a copied or renamed folder).</summary>
    FolderVersionMismatch,
    /// <summary>The manifest's keyId differs from the signature file's: the two files are not one publish.</summary>
    KeyIdMismatch,
    NotNewer,
    NeedsEarlierRelease,
}

public sealed record UpdateFeedFinding(string FolderName, UpdateFeedRejection Rejection, ReleaseSignatureOutcome? Signature, ReleaseManifestProblem? Manifest, string? Detail);

public enum UpdateFeedStatus
{
    /// <summary>A newer release verified and may be offered.</summary>
    UpdateAvailable,
    /// <summary>Everything that verified is this version or older.</summary>
    UpToDate,
    /// <summary>No key is compiled in: "Updates are not set up on this PC". Nothing in the feed was read as trusted.</summary>
    NotSetUp,
    /// <summary>The feed folder is missing or could not be listed (OneDrive not signed in, path wrong).</summary>
    FeedUnavailable,
}

public sealed record UpdateFeedChoice(UpdateFeedStatus Status, ReleaseManifest? Offer, string? OfferFolderName, IReadOnlyList<UpdateFeedFinding> Findings)
{
    /// <summary>
    /// Something in the feed failed its signature or did not match itself. Not an error for the Owner to fix by clicking:
    /// the "last check" line says the feed holds files ETP will not trust, and Sagar looks at the folder.
    /// </summary>
    public bool HasUntrustedEntries => Findings.Any(f => f.Rejection is UpdateFeedRejection.SignatureNotValid or UpdateFeedRejection.ManifestNotValid
        or UpdateFeedRejection.FolderVersionMismatch or UpdateFeedRejection.KeyIdMismatch);
}

/// <summary>
/// 1.12 (B1). Chooses what to offer from the feed. There is no "latest" pointer (a half-synced pointer cannot exist): every
/// version folder is checked and the highest release that verifies AND may be installed from the running build is offered.
/// A newer release that needs an earlier step first is reported, and the earlier step (if present and valid) is offered.
/// Order of checks per folder: name, both files present, signature over the exact bytes, then (only then) the manifest is
/// parsed, its version must equal the folder name and its keyId the signature's.
/// </summary>
public static class UpdateFeedSelector
{
    public static UpdateFeedChoice Choose(ReleaseVersion running, IEnumerable<UpdateFeedEntry>? entries) =>
        Choose(running, entries, ReleaseSigningKeys.Trusted);

    public static UpdateFeedChoice Choose(ReleaseVersion running, IEnumerable<UpdateFeedEntry>? entries, IReadOnlyList<ReleaseSigningKey> trusted)
    {
        if (trusted.Count == 0) return new(UpdateFeedStatus.NotSetUp, null, null, []);
        if (entries is null) return new(UpdateFeedStatus.FeedUnavailable, null, null, []);

        var findings = new List<UpdateFeedFinding>();
        var offerable = new List<(ReleaseManifest Manifest, string Folder)>();
        foreach (var entry in entries)
        {
            if (!ReleaseVersion.TryParse(entry.FolderName, out var folderVersion))
            {
                findings.Add(new(entry.FolderName, UpdateFeedRejection.FolderNameNotAVersion, null, null, null));
                continue;
            }
            if (entry.ManifestBytes is null) { findings.Add(new(entry.FolderName, UpdateFeedRejection.ManifestMissing, null, null, null)); continue; }
            if (entry.SignatureBytes is null) { findings.Add(new(entry.FolderName, UpdateFeedRejection.SignatureMissing, null, null, null)); continue; }

            var signature = ReleaseSignatureVerifier.Verify(entry.ManifestBytes, entry.SignatureBytes, trusted);
            if (!signature.IsVerified)
            {
                findings.Add(new(entry.FolderName, UpdateFeedRejection.SignatureNotValid, signature.Outcome, null, null));
                continue;
            }
            var parsed = ReleaseManifestParser.Parse(entry.ManifestBytes);
            if (parsed.Manifest is not { } manifest)
            {
                findings.Add(new(entry.FolderName, UpdateFeedRejection.ManifestNotValid, signature.Outcome, parsed.Problem, parsed.Field));
                continue;
            }
            if (manifest.Version != folderVersion)
            {
                findings.Add(new(entry.FolderName, UpdateFeedRejection.FolderVersionMismatch, signature.Outcome, null, manifest.Version.ToString()));
                continue;
            }
            if (!string.Equals(manifest.KeyId, signature.KeyId, StringComparison.Ordinal))
            {
                findings.Add(new(entry.FolderName, UpdateFeedRejection.KeyIdMismatch, signature.Outcome, null, null));
                continue;
            }
            switch (UpdateVersionRules.Evaluate(running, manifest))
            {
                case UpdateVersionVerdict.NotNewer:
                    findings.Add(new(entry.FolderName, UpdateFeedRejection.NotNewer, signature.Outcome, null, null));
                    break;
                case UpdateVersionVerdict.NeedsEarlierRelease:
                    findings.Add(new(entry.FolderName, UpdateFeedRejection.NeedsEarlierRelease, signature.Outcome, null, manifest.MinimumFromVersion.ToString()));
                    break;
                default:
                    offerable.Add((manifest, entry.FolderName));
                    break;
            }
        }

        if (offerable.Count == 0) return new(UpdateFeedStatus.UpToDate, null, null, findings);
        var best = offerable.OrderByDescending(o => o.Manifest.Version).First();
        return new(UpdateFeedStatus.UpdateAvailable, best.Manifest, best.Folder, findings);
    }
}
