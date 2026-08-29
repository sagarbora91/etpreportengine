# ETP Reporting Engine 1.8.4 preserved candidate record

Status: **REJECTED; NEVER PROMOTED; PAYLOADS PRESERVED AS HISTORICAL EVIDENCE**

This record preserves the exact 1.8.4 engineering payload identities. They were produced from committed source `8c8d57e37a26fcd8a9a145ac166b34ac952c8b4b`, but later review found a shipped operational-audit contract defect and an inconsistent committed SBOM. These payloads are permanently ineligible for promotion. Do not rebuild, modify, sign, distribute or accept them as 1.8.5.

The authoritative disposition is [ETP Reporting Engine 1.8.4 rejection disposition](ETP-REPORTING-ENGINE-1.8.4-REJECTION-DISPOSITION.md).

## Preserved identities

| Role | Repository-relative path | Bytes | SHA-256 |
|---|---|---:|---|
| Self-contained application | `artifacts/windows-release/Etp.Reporting.Desktop.exe` | 160083442 | `73C615C1EA9A943A74893CE8BE6C4CFDF28796B4BD806902A7EA3A5A014A2B37` |
| Bootstrap installer | `artifacts/installer/EtpReportingEngine-Setup-1.8.4-x64.exe` | 48862473 | `67916955FDE3CDD8BB92075023C4108509FF4102966E691DFB95786092B26AFC` |
| Offline deployment package | `artifacts/offline-deployment/EtpReportingEngine-Offline-1.8.4.zip` | 48385246 | `0A1C21DFB9252D77D0DA23EBF5632633B83DE6BC2F7639DE609C42478C023495` |

The application and installer are unsigned. Their historical hashes and files, plus the committed 1.8.4 SBOM/provenance JSON, are intentionally not rewritten.

## Historical build evidence

The following statements describe the original engineering run only; they do not cure the rejection:

- The official Windows release/installer pipeline reported restore, Release build, 516 tests, self-contained `win-x64` publish and Inno Setup compilation success.
- The release build reported zero warnings and zero errors.
- The offline-deployment script produced the recorded ZIP.
- The installer lifecycle exercised 1.8.3-to-1.8.4 install/upgrade/uninstall in per-user mode with SQL bootstrap disabled.
- The provenance record binds the preserved candidate to source commit `8c8d57e` and the hashes above.

## Rejection findings

1. Migration `0014_productisation.sql` did not allow every operational-audit event emitted by the shipped source. Valid document-review, sharing-contact or visual-render activity could fail audit persistence; sharing-contact mutation and audit were not protected by one explicit transaction.
2. `etp-reporting-engine-1.8.4.cdx.json` records base commit `08e39c889fe2a13b09aa33ffa788df63f0b800fd`, `source-worktree-clean=false` and application hash `AA2EE80191F79402C340A2F3C8BBE240AB45B4A2C2EA6934026D5733122D0ABE`, while the candidate/provenance record source commit `8c8d57e37a26fcd8a9a145ac166b34ac952c8b4b`, clean state and application hash `73C615C1EA9A943A74893CE8BE6C4CFDF28796B4BD806902A7EA3A5A014A2B37`.

Either finding is sufficient to block promotion. Together they invalidate the earlier no-rebuild acceptance path.

## Identity check

This read-only check can confirm that preserved payloads have not changed; it does not make them acceptable:

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

## Disposition

- Promotion authorization: **REJECTED**.
- External acceptance execution for these hashes: **CANCELLED**.
- Signing/tag/release eligibility: **NONE**.
- Recovery: retain the payloads and machine-readable evidence unchanged for audit/reproducibility.
- Successor: source version 1.8.5, which still requires a fresh clean artifact build, consistent SBOM/provenance, complete tests, installer/live SQL acceptance, signing and explicit promotion authorization.
