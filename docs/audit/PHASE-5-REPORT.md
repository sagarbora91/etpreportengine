# Phase 5 — historical secondary modules completion report

24 September 2026 · branch `phase-5/continuous-sprint` · integration baseline `4e94c4d`.

**Historical coding handoff for `99c1f2e`: Debug and Release built cleanly, 1,003 regression tests passed, and the separate 66-image Phase 5 capture passed. The auditor reproduced the 1,003 passed / 0 failed / 4 skipped gate on 25 September, but found defects that this green run missed. A5.1 and A5.2 remain UNPROVEN.**

The numbers and module evidence below describe the 24 September implementation, not the repaired candidate. Read the [25 September audit findings](PHASE-5-AUDIT-FINDINGS.md) and [audit remediation report](PHASE-5-AUDIT-REMEDIATION.md) for current repairs, failing-then-passing tests and the latest full gate. Documentation corrections dated 25 September clarify the original evidence without replacing its historical results.

This was the consolidated Phase 5 handoff. It superseded the remaining-work list in the [17 September report](claude-audit-2026-09/PHASE-5-REPORT.md); that report remains historical evidence for the first three accounting increments. The separate sharing/automation implementation note was consolidated here.

The product/recovery work available on 24 September was integrated before this sprint. Module commits integrated by that handoff include `f33616c` (accounting), `1813fa5` (accounting preview/selection protection), `2540b1b` (Tally settings draft saving), `e7f83e4` (registers/approvals/investigation), `a91bfb8` (sharing/automatic import), `365abe5` (automatic-import store catalogue), and `d5492cc` (configured stores/navigation). The final implementation covered by this historical report is `99c1f2e`; follow-up module commits include `1819007` (register draft/query fixes), `c7e73ec` (import context), `65b7259` (SMTP test role state), `4062a59` (SMTP TLS drafts) and `61c4577` (capture harness).

## Decisions and scope retained

The [master plan](ETP-MASTER-AUDIT-AND-PHASED-PLAN.md) remains the scope reference. The following later decisions prevent reverting recovered features or applying obsolete role rules:

- **D22, decided 16 September:** adjustment submission and sales-target entry are Owner-only. This is explicitly recorded in [Opus feature-retention plan, authority table](OPUS-AUDIT-PLAN-FEATURE-RETENTION.md) and reaffirmed in [Opus recovery track, decisions in force](OPUS-RECOVERY-TRACK.md). It supersedes the older Phase 5 row saying a Store Manager raises adjustments. Owner approval remains mandatory. The master plan separately proposes the identifier D22 for a future Phase 8 cash-custody question; that proposal is not this accepted decision and should be renumbered by the planner.
- **Q1 = yes:** Store Managers can create/edit draft register entries and use investigation. VERIFIED transitions, edits to verified entries, and approval decisions stay Owner-only, including SQL enforcement. Store Managers can request a restatement; only an approved request can authorise its bound replacement.
- **Eight stored register types are preserved:** Inward, Outward, Credit Note, Service Receipt, Stock Transfer, Expense, Vendor Invoice and Courier. The old database already supported Expense; removing it merely to match the plan's phrase “seven types” would lose existing functionality. Courier is now reachable.
- **Received files are retained.** The accepted Phase 2/3 closure prompt explicitly kept Import → History → Received files as the document inbox, separate from durable import outcomes; see the [Phase 2 closure report](claude-audit-2026-09/PHASE-2-REPORT.md). Original ETP evidence and manual PDF/image attachment, integrity verification and opening remain. OCR extraction, native PDF financial parsing, PaddleOCR settings and misleading extraction promises remain removed. Historical migration compatibility references do not reintroduce an extraction feature.
- One-action folder import, failed-file retry, the new shell, source-truth accounting amounts and the prior recovery work are preserved. The import failure register still has IF-001–IF-013 marked VERIFIED and no OPEN row assigned to Phase 5; this sprint has not changed those audit verdicts.

## Implemented module behaviour

### Accounting — Prepare → Review → Export

Accounting is Owner-only in navigation, application services and SQL access. The single workspace contains preparation, saved-entry review, mapping approval, batch decisions and permanent export history. Redundant Mapping Review, Validation, Export History and Reconciliation tasks are consolidated into this workflow.

