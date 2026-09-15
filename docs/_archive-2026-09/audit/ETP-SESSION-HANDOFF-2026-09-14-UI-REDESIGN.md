# ETP UI redesign session handoff — 14 September 2026

Status: **INCOMPLETE — installed UI acceptance remains open.** This is the current resumption record; earlier dated observations are historical.

## Repository and published artifacts

Repository: `C:\Codex\Reporting Manger\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f`.
Branch: `ui/uiux-v4-touch-first-redesign`.
Source and portable artifacts were committed and pushed as **bdba5a79142fd7271156df60206d5f5d22a60057**. Local HEAD and the remote branch matched, and the working tree was clean before this documentation update. This handoff is a subsequent documentation-only commit; verify its HEAD and remote when resuming.

See the [publication inventory](ETP-UI-REDESIGN-PUSH-CHECKPOINT-2026-09-14.md). Selected r9/r10/r11 installers, source snapshots, receipts, evidence and the supplied screenshot are now in Git. Private settings, original database backups, VM disks and rebuildable/duplicate outputs remain local. Do not overwrite frozen candidate archives or rerun old finalization scripts over them.

## Last verified installed candidate and data

**1.8.8-r11** was installed in **ETP-Acceptance-186** on 13 September at 09:10:57 UTC. This is the last recorded installation, not a fresh guest inspection today. Embedded version is `1.8.8+d24a525`; its immutable source snapshot includes the then-uncommitted changes now in bdba5a7. Do not rebuild merely to change that identifier.

| Item | SHA256 |
|---|---|
| Installed executable | `432BF3914BD97B4DAC3280B52E2D1E401ADB0F99DAEC2B0E3429A3DFFBA52E3A` |
| r11 installer | `04ADFDBA153C5B07864C589AE08087E69098B1BB9607F100BE8C6C5F185C66C4` |
| r11 source archive | `DA5A57C8119A0041FDEFE129AD9EED72FD92769C0D28292E9B9BA87C8D208E03` |
| r11 evidence archive | `26A606E437392F75F9B59B58F032C67F42DE7853A709EA03A976DE54C678B91F` |

[Installed results and artifact links](ETP-UI-REDESIGN-R10-2026-09-13.md). r11 passed 669 Release tests (Desktop 345, SQL 195, Reporting 66, Import 51, Domain 12). Runtime source matched the frozen manifest before publication. These checks do not prove UI interaction.

Original guest payload/settings are preserved at `C:\ETPAcceptance\UI-20260913-baseline`. Backup: `C:\ProgramData\EtpReporting\Backups\EtpReporting-UI-20260913-original.bak`, SHA256 `9D8D069D3D08D14215C8CFAFD720F59E8442C0727C4B7AEA0CAFAE8A52CA3A3F`. COPY_ONLY/CHECKSUM and RESTORE VERIFYONLY passed; this is not a full isolated restore. After installation, original counts remained 3 sales rows, 2300 net, 2 units, 1 imported file and 3 lineage rows; saved settings hashes matched and DBCC CHECKDB passed. Recheck before further changes. Use disposable fixtures and isolated recovery targets; no production deployment or external messages.

## Observed results and open defects

| Requirement / report | Status and next proof required |
|---|---|
| Settings sidebar clicks appear to do nothing | User-reported; NOT reproduced or repaired. Capture the same installed journey before changing source, then repeat it after repair. Check every Settings destination with mouse and keyboard. |
| Compact/Comfortable in Settings → Display | Implemented, tested, packaged and installed. Global footer toggle removed; personal preferences reachable by Viewer, Store Manager and Owner, admin tasks remain guarded. Actual selection, application-wide response, restart persistence and focus are UNVERIFIED. |
| Missing Start menu entry | Shortcut file and installed target verified before/after upgrade. Visible discovery and launch are UNVERIFIED. |
| Missing Close/Minimise/Maximise buttons | User-reported; NOT reproduced or repaired. Source retains standard WPF chrome. Obtain full window bounds before diagnosing. |
| Singular category labels | Supplied screenshot visibly shows `1 tasks`; wording defect recorded, not yet repaired. Include in next immutable repair candidate. |
| J01–J18, all active destinations and 29 reports | Full installed journeys and saved UI exports remain UNVERIFIED; component/routing/programmatic export results are supporting evidence only. |
| Roles, keyboard, scaling, accessibility, performance, installer/recovery lifecycle | Remaining mandatory checks are UNVERIFIED. Record unsupported device checks explicitly, including physical touch without suitable hardware. |

The [user screenshot](../../artifacts/ui-redesign/20260914-user-screen/settings-overview-user.png) proves only the six-category Settings overview. It does not identify the candidate, show a click result or include native window captions. No before/after UI repair evidence exists for the reported sidebar/caption defects. G3 implementation remains passed; G1/G2/G4 mandatory acceptance remains open. Do not claim all four phases accepted.

## Access limitation and exact next action

On 14 September the user reported the VM active. The VM Connection window was found and restored from minimized state. Native capture still failed after fresh selection with `SetIsBorderRequired failed: No such interface supported (0x80004002)`. The worker status was **CLOSED**; prior credential access must not be assumed live. No new guest clicks or installation were performed during that retry. This documentation update does not recheck VM state.

1. Read AGENTS.md, knowledge context/router, the governing sprint plan, ledger, acceptance matrix and VM runbook. Inspect currently available computer-control tools and follow the computer-use skill. Recheck actual Git, VM connection, installed identity and worker state; do not assume old handles are current.
2. Establish observable, supported VM screen capture/input. Do not repeat unchanged failed capture calls indefinitely or treat process/accessibility/backend output as a visual pass. If available access cannot solve the capture failure, state that precise blocker and request only the necessary connection/interactive step. Reconnect the worker only when needed; user enters Windows credentials locally if required.
3. **First application action: on the hash-verified installed build, click Settings → Database & Recovery and capture the resulting screen, even if unchanged.** The last requested user observation is still outstanding. Include uncropped window bounds for caption controls. Reproduce sidebar no-op before attributing its cause; then check every Settings destination and Start menu launch.
4. Fix established causes and the recorded singular-count defect, run relevant regression checks, package a new immutable candidate, install only in the acceptance VM, and repeat each failing journey successfully with linked before/after evidence. Preserve original data/backups and earlier candidates.
5. Continue the prepared J01–J18 and full destination/report/dialog checks, persistent search, exact routes, breadcrumbs, Back, focus/scrolling, permissions, busy/error states and saved exports; execute supported device/scaling/accessibility/performance/lifecycle/recovery checks. Update route rows and requirement results only from observed evidence.

No routine approval is needed within the user's authorized acceptance scope. No new source repair or acceptance pass was performed for this documentation handoff.
