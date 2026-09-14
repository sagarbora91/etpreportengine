# ETP UI redesign acceptance — current installed candidate 1.8.8-r11

**Latest checkpoint: INCOMPLETE — WORK REMAINS.** [r10/r11 acceptance evidence](ETP-UI-REDESIGN-R10-2026-09-13.md) records solved VM access, preserved original baseline/backup, 669 passing tests, r11 installation/hash and unchanged counts/settings/integrity, and Settings-only density. Screen capture still fails; original Settings sidebar and missing window-button reports remain unreproduced. Start menu shortcut file/target is verified, visible launch is not. UX-22 now has an installed hash/preservation PASS subset; its full launch/lifecycle requirement remains UNVERIFIED. Other full UX/J outcomes below are not upgraded. Remaining r9 material is historical evidence, superseded where this latest report records new observations.

**13 September resumption: INCOMPLETE — WORK REMAINS.** [Fresh evidence and blocker record](ETP-UI-REDESIGN-ACCEPTANCE-RESUME-2026-09-13.md): Settings sidebar defect reported by user, not yet reproduced or repaired; host executable identifies as 1.8.5; r9 archives match; VM permission and native capture still blocked. The matrix below preserves historical scoped evidence, with no new PASS. UX-03 has this additional open reported defect; all full J01–J18 remain UNVERIFIED.

Verification updated UTC: 2026-09-12T12:53:09.931255+00:00. Source-push/handoff documentation updated: 13 September 2026; no new UI acceptance claimed. Run: `20260912-sprint`.

**Disposition: IMPLEMENTED — VERIFICATION BLOCKED.** The full source conversion and available automated/component checks are complete. Mandatory installed, DPI, real-role and physical-input checks have not passed; this is not a fully verified sprint or production release.

## Candidate and protected scope

Branch: `ui/uiux-v4-touch-first-redesign`. Implementation started at `491f5ba5e29dc0d9f9a7458ccaa9d46ca1eb2294`. Source implementation is committed and pushed as `d7eb51538881c7ce46be2e1653d3ed5e7f9523b3`. The r9 build predates this commit: it used planning HEAD `847f22eb033f6d77bab34481ada5605eac11dd57` with implementation in the working tree. Only two trailing blank lines were removed before committing. The immutable source archive identifies exact build inputs; see the [13 September handoff](ETP-SESSION-HANDOFF-2026-09-13-UI-REDESIGN.md). [Exact source/file/hash receipt](../../artifacts/ui-redesign/20260912-sprint/candidate-r9-receipt.json) binds the executable, installer and source snapshot. Previous candidates r1–r8 remain preserved.

[Candidate](../../artifacts/ui-redesign/20260912-sprint/candidate-1.8.8-r9/Etp.Reporting.Desktop.exe) · [Installer](../../artifacts/ui-redesign/20260912-sprint/installer-1.8.8-r9/EtpReportingEngine-Setup-1.8.8-x64.exe) · [Source archive](../../artifacts/ui-redesign/20260912-sprint/candidate-1.8.8-r9/source-snapshot.zip) · [Coverage CSV](../design/ETP-UI-REDESIGN-ROUTE-COVERAGE.csv) · [Decision log](../design/ETP-UI-REDESIGN-DECISIONS.md) · [Complete gallery](../../artifacts/ui-redesign/20260912-sprint/gallery.html) · [Before/after](../../artifacts/ui-redesign/20260912-sprint/before-after.html)

No production deployment. No VM install, repair, rollback, restore or original VM database write was performed. Local host configuration was temporarily pointed at an isolated synthetic database for native checks and restored byte-for-byte afterward; see `host-settings-restored-r7.json`. Host original sales aggregates are checked separately in `host-original-r7.txt`. Historical VM counts (3 sales rows,2300 net,2 quantity,1 import,3 lineage) are not a fresh verification.

## Available checks