- ADJUSTMENT mappings, atomic mapping-request/decision/save, persisted approval reasons and mandatory rejection reasons from the earlier increments remain.
- The only batch statuses are `DRAFT`, `BLOCKED`, `APPROVED_READY`, `EXPORTED_AWAITING_IMPORT` and `REJECTED`. Missing mappings, an unset company, a disabled production destination or an empty accounting source produce a persisted BLOCKED reason. Correct the setup, reject the blocked batch with a reason and prepare its replacement; blocked entries cannot be approved.
- Each day batch reserves its Phase 1 invoice keys `(store_code, invoice_year, document_number)`. Concurrent preparation has one winner. A duplicate names the earlier batch. Rejection releases reservations; exported batches keep them and cannot be rejected, reset or exported again. Decided entries and receipt history are immutable.
- Owner settings persist the intended company and TEST/PRODUCTION label. Default label is TEST and company is unset. Saving PRODUCTION requires typing the exact company name; it does **not** enable production export. No hard-coded firm name is used.
- File export takes a SQL lock across file creation and receipt/status recording, preventing a concurrent rejection from overtaking it. Existing files are not overwritten. A receipt-write failure before commit rolls back status/history and removes the new file. An uncertain commit retains its evidence file for investigation.
- At `99c1f2e`, a successful export recorded an append-only receipt with batch, absolute path, SHA-256, intended company, environment, actor and UTC time. The 25 September F-17 repair stores and returns only the file name for new receipts; existing receipts remain immutable and may contain their historical absolute paths. The history grid reads persisted rows. Hash tests independently recompute the file hash. The wording describes a file awaiting an unverified Tally result.
- Starting a new preview or changing its scope clears saved-batch selection. Decision buttons cannot act on an old saved batch while the screen shows a different preview. Late entry loads cannot replace a newer preview. Navigation Save also persists pending Tally settings without requiring an unrelated email-settings reason.

The voucher shape is unchanged: one Journal per day. Sales vouchers, inventory integration, live transfer, Tally read-back and reconciliation belong to Phase 7.

### Archive, contacts and sharing

One archive pack list provides Open / Excel / PDF / ZIP / Share. Export actions load and verify the selected archived generation directly; Open is not a prerequisite. The obsolete Compare and Restatements filter are removed. Active contacts can be selected for sharing; Owner contact administration includes adding a new contact.

WhatsApp uses the `whatsapp://` Desktop protocol. The phone number is optional and validated only when non-blank; a blank box opens WhatsApp without a recipient. The selected pack's PDF is prepared and its path copied for attachment. History records the constant "Configured phone", not the number. The result is **HANDOFF_READY**: the user still attaches and sends the file. The app does not claim message delivery.

Email uses MailKit 4.18 with saved host, port, TLS, sender and attachment limit. Port 465 uses implicit TLS; other TLS ports require STARTTLS. Certificate validation is not bypassed. Optional credentials are protected with Windows DPAPI for the current Windows user and the host/port/sender, outside SQL and audit. Clearing them removes only that protected credential file. Owner test-send uses saved settings without an attachment. Sending is an explicit UI action with recipient confirmation.

A report email first records an attempt, then appends `SMTP_ACCEPTED`, `FAILED` or `UNKNOWN`. SMTP acceptance is not inbox delivery. Lost confirmation or a failure recording the final history tells the user to check the mail server before retrying. History shows each attempt's latest state and remains tied to the selected generation. This sprint sent no real email or WhatsApp message.

### Registers, approvals and investigation

Courier is available alongside the seven existing register types. Saving records and changing DRAFT/REVIEW_REQUIRED to VERIFIED require a reason; verification requires a previously saved draft. SQL checks role and locked-day status. Close day can show that day's register entries and navigate to a selected entry.

The approval queue includes pending and decided history. Adjustments stay pending until an Owner decision and enter accounting only after approval. Per D22 the Owner also submits adjustments. Restatement requests are produced by the import workflow and bind the previous import, replacement SHA-256, report, store, period and reason. Approval cannot be replayed against a different source or scope. Facts remain current while a request is pending; the approved replacement and its request usage participate in the import transaction.

