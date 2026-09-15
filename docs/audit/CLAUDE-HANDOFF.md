# Claude handoff — ETP Reporting Engine rebuild

Last updated: 15 September 2026, end of day. This is the one file a future Claude session reads first. It supersedes every earlier progress note.

## Where we are

| Item | State |
|---|---|
| Plan | `docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md` v1.3. Six phases. Owner decisions D1–D11 all frozen on 15 Sep. Published page: artifact "ETP Rebuild Plan" (source `C:\Codex\Reporting Manger\.claude\tools\etp-rebuild-plan.html`). |
| Phase 0 (stabilise) | **CLOSED** 15 Sep, `PHASE-0-AUDIT.md` §8, commit `6791c9e`. Merged fast-forward into `main` (`2cdc258`) and pushed. Live database `EtpReporting` migrated to 0016 on 15 Sep 09:54 UTC (indexes present, 490 invoices unchanged). |
| Phase 1 (data truth: import every ETP family) | **NEXT.** Codex prompt was handed to Sagar on 15 Sep (see "Phase 1 prompt" below to regenerate). Branch `phase-1/data-truth` from `main`. Not started as of this note. |
| Import Failure Register | `docs/audit/IMPORT-FAILURE-REGISTER.md`, rows IF-001–IF-013, all OPEN, all mapped to Phase 1. BC = bill cancellation (proven from 11 files). |
| Two-year Helios corpus | `..\ETP Source Data\HEMW\till 6 sep 26\` (31 consolidated files, 16 Sep 2024 → 6 Sep 2026, extra "Info" sheet, Excel dates). Golden monthly totals: `..\ETP Source Data\HEMW\golden-monthly-HEMW-R025.csv`. Data-only copies that the current importer accepts: `..\ETP Source Data\HEMW\dataonly-for-current-importer\`. Titan World and service-centre consolidated sets: not yet received; import them after Phase 1. |
| Corpus database | `EtpReportingHelios` on `.\SQLEXPRESS`: created 15 Sep with the app's `--initialize-database`, four files imported through the UI, **incomplete** (272 rows dropped by the invoice-year collision, IF-012). Phase 1's whole-folder acceptance A1.11 drops and rebuilds it. Safe to drop any time. |
| App settings | `%LOCALAPPDATA%\EtpReporting\settings.json` points at the live `EtpReporting`. Spare pointers beside it: `settings.json.helios-corpus`, `settings.json.bak-before-helios-corpus`. |
| Tooling | Skill `/etp-import-failure` and agent `etp-import-triage` under `C:\Codex\Reporting Manger\.claude\`; pre-flight checker `.claude\skills\etp-import-failure\check_workbook.py`; desktop automation helpers with README under `C:\Codex\Reporting Manger\.claude\tools\`. |

## The working loop

Claude plans and audits; Codex builds; Sagar decides and merges. Per phase: Codex writes `docs/audit/claude-audit-2026-09/PHASE-N-REPORT.md` → Claude audits by building, launching, screenshotting, re-running every acceptance item and recomputing numbers from the workbooks → writes `PHASE-N-AUDIT.md` with PASS/FAIL → Sagar merges on CLOSED. Plan §2 has the full rules (Codex rules 1–10; rule 9: never edit a committed migration; rule 10: source data never enters the repo).

## Exact next actions

1. When Sagar says Codex has finished Phase 1: read `PHASE-1-REPORT.md`, then run the Phase 1 audit: create an empty database, import `till 6 sep 26` in ONE action with Info sheets present and no manual store/date entry, check A1.1–A1.11 (790 lines / 759 invoices from R025, zero CONFLICT, invoice 100000068 ×3 with FY-end years 2025/2026/2027, every month equals the golden CSV, stock ledger 3,955 rows imports, tender modes sum to invoice totals, re-import adds zero rows). Flip register rows to VERIFIED only on observed success. Review Codex's tender-mode table (D11) with Sagar.
2. If Phase 1 closes: Sagar merges; ask Sagar for the Titan World and service-centre consolidated folders and the 2025-26 exports (D8); run the checker on them; then hand over the Phase 2 prompt (six evening reports, brand rows master, historical import, exports).
3. Standing reminders for Sagar: export ETP after the last bill of the day; keep the backup certificate off the PC once Phase 4 lands (D9); staff use one shared Store Manager login (D7).

## Phase 1 prompt (regenerate from these ingredients if the chat copy is lost)

Scope = plan §5 Phase 1 tasks 1–15 plus register IF-001–IF-013; branch `phase-1/data-truth` from `main`; read order: plan (§2 rules, §3 decisions D1/D3/D11, §5 Phase 1), register, audit 03 and audit 01 headlines; data locations as in the table above (never committed; sanitised sample allowed under `tests-dotnet/fixtures/etp-sample/`); proven facts (NETVALUE = NETAMOUNT − TAX; FY-end INVOICEYEAR identity; BC rule; ten stock types; PAYMENTTYPE25 = Airpay, AGENCYNAME mapping; R013 sign quirk; BinWise bins); suggested order identity → reader → scope → profiles → type rules → tender modes → folder UI → completeness; self-run A1.1–A1.11 on a fresh database; report sections: what changed, how to verify, test results, screenshots, decisions, tender-mode table for D11, register disposition, known gaps, acceptance checklist.

## History (short)

- 14 Sep: first real run of the app; fixed window off-screen, date default, dashboard reflow; found ex-GST vs GST-inclusive discrepancy.
- 15 Sep: six module audits; plan v1.0 → v1.3; decisions frozen; Phase 0 built by Codex, reopened once (0016 audit write, red CI), closed; two-year Helios corpus analysed and imported, exposing IF-012/IF-013; register, skill, agent created.
