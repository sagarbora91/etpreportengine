# Opus audit plan — Phases 7 and 8 (and the v1.4 amendments to Phases 2, 4, 5)

Version 1.0 — 16 September 2026. Author: Claude (planner). Executor of this plan: Opus (auditor). Builder: Codex. Decision owner: Sagar.

This plan tells the auditor how to verify Codex's delivery of Phase 7 (Tally transfer, Stage 2) and Phase 8 (Collections reconciliation, Stage 3) of `ETP-MASTER-AUDIT-AND-PHASED-PLAN.md` v1.4, and the acceptance items that v1.4 added to Phases 2, 4 and 5 (A2.7–A2.10, A4.4/A4.4a/A4.7, A5.5–A5.7). The plan's acceptance items already state what is opened, what is done, the expected observation and the evidence to capture (in square brackets). This document adds the rules, the order, the environments, the negative tests that must be attempted, and the report template.

## 1. Rules

1. **Read-only on live data.** Never run the audit against the shop database `EtpReporting` or against any Tally company whose profile is `PRODUCTION`. Create `EtpPhase7Audit_<date>` / `EtpPhase8Audit_<date>` from the migrations and, where the item asks for "a copy of the shop database", restore the latest backup under a new name. Point `%LOCALAPPDATA%\EtpReporting\settings.json` at the audit database before launching (keep a copy of the live file).
2. **Tally is TEST only.** Use the TEST companies named in D18 (names must contain "TEST"). Record Help → About of the installed TallyPrime build in the report before the first live item. If the D18 machine is not available, every live item is `NOT_RUN` and the phase stays open. Codex's or the owner's captures never turn an item to PASS.
3. **Labels are not interchangeable.** Record each item as one of `PASS` (auditor observed it), `FAIL` (observed contrary), `NOT_RUN` (environment or decision missing, reason stated), `BLOCKED_BY_DECISION` (a D-number is still OPEN and the plan says the task builds only the blocked state). A phase is CLOSED only when every item is PASS or the owner has explicitly accepted a named `NOT_RUN`/`BLOCKED_BY_DECISION` list in writing.
4. **Recompute, never trust.** Every money figure in an acceptance item is recomputed by the auditor from the ETP workbooks (Python over `..\ETP Source Data\`) or from SQL over the source tables, not read from Codex's report. Paste the query and its result.
5. **Evidence discipline.** Screenshots at 1366x768 maximised (and 816x480 where the item touches a screen), file names `p7-<item>-<what>.png` / `p8-…`, saved under `docs/audit/claude-audit-2026-09/phase-7-audit/` (resp. `phase-8-audit/`). Hashes via `Get-FileHash -Algorithm SHA256`. SQL pasted verbatim with row counts. Nothing from `..\ETP Source Data\` or the evidence folders (customer names, phones, UTRs, account numbers) is copied into the audit report.
6. **Rules 9 and 10 of the plan** are audited every time: `git log -p --follow database/migrations/` shows no committed migration edited; `git ls-files | grep -i "collections-samples\|ETP Source Data"` is empty; fixtures under `tests-dotnet/fixtures/` contain no ten-digit phone run (`grep -rE "[0-9]{10}"`).
7. **Helpers.** `C:\Codex\Reporting Manger\.claude\tools\README.md` documents the screenshot, UI Automation and Open-dialog scripts. Never restore the window; activate before capture.

## 2. Preconditions checklist (fill before starting; a missing line is a `NOT_RUN` cause, not a reason to skip the audit)

| # | Precondition | Where to check |
|---|---|---|
| P1 | Codex report exists: `docs/audit/claude-audit-2026-09/PHASE-7-REPORT.md` (resp. `PHASE-8-REPORT.md`) with the sections the plan's section 2 requires, on branch `phase-7/tally-transfer` (resp. `phase-8/collections`) | `git log -1`, report path, commit recorded in the audit |
| P2 | Previous phase CLOSED and merged (Phase 6 release for 7; Phase 7 slice 7c for 8d, 7a for 8a) | `PHASE-6-AUDIT.md` / `PHASE-7-AUDIT.md` sign-off |
| P3 | Decisions frozen: D12–D18 for Phase 7; D19–D21 for Phase 8 slices 8b–8d (8a may run with them OPEN) | plan section 3 rows say DECIDED with date |
| P4 | Migrations apply on an empty database and on a restored copy; checksum table lists every script once | `Etp.Reporting.Desktop.exe --initialize-database`; `SELECT * FROM schema_migrations` |
| P5 | TallyPrime 7.1 installed on the D18 machine, TEST company A and B open-able, loopback port enabled; build string recorded | Help → About; `netstat -an | findstr 9000` |
| P6 | Phase 8: `..\ETP Source Data\collections-samples\` holds the D19 samples and the synthetic fixtures under `tests-dotnet/fixtures/collections-golden/` exist | listing |
| P7 | `dotnet build Etp.Reporting.slnx -c Release` and `dotnet test` green; summary lines pasted | terminal |

## 3. Procedure per acceptance item

Work through the plan's acceptance list in order; slice boundaries (7a → 7b → 7c → 7d; 8a → 8b → 8c → 8d) are the natural stopping points, and an audit may be issued per slice with the phase left OPEN. For each item:

1. Copy the item text from the plan into the audit table.
2. Perform exactly the actions the item names, on the audit database and TEST company. Where the item names a SQL check, run it and paste the result. Where it names a screen, capture it. Where it names a hash, recompute it.
3. Attempt the **negative** half of the item (the refusals, the wrong company, the replay, the second import, the byte edit). An item whose negative half was not attempted is `NOT_RUN`, not PASS.
4. Record PASS/FAIL with the evidence file names.

Phase 7 items with mandatory negative tests: A7.2 (remote endpoint, second TEST profile, self-approval), A7.4 (inactive mapping), A7.8 (replay from a re-exported workbook, second manual import), A7.9 (company B read-back, echo removed), A7.10 (one-byte payload edit, restated source), A7.11 (single-login approval, overlapping windows), A7.14 (swapped ledgers, missing CGST, stray voucher), A7.15 (edit, cancel, delete in Tally), A7.17 (company closed, Tally stopped, mid-request timeout, one rejected voucher), A7.18 (every Owner-only action tried as Store Manager and Viewer), A7.19 (PRODUCTION profile before enablement, second live batch before first reconciled).

Phase 8 items with mandatory negative tests: A8.2 (tender code pointed at the wrong provider kind, Store Manager save), A8.3 (layout with no amount convention, second active layout), A8.4 (same file twice, CSV twin, superset), A8.6 (identical re-import, restatement after matching), A8.7 (locked-day expense, finalise without count, overdue deposit), A8.8 (equal bank credits, over-allocation by one paisa, unbalanced manual match), A8.10 (review with an unowned case, Store Manager write-off), A8.11 (prepare on an unlocked day, direct UPDATE on version 1), A8.13 (₹1 alteration in the TEST company), A8.16 (month close with an OVERDUE item, August expense after close).

Amended Stage 1 items audited with the same discipline when those phases are re-audited: **A2.7** (open every export, grep PDF text and Excel metadata for the basis string; no bare "Revenue" header), **A2.8** (every row of the report acceptance record filled; every named golden test exists and is green), **A2.9** (deposit 4,000 slip A1 → in-transit 4,000; finalise; Bank credited 27 Aug B1 allowed; Add deposit on the locked day refused; `cash_deposit_status` = `BANK_CREDIT_CONFIRMED`), **A2.10** (samples folder present, listed in the report, nothing from it in git), **A4.4/A4.4a** (drill row counts equal the receipt and the live counts; a tampered receipt fails and names the table), **A4.7** (`Get-AuthenticodeSignature` Valid on exe, scripts and installer; `AllSigned` inside a task; CHANGELOG hashes match `Get-FileHash`; OPERATIONS.md has the SmartScreen step and no purchase instruction), **A5.5–A5.7** (status set exact; no "posted"/"imported" strings; second batch for a day refused until the first is REJECTED; export receipt row with hash, company and TEST label; Approve refused with an empty reason).

## 4. Register and documentation checks

- `docs/audit/IMPORT-FAILURE-REGISTER.md`: no new OPEN rows caused by the phase; any new row has a phase mapping.
- Codex's report has every section of the plan's phase-report format; the completion matrix distinguishes implemented / automated-tested / live-tested / not-run; `findstr` finds no "fully working", "Tally-compatible", "rollback supported", "production ready" or "connector working" without a stated scope and evidence.
- Migration checksums: no committed script edited (rule 9). No source data in the repository (rule 10).
- Evidence roots (`Tally\…`, `Collections\…`, `BankSources\…`): `icacls` shows no BUILTIN\Users entry; a grep of the tree finds no phone, customer name, full account number or UTR in any path.
- `docs/OPERATIONS.md` updated with the file-mode and connected-mode runbooks, the recovery steps for `OUTCOME_UNKNOWN` and `PARTIALLY_APPLIED`, and the collections daily routine.

## 5. Report template — `docs/audit/claude-audit-2026-09/PHASE-7-AUDIT.md` (same shape for Phase 8)

1. **Verdict** — one paragraph: `PHASE 7 CLOSED`, `PHASE 7 (slice 7a) PASSED — phase OPEN`, or `PHASE 7 REOPENED`, with the count of PASS / FAIL / NOT_RUN / BLOCKED_BY_DECISION.
2. **Environment** — audit database name and how it was built; TallyPrime build string; TEST company names; branch and commit audited; test summary lines.
3. **Acceptance table** — one row per A-item: item · verdict · evidence (queries, results, file names), recomputed figures beside the expected ones.
4. **Negative tests attempted** — the list from section 3 with outcome each.
5. **Returned items** — numbered; each with what was observed, what the plan requires, and the exact fix expected. Only for REOPENED.
6. **Decisions for the owner** — anything that cannot be fixed by code (an OPEN D-number, a source-data limitation).
7. **Register and documentation checks** — section 4 results.
8. **Sign-off** — `CLOSED` block with date, commit, and the sentence "every acceptance item PASS; owner-accepted exclusions: none / <list>". Absent until closure.

Verdict rules: PASS only when every item is PASS or on the owner's written exclusion list. A single FAIL reopens the phase. `NOT_RUN` because Tally or samples were unavailable keeps the phase OPEN, never CLOSED, and is not Codex's failure.

## 6. Ready-to-paste prompts

### 6a. Phase 7

```
You are the auditor for Phase 7 (Tally transfer, Stage 2) of the ETP Reporting Engine.
Repository: C:\Codex\Reporting Manger\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f
Branch to audit: phase-7/tally-transfer (record the head commit).
Read in this order: docs/audit/OPUS-AUDIT-PLAN-PHASES-7-8.md (this procedure),
docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md section "Phase 7" and section 3 (D12-D18),
docs/audit/claude-audit-2026-09/PHASE-7-REPORT.md (Codex's report), docs/audit/CLAUDE-HANDOFF.md.
Rules: read-only on the live EtpReporting database and on any PRODUCTION Tally company;
build your own audit database from the migrations; use only the TEST companies named in D18;
recompute every figure yourself; attempt every negative test listed in section 3 of the audit plan;
a captured screenshot from Codex or the owner never turns an item to PASS.
Deliverable: docs/audit/claude-audit-2026-09/PHASE-7-AUDIT.md in the template of section 5 of the audit plan,
with screenshots under phase-7-audit/. Commit it on the same branch with the message
"Audit Phase 7: <verdict>" and push. If Tally is not available, mark the live items NOT_RUN
and leave the phase OPEN; do not close it. Do not modify application code or migrations.
Before the first live item record the TallyPrime Help -> About string in the report.
```

### 6b. Phase 8

```
You are the auditor for Phase 8 (Collections reconciliation, Stage 3) of the ETP Reporting Engine.
Repository: C:\Codex\Reporting Manger\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f
Branch to audit: phase-8/collections (record the head commit).
Read in this order: docs/audit/OPUS-AUDIT-PLAN-PHASES-7-8.md, docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md
section "Phase 8" and section 3 (D19-D21), docs/audit/claude-audit-2026-09/PHASE-8-REPORT.md,
docs/audit/CLAUDE-HANDOFF.md, and PHASE-7-AUDIT.md (the accounting loop in A8.13 depends on it).
Rules: read-only on the live EtpReporting database; build EtpPhase8Audit_<date> from the migrations
and the synthetic fixtures under tests-dotnet/fixtures/collections-golden/; the real samples under
..\ETP Source Data\collections-samples\ are read by absolute path and never copied anywhere;
no customer name, phone, UTR or account number appears in your report; recompute residuals,
obligations and case lists from SQL; attempt every negative test listed in section 3.
Deliverable: docs/audit/claude-audit-2026-09/PHASE-8-AUDIT.md in the section 5 template, screenshots under
phase-8-audit/, committed on the same branch as "Audit Phase 8: <verdict>" and pushed.
If D19-D21 are still OPEN, slices 8b-8d are BLOCKED_BY_DECISION and the phase stays OPEN.
Do not modify application code or migrations.
```