No application producer creates REOPEN_DAY, MASTER_MAPPING or CONTROL_WAIVER requests. Migration 0035 cancels existing pending requests of those types with an explicit retirement reason; historical requests remain readable, and the database type constraint still permits those values. This is not a database refusal of future inserts. Investigation results now carry a concrete target task and source reference; register targets preserve their role restrictions. The shell now carries result store/date/reference into the target, selects recorded objects by ID and filters invoice/item details. Close day loads the selected store-day's register entries. Inactive-store results explain that the Owner must enable their scope instead of silently opening all stores.

### Store masters, reports and navigation

`dbo.stores` supplies active store choices, names and combined-report membership; literal shop-code choices are removed from runtime screens and report construction. New stores appear in DSR, targets, combined manual totals and header choices. Store count is dynamic, including the schedule's latest complete business date. Tests cover one and three active stores, newer incomplete days, inactive sources and no active stores.

Tender mapping uses the Phase 1 tender master. The unused generic `controlled_master_values` table and editing surfaces are retired; its audit history remains. Store, brand and tender administration use their actual business masters.

The integration review identified and corrected the hard-coded two-store scheduling assumption and missing store-catalogue injection in automatic import. Custom CSV store filters now retain an explicit Custom scope through refresh, including unknown codes rather than silently broadening totals. Tests verify that selecting another store deliberately replaces the custom subset. Legacy store-report favourites migrate to Store Sales Summary.

### Automatic import

The screen shows installed Windows task state, last run/result and next run separately from saved configuration. Missing or unreadable task information remains explicit. Saving the enable switch does not claim to install or start a watcher. Windows Task Scheduler owns cadence; the unused `poll_minutes` field and column are removed.

The SQL session application lock is explicitly released before a pooled connection returns, including cancellation paths; a failed release clears that pool. An unsupported numbered ETP workbook inside a ZIP is “Not needed” and does not fail other supported files. Unknown layouts for supported report families still require review. Only XLSX/ZIP sources are scanned; obsolete OCR/image intake is not performed by automation. Both automated import entry points receive the configured store catalogue, including context detection for empty workbooks.

### Help and retained evidence

Help was rewritten around the shell and available workflows; Coming Soon states and OCR promises were removed. Five themed WPF shell screenshots are embedded in Help. They show a synthetic fixture with one populated "Demo store" and mostly blank metrics; the catalogue also retains the two migration-seeded stores. They are not production evidence. The historical disposable capture run produced 66 screenshots across all three application roles at 1366×768 and 816×480; see the historical gate below. The subsequent F-19/F-20 repair runs the complete role walk by default and keeps PNG capture optional. Received files remain available for original-document evidence and manual attachment, not extraction or financial fact generation.

## New migrations and upgrade behaviour

No previously committed migration was edited.

| Migration | Changes |
| --- | --- |
| `0033_accounting_foundation.sql` | New batch vocabulary; wider status column; blocking reason; company/environment settings; invoice reservations and concurrency protection; immutable receipts; updated entry/status/rejection guards; Owner-only accounting permissions. |
| `0034_sharing_and_automatic_import.sql` | Sharing attempt keys and observable final outcomes; append permissions matching Viewer-authorised archive actions while keeping history immutable; removal of watch-folder poll constraint/default/column. |
| `0035_registers_approvals.sql` | Audited register save/verification procedure and role/day checks; bound restatement requests and approved replacement handling; D22 adjustment submission checks; retirement of unused request producers while retaining history. |
| `0036_retire_unused_master_values.sql` | Retires the unused generic master table while retaining history; updates application-role configuration so it no longer requires that retired table. |

The 25 September remediation adds `0037_accounting_invariants.sql`; it is separate from the historical migration list above. Its preflights refuse existing duplicate active store-days or duplicate export receipts before adding either unique index, and its reservation refusal distinguishes exported final batches. See the remediation report for its tests and upgrade boundaries.

The accounting upgrade maps `REVIEW→DRAFT`, `APPROVED→APPROVED_READY`, `EXPORTED→EXPORTED_AWAITING_IMPORT`; it preserves recorded amounts and reasons. Existing batches receive invoice reservations. Existing exports do **not** receive fabricated receipt paths, companies or hashes.

**Legacy duplicate preflight:** if two non-rejected historical batches cover the same invoice, 0033 refuses the migration with an explicit message. Review and reject duplicate **unexported** batches with the old application before upgrading. Already-exported overlaps require Owner/accountant review; the migration does not invent a rejection or discard evidence. A migration failure is not permission to edit old migrations or force the new status.

