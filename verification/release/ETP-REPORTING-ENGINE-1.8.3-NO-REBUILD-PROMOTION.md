# ETP Reporting Engine 1.8.3 no-rebuild promotion record

Status: **EVIDENCE PREPARED; PROMOTION NOT AUTHORIZED**

This record identifies the existing 1.8.3 payloads that may be evaluated for promotion. It does not rebuild, repackage, sign or authorize them. The implementation is commit `51469bf5a6bb86d9790fc0ffe03573b08c46b319`; checkpoint `08e39c889fe2a13b09aa33ffa788df63f0b800fd` records later verification and contains that implementation commit.

## Frozen payload identities

| Role | Repository-relative path | Bytes | SHA-256 |
|---|---|---:|---|
| Self-contained application | `artifacts/windows-release/Etp.Reporting.Desktop.exe` | 160071154 | `F1F0D5E083D4ADE84DD8E9CC56DCE88123BBCD368C71585BA1A967721844C01F` |
| Bootstrap installer | `artifacts/installer/EtpReportingEngine-Setup-1.8.3-x64.exe` | 48847973 | `40F69FF33469944A61DBB5B443C37D443A4E51125DB1E777D184D87825CFF39F` |
| Offline deployment package | `artifacts/offline-deployment/EtpReportingEngine-Offline-1.8.3.zip` | 48360973 | `8D227842794002AD4D6417CD51D19B274C7D9CA0F153D8F05DC7F9F346B7B69F` |

The application and installer are unsigned. Signing either file later will change its hash and create a different candidate; this record must not be reused for that candidate.

## Evidence set

- `artifacts/windows-release/release.json` — original build manifest: version 1.8.3, runtime `win-x64`, implementation commit prefix `51469bf5a6bb` and build time.
- `verification/release/etp-reporting-engine-1.8.3.provenance.json` — machine-readable artifact, commit and limitation binding.
- `verification/release/etp-reporting-engine-1.8.3.cdx.json` — CycloneDX 1.6 production NuGet dependency inventory generated from the desktop project metadata. It is release evidence stored beside this record, not embedded in the already-built payloads.
- `docs/audit/ETP-CLOSURE-SPRINT-CHECKPOINT-2026-08-28.md` — engineering verification and remaining-gate authority.

## Repeatable verification

Run from the repository root without invoking any build or packaging script:

```powershell
$expected = @{
  'artifacts/windows-release/Etp.Reporting.Desktop.exe' = 'F1F0D5E083D4ADE84DD8E9CC56DCE88123BBCD368C71585BA1A967721844C01F'
  'artifacts/installer/EtpReportingEngine-Setup-1.8.3-x64.exe' = '40F69FF33469944A61DBB5B443C37D443A4E51125DB1E777D184D87825CFF39F'
  'artifacts/offline-deployment/EtpReportingEngine-Offline-1.8.3.zip' = '8D227842794002AD4D6417CD51D19B274C7D9CA0F153D8F05DC7F9F346B7B69F'
}
$expected.GetEnumerator() | ForEach-Object {
  $actual = (Get-FileHash -LiteralPath $_.Key -Algorithm SHA256).Hash
  if ($actual -ne $_.Value) { throw "Hash mismatch: $($_.Key)" }
}
git merge-base --is-ancestor 51469bf5a6bb86d9790fc0ffe03573b08c46b319 08e39c889fe2a13b09aa33ffa788df63f0b800fd
if ($LASTEXITCODE -ne 0) { throw 'Checkpoint does not contain the implementation commit.' }
```

## Promotion gates

- [x] Exact artifact names, sizes and SHA-256 values recorded.
- [x] Release manifest binds version, runtime and implementation commit.
- [x] Implementation/checkpoint ancestry verified.
- [x] Machine-readable SBOM and provenance record present.
- [x] Evidence contains no source rows, PII, secrets or absolute machine paths.
- [ ] Clean-PC elevated SQL bootstrap, reboot and rollback evidence accepted.
- [ ] Target database backup/restore drill accepted.
- [ ] Production hardware, printer, Excel and accessibility acceptance completed.
- [ ] Owner/Manager/Viewer business UAT completed.
- [ ] Required source mappings and Owner decisions supplied.
- [ ] Publisher identity and code-signing certificate supplied; signed artifacts separately hashed and recorded.
- [ ] Runtime licensing authorization and production identity/key inputs supplied.
- [ ] Owner gives final promotion authorization for the exact candidate hashes.

Until every applicable unchecked gate is closed, these files remain an unsigned engineering candidate and must not be represented as production-approved.