| Evidence | Observed result | Limit |
|---|---|---|
| [Final release build](../../artifacts/ui-redesign/20260912-sprint/local-followup/build-r9.log) | Build and668 tests pass:344 Desktop,195 SQL,66 Reporting,51 Import,12 Domain; baseline617 | Test count does not prove UI journeys |
| [Function audit](../../artifacts/ui-redesign/20260912-sprint/function-audit-r4/audit-summary.json) | All 10 steps PASS; disposable database removed; Excel/PDF/DSR exports, financial/import/pack/backup/full restore/lineage | Historical r4 audit. r9 also repairs export rendering/status/close handling; Domain/Application/Import/SQL calculations and migrations remain unchanged |
| [Task matrix](../../artifacts/ui-redesign/20260912-sprint/matrix-r19/task-layout-results.json) | 128 active canonical task compositions; zero render exceptions; 50 overview pages without normal overflow | Direct composition bypasses navigation guards; not user input |
| [Report states](../../artifacts/ui-redesign/20260912-sprint/report-states-r7/report-state-results.json) | 29 reports × populated/missing =58; each at1000x600/800x440 DIP | Live disposable queries into WPF; captured export model, not every saved UI export |
| [Controls](../../artifacts/ui-redesign/20260912-sprint/controls-r3/controls.json) | Native date parsing/selection,42 calendar day targets ≥44x44, button44x48; bounded status/scope dialog capture | Component events/offscreen, not popup interaction |
| [Task contrast/targets](../../artifacts/ui-redesign/20260912-sprint/matrix-r19/visual-contract-findings.json) and [report contrast/targets](../../artifacts/ui-redesign/20260912-sprint/report-states-r7/visual-contract-findings.json) | 2675/3200 text samples and1890/1826 targets; zero scoped findings | Solid-colour tree measurements exclude disabled text and business rows |
| [Source/control reconciliation](../../artifacts/ui-redesign/20260912-sprint/control-reconciliation.json) | 243 original controls,202 retained/exposed and41 explicit replacement/conditional mappings;527 total coverage rows | A mapped handler is not a completed installed action |
| [Warm search](../../artifacts/ui-redesign/20260912-sprint/matrix-r19/search-performance.json) | 100 host metadata-index queries, p95 0.2502 ms | Excludes popup, debounce, first-open and VM timing |

The future-date case has no same-day source imports; it is not an assertion that every historical/as-of or exception report must return an empty table. Captured result models and statuses remain authoritative, and every complete state journey remains unverified.

Matrices use96DPI device-independent client bounds, not actual screen-resolution/Windows-scaling evidence. Representative tasks use1280x720,1000x600,960x600,800x440,1024x768,1280x800,800x1000 with Comfortable/Compact. The final r7 visual code is unchanged from these captures; r7 additionally routes keyboard refresh through the snapshot scope helper and F6 through visible regions.

## Host interaction and preservation

[Native host observations](../../artifacts/ui-redesign/20260912-sprint/host-candidate-r6-observed.json) show the r6 packaged executable on the unchanged report/navigation path accepting the Windows identity, displaying the exact DSR search path, opening DSR, changing its explicit date to 25 August 2026, refreshing to ₹4,600, moving with F6 to density and toggling Compact, then returning to Today Overview with Back. The attempted Settings transition was not established, and no saved UI export was observed; full J01 remains unverified. Capture was retried and still failed with 0x80004002.

[Original settings and preferences](../../artifacts/ui-redesign/20260912-sprint/host-settings-restored-r7.json) were restored byte-for-byte after test app closure. [The extra UI fixture database was removed](../../artifacts/ui-redesign/20260912-sprint/fixture-ui-cleanup-r7.txt). [Protected host totals](../../artifacts/ui-redesign/20260912-sprint/host-original-r7.txt) remain 540 sales rows, 3,203,362.6900 net and 514.0000 units. No original VM or production database was modified. [R7 native observations](../../artifacts/ui-redesign/20260912-sprint/host-candidate-r7-observed.json) independently show the exact DSR search path, the focused report for 25 August 2026 and the expected ₹4,600. The screenshot retry still fails with 0x80004002. R7 settings/preferences were restored with matching hashes, and its fresh synthetic database was removed after the test.

[Source-derived route-depth check](../../artifacts/ui-redesign/20260912-sprint/route-depth-r7.json) finds at most two selections from module overview for all 128 active destinations. [Knowledge validation](../../artifacts/ui-redesign/20260912-sprint/knowledge-r7.json) resolves all 23 notes with no broken wiki links or stale-date warnings. Graphify was refreshed to 14,070 nodes and 29,571 edges; code graph review is advisory, and its static test-gap counts are not runtime coverage. The final whitespace check passes.

## Requirement-by-requirement result

All requirements are implemented in source. Overall status below reserves the full governing acceptance condition; scoped local passes are described alongside it. Coverage CSV check columns similarly do not convert offscreen rendering into interaction PASS.