All SQL verification used disposable generated-name fixture databases. The sprint did not migrate `EtpReporting` or `EtpReportingHelios`, alter live Windows permissions, install/run scheduled tasks, deploy an installer or change production Tally books.

## D12–D18 remain open

Implementation defaults do not decide the owner's accounting policies. Formal Phase 5 closure still needs the decisions specified by the plan. This sprint does not create Phase 7 destination profiles or claim that its future policy composers exist.

| Decision | Current safe behaviour / boundary |
| --- | --- |
| D12 — company per store versus firm/company structure | Company starts unset; preparation records BLOCKED and export is refused. Owner may set the intended TEST company; the final company/store structure still needs Owner/accountant agreement. |
| D13 — invoice versus daily Sales voucher granularity | Phase 5 retains the existing daily Journal file only. Phase 7's per-invoice default is not an owner-approved Sales-voucher policy. |
| D14 — named parties versus one retail ledger | No new customer-ledger export is introduced. Customer names/phones are not added to the Phase 5 Journal. Phase 7 single-ledger default remains provisional. |
| D15 — GST ledgers, rates and HSN | No new GST/HSN voucher composer is enabled. Existing event mappings must be approved; missing mappings block readiness. Accountant policy and Phase 7 validation remain required. |
| D16 — tender allocation/clearing and settlement policy | No new Receipt-voucher or settlement path is introduced. CN/Gift Card and other Phase 7 policies are not inferred from existing Journal mappings. |
| D17 — inventory-integrated versus accounting-only | Phase 5 adds no inventory voucher lines or inventory verification. The future accounting-only default is not a final owner choice. |
| D18 — installed Tally build, TEST company/machine and transport | No live Tally probe, import or read-back was performed. Production-labelled destinations cannot export. Exact-company confirmation only saves settings; it does not authorise live books. |

## Historical verification results

These are implementation tests from the original handoff, not Claude's formal acceptance verdict. Module-local counts below overlap across filters and must not be added together. Use the audit remediation report for the repaired candidate's gate.

| Scope | Observed result | Behaviour covered |
| --- | --- | --- |
| Accounting SQL integration | **15 passed, 0 failed** | WPF/SQL positive and negative adjustment preparation; mapping rollback; security regressions; missing-company/mapping BLOCKED; concurrent duplicate guard; rejection release; immutable receipts and entries; independent file hash/settings match; receipt-failure rollback/cleanup; reject/export race; 0032→0033 upgrade preserving old checksums and amounts. |
| Accounting boundary/composer | **12 passed, 0 failed** | Owner checks, totals, exact production confirmation, unavailable live export and composer behaviour. |
| Accounting/relevant Desktop regression | **36 passed, 0 failed** | Status/role button state, settings injection, presentation/session/composition regressions. |
| Later accounting/settings navigation checks | **12 passed, 0 failed** | Clearing old selection before preview/scope changes; Tally-only draft Save and retained/discarded settings. |
| Sharing SQL Server unit suite | **250 passed, 0 failed** | Existing adapters/folder/operations plus new sharing and ZIP behaviour. |
| Focused email transport checks | **10 passed, 0 failed** | Fake transport policy/history and real MailKit against an ephemeral loopback SMTP server: accept, refuse and lost confirmation; synthetic attachment only. All three loopback cases use plaintext and no credentials, so they do not cover implicit TLS on port 465, STARTTLS, certificate negotiation or authentication. |
| Sharing/automatic import SQL integration | **3 passed, 0 failed** | Per-generation final history, immutable history, Viewer append; separate-session lock contention/reacquisition; poll removal and watch-settings save. |
| Sharing targeted Desktop | **25 passed, 0 failed, 1 optional capture skipped** | Role/task state, retained drafts, archive-generation binding, WhatsApp protocol validation. |
| Registers/approvals SQL adapters | **35 passed, 0 failed** | Operations-administration, persistence-use-case, registers/contact and distribution boundaries. |
| Registers/approvals WPF | **15 passed, 0 failed** | Register drafts/verification, approval history, investigation targets and day-workflow behaviour. |
| Registers/approvals SQL integration | **18 passed, 0 failed** | Phase Five operations, Phase Four security, corrective restatement, reimport and Store Manager import regressions. |
| Dynamic store SQL integration on sprint worktree | **3 passed, 0 failed** | New store DSR/target totals and retired masters; one/three active-store complete-day detection; inactive/partial/zero-store cases. |

