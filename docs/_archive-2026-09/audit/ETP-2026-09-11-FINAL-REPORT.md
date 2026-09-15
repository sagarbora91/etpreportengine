# ETP four-phase review — 11 September 2026

Status: engineering candidate delivered. Production acceptance is not established by this review.

## Scope and source

Started from clean commit 65511fa3eda7c368b65b2901be88c0e1968f791d on ui/uiux-v4-touch-first-redesign. Active .NET 10/WPF scope only. No push or release publication. Prior candidates and production database were preserved. Version is 1.8.6 under Directory.Build.props and the changelog policy; runtime licensing remains deferred.

## Findings and implementation

Three logic defects were corrected: empty tender/stock inputs falsely passed reconciliation, and cancellation during transient import failure could invoke another retry. Desktop fixes protect daily readiness, repeated writes, report request ordering, export scope, and database settings from obsolete asynchronous completions. Manual-entry labels and date widths were improved. Windows builders now reject existing outputs and permit explicit payload/output paths with matching executable and installer versions.

The persistent issue register is ETP-2026-09-11-ISSUES.csv. Detailed source/reproduction evidence is in the LOGIC-AUDIT and UI-AUDIT documents. Coverage has 244 active entries with explicit review statuses, plus seven deferred entries. Many statuses are static inspection or render-only: this is not full interaction acceptance.

## Executed verification

- Integrated verified/audit-summary.json: PASS. 615 tests, no failures or skips (Domain 12; Import 51; Reporting 63; SQL 195; Desktop 294).
- 12 synthetic workbooks across six approved profiles and two stores; 42 persisted evidence rows. Duplicate/overlap handling, manual inputs including zero, daily finalise/reopen, pack generation, archive integrity, operational automation, and full backup/restore lineage comparison passed in live SQL.
- Independent direct SQL oracle: six canonical sales rows; net sales 4,600; signed units 4; sales returns -400; closing stock 60. Per store the three signed sales values are 1,000 + 1,500 - 200 = 2,300 and quantities 1 + 2 - 1 = 2. These expected amounts were compared directly with persisted facts.
- 140 offscreen 96-DPI renders: 14 workspace and 29 report routes at 960x600, 1366x768, 1920x1080, plus 11 baseline views. Rendering is not click, keyboard, screen-reader or operating-system scaling verification. Final date-width correction was rerendered and inspected; see final checks below.
- Initial audit failures from new packaging regression harness were retained in evidence: analyzer error, PowerShell invocation policy, then a real absolute-path resolution defect. These were corrected and the final suite passed.

Evidence root: ../../verification/review-2026-09-11/ (generated local artifacts retained and excluded from Git). Before screenshots are in before/; integrated evidence in verified/. Build, packaging and packaged-UI evidence is recorded below.

## Remaining acceptance requirements

- No isolated Windows VM/sandbox is available. WindowsSandbox, Hyper-V Get-VM, VMware and VirtualBox commands were absent; HypervisorPresent was false. Installer bootstrap targets a fixed EtpReporting database and machine scheduled tasks; an alternate installation directory does not isolate these effects. Install/upgrade/uninstall must be tested on a disposable machine.
- All 85 role-UAT scenarios remain unobserved as a complete matrix. Synthetic Owner adapter tests do not prove Windows identity separation under real Viewer/Store Manager/Owner sessions.
- Full invalid/loading/error/restricted/locked/cancel/navigation coverage across all 244 functions remains incomplete. See explicit coverage statuses rather than the historical category-label count.
- OS scaling at 100%, 125%, 150%, keyboard-only and screen-reader acceptance remain open except any specifically recorded packaged observations.
- Owner control-total review, representative approved workbook UAT, deployed backup retention/recovery, email integration, code signing, SBOM/provenance promotion gates remain external requirements.

No confirmed high/critical issue is intentionally waived; unresolved verification gaps require engineering-candidate labelling.

## Final artifact and packaged checks

Delivered **1.8.6 engineering candidate**, built from clean commit `3752600e4d862379b3f081763bb58d41c31ef66c`. Later commits update evidence only. Windows executable ProductVersion is `1.8.6+3752600e4d862379b3f081763bb58d41c31ef66c`; FileVersion is `1.8.6.0`.

- Executable: `artifacts/review-1.8.6-20260911/windows/Etp.Reporting.Desktop.exe` (160,189,938 bytes). Keep the adjacent migrations/scripts when using the unpacked application.
- Installer: `artifacts/review-1.8.6-20260911/installer/EtpReportingEngine-Setup-1.8.6-x64.exe`.
- Executable SHA-256: `71DC4529048AEB7ACEA4BA5660EA8619BB95D72B0CA40D88C9719EB12BF52148`.
- Installer SHA-256: `1017BC362A7655A166F48C99C2EE71CCAA5739337C6BF7F7491ABB8204172F66`.
- `package-verification.json` records every payload file checksum, all 15 migration matches, and self-contained .NET/WindowsDesktop 10.0.11 runtime configuration. The executable embeds managed/native runtime dependencies; the build log verifies self-contained single-file publishing. The installer compiled successfully from this explicit payload. Signature status is NotSigned.
- Release builder reran all 615 tests successfully after the final date-width change, with zero warnings/errors. `after/` contains 140 refreshed renders. Direct inspection at 960x600 confirms the full date is now readable; wide renders retain the visible labels and wrapping layout.
- Packaged application launched against newly created `EtpReportingReview_20260911`, loaded healthy SQL state, 12 files and 42 source rows, and recorded version 1.8.6 session events. F1 opened Help, Alt+Left returned to Today, and Ctrl+G reached Reports. Alt+F4 exited with code 0. `packaged-ui.txt` retains accessibility-tree evidence; `packaged-launch.log` records launch/exit and restoration of original settings/preferences byte-for-byte.
- Live screenshot capture failed with `SetIsBorderRequired ... 0x80004002`; pointer action failed with `coordinate input geometry is unavailable`; value setting failed with UIA CacheRequest `0x80070057`. Accessibility reads and some keyboard navigation worked. These tool limitations leave full packaged interaction, tab/focus, export-dialog and end-to-end UI acceptance incomplete. No fallback pointer automation was used.
- The isolated interactive SQL database was removed after the packaged process exited. Automated disposable databases were removed by the audit runner. Production database, source data, backups and audit history were not targets of these tests.

Twelve confirmed software/UI/packaging issues were fixed; the coverage wording issue was also clarified. No known unresolved confirmed Critical/High defect is waived. The four-phase request has yielded a tested engineering artifact and explicit coverage/issue records, but full functional/UAT and installer lifecycle exit gates remain unmet. Promote only after the remaining acceptance requirements above are completed.