| Requirement | Promise | Overall | Established evidence | Remaining scope / qualification |
|---|---|---|---|---|
| UX-01 | Shared existing palette | PASS | Palette values unchanged; shared theme, darker existing teal for primary text contrast; matrix-r19 and gallery. | Native high-contrast/Narrator modes are separate from this palette check. |
| UX-02 | Complete active inventory | UNVERIFIED | 128 active canonical routes; 29 reports; 243 original controls reconciled; 527 coverage rows including aliases and supporting dialogs. | Full installed action/dialog interaction is not established by source mapping. |
| UX-03 | Exact task destination | UNVERIFIED | Canonical route, alias and access tests; all active task compositions render. | Full observed tile/search identity matrix on the installed candidate. |
| UX-04 | Normal overview fits | UNVERIFIED | 50 module/category overviews at 1000x600 DIP have no vertical page overflow. | Native viewport and Windows-scale evidence remains blocked. |
| UX-05 | Constrained identity/status/actions | UNVERIFIED | 12 representative tasks × 2 densities × 7 client sizes; populated/missing reports at 1000x600 and 800x440; bounded dialogs. | Complete actual window/DPI/long-data interaction matrix. |
| UX-06 | Bounded scrolling | UNVERIFIED | TaskBodyLayout places tables on independent selected tabs; report Summary/Detail rows and DSR tabs; ordinary forms have no horizontal page scrollbar. | Native scrolling, large-table selection and all detail interaction. |
| UX-07 | Persistent location/search/history | UNVERIFIED | Shared shell and route/history tests; source metadata is common to tiles/search/breadcrumbs. | Complete installed focus/Back/Forward journeys. |
| UX-08 | At most three selections | PASS | Canonical module → category → task graph; direct shortcuts for daily tasks; source-derived route-depth evidence records the module/category/task edges. | This is the route-depth check; observed full-route interaction remains UX-03. |
| UX-09 | DSR exact search | UNVERIFIED | DSR exact path and ranking tests; r3 host exact navigation observed; r4 host query shows Reports → Sales → Daily Sales Report. | Final installed J01 from each top-level area. |
| UX-10 | Authorised search coverage | UNVERIFIED | All canonical destinations/34 report aliases tested; role filtering and direct-route denial; seven deferred entries stay unavailable. | Real Viewer/Store Manager/Owner identities on installed build. |
| UX-11 | Search/first-open latency | UNVERIFIED | 100 warm host index samples: p95 0.2502 ms, max 12.6804 ms (matrix-r19). | VM p95 including popup/debounce, first-open ≤1 s, 30-transition navigation and comparative startup/memory. |
| UX-12 | Explicit stable date/store | UNVERIFIED | Task override, import invalidation, stale-report rejection, fixed DSR scope, explicit single-store selection and snapshot/range regression tests. | Complete J01/J03/J18 via real input on final installed build. |
| UX-13 | Draft/busy/locked safety | UNVERIFIED | Per-record drafts/reasons, failed-save retention, closing guards and operation reentry tests; disposable locked-day regression. | Full UI Save/Discard/Stay, cancellation and locked-day journeys. |
| UX-14 | Comfortable target sizing | UNVERIFIED | 1890 navigation/task targets and 1826 report targets measured without findings; native calendar 42 days ≥44x44 DIP and opener44x48. | Physical touch comfort and actual popup interaction; disabled/business-row exclusions disclosed. |
| UX-15 | Keyboard/focus/contrast | UNVERIFIED | 2675 task and 3200 report text samples, zero scoped contrast/font findings; explicit focus resources/names. Partial host keyboard evidence retained. | Complete keyboard/focus return, Narrator reading order and actual Windows scaling. |
| UX-16 | Useful application states | UNVERIFIED | All 29 reports reach populated/missing results; query failure replaces loading; concise/full status; module failure/draft tests. | Every route/state combination through installed interaction, including external OCR/helpers. |
| UX-17 | Missing versus zero/readiness | UNVERIFIED | Synthetic DSR explicit zero, missing LY and readiness/locked-pack assertions retain underlying rules. | Installed J07/J08 observability across all state transitions. |
| UX-18 | 29 reports and exports | UNVERIFIED | 58 SQL-to-WPF report states; export models captured; report/SQL suite and Excel/PDF/DSR export audit pass. | Each report preview versus saved UI export with final installed candidate. |
| UX-19 | Safe wrong-date/duplicate import | UNVERIFIED | Import/SQL regression, overlap/conflict/concurrent and identical-restatement protections; host original aggregate comparison. | Final installed wrong-date/correction/duplicate/restatement journeys and original VM comparison. |
| UX-20 | Financial/role/recovery controls | UNVERIFIED | Function-audit-r4 passes all 10 steps, checksum/full restore/lineage, packs/locks and accounting/export regression. Lower-layer financial rules/migrations unchanged. | Real role identities, UI recovery/approval/archive journeys and VM preservation. |
| UX-21 | Help/Profile/dialogs | UNVERIFIED | Five Help categories/exact destinations and return tests; source dialog inventory; bounded calendar/status/report-scope/draft composition. | Full installed modal/focus/long-error interaction and native file-dialog lifecycle. |
| UX-22 | Installed artifact agreement | UNVERIFIED | Versioned executable, installer, exact source archive and per-file hashes; receipt identifies dirty source beyond HEAD. | VM installation/upgrade, installed hash, repair/reinstall/offline/rollback. |
| UX-23 | Evidence matches claims | PASS | Scoped local results, failed attempts, source/route ledger and explicit environment/device limits are retained. | No service test, offscreen render or running process is labelled installed interaction. |
| UX-24 | Continuous four-phase execution | PASS | All four phases executed autonomously; implementation, repair, package and evidence work completed; external gates remain named. | Full verified sprint closure is prevented by the mandatory blocked checks, not a routine approval checkpoint. |

