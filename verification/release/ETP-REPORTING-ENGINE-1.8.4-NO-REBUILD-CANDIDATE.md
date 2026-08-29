# ETP Reporting Engine 1.8.4 no-rebuild candidate record

Status: **UNSIGNED ENGINEERING CANDIDATE; PROMOTION NOT AUTHORIZED**

This record freezes the exact existing 1.8.4 payload identities for further acceptance without another rebuild. The payloads were produced by the official Windows release and installer scripts from clean committed source `8c8d57e37a26fcd8a9a145ac166b34ac952c8b4b`; `release.json` records the same commit prefix.

## Frozen candidate identities

| Role | Repository-relative path | Bytes | SHA-256 |
|---|---|---:|---|
| Self-contained application | `artifacts/windows-release/Etp.Reporting.Desktop.exe` | 160083442 | `73C615C1EA9A943A74893CE8BE6C4CFDF28796B4BD806902A7EA3A5A014A2B37` |
| Bootstrap installer | `artifacts/installer/EtpReportingEngine-Setup-1.8.4-x64.exe` | 48862473 | `67916955FDE3CDD8BB92075023C4108509FF4102966E691DFB95786092B26AFC` |
| Offline deployment package | `artifacts/offline-deployment/EtpReportingEngine-Offline-1.8.4.zip` | 48385246 | `0A1C21DFB9252D77D0DA23EBF5632633B83DE6BC2F7639DE609C42478C023495` |

The application and installer are unsigned. Signing either executable changes its hash and creates a different candidate requiring a new identity record.

## Build and local packaging evidence

- Official `scripts/build-windows-installer.ps1 -Configuration Release` pipeline passed: restore, Release build, all 516 tests, self-contained `win-x64` publish and Inno Setup compilation.
- Release build completed with zero warnings and zero errors.
- Official `scripts/new-offline-deployment-package.ps1 -Version 1.8.4` created the offline ZIP.
- Official installer lifecycle upgraded 1.8.3 to 1.8.4 and uninstalled successfully using silent per-user mode with SQL bootstrap disabled.
- `etp-reporting-engine-1.8.4.cdx.json` records the production NuGet dependency closure in CycloneDX 1.6 format.
- `etp-reporting-engine-1.8.4.provenance.json` records build facts, artifact identities and limitations.

## Repeatable identity check

Run from the repository root without invoking a build or packaging script:

```powershell
$expected = @{
  'artifacts/windows-release/Etp.Reporting.Desktop.exe' = '73C615C1EA9A943A74893CE8BE6C4CFDF28796B4BD806902A7EA3A5A014A2B37'
  'artifacts/installer/EtpReportingEngine-Setup-1.8.4-x64.exe' = '67916955FDE3CDD8BB92075023C4108509FF4102966E691DFB95786092B26AFC'
  'artifacts/offline-deployment/EtpReportingEngine-Offline-1.8.4.zip' = '0A1C21DFB9252D77D0DA23EBF5632633B83DE6BC2F7639DE609C42478C023495'
}
$expected.GetEnumerator() | ForEach-Object {
  $actual = (Get-FileHash -LiteralPath $_.Key -Algorithm SHA256).Hash
  if ($actual -ne $_.Value) { throw "Hash mismatch: $($_.Key)" }
}
```

## Promotion gates

- [x] Exact candidate names, sizes and SHA-256 values recorded.
- [x] Version 1.8.4 and runtime `win-x64` recorded by the release manifest.
- [x] Release build and 516 automated tests passed.
- [x] Silent 1.8.3-to-1.8.4 installer upgrade and uninstall passed with SQL bootstrap disabled.
- [x] Machine-readable SBOM and provenance record present.
- [x] Integrated source committed and exact source commit bound to the candidate.
- [ ] Clean-PC elevated SQL bootstrap, reboot and rollback evidence accepted.
- [ ] Target database backup/restore drill accepted.
- [ ] Production hardware, printer, Excel and accessibility acceptance completed.
- [ ] Owner/Manager/Viewer business UAT completed.
- [ ] Required source mappings and Owner decisions supplied.
- [ ] Publisher identity and code-signing certificate supplied; signed artifacts separately hashed and recorded.
- [ ] Runtime licensing authorization and production identity/key inputs supplied.
- [ ] Owner gives final promotion authorization for the exact candidate hashes.

Until every applicable unchecked gate is closed, this candidate must not be represented as signed, externally accepted or production-approved.
