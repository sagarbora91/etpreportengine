# r10/r11 density placement and acceptance VM upgrade

**INCOMPLETE — WORK REMAINS.** This checkpoint implements the user's density placement request. It does not claim repair of the separately reported Settings sidebar, Start menu visibility or missing window controls.

Current resumption instructions: [14 September handoff](ETP-SESSION-HANDOFF-2026-09-14-UI-REDESIGN.md).

## Fresh VM baseline and preservation

The elevated worker connected to ETP-Acceptance-186 as its existing ETPTest account. Installed baseline is 1.8.7 (`B5C74F5415EF1C6DE91C58424ABF131E3DBAD1399567DA152C300FAE8001264B`), not the host's 1.8.5 or the archived r9 candidate. Original counts are 3 sales rows, 2300 net, 2 units, 1 imported file and 3 lineage rows. Original application payload and local settings were copied to `C:\ETPAcceptance\UI-20260913-baseline` inside the VM.

Original COPY_ONLY/CHECKSUM backup passed RESTORE VERIFYONLY WITH CHECKSUM. Its hash is `9D8D069D3D08D14215C8CFAFD720F59E8442C0727C4B7AEA0CAFAE8A52CA3A3F`; path is recorded in the [protected baseline receipt](../../artifacts/ui-redesign/20260913-acceptance-resume/vm-protected-baseline.json). This is backup verification, not an isolated full restore test. Original source tables were not changed by the baseline/backup job. An evidence-copy step initially failed because zero running ETP processes produced no JSON file; the follow-up wrote an explicit empty array and completed evidence copying. Both attempts are retained.

The baseline found the all-users Start menu ETP shortcut and Desktop shortcut, each pointing to the installed executable. File presence is not proof of visible Start menu discovery or launch.

## Implemented change and regression

- Removed the density selector from the global footer. Settings → Display → Display & Preferences now shows the selected Compact/Comfortable option and saves the preference through the existing local store.
- Personal preferences remain reachable by Viewer, Store Manager and Owner. Database Connection, Users & Roles and Recovery retain administration guards. Administrative Help now opens Users & Roles instead of personal display preferences.
- F6 cycles search, scope and focused task content. Status details remains docked correctly after removal of the old footer control. Opening personal display preferences does not start a database-bound work session.
- Final Release validation passed **669 tests**: 345 Desktop, 195 SQL, 66 Reporting, 51 Import and 12 Domain. Earlier failed attempts are preserved: obsolete named-control count, a test using the wrong navigation property name, and the administrative Help link caught by the existing role test. These were repaired before packaging.

The build started at `d24a525` with working-tree runtime changes; these changes are now committed and pushed as `bdba5a7`. The immutable candidate source archive/manifest identifies the exact build inputs. No financial, SQL, import or report calculation implementation changed.

## Installation checkpoint

r10 installed successfully at 09:06:38 UTC. The installed executable SHA256 is `F556E125EFB61C9A48451BB8D1310983902835F435E062EC715EAB7A8F5B90E7`; installer SHA256 is `9F3B85F2D3224D6A530BCADC90974682578C1C31A96CFB3D9EDBFFABEE91C926`. Installer exit code was 0; original sales/import/lineage counts and settings hashes were unchanged; DBCC CHECKDB passed. The all-users Start menu shortcut targets the hash-verified installed executable. [Installed result](../../artifacts/ui-redesign/20260913-acceptance-resume/vm-r10-install-result.json). This is an installed file/data check, not a UI launch or journey pass.

Final source review found the reused density selector's accessibility help text was set only during initialization and could describe the previous density after a selection. r11 updates that description when the selected radio button changes. The r10 source snapshot preserves the before implementation; no visual before/after evidence is available. r10 and its evidence remain immutable.

**Current installed candidate: 1.8.8-r11**, installed at 09:10:57 UTC on 13 September. All 669 Release tests passed again. Installer exit code 0; installed hash agrees; protected sales/import/lineage counts and saved settings remain unchanged; DBCC CHECKDB passed. [r11 installed result](../../artifacts/ui-redesign/20260913-acceptance-resume/vm-r11-install-result.json) and [immutable source/artifact receipt](../../artifacts/ui-redesign/20260913-acceptance-resume/candidate-r11-receipt.json).