## Complete-journey results

Every full journey remains **UNVERIFIED** until its required actual-UI steps are observed. The table records useful independent progress rather than labelling the journey passed.

| Journey | Scenario | Supporting result | Remaining actual-UI requirement |
|---|---|---|---|
| J01 | DSR search/context/export/Back | Exact routing/scope/stale-export tests; populated DSR values; partial host search/navigation in r3/r4. | Final installed end-to-end export and restored context. |
| J02 | Support package from Imports/Settings | r3 packaged host created verified aggregate-only ZIP; dedicated page and source handler retained. | Final installed category and search paths with output. |
| J03 | Wrong-date correction/import | Existing wrong-date and selection-invalidation regression; synthetic imports pass. | Actual picker/date/validation/import interaction. |
| J04 | Duplicates/restatement/conflicts | SQL duplicate/concurrent/overlap/restatement controls pass; no unchanged-file second write. | Final UI outcomes and original VM lineage comparison. |
| J05 | Batch/cancel/retry | Immutable per-batch context; busy/reentry/cancel controls retained and state tests pass. | Complete interactive cancellation/retry/history cycle. |
| J06 | Inbox/extraction/OCR | Exact document/extraction selection and late-result guards; unavailable helper states preserved. | Actual OCR helper/file dialogs and failure recovery. |
| J07 | Inputs/zero/readiness/pack/lock/reopen | Draft/explicit-zero tests; live fixture reaches Locked and pack Passed. | Full UI entry/finalisation/locked edit/authorised reopen. |
| J08 | 29 populated and missing reports | 58 live synthetic SQL-to-WPF states with export model data; final gallery and export tests. | Every saved UI export comparison and installed route interaction. |
| J09 | Register draft/source/save | Per-register draft/source/store association, Save/Discard/Stay and failed-save tests. | Native input/dialog/return/save observation. |
| J10 | Accounting mapping/validate/Tally | Module-owned mapping/preview/approval state and underlying financial/export tests pass. | Complete installed batch/review/export journey. |
| J11 | Archive/compare/lineage/restatement | Exact generation routing and late-open guards; immutable archive/lineage regression. | Interactive generation comparison, source detail and re-export. |
| J12 | Exception source/resolve/approve | Focused issue category filters, retained per-ID reasons and service/route roles. | Actual final-candidate issue/source/approval refresh. |
| J13 | Connection/backup/health/recovery | Active target validation and live disposable checksum/restore/lineage audit pass. | UI actions and installed recovery lifecycle. |
| J14 | Three real roles | Synthetic policy/search/direct-route and service tests pass. | Actual Windows/SQL Viewer, Store Manager and Owner identities. |
| J15 | Keyboard/touch/Help | Shared shortcut/focus/name implementation; Help round-trip tests; partial host keyboard evidence. | Complete keyboard-only, focus restoration, Narrator and physical touch. |
| J16 | Size/DPI/density/dialogs | Seven client-size representative matrix, both densities; all normal tasks/overviews; 29 report states and dialog captures. | Actual physical-resolution/100–150% scaling and popup/focus interaction. |
| J17 | Install/upgrade/restart/offline | Versioned package and source receipt; self-contained host launch attempted; original configuration restored after fixture checks. | VM upgrade, repair/reinstall, offline smoke and safe rollback. |
| J18 | Busy/dirty/locked/failure navigation | Operation gates, stale-result revisions, per-record drafts and failed-follow-up messages tested. | Full simultaneous UI input and side-effect observations. |

## Repairs and retained failures

