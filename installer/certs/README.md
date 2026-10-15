# installer/certs

This folder holds only the PUBLIC ETP code-signing certificate, `etp-codesign.cer`.

- It is added here by Sagar after the certificate ceremony (`scripts\release\New-EtpCodeSigningCertificate.ps1`).
  Until then this folder holds only this README.
- The `.cer` is safe to commit: it contains no private key. The private key (`etp-codesign.pfx`) lives only on the two USB
  sticks and is protected by a passphrase kept on paper. `.gitignore` already blocks `*.pfx`. Never copy a PFX here.
- Each PC trusts the certificate by hand: `scripts\Install-EtpPublisherCertificate.ps1` (elevated), after comparing the
  fingerprint with the paper card. Setup never installs it.
- The fingerprint is not recorded in this file on purpose: a fingerprint next to the certificate proves nothing. Use the paper card.

See `docs/security/CODE-SIGNING.md`.