| Artifact | SHA256 |
|---|---|
| Installed executable | `432BF3914BD97B4DAC3280B52E2D1E401ADB0F99DAEC2B0E3429A3DFFBA52E3A` |
| [r11 installer](../../artifacts/ui-redesign/20260913-acceptance-resume/installer-1.8.8-r11/EtpReportingEngine-Setup-1.8.8-x64.exe) | `04ADFDBA153C5B07864C589AE08087E69098B1BB9607F100BE8C6C5F185C66C4` |
| r11 source archive, 619 files | `DA5A57C8119A0041FDEFE129AD9EED72FD92769C0D28292E9B9BA87C8D208E03` |

The embedded product version remains 1.8.8+d24a525; r11 is the immutable candidate label identified by hashes and source snapshot. Current evidence documentation was updated after packaging; runtime source must match the archived manifest. Selected r9/r10/r11 portable candidate artifacts are now pushed in bdba5a7; see the [publication inventory](ETP-UI-REDESIGN-PUSH-CHECKPOINT-2026-09-14.md). No production deployment occurred.

[Final evidence bundle](../../artifacts/ui-redesign/20260913-acceptance-resume/evidence-r11.zip) and [source/artifact consistency result](../../artifacts/ui-redesign/20260913-acceptance-resume/final-consistency-r11.json) retain failed attempts and current records. Original settings and the database backup remain protected in the VM and are excluded from this bundle.

| User request | Current result |
|---|---|
| Start acceptance VM and install latest build | Worker connected and VM connection opened; r11 installation/hash/preservation checks PASS. Actual app launch through the screen is UNVERIFIED. |
| Compact/Comfortable only in Settings Display | IMPLEMENTED and regression-tested, packaged and installed. Selected state, live application-wide change and restart through actual UI remain UNVERIFIED. |
| Missing Start menu entry | Shortcut exists in the VM before and after upgrade, and targets the installed r11 executable. Visible menu discovery and launch remain UNVERIFIED; host observation cannot stand in for VM testing. |
| Missing Close/Minimise/Maximise controls | User-reported; source uses standard WPF Window chrome. Visible reproduction/cause/repair remain UNVERIFIED. No speculative chrome replacement was made. |
| Settings sidebar selections do nothing | User-reported, not reproduced or repaired. The earlier observed host 1.8.5 differs from the installed VM build. Must reproduce against r11 before attributing a cause. |

## Remaining acceptance and exact next action

Native capture of the VM window failed twice with `SetIsBorderRequired … 0x80004002`, including refreshed window selection. Worker access was available at installation, but was CLOSED on the 14 September retry; screen control/capture remains a separate unresolved limitation. No app clicks, keyboard actions, physical touch, screenshots or visible before/after results are claimed for this VM run.

Obtain supported VM screen capture/input; do not repeat the solved credential request or reinstall r11 merely to resume. Reproduce the original Settings sidebar symptom and the reported missing title-bar buttons on the actual installed build before attributing a cause. The source currently retains standard WPF window chrome; no source repair for missing caption buttons is justified by the available evidence. Observe Start menu discovery and shortcut launch. Test Display in both densities with saved selection, restart, all roles and keyboard focus. Then resume all J01–J18, every active destination, 29 reports and saved UI exports, dialogs, native scaling, Narrator, physical touch, performance and installer/recovery lifecycle. All complete journeys remain UNVERIFIED; no four-phase acceptance gate is upgraded.

## 14 September screen-access retry

The user reports the VM is active. Native window enumeration found ETP-Acceptance-186 in Virtual Machine Connection. The window was minimized; activation restored it, but the refreshed screenshot request again failed with `SetIsBorderRequired failed: No such interface supported (0x80004002)`. No guest application input or installation was performed. The worker status file now reads CLOSED, so the earlier connected worker cannot be assumed reusable. Resolve screen capture first; reconnect the worker only when needed for further baseline/install/data checks. A running VM does not establish available screen control or an acceptance pass. Exact next action: obtain a full-window Settings screenshot or working supported native capture, then reproduce the reported Settings and caption-control symptoms against installed r11.

## User-supplied Settings image

The subsequently supplied [Settings overview screenshot](../../artifacts/ui-redesign/20260914-user-screen/settings-overview-user.png) shows six categories, including Display. The title bar is not included in the captured area, and no before/after input sequence is supplied. This establishes only the displayed overview content, not exact installed identity, successful navigation or missing native caption buttons. The visible singular-count labels read `1 tasks` for Display, General and Users & Access; record this wording defect for the next repair candidate. [Observation receipt](../../artifacts/ui-redesign/20260914-user-screen/observation.json). Next observation: click Database & Recovery and inspect the resulting screen; obtain uncropped window bounds for the caption-control report. Frozen r11 evidence is unchanged.