- Source conversion replaced the ordinary combined-page path with canonical focused tasks while retaining existing action owners and service permissions.
- Resumed work fixed record-specific drafts/reasons, busy reentry, stale source selection, settings/save-follow-up truth, Help return navigation, favourites, narrow task tables and footer overlap.
- Report-states-r1/r2 exposed unsupported combined store scope for snapshot/exception reports and loading not replaced by failures. The fixes preserve existing report contracts.
- Report-states-r3/r4 exposed crowded short previews and text contrast. DSR tabs, compact filter dialogs and existing darker text colours repaired them; r7 has 58 successful states and no scoped findings.
- Matrix-r15 erroneously sampled an empty Window visual root; the harness now measures Content and rejects zero samples. Matrix-r16 findings were repaired and rerun. Controls-r2 transparent-background captures were corrected by compositing Window background in controls-r3.
- Regression-r40 used incomplete theme resources; the same native calendar parsing/selection/target assertions now execute with full App resources in controls-r3. Other failed build/test/matrix logs remain in the evidence folder.
- Final review found shortcut refresh bypassing snapshot range preservation. R6 calls ApplyTaskScope, with a test proving stock-physical retains From and stock-variance can change its range.

- F6 now selects a focusable control in the current visible region instead of the hidden legacy workspace; the full keyboard/Narrator gate remains unverified.

- R7 additionally blocks changing the database after integration settings have loaded, even if the form is clean. The regression verifies connection-test and bootstrap entry points preserve the original session and still allow checking its current target.

## Precisely identified limitations

1. `Get-VM ETP-Acceptance-186` is denied to the current token. The previously requested elevated connection worker is not connected. `local-followup/environment-r9.json` contains the latest read-only result; `vm-access-r7.json` preserves the prior check. Do not repeat the credential request or infer permission from the existing VM Connection window.
2. Supported native automation cannot capture the app (`SetIsBorderRequired`,0x80004002) or obtain pointer geometry. Focus reporting is stale, and some key attempts do not prove intended selection. Partial r3/r4/r5/r6 observations are recorded at the level actually seen. Offscreen images are separately labelled.
3. Installed upgrade/hash, repair/reinstall/offline/rollback, complete J01–J18, three real Windows roles,100/125/150% scaling,Narrator and physical touch remain unverified. VM first-open/navigation/startup/memory performance also remains unverified.
4. External email-client policy and optional OCR/helper integrations need their actual configured test environment; no email was sent. Seven deferred capabilities remain deferred. Historical SQL-absent prerequisite-installer and unreproduced dispatcher issues are not declared fixed.
5. Changing a database after opening a working module requires restart and Connection first, by design. Snapshot single-store reports require an explicit store. These are visible protections, not disabled financial capabilities.

## Resume without restarting

Use the exact r9 receipt/source snapshot and the prepared VM acceptance runbook. When elevated VM and supported UI access become available, first run the queued read-only baseline job; verify original VM counts and installed hash. Upgrade only the acceptance VM, repeat J01–J18 and the required DPI/role/device matrix, verify preservation, then repair/reinstall/offline/rollback. Record each expected/actual result against this candidate. Repackage and rerun affected checks if a source repair is needed. G4 remains FAIL / BLOCKED until all mandatory acceptance has evidence.

## Completed local follow-up — r9

[Full follow-up findings and evidence](ETP-UI-REDESIGN-LOCAL-FOLLOWUP-2026-09-12.md) records all six locally available activities. All29 reports were written through the production export coordinator for both date cases:58 Excel/PDF pairs;2,431 workbook cells,complete non-DSR PDF content,12 independent fixture anchors and11 DSR document checks passed. Wide/Unicode/negative/long-row stress adds797 checked workbook cells;257 PDF pages have no out-of-page glyphs. These strengthen UX-12/13/17/18 and J08/J18 but leave their complete UI outcomes UNVERIFIED.

R9 fixes truncated/omitted PDF details/totals, unsigned chart bars and omitted Other points, late export status overwrites, misleading post-save audit failure, and closing during report export. The new pages preserve the existing report models. Package contents, recovery scripts/migrations and version hashes are checked; installer/executable remain unsigned. r8 is an immutable intermediate checkpoint before the close guard. Historical r7 host UI evidence does not establish r9 interaction.

The [VM acceptance runbook](ETP-UI-REDESIGN-VM-ACCEPTANCE-RUNBOOK.md) is ready. Current candidate identity comes from the r9 receipt. Original display galleries remain r7/r6 evidence for unchanged layouts; the follow-up contains the new exported-PDF evidence. No mandatory installed/role/DPI/touch/accessibility check has been upgraded to PASS.
