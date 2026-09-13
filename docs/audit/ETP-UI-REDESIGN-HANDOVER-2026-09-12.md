# ETP UI redesign handover — 12 September 2026

**IMPLEMENTED — VERIFICATION BLOCKED.** Full conversion and all six authorised local follow-up activities are complete. Current candidate: **1.8.8-r9**. No production deployment or acceptance-VM installation was performed.

- [Installer](../../artifacts/ui-redesign/20260912-sprint/installer-1.8.8-r9/EtpReportingEngine-Setup-1.8.8-x64.exe), [candidate executable](../../artifacts/ui-redesign/20260912-sprint/candidate-1.8.8-r9/Etp.Reporting.Desktop.exe), [source archive](../../artifacts/ui-redesign/20260912-sprint/candidate-1.8.8-r9/source-snapshot.zip) and [hash receipt](../../artifacts/ui-redesign/20260912-sprint/candidate-r9-receipt.json)
- [Local review and repairs](ETP-UI-REDESIGN-LOCAL-FOLLOWUP-2026-09-12.md), [requirement-by-requirement acceptance](ETP-UI-REDESIGN-ACCEPTANCE.md), [527-row coverage](../design/ETP-UI-REDESIGN-ROUTE-COVERAGE.csv), [ledger/next action](../design/ETP-UI-REDESIGN-SPRINT-LEDGER.md), [decisions](../design/ETP-UI-REDESIGN-DECISIONS.md)
- [Original UI before/after](../../artifacts/ui-redesign/20260912-sprint/before-after.html), [task gallery](../../artifacts/ui-redesign/20260912-sprint/gallery.html), [export repair comparison](../../artifacts/ui-redesign/20260912-sprint/local-followup/export-comparison.html), [r9 evidence bundle](../../artifacts/ui-redesign/20260912-sprint/evidence-r9.zip)
- [Prepared installed acceptance runbook](ETP-UI-REDESIGN-VM-ACCEPTANCE-RUNBOOK.md)

## What changed and how to use it

The application has organised module/category tiles and focused task screens. Use Ctrl+K to find an authorised task; each result shows its exact path. Search `DSR` to open the Daily Sales Report directly. Breadcrumbs show its location; Alt+Left returns to the previous task and retained context. Ctrl+F filters the current page. F6 moves between visible shell regions. Contextual Help opens guidance for the task.

Keep the displayed date/store in view. DSR uses its fixed combined business scope; reports requiring one store ask for Titan or Helios. Change an open form's own date/store explicitly. Save/Discard/Stay protects persistent drafts; a busy operation may require completion before leaving. Changing to another database requires a fresh session before work or integration settings are loaded.

Generic report PDFs now include every detail value and total in readable column sections, repeating row numbers and identifying columns. Long notes continue across pages; negative chart bars retain their sign and Other remains included. Excel keeps its five sheets; DSR keeps its dedicated one-page management PDF. After a scope change, refresh before exporting. The app waits for a report file write before closing and distinguishes a saved file from a later activity-history failure.

## Verification and remaining limits

668 tests pass. All29 reports × two date cases produced Excel/PDF files;2,431 workbook cells and complete non-DSR PDF content match the models, with separate DSR checks. A further797-cell stress report passes. All58 detailed models remain unchanged; fixture totals include4600 net,4 units,-400 returns and60 closing-stock quantity. Across257 PDF pages no glyph lies outside the page; representative visual pages were inspected.

The33-file payload was audited, including9 recovery/support scripts and15 migrations. Source implementation is now committed and pushed as `d7eb515`. The r9 build predates the commit; its immutable source archive and hash receipts identify exact build inputs. Only two trailing blank lines were removed before committing. See the [current session handoff](ETP-SESSION-HANDOFF-2026-09-13-UI-REDESIGN.md). Installer and evidence archives remain local ignored artifacts and were not uploaded by the Git push. Executable/installer remain unsigned. Earlier candidates and failed attempts are preserved. The disposable follow-up database was removed, host settings were not redirected, and protected host aggregates remain unchanged.

Full installed journeys, real Windows roles, native scaling, keyboard/Narrator/physical touch, installer lifecycle/offline/recovery and VM performance remain unverified. Actual host keyboard observations belong to r7/r6; r9 has no installed UI claim. External OCR/email limitations remain separate, and no external message was sent.

Resume with the r9 receipt: establish the already requested VM connection, run the queued read-only baseline, preserve original data/backups, then install and test only in the acceptance VM using the runbook. Do not restart source conversion. For recovery, preserve failed setup logs and use a verified compatible backup in an isolated target; never assume automatic reverse migration or delete the original database. A further source repair requires another immutable candidate.