Representative commands, all serial with `-m:1` and Release configuration:

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release -m:1 -nodeReuse:false --filter 'FullyQualifiedName~AccountingFoundationTests|FullyQualifiedName~PhaseFiveAccountingTests|FullyQualifiedName~AccountingMappingAtomicityTests|FullyQualifiedName~PhaseFourSecurityTests' --verbosity quiet
dotnet test tests-dotnet/Etp.Reporting.SqlServer.Tests/Etp.Reporting.SqlServer.Tests.csproj -c Release -m:1 -nodeReuse:false --filter 'FullyQualifiedName~AccountingServiceBoundaryTests|FullyQualifiedName~ProductisationServiceTests' --verbosity quiet
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj -c Release -m:1 -nodeReuse:false --filter 'FullyQualifiedName~SettingsWorkspaceViewTests|FullyQualifiedName~AccountingWorkspaceControlsTests' --verbosity quiet
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release -m:1 -nodeReuse:false --filter FullyQualifiedName~PhaseFiveStoreCatalogTests --verbosity quiet
```

The register agent's integration filter was `PhaseFiveOperationsTests|PhaseFourSecurityTests|Customer_only_corrections_require_restatement|Reimporting_the_same_Phase_zero_file|CrossPhaseStoreManagerImportTests`; sharing's SQL class was `PhaseFiveSharingAutomationTests`. Release module/solution builds reported zero warnings/errors during their gates. The first sharing migration test caught a dynamic EXEC syntax error; it was corrected before the successful three-case rerun. A settings test exposed unintended concrete SQL loading from a view; it was corrected to use the composition boundary and injected service before the passing regression runs. Neither failure is concealed by a claimed final-head gate.

### Historical combined gate (`99c1f2e`)

These logs and the capture manifest exist only on the sprint machine and are not preserved in the repository; an auditor must rerun the commands. `tmp/` is gitignored. The numbers below are retained historical results, independently reproduced by the auditor for the Release build/test gate; they are not the repaired candidate's results.

- Historical tested implementation: **`99c1f2e`**. The original report-only commit did not alter that tested implementation. The sprint is isolated at `C:\Codex\Reporting Manger\phase5-sprint` on `phase-5/continuous-sprint`; the pre-existing recovery checkout was left intact.
- Release regression: **1,003 passed, 0 failed, 4 opt-in captures skipped**. Desktop 447, Domain 12, Import 110, Reporting 63, SQL integration 119, SQL adapters 252. Log: local `tmp/phase5-final-release.log`. The Phase 5 capture is run separately below.
- Debug and Release solution builds: **PASS, 0 warnings, 0 errors** in each. Commands: `dotnet build Etp.Reporting.slnx -c Debug -m:1 -nodeReuse:false` and the equivalent `-c Release`. Logs: local `tmp/phase5-build-Debug.log` and `tmp/phase5-build-Release.log`.
- Navigation behavior tests pass: archive contacts stay separate, Share reveals recipients, Close day exposes registers, Automatic import includes schedules/history, investigation retains scope/role gates, and accounting remains scrollable at compact size. The three application-role capture runs close without unexpected drafts. Separate Windows-account acceptance is still required.
- Separate Phase 5 capture: **PASS**, 66 screenshots at 1366×768 / 816×480 for Owner, Store Manager and Viewer. Final run log: local `tmp/phase5-capture-final.log`; manifest and screenshots: local `tmp/phase5-ui-final/`. Each role closes without unexpected drafts, fixture data loads, and the real connection/preferences files retain their hashes. All generated fixture databases were removed. These are shown-window WPF logical-pixel captures, not separate Windows-login/native-DPI certification.
- Five Help images are committed in `src/Etp.Reporting.Desktop/HelpScreenshots/`. The historical Release build embedded all five. Matching resource/source PNG SHA-256 values were reported as a one-off manual check, not an automated regression test. Representative normal/compact accounting, archive, Close day, automatic import and Help screenshots were visually inspected; accounting scrolling and archive action-row height were corrected. These are the synthetic fixture images described above.
- Historical runtime source scan: **no matches** for `WLMHW|HEMW` in `src` C#/XAML, excluding `bin` and `obj`. This checked store codes only. Four hard-coded name strings remained in three files: two combined-pack labels in `DailyReportingPackService.cs`, the touch-entry prompt in `DailyWorkflowTouchLayout.cs`, and the DSR guide in `HelpCentre.cs`. F-12 subsequently replaced those names with catalogue-derived labels or neutral wording; F-13 adds a code-literal regression guard. Fixed store codes remain in historical migrations, fixtures and evidence as expected.
- The repository commit hook refreshed its local code-review graph. No generated graph database or private test output is included in the commit.
- All 110 Import tests ran with zero skips on the sprint and auditor machines, which had the private corpus. Without that corpus, this historical Import suite reports 108 passed and two golden-test skips. Existing report/SQL golden tests are also in the full suite. These regression results do not decide the earlier deferred historical-source acceptance or replace Claude’s source-workbook/manual audit.

Additional integration fixes verified in the suite: invoice preview cannot leave actions targeting an older saved batch; Tally-only navigation Save persists its own draft; SMTP TLS loads/saves and participates in draft protection; the SMTP test button follows the loaded role; New register entry asks before discarding edits; register type is filtered before SQL paging and literal underscores search correctly; programmatic header scope does not create a draft; concurrent register refreshes cannot replace the selected type; personal SMTP credentials remain available to each sender in Archive, with Owner-only server settings and test send.

## Phase 5 acceptance evidence and remaining boundaries

**A5.1 and A5.2 remain UNPROVEN after the automated repairs.** Their proof requires the installed application against a populated database under Owner, Store Manager and Viewer. The original audit marked both NOT MET; the fixture role walk and alias guards described in the remediation report do not substitute for that installed walkthrough.

| Criterion | Implementation evidence | Still required for formal acceptance |
| --- | --- | --- |
| A5.1 — every role-reachable destination works | Historical module, SQL-role and UI tests missed five broken layouts; subsequent repairs have an ungated fixture role walk. | **UNPROVEN:** walk every reachable destination and read its status in the installed, populated app as Owner, Store Manager and Viewer; include denied actions and retained drafts. |
| A5.2 — no staff alias screens | The historical consolidation left duplicate destinations; subsequent alias repairs and fingerprint checks are implementation evidence. | **UNPROVEN:** confirm distinct navigation/search/Help destinations and the consolidated trend data in the installed, populated app under all three roles. |
| A5.3 — approved adjustment prepares for 25 August | Real WPF/SQL synthetic positive and negative cases pass; approved mapping and final report required. | Auditor rerun on the intended accepted scope; actual source evidence is not replaced by fixtures. |
| A5.4 — configured store catalogue | Historical dynamic-store tests and the code-only scan passed; the audit still found name literals and an implicit cash-store choice. F-09/F-12/F-13 address those gaps. | Auditor confirms intended active stores, names and explicit cash-store selection on the installation. |
| A5.5 — honest accounting statuses | Schema CHECK, legacy upgrade and lifecycle tests pass; no success claim for a Tally result. | Auditor queries final migrated fixture statuses and checks screen/export/audit wording. |
| A5.6 — duplicate refusal and rejected replacement | Concurrent prepare, approved/exported denial and rejected replacement tested with named earlier batch. | Auditor exercises the final screen and confirms the friendly message under Owner login. |
| A5.7 — exact company/hash receipt and required reason | Fresh-service receipt read, byte hash match, TEST label and blank approval-reason refusal tested. | Final grid/export walkthrough with intended TEST company and independent file hash. |

The installed walkthrough must also exercise every Archive share button as a Viewer; check the surviving trend report's populated rows, dates and chart; confirm migrations 0033–0036 and the new 0037 are applied on the candidate installation; and verify the accounting DENYs for `etp_store_manager` and `etp_viewer`. The original audit's section 8 lists these checks. This remediation does not run them against the live database.

Real SMTP TLS negotiation, certificate validation and authentication, actual WhatsApp Desktop handoff, installed-task observations under the intended Windows identities and touch/native-window review remain **NOT_RUN**. Loopback SMTP and generated WPF images are implementation evidence, not real delivery or native DPI certification.

Phase 5 coding readiness does not close the remaining Phase 4 deployment/recovery evidence, produce a Phase 6 release, freeze the accountant's D12–D18 choices or satisfy Phase 7 Tally read-back. Claude's formal audit and those acceptance boundaries remain explicit.
