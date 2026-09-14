# Installed acceptance resumption — 13 September 2026

**Superseded connection checkpoint:** VM access was subsequently established and r11 installed. See [latest evidence and exact next action](ETP-UI-REDESIGN-R10-2026-09-13.md). The failed access and capture attempts below remain historical evidence; do not repeat the credential request based on this earlier record.

**INCOMPLETE — WORK REMAINS.** The user reports that Settings sidebar selections do nothing. Reproduction, cause, repair and installed rerun remain open. No new UI pass or repaired candidate is claimed.

## Fresh read-only observations

- Repository started clean on `ui/uiux-v4-touch-first-redesign`, HEAD `d24a525`.
- Archived r9 installer, executable, source archive and evidence archive match all four hashes in the session handoff. Earlier archives remain unchanged.
- The only returned ETP window belongs to the **host** release folder `artifacts/windows-release/Etp.Reporting.Desktop.exe`. Its file version is **1.8.5+55b3954d70b2bab19fca16937ac430fd11412944**, SHA256 `281FDED6D2642C146716C0BF10A8648FBA7AA2BF56C27E2B539471AA37267DF6`. It is not the r9 executable. No ETP Reporting entry was found in standard host HKLM native/WOW6432Node or HKCU uninstall locations. This does not establish VM installation identity or explain which executable the user tested.
- Host OS is Windows 10 Pro, build 19045. Hyper-V `Get-VM` for `ETP-Acceptance-186` returns permission denied by the host authorization policy. No worker status file, reusable session in the command process, or targetable VM window was found.
- The computer-use skill's `@oai/sky` runtime initialized and enumerated windows successfully. ETP screenshot capture failed with `SetIsBorderRequired failed: No such interface supported (0x80004002)`, including one retry after refreshing window selection. Accessibility-only reading succeeded, but does not prove visible layout or pointer response. No Settings click or keyboard input was attempted against the mismatched host build.
- No application source, installation, settings or database was changed. No backup/restore was run. Original VM data and backups remain freshly **UNVERIFIED**, rather than inferred preserved from historical counts.

[Machine-readable environment evidence](../../artifacts/ui-redesign/20260913-acceptance-resume/environment.json). No screen image could be captured; therefore neither before nor after visible Settings evidence exists for this attempt. Do not present the capture error as an application defect.

## Requirement and journey disposition

The [existing UX-01–24 matrix](ETP-UI-REDESIGN-ACCEPTANCE.md#requirement-by-requirement-result) retains its scoped historical evidence. No requirement receives a new PASS. UX-03 has an additional user-reported Settings defect requiring reproduction. UX-22 additionally has a freshly observed host/r9 identity mismatch; VM installed identity remains unknown. All complete J01–J18 journeys, all 29 installed report/export checks, real-role, device/scaling, physical touch, Narrator, performance and installer/recovery acceptance remain UNVERIFIED. Settings rows in route coverage now carry the open reproduction issue.

## Exact next action

The essential elevated connection and local credential entry have been requested. Run the existing `artifacts/ui-redesign/20260912-sprint/Connect-TestVm.ps1` as Administrator, enter acceptance VM credentials locally, and make the signed-in `ETP-Acceptance-186` desktop available in Virtual Machine Connection. Credentials must not be pasted into chat or evidence.

Then verify worker health and run the queued read-only installed/data baseline. Supplement it with the runbook's installation/database/role/display identity, import and lineage counts; the existing baseline job alone reports only executable identity and sales aggregates. Preserve and verify original backup/settings before installation or mutating journeys. Retry supported screen capture against the returned VM window; current host capture failure may remain a separate tooling blocker.

Reproduce the Settings sidebar symptom first on the actual installed build, with visible before evidence and exact build identity. Diagnose from matching source, repair, regress, package a new immutable candidate if source changes, install only in the VM and observe the same input journey succeeding. Check every Settings destination with mouse/keyboard and applicable touch, then continue the full runbook. Do not rebuild or relabel r9 merely because the host was running 1.8.5.
