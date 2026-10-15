# ETP code signing

Status: lane S1 of the code-protection plan (decisions D5, D10). Self-signed Authenticode certificate, RSA-3072, SHA-256, valid
10 years. No passphrase, key or private card is stored in this repository.

## What it is for, and not for

For: tamper-evidence on the shipped exe, ETP assemblies, scripts and installer (closes AUDIT-2 S-14); lets scheduled tasks on
shop PCs move to `AllSigned`; UAC shows the publisher name on PCs that trust the certificate.

Not for: hiding code (see RELEASE-PROTECTION.md), or SmartScreen reputation. SmartScreen can still say "unrecognised app" for a
new installer; a self-signed certificate never earns reputation. Staff are told to expect it.

## Custody

The private key exists only as `etp-codesign.pfx` on the two USB sticks, protected by a passphrase (16+ characters) kept on paper
apart from the sticks, together with the release-key passphrase. It is used from the stick at build time and never imported into the
Windows certificate store. Claude never asks for the stick, the PFX or the passphrase and never sees them: the passphrase is typed at
a hidden prompt (SecureString) by Sagar, never passed on a command line, never logged.

## Ceremony (once, Sagar, about 20 minutes)

1. Plug in both USB sticks. Open PowerShell in the repository.
2. Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& .\scripts\release\New-EtpCodeSigningCertificate.ps1 -UsbFolder 'F:\ETP-Key','G:\ETP-Key'"` (`-Command`, not `-File`: `-File` hands the two folders over as one text and the script refuses)
   (use your drive letters; add `-AllowFixedDrive` if Windows reports a stick as a fixed disk and you are sure it is a stick).
3. Type a passphrase of 16+ characters twice. Write it on paper.
4. The script writes the PFX, the public `.cer` and a fingerprint card to both sticks, reads each back, and prints the SHA-1
   thumbprint and the SHA-256 fingerprint. Write BOTH on the paper.
5. Copy `etp-codesign.cer` from a stick to `installer\certs\` in the repository and tell Claude. Eject the sticks; keep them in two places.

The script refuses system, network, OneDrive and non-removable drives and never replaces an existing PFX.

## Signing a release

`scripts\build-windows-installer.ps1 -PfxPath F:\ETP-Key\etp-codesign.pfx -TimestampServer https://timestamp.digicert.com`
(add `-Protect` for the obfuscated build). It asks the passphrase once, builds, refuses a release folder containing `*.pdb`,
`Mapping.txt` or `obfuscation-report.json`, signs the exe, every `Etp.*.dll`, every `.ps1`/`.psm1` and then the installer, refreshes
the release `SHA256SUMS.txt`, and verifies every signature (one signer, timestamp present). The older `-CertificateThumbprint` path
(certificate in the user store) still works. A timestamp service (internet) is mandatory.

Before trust, Windows reports the signature of a self-signed file as `UnknownError` / `NotTrusted`. The scripts accept that status
only when the signer thumbprint is ours and a timestamp is present; `HashMismatch` and `NotSigned` always fail.

## Trusting the certificate on a PC (Sagar, each PC, by hand)

Open PowerShell as administrator and run `scripts\Install-EtpPublisherCertificate.ps1 -CertificatePath <path to etp-codesign.cer>`.
It prints the subject and both fingerprints. Compare them with the paper card; if both match, type `YES`. It then adds the public
certificate to Local Machine Trusted Root and Trusted Publishers. Setup never does this and nothing here is silent. Trusting means
"anything signed with this key is trusted on this PC", which is why custody equals the release key's. To undo: `certlm.msc`, remove
"Saagar Traders - ETP Reporting Engine" from both stores.

## Renewal and loss

Timestamped signatures stay valid after the certificate expires. Rotation (or loss of both sticks or the passphrase) means a new
ceremony, a new `.cer` in `installer\certs\`, and the trust script again on every PC. Record key dates (no secrets) in SECURITY.md.

## Checks

`scripts\release\tests\Signing.Tests.ps1` (plain PowerShell, no Pester) proves the refusals and, when a timestamp URL is supplied,
signs a throwaway file with a throwaway certificate. After install, `Get-AuthenticodeSignature <file>` shows `Valid` on a PC that
trusts the certificate.
