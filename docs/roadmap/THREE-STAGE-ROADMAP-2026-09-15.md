> **Reference material — not the plan.** This document was written on 15 September 2026 against a snapshot of the
> repository that no longer exists: `main` at `8e35d83`, branch `ui/uiux-v4-touch-first-redesign`, the `knowledge/` vault,
> `AGENTS.md` graphify instructions, the 14 September UI handoff, "29 reports" and the PENDING_INPUT register — all archived
> or deleted by Phase 0 of `docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md`.
> The plan governs. Its Section 8 maps this document's stages, milestones (S1-01…S3-04) and annex phases 0–9 to plan phases
> and lists the statements here that no longer hold (NETVALUE is ex-GST; customer names and phones are stored per D3;
> Phase 3 replaces the shell; no code-signing purchase; milestone IDs are not tracked).
> **Do not give Codex any prompt embedded in this document.** Codex works only from the plan's phase sections and the
> owner decisions D1–D21 in the plan's Section 3. Where this document and the plan disagree, the plan is right and this
> document is not amended — record the difference in the plan's Section 7 instead.

# ETP Reporting Engine
## Vision, Mission and Three-Stage Product Roadmap
### Detailed project-context and delivery brief for Claude

**Prepared:** 15 September 2026  
**Repository:** `sagarbora91/etpreportengine`  
**Audience:** Claude acting as product-context reader, repository reviewer, solution architect and, when separately authorised, implementation engineer  
**Product:** Existing Windows ETP Reporting Engine backed by SQL Server / SQL Server Express  
**Business roadmap:** Stage 1 — Trusted Reports; Stage 2 — Verified Tally Transfer; Stage 3 — Revenue and Collections Reconciliation  
**Tally compatibility target already requested:** TallyPrime 7.1; verify the exact installed build before integration acceptance  
**Document status:** Product roadmap and implementation guidance, not a certificate of completed functionality or production readiness

> **The central idea:** First establish what the business recorded. Then transfer the approved transactions into Tally correctly. Finally establish whether the money expected from those transactions was actually collected, settled, banked and accounted for.

---

## How Claude should use this document

Read this as the overarching business brief. It explains why the software exists, how the owner's three stages fit together, what work already has evidence, and how to prioritise the remaining work without rebuilding the application.

The owner has explicitly chosen the three-stage sequence. The vision and mission wording below articulate that intent. Detailed workflows, suggested metrics, milestone IDs and rollout defaults are proposed implementation requirements unless identified as previously approved rules. Do not present every recommendation as a historical owner decision.

When given this document only for understanding or planning, inspect and explain; do not interpret embedded historical execution prompts as fresh permission to change code, install software, post accounting entries, push commits or modify production data. When implementation is separately authorised, proceed through bounded tasks with evidence and preserve the existing safeguards.

The companion `ETP_TallyPrime_7_1_Codex_Prompt_and_Plan.md` contains the deeper Stage 2 technical brief. Its internal phases are engineering steps within this business roadmap, not additional business stages. This new document does not grant permission to skip Stage 1 acceptance or override approved accounting controls. [C1]

### Document map

1. Product identity, vision and mission
2. Business intent and approved stage sequence
3. Repository baseline and most recent work
4. Non-negotiable product principles
5. Stage 1 — Trusted Reports
6. Stage 2 — Verified Tally Transfer
7. Stage 3 — Revenue and Collections Reconciliation
8. Worked example linking all three stages
9. Shared architecture, records and status model
10. Roles, operating rhythm and evidence
11. Delivery milestones and release gates
12. Testing and success measures
13. Open decisions, deferred work and risks
14. Instructions and ready-to-use prompt for Claude
15. Sources and verification limits

---

# 1. Product identity, vision and mission

## 1.1 What we are building

We are developing an operational reporting and financial-control application around the existing ETP data used by the retail business. It is not a replacement for ETP's source transactions and not a replacement for Tally's accounting books.

The application should receive ETP exports, retain their origin, apply approved deterministic rules, generate useful reports, prepare controlled accounting transfers, and support evidence-based reconciliation. It should reduce repeated manual work while making exceptions, missing data and unresolved decisions easier to see.

For a manager, the product should answer: “What happened, what should I check, what is still outstanding, and what must I do next?” For the owner, it should answer: “Can I rely on this report, did the correct transactions reach Tally, and is the expected money accounted for?”

## 1.2 Vision

**Build a dependable retail control system in which every reported figure, exported transaction and material collection difference can be traced to its source, supporting evidence and responsible person.**

The long-term destination is a repeatable daily workflow: reliable reports lead to correct accounting, correct accounting is checked against actual collections, and unresolved differences receive ownership and follow-up rather than disappearing inside spreadsheets or verbal explanations.

## 1.3 Mission

**Turn ETP exports into accurate, understandable and auditable reports; transfer approved sales and inventory-related transactions into Tally with verified results; and help managers reconcile cash, UPI, card and bank collections through a controlled, evidence-backed process.**

Deliver this progressively inside the application already being built. Retain local operation, deterministic calculations, source lineage, existing permissions and cost discipline. Avoid replacing working components merely to adopt a different framework, protocol or visual design.

## 1.4 The mission in practical terms

| Commitment | What it means in the product |
|---|---|
| Reliable information | The same approved input and rule versions produce the same result. Missing information is visible, not converted to zero. |
| Less repetitive work | Reuse imported facts for reports, accounting and reconciliation instead of retyping the same transaction three times. |
| Verifiable accounting | A generated file is not treated as an imported voucher; read the actual target records before declaring success. |
| Accountable collections | Every material difference has a classification, supporting evidence, owner, next action and review history. |
| Usable daily operations | Managers can complete focused tasks without finding their way through giant combined screens. |
| Safe improvement | New stages extend the existing product and preserve historical data, accepted calculations and recovery controls. |

## 1.5 What this product is not

It is not a new point-of-sale system, an unrestricted accounting autopilot, a generic ERP rewrite, or a system that assumes every bank credit is sales revenue. It is not a cloud-hosting or software-licensing project in disguise. It must not use an AI model to invent amounts, GST treatment, source mappings or explanations for financial differences.

Existing Titan/Helios store contexts and history must remain supported according to the approved configuration. Do not infer that every other business operated by the owner, including jewellery operations, automatically belongs in the present release scope.

---

# 2. The owner's three stages

## 2.1 Stage definitions

| Business stage | Owner's intended outcome | Main question | Completion must demonstrate |
|---|---|---|---|
| **Stage 1 — Trusted Reports** | Generate all necessary, agreed reports from ETP and approved supplementary inputs. | What did the business record, and what does management need to know? | Correct source imports, approved calculations, useful outputs and a working installed reporting workflow. |
| **Stage 2 — Verified Tally Transfer** | Export sales and approved inventory-related transactions so that import into Tally works correctly. | Did the intended transactions reach the intended Tally company correctly, without omissions or duplication? | Expected transactions, actual payload and actual Tally records agree at the required level of detail. |
| **Stage 3 — Revenue and Collections Reconciliation** | Let the manager verify cash, UPI, card and bank receipts, explain differences and complete follow-up. | Was the expected money actually collected, settled, banked and accounted for? | Independent collection evidence, cash counts, settlement/bank matching, reviewed exceptions and controlled accounting consequences. |

## 2.2 Do not confuse the two kinds of reconciliation

**Stage 2 is transfer reconciliation.** It compares the approved ETP-derived posting plan with the generated XML/native JSON and the actual vouchers retrieved from Tally.

**Stage 3 is collections reconciliation.** It compares expected collections with evidence of payment, cash custody, settlement and bank movement. It also checks the corresponding accounting where relevant.

A transaction can be imported perfectly into Tally while its UPI settlement is still outstanding. A bank credit can exist while its Tally receipt is missing. Those are different failures and must remain distinguishable.

## 2.3 Existing report names do not prove a stage is complete

A cash or tender report may already exist in Stage 1. Stage 3 extends that foundation into a complete manager-operated verification process with independent actuals, matching, evidence, exceptions, approvals and closure. Do not create a second unrelated cash report merely because Stage 3 has a new name.

Similarly, an existing XML export button is a Stage 2 foundation, not proof that sales, inventory, returns, duplicate handling and actual Tally read-back have all passed acceptance.

## 2.4 Stage sequence and parallel preparation

The business acceptance sequence is **Stage 1 → Stage 2 → Stage 3**. A later stage must not become a reason to leave the core reporting workflow unreliable.

It is reasonable to prepare Stage 2 contracts or collect Stage 3 settlement samples while Stage 1 acceptance is being completed. Such preparation must not trigger production posting or expand the first release indefinitely. Acceptance is by coherent, approved scope, not by the mere existence of code.

## 2.5 Terminology crosswalk

| Existing terminology | Meaning under this roadmap |
|---|---|
| Repository `Phase 2 Operations` | Historical engineering/module terminology; not automatically Business Stage 2. |
| September UI redesign's four phases and G1–G4 gates | A UI engineering and acceptance programme supporting Stage 1 and shared product quality. |
| Companion Tally plan's internal phases | Implementation steps inside Business Stage 2. |
| This document's `S1-*`, `S2-*`, `S3-*` milestones | Business-stage delivery and acceptance work. |

Keep historical filenames and evidence intact. Add a cross-reference instead of renaming past work or declaring unrelated phases complete.

---

# 3. Repository baseline and the most recent work

## 3.1 Snapshot inspected for this brief

The following remote references were observed on **15 September 2026**. Recheck them at the beginning of a future session; they are a dated snapshot, not an instruction to reset a newer checkout. [R1]

| Reference | Observed commit |
|---|---|
| `main` | `8e35d83f6d442e3add10920f4462042bece8ffce` |
| `ui/uiux-v4-touch-first-redesign` | `8bf8ac1512c65dc2f0773cb42a294e95a9d93107` |

The development branch contains newer Windows-product knowledge and handoffs that are not represented by the older `main` snapshot. Do not base a closure decision on `main` alone. Read the actual working branch, upstream state and uncommitted changes before proposing a merge or implementation change.

The development README still points to an earlier handoff. Following the supersession notices leads through 11, 12 and 13 September to the **14 September UI redesign handoff**, which identifies itself as the current resumption record at the inspected commit. [R2][R3]

## 3.2 Active product architecture

The repository's knowledge entry point identifies the active application as the **.NET 10 / WPF Windows product**, using `src/`, `tests-dotnet/`, `database/`, `installer/` and `scripts/`. SQL Server Express stores the reporting facts. The JavaScript/Capacitor material is legacy/reference material unless a task explicitly targets it. [R4]

The solution is `Etp.Reporting.slnx`. Desktop changes must respect the existing workspace ownership model, `WorkspaceModuleOwnershipRegistry`, owning `Modules/` workspaces and `DesktopCompositionRoot`; `MainWindow` is a shell host, not the place to accumulate new business logic. [R2][R4]

This architecture is the starting point. Do not create a second importer application, replace SQL authority with loose JSON files, or accidentally implement the Windows roadmap in the legacy Android code.

## 3.3 Last substantial work recorded

The latest handoff records substantial desktop/tablet UI redesign work and a last recorded installed acceptance candidate of **1.8.8-r11** in the isolated acceptance VM. It records **669 passing Release tests** for that candidate, but explicitly says these checks do not establish installed UI interaction acceptance. These are repository-reported historical results, not tests rerun while preparing this brief. [R3]

The same handoff lists open acceptance items: the reported Settings sidebar no-op needs reproduction; reported window-caption problems need observable evidence; singular task-count wording needs repair; full installed journeys, saved UI exports, role checks and other device/lifecycle checks remain open. The overall handoff status is **INCOMPLETE — installed UI acceptance remains open**. [R3]

**Roadmap consequence:** Stage 1 should focus on completing and verifying the existing reporting product, including actual installed workflows, rather than restarting the UI or announcing completion from route counts or old test totals.

## 3.4 Existing reporting foundations

The inspected README and report-to-source matrix describe deterministic imports, business-date reporting, source lineage, report generations, finalise/reopen controls, stock/staff/service/cash controls and Excel/PDF output. Treat these as documented capabilities to inspect and verify in the current checkout, not proof that every end-user journey has passed. [R2][R5]

The current handoff refers to 29 reports. Claude must enumerate the actual report registry and accepted requirements, then reconcile that inventory with the documentation. Do not freeze the product around a remembered count if the current registry or approved scope differs. [R3][R5]

## 3.5 Existing Tally foundation observed in source

The inspected `AccountingAndSharingServices.cs` contains `AccountingBatchComposer` and `TallyXmlExportService`. The composer creates debit/credit entries from mappings and checks balance. The exporter writes an XML `Import Data` envelope for a **single Journal voucher**, writes ledger amounts, uses a temporary output file and returns a SHA-256 hash. [R6]

That is useful work to preserve. However, this inspected implementation alone does not demonstrate a complete invoice-level sales/inventory integration, master synchronisation, target read-back, local TDL audit or bank/UPI reconciliation. The present review was targeted, so do not infer that a capability is absent everywhere merely because it was not present in this file.

Claude must trace the module's callers, contracts, persistence, UI, tests and other adapters before identifying exact gaps. In particular, do not relabel a summary journal export as complete item-level sales and inventory integration.

## 3.6 Status discipline

Use separate labels for **documented**, **implemented**, **automated-tested**, **installed-tested**, **live-Tally-tested**, **owner-accepted** and **production-enabled**. They are not interchangeable.

No application build, SQL execution, VM operation, Tally import or TDL load was performed in preparing this roadmap. Remote document/source inspection establishes context; executable acceptance still requires the actual environments.

---

# 4. Non-negotiable product principles

| Principle | Required behaviour |
|---|---|
| One business fact, multiple controlled uses | Reports, accounting and reconciliation consume shared canonical facts; they do not maintain competing copies of sales truth. |
| Deterministic financial results | Approved rules calculate amounts. AI may assist explanation or review, but cannot silently determine financial postings. |
| Preserve source meaning | Keep source signs, document types, dates and identifiers. Do not substitute a convenient interpretation for an unknown field. |
| Missing is not zero | Unknown, unavailable, not imported and explicitly zero are distinct states. |
| Evidence before a success label | A saved file, HTTP success, screenshot or aggregate total alone cannot certify a complete workflow. |
| Human approval at material control points | Managers verify operations; authorised owners/accounting roles approve rules, exceptional treatments and postings within defined permissions. |
| No silent changes to history | Restatements, mapping changes, reopens and corrections create linked revisions and audit events. |
| Local operation first | Normal reporting must not depend on internet access. Optional synchronisation must not determine transaction correctness. |
| Reuse before replacing | Extend existing imports, reports, modules, permissions, archives, diagnostics and recovery mechanisms. |
| Costs remain controlled | Do not add paid middleware, mandatory cloud hosting, paid compiler/tool dependencies or signing purchases without approval. |
| Security remains real | Keep production credentials, customer PII and private banking data out of prompts, public source, knowledge notes and synthetic fixtures. |
| Stage closure is measurable | Close a stage against its acceptance scope and observed evidence, not an optimistic completion percentage. |

Repository rules also reserve mapping/control-rule approval to Owner/Admin, preserve business data and backups from automatic deletion, and defer runtime licensing enforcement until the functional product is complete and explicitly authorised. Preserve these recorded decisions; do not reactivate licensing merely because it has an engineering specification. [R7]

---

# 5. Stage 1 — Trusted Reports

## 5.1 Objective and boundary

**Deliver a working reporting application that reliably generates the agreed operational and management reports from ETP and approved manual inputs.**

Stage 1 includes report accuracy, completeness, usability, installation and recovery. It is not just a collection of attractive PDFs. Its output becomes the controlled source for Stage 2 and the expected-collection baseline for Stage 3.

Define a finite **Stage 1 acceptance catalogue**. Every required report must be either accepted, explicitly deferred by the owner with a visible limitation, or blocked on a precisely identified source/policy input. “All reports completed” is unacceptable while required reports are silently unavailable. An approved limited release must be labelled as limited.

## 5.2 Preserve approved source rules

The following meanings are recorded in the repository knowledge and control matrix. Reconcile later amendments, but do not ask the owner to decide them again unnecessarily. [R4][R5][R7]

| Source or concept | Required treatment |
|---|---|
| R025 / SDB-VariantwiseSales | Canonical item-level sales source. |
| `NETVALUE` | Primary GST-inclusive sales value, not automatically tax-exclusive accounting revenue. |
| R022 / Revenue Report | Invoice/tender control source. Compare matching scope with canonical sales. |
| `INV` | Completed invoice. |
| `SR` | Sales return; preserve the negative quantity/value supplied by ETP. Do not negate twice. |
| `CLUSTER` | Brand Segment, not product category. `GAUTO` means Titan Automatic. |
| Business date | Use the source business date for reporting; keep it separate from import time and system clock. |
| R013/R003 | Approved enrichment sources where defined by the existing profiles; do not let enrichment duplicate canonical sales. |
| Service | Keep controlled service inputs distinct from retail sales. The inspected matrix uses audited manual entry until an approved source replacement exists. |
| Customer identity | Names and phone numbers are excluded from canonical reporting facts under the current policy. |

Unknown tender codes such as `TC`, unsupported transaction types, missing category masters and unapproved denominators remain explicit decision/input items. Do not invent a mapping to make totals balance. [R7]

## 5.3 Required report families

The table is the business coverage framework, not a replacement for the executable report registry.

| Family | Required business outcome | Acceptance focus |
|---|---|---|
| Daily and period sales | Store-wise and combined sales; day, month-to-date, year-to-date and comparable prior-year views. | Dates, returns, scope, totals, comparison periods and drill-down agree with source. |
| Invoice and item detail | Trace a reported amount through invoice, item and source row. | No unexplained duplication or missing document components. |
| Brand and Brand Segment | Meaningful watch-business performance breakdown. | Correct approved mapping; `CLUSTER` is never repurposed as category. |
| Inventory and movement | Explain system stock, supported movement and physical-count differences. | Opening/movement/closing scope and signs are consistent; a snapshot is not misrepresented as a movement ledger. |
| Staff/CRO | Sales attribution, targets and accepted productivity measures. | Attributed plus unassigned amounts reconcile; denominators remain approved and clearly labelled. |
| Service | Separate service sales and tender controls. | Distinct source type, explicit manual zero, approvals and no retail double counting. |
| Cash and tender summaries | Show expected collections and any existing controlled cash calculations. | Expected figures remain distinguishable from independent actual receipt evidence. |
| Exceptions and data quality | Make missing, conflicting, quarantined and out-of-scope data actionable. | Each exception identifies source, scope, impact and next action. |
| Management and report packs | Give the owner a consistent, understandable operational view. | Screen, Excel and PDF use the same accepted calculations and generation identity. |

Category sales, sell-through, stock turn, days of cover or other derived measures must not be activated without their required approved masters, definitions and adequate source coverage. An unavailable metric should say why it is unavailable. [R7]

## 5.4 Stage 1 workflow

```text
Receive ETP files
  → identify approved source profile/version
  → validate structure, scope and duplication
  → import with lineage and controlled error handling
  → normalise into canonical SQL facts
  → perform source controls and collect approved supplementary inputs
  → generate and review reports
  → resolve blockers or record explicit scope exclusions
  → finalise the selected generation
  → archive outputs, approvals and source references
```

An ordinary exact duplicate should not add facts. A changed export for an already imported scope must follow the existing restatement rules rather than silently replacing data. Finalised dates must remain protected, and any authorised reopen must produce a new linked generation.

## 5.5 Report acceptance record

For each report, record its ID/name, business question, source profiles and required versions, canonical fields, filters, calculation definitions, manual inputs, control totals, missing-data behaviour, owner, screen route, export paths, test cases and acceptance status.

Define whether the report is operational, financial, inventory or management reporting. State its amount basis explicitly: GST-inclusive sales, tax-exclusive revenue, tender received, quantity, balance or variance. Do not let an ambiguous label such as “Revenue” conceal different amount bases.

## 5.6 Required quality and usability work

Trace each report from import to model, screen, export and saved output. Compare at least a normal case, a return case, incomplete coverage, a date boundary and a corrected-source case. Check non-ASCII names/labels, leading-zero document references, rounding, negative values and multi-page output.

Resume the existing desktop/tablet design and acceptance programme instead of redesigning it again. Preserve focused category/task navigation, exact report search, breadcrumbs, Back, visible store/date context, keyboard access and appropriate touch support. Verify actual clicks and saved exports on the installed candidate; registry coverage and component renders remain supporting evidence only. [R3][R4]

Protect real data during installer and recovery tests. Prove an isolated restore by restoring and checking the recovered data, not only by checking that a backup file exists or passes a header check.

## 5.7 Stage 1 exit gate

Stage 1 is accepted for its declared scope only when the report catalogue is reconciled, required sources and policies are available, calculations pass worked examples, source totals reconcile, finalise/restatement controls work, and the manager/owner can complete the required installed reporting journeys.

Screen/Excel/PDF agreement, permissions, failed-import handling, usable diagnostics, repeatable installation and isolated recovery evidence belong in the gate. Remaining exclusions must be named and owner-approved; they must not silently flow into Stage 2 as complete data.

**Stage 1 deliverable:** A report-ready Windows release, accepted report catalogue, evidence pack, known-limitations register and reproducible manager workflow.

---
# 6. Stage 2 — Verified Tally Transfer

## 6.1 Objective

**Extend the existing accounting/Tally module so approved sales and inventory-related transactions can be imported into the correct Tally company and verified against the intended result.**

The required outcome is not “an XML/JSON file was created.” It is “the correct, complete and non-duplicated accounting/inventory result exists in the intended company, and we have evidence of that result.”

## 6.2 Hybrid architecture already agreed

```text
Approved ETP facts and frozen report/source generation
  → versioned mapping and expected posting plan
  → pre-import validation and approval
  → Tally-compatible XML or native JSON
  → controlled file import or supported local connection
  → actual Tally voucher/master read-back
  → local TDL visibility and audit support
  → ETP engine's transfer-reconciliation result
```

The **ETP engine** owns mapping, business rules, validation, batch identity, approval, evidence and reconciliation. **XML/native JSON** carries the exchange. The **local TDL** supplies in-Tally views and checks of actual imported records, plus an actual-data export contract. It must not become a second competing financial-rule engine. [C1]

## 6.3 Protocol and deployment policy

The explicit compatibility target remains TallyPrime 7.1, not legacy Tally 7.2 and not an assumed future release. Current official guidance documents native JSON integration from TallyPrime 7.0 onward and XML import/integration. Native JSON means Tally's own object structure, not any arbitrary JSON exported by our application. [T1][T2]

**Recommended implementation order:** Preserve and inspect the existing XML path. Use whichever supported adapter can first deliver a verified end-to-end result without needless rework. Add native JSON behind the same business contract when its schema and actual installed-build behaviour are verified. Do not require two complete production adapters before proving one safe path, and do not downgrade a working tested adapter purely because another format appears newer.

Keep format-specific code separate from business calculations. Changing XML to JSON must not change posting semantics. If both formats are enabled, verify equivalent results with the same accepted fixtures.

Local TDL is the present deployment requirement. Account TDL is a later distribution option: official documentation distinguishes local loading from account-level deployment. It is not a separate financial architecture and does not justify a cloud project now. [T3]

Do not generate a custom JSON manifest and call it an importable Tally file. Do not call a text `.tdl` a compiled `.tcp`; compilation, when needed, requires actual compatible tooling and verification.

## 6.4 Establish the accounting scope before expanding the exporter

| Scope area | What Claude must establish |
|---|---|
| Sales | Approved voucher type, invoice granularity, party/ledger treatment, taxable values, taxes, discounts, rounding, references and tender/clearing treatment. |
| Returns and credit notes | Source type/sign meaning, reference to the original where available, stock effect and approved accounting treatment. |
| Inventory in sales vouchers | Item, unit, quantity, godown and accounting allocations, with actual Tally read-back of the stock effect. |
| Other inventory movements | Only approved transaction sources and movement categories; do not invent purchases/transfers from closing-stock differences. |
| Opening balances | A separately controlled cutover scope, never an automatically repeated daily import. |
| Service | A separately identified source/profile and accounting policy, not a hidden addition to retail sales. |
| Unknown or unsupported cases | Explicitly blocked or excluded with approval and visible coverage; never forced into a generic journal. |

The inspected exporter currently writes one Journal voucher. A journal-only release may be useful for an approved narrow accounting profile, but cannot be described as complete sales-and-inventory integration. Each business scope needs its own acceptance result. [R6]

Do not post inventory twice: once through an inventory-bearing sale and again through a separate stock movement for the same economic event. Distinguish integrated invoice effects from independent movements.

## 6.5 Masters, mappings and pre-import controls

Read or obtain the target company configuration and required masters. Validate ledger names/IDs, groups, voucher types, stock items, units, godowns, cost centres and required tax/party attributes. Creating or altering masters is a separately approved operation, not an automatic fallback for every mismatch. Official import guidance requires the transaction's referenced masters to exist. [T2][T4]

Mappings must have a version, effective scope, approver and change history. Validate company/store routing, period, source completeness, signs, taxes, balanced posting, invoice totals, item quantities, known tender codes and supported inventory effects. Validate by voucher/document, not merely by batch: offsetting mistakes can make a batch total look correct.

No default GST rate, guessed place-of-supply rule, invented party identity or “round-off plug” may be used to pass validation. Required accounting/tax decisions belong to the owner's approved accounting policy and qualified accounting review.

Freeze the expected posting plan before producing the payload. Re-parse the exact saved file or transmitted bytes and compare them with that plan. A changed payload invalidates its prior approval.

## 6.6 Identity, duplicate prevention and retry safety

Every transfer needs a stable source/business identity, a batch ID and a posting-component ID. Scope identity to the target company, source store/entity, source document type and stable source document identity; include the appropriate financial-year or source namespace where identifiers repeat.

The source file hash identifies exact file content, but cannot alone prevent duplicate business documents arriving in differently named/reformatted exports. Keep both file-level lineage and business-level uniqueness. A changed version of an existing invoice is a proposed correction, not automatically a new sale.

Before submission, persist the approved plan and attempt record. After submission, persist the raw response and retrieve actual records. An interrupted import or timeout has **unknown outcome** until read-back establishes which writes occurred. Never blindly resubmit the batch or switch to the other format after an uncertain result.

A retry should target only proven missing eligible components under the approved recovery policy. Prefer durable queues/attempt history and database uniqueness controls to UI-only duplicate warnings. Define operator behaviour for manual imports, where the application does not control every external action.

## 6.7 Three-way transfer reconciliation

| Comparison | What must agree |
|---|---|
| Approved source-derived plan ↔ exact payload | Included/excluded documents, quantities, amounts, ledgers, taxes, dates, target company and expected component count. |
| Exact payload ↔ actual Tally records | Intended fields and financial/inventory effects actually stored, allowing only documented target normalisations. |
| Approved plan ↔ actual Tally records | End-to-end completeness, uniqueness and semantic correctness, independent of an import success response. |

Compare stable identities and line-level detail. Tally may assign different voucher numbers or internal IDs; retrieve and preserve that correspondence rather than pretending source and target identities are always equal.

Capture source scope, mapping version, target company identity, expected counts, actual counts, duplicate/extra records, rejected items, unresolved items and material field differences. A successful response is useful diagnostics, but does not replace actual-data reconciliation. Official XML examples expose per-import outcomes and errors; implement the actual response semantics for the chosen interface. [T4]

## 6.8 Local TDL audit scope

Provide focused Tally views for batch summary, voucher detail, imported component identities, ledger/tax/quantity differences, duplicates, missing expected components and unrecognised extra records. Export actual values through a documented, versioned contract the ETP engine can consume.

**Completeness requires an expected manifest.** A TDL that only lists existing vouchers cannot know which expected invoice never arrived. Make the approved expected identities/counts available to the audit workflow, then compare that expected set with actual target records.

Imported hashes or metadata are useful links, not proof of correctness by themselves. Compare values read from the actual voucher/stock records, not simply a copy of the amount/hash originally sent. Record audit time, company identity, TDL version, query scope and completeness.

A local TDL is not proof of bank receipt and does not independently certify statutory compliance. Its role here is target-side visibility and transfer verification.

## 6.9 Safety, evidence and correction policy

Start with synthetic fixtures and an isolated Tally test company. Production posting stays disabled until the relevant scope, company profile, accounting policy and live integration gate are accepted. Do not expose Tally endpoints publicly or assume a port response proves the correct company is active.

Keep manifests, source-generation references, expected postings, payload hashes, raw responses, actual-data snapshots, reconciliation differences and approvals. For manual import, record the intended company and require company-confirmed read-back; acknowledge that a user can import an exported file into another company outside the controlled workflow. [C1]

Do not promise that a multi-voucher import is an all-or-nothing database transaction across both systems. Design for partial success and unknown outcomes. Recovery may require approved missing-item retries, controlled correction/reversal or an isolated backup restore; never blindly delete a batch from the live books or restore an entire company over unrelated later work.

Later Tally edits, source restatements or mapping changes may invalidate a prior current-state reconciliation. Preserve the historical result and mark the affected scope for re-audit rather than silently rewriting it.

## 6.10 Stage 2 delivery slices and exit gate

Prove one known ETP sales invoice through mapping, validation, export, actual Tally import, target read-back and reconciliation. Then add returns, multi-line invoices, tender splits and approved inventory effects. Expand document types only when each slice has adequate source and policy evidence.

Stage 2 is accepted only for the profiles demonstrated in the actual target build, including correct company routing, duplicate prevention, partial/unknown outcome recovery, actual financial/inventory read-back and functioning local TDL audit where required. Record separately any profile that is implemented but not live-tested.

**Stage 2 deliverable:** An integrated, controlled Tally transfer workflow, tested local TDL package, supported-profile matrix, evidence-backed reconciliation and recovery runbook.

---

# 7. Stage 3 — Revenue and Collections Reconciliation

## 7.1 Objective and meaning of “revenue”

**Create a manager-operated control process that establishes what should have been collected, what was actually collected or settled, what reached the bank or remains in cash custody, and what differences remain unresolved.**

The owner's phrase “revenue reconciliation” covers the operational collection process. The application must still distinguish gross sales, GST-inclusive invoice values, tax-exclusive revenue, tender allocation, cash on hand, processor receivables and bank receipts. They are not interchangeable figures.

The manager should be able to inspect a daily statement, verify the underlying evidence, assign unresolved items and sign off their review without editing the original ETP sales to make them agree with collections.

## 7.2 The evidence chain

```text
ETP expected tender obligations
  → payment evidence / physical cash count
  → provider settlement and deduction bridge where applicable
  → bank credit or cash deposit evidence
  → corresponding Tally postings and balances
  → manager review, exception follow-up and controlled closure
```

Some payment channels have fewer steps. Direct-to-bank UPI, for example, may have no separate merchant-acquirer settlement file. Make the flow channel-specific. Where records lack transaction identifiers or granularity, expose a totals-level or incomplete reconciliation rather than fabricating precise matches.

## 7.3 Inputs and their authority

| Input | What it establishes | What it cannot establish alone |
|---|---|---|
| ETP R022 and approved tender facts | Expected invoice/tender allocation in the source system. | That money was received by a bank or processor. |
| Controlled manager/cashier cash count | Physical cash observed at a particular time, by a named actor. | Why a difference arose, or that a deposit reached the bank. |
| Bank statement | Recorded bank debits/credits with bank reference and relevant dates. | Which sale a credit belongs to without matching evidence. |
| UPI/payment-provider records | Channel-side payment or settlement status, depending on the export. | Final bank receipt when settlement is separate. |
| Card/acquirer settlement | Settlement grouping, recorded deductions and net settlement expectation. | That the net amount was actually credited without bank evidence. |
| Deposit slip/reference | Evidence that a cash deposit was prepared or submitted. | Confirmed bank credit unless supported by bank data. |
| Supporting refund/chargeback/fee evidence | Nature and authorisation of a supported adjustment. | Permission to alter historical sales facts. |
| Actual Tally records | Accounting recognition and current ledger effects. | Physical receipt or correct accounting merely because an entry exists. |

Start with supported file import and controlled manual evidence entry. Direct bank/provider APIs are not prerequisites for the first Stage 3 release and require separate capability, permission and cost assessment.

## 7.4 Manager workflow

1. **Open the store/business-date statement.** See expected totals, source completeness, cash opening position and the unresolved carry-forward balance.
2. **Collect actuals.** Enter a controlled cash count and import approved bank/payment/settlement records with their source references.
3. **Review suggested matches.** Inspect strong-reference matches, grouped settlements, ambiguity and residual amounts; do not accept amount-only coincidence as proof.
4. **Classify differences.** Distinguish timing, shortage/excess, fees, refunds, failed transactions, data gaps, wrong mapping and unidentified receipts.
5. **Attach evidence and responsibility.** Record reviewer, reason, owner, next action and due date according to the approved policy.
6. **Approve the daily review.** Freeze the reviewed version while legitimate outstanding settlements remain open and age across days.
7. **Resolve and account.** Close the collection exception when evidence supports closure; send approved accounting consequences through Stage 2 and verify the resulting postings separately.

The operational manager may verify and propose. That must not silently grant them Owner/Admin rights to change mappings, tax policy, write-off rules or protected accounting controls.

## 7.5 Cash reconciliation

For the defined till/store and count time, use a versioned, explicit formula:

```text
Expected closing cash
  = verified opening cash
  + cash receipts actually due to the till within the scope
  + separately authorised cash inflows
  - authorised cash refunds
  - authorised cash expenses/payments
  - cash physically handed over or deposited from that till

Cash variance = physically counted closing cash - expected closing cash
```

Specify whether imported cash receipts are gross or already net of returns. A refund must appear exactly once; do not net it in the source and subtract it again. Likewise, service cash must not be counted twice if already included in an approved source total.

Maintain both custody and banking views: cash handed to a depositor may leave the till but remain cash-in-transit until the bank credit is verified. Retained float, cashier handover, safe transfer and deposit should have distinct approved movement types.

An unexplained shortage is not automatically an expense, employee recovery or write-off. Preserve the amount and evidence, then follow the approved investigation/approval policy. An excess is not automatically sales revenue.

## 7.6 UPI and card settlement reconciliation

Preserve source transaction references, invoice references where available, merchant/terminal identifiers, transaction date/time, provider status, settlement IDs, net amounts, deductions and bank references. Store raw and normalised values so lossy cleanup does not destroy evidence.

Treat transaction time, business date, settlement date and bank value/posting date as separate fields. Use the provider's actual evidence and approved service-level expectation; do not hard-code a universal settlement timetable.

For a provider statement that starts from gross captured collections, an illustrative bridge is:

```text
Expected net settlement
  = eligible gross collections
  - refunds included in this settlement
  - chargebacks/withholds included in this settlement
  - supported fees and applicable deduction components
  + supported releases or adjustments
```

Each input must be counted once according to the provider's documented layout. If the statement already nets refunds or fees, do not subtract them again. Tax components and accounting splits require approved treatment, not guessed rates.

A settlement can combine multiple sales days; a sale can settle in parts. Support one-to-many and many-to-one allocation without losing residual balances. Matches need strong references where available, amount consistency, dates, store/channel scope and ambiguity handling.

## 7.7 Bank matching and unexplained receipts

Import bank records with file/row lineage and duplicate controls. A repeated bank export must not create another receipt. Use the best stable bank identity available plus an explicit duplicate-collision policy, rather than assuming amount/date uniquely identifies a bank transaction.

Identify bank credits for UPI, cards, cash deposits and direct customer transfers separately. Loans, owner funding, interest, refunds from suppliers or internal bank transfers must not automatically be classified as sales collections. Unclassified entries remain visible until resolved.

Do not match the same bank-credit amount to multiple obligations beyond its unallocated balance. Preserve many-to-many allocation links, who approved them, and all changes. Detect ambiguous candidates and require review instead of auto-selecting the first equal amount.

## 7.8 Exception taxonomy and resolution

| Classification | Interpretation | Default handling |
|---|---|---|
| Awaiting settlement | Expected collection not yet due under the approved channel policy. | Carry forward with expected date; not a confirmed shortage. |
| Overdue settlement | Expected evidence/credit has not arrived by the policy threshold. | Investigate and escalate; do not erase the receivable. |
| Cash shortage/excess | Counted cash differs from the approved cash roll-forward. | Evidence, explanation and authorised resolution. |
| Documented deduction | Provider evidence explains a fee or other deduction. | Check approved treatment and route any required accounting action. |
| Failed/reversed payment | Payment evidence contradicts source tender status. | Investigate source/customer resolution without silently editing ETP facts. |
| Refund or chargeback | Money was or may be returned/recovered. | Link original transaction, evidence, approval and accounting consequence. |
| Unidentified bank credit | Bank amount lacks a defensible source match. | Keep unapplied; never invent a sale. |
| Ambiguous/duplicate match | More than one plausible allocation or repeated source evidence. | Block automatic closure and resolve identity. |
| Missing evidence/data | Required file, count or detail is unavailable. | Mark incomplete, not zero or reconciled. |
| Accounting mismatch | Collection evidence exists but related Tally treatment is missing/wrong. | Create an approved Stage 2 correction request. |

Each case needs an ID, store/entity, amount/currency, source and match references, classification, evidence, assigned owner, age, next action, review decision and linked accounting status. Thresholds and escalation timings are configurable and owner-approved; this document does not invent a universal acceptable shortage.

## 7.9 Daily review and final settlement are different states

A manager may finish today's review while tomorrow's settlement remains open. Model these independently:

- **Daily review:** Draft → Prepared → Manager reviewed → Approved/returned for correction.
- **Collection obligation:** Unmatched → Partially matched → Matched, or Pending/Overdue/Disputed with visible residual amount.
- **Accounting consequence:** Not required → Proposed → Approved → Submitted → Verified, with failure/unknown-outcome states where needed.

“Approved with outstanding settlements” is valid when policy permits and the outstanding amount remains visible. “Fully settled” is not valid until every required obligation is resolved or has an explicitly authorised, separately recorded disposition.

Day-end sales finalisation and later bank settlement closure must not use a single lock flag. Otherwise normal timing delays will either block every report or be hidden to allow the day to close.

## 7.10 Stage 3 to Stage 2 feedback loop

Stage 3 creates a structured adjustment proposal with source evidence, reason, impacted ledgers/profile, amount, approver and affected period. Stage 2 applies the approved mapping, validates, submits and verifies it using the same controls as other accounting batches.

Prevent duplicate revenue recognition. A sale already recorded with a UPI/card clearing receivable is not another sale when the bank settles it. A manager's approval alone must not automatically create additional revenue or delete the original invoice.

Operational closure and accounting closure may differ. Record both and keep any outstanding accounting correction visible after the manager has resolved the operational explanation.

## 7.11 Stage 3 reports and exit gate

Required views include daily expected-versus-actual collections, till/cash roll-forward, UPI pending/settled/overdue items, card settlement bridges, bank match/unapplied-credit lists, deposit-in-transit, exception ageing, manager sign-offs and the accounting-adjustment queue.

Accept Stage 3 only when a manager can complete a realistic multi-day cycle, with partial settlements, bank duplicates, cash differences, refunds, fees, ambiguous matches, approvals and carry-forward. Outstanding balances must roll forward correctly and approved accounting actions must pass Stage 2 verification.

**Stage 3 deliverable:** A usable collection-control workspace, evidence-backed daily statements, an exception/ageing workflow, auditable approvals and verified links to the accounting layer.

---

# 8. Worked example: why all three stages are necessary

**All amounts below are synthetic illustrations, not the user's actual business data, a payment-provider fee schedule, or approved tax/accounting treatment.** The example uses a simplified scope with no opening provider receivable and no refunds. The ₹100,000 figure is tendered transaction value; it is not an assertion of tax-exclusive revenue.

## 8.1 Stage 1 reports what ETP recorded

| Tender | Expected collection |
|---|---:|
| Cash | ₹20,000 |
| UPI | ₹50,000 |
| Card | ₹30,000 |
| **Total** | **₹100,000** |

The reporting engine confirms the approved source control, retains invoice/item/tender lineage and generates the daily statement. At this point it knows the expected tender allocation, not whether the bank has received all of it.

## 8.2 Stage 2 verifies transfer into Tally

The engine applies the approved ledger/tax/inventory policy, creates a controlled batch and checks the actual imported records. The invoice values and tender-related accounting agree with the approved plan. Applicable item quantities also agree.

**Transfer result: reconciled.** This still says nothing conclusive about actual bank settlement or the cash counted by the manager.

## 8.3 Stage 3 tests actual collections

| Channel/control | Evidence | Result |
|---|---|---|
| UPI | ₹50,000 expected; ₹49,000 supported by bank credit; ₹1,000 remains within the illustrative approved settlement window. | ₹49,000 matched; ₹1,000 pending, not yet classified as a shortage. |
| Card | ₹30,000 gross; settlement statement supports ₹180 deductions; ₹29,820 credited by bank. | Settlement bridge balances, subject to verification and approved deduction accounting. |
| Cash in till | Opening ₹5,000 + receipts ₹20,000 − approved payments ₹2,000 − cash sent for deposit ₹15,000. | Expected cash ₹8,000. |
| Cash count | Manager verifies ₹7,900 physically present. | Cash variance **−₹100**, requiring investigation. |
| Cash deposit | ₹15,000 left the till; corresponding bank credit is not yet present. | Deposit-in-transit ₹15,000; not additional sales and not automatically a shortage. |

The manager can approve the daily review with ₹1,000 UPI pending, ₹15,000 deposit-in-transit and a separately tracked ₹100 shortage, if that review status is permitted by policy. The software must not show “everything reconciled” simply because the manager completed their review.

When the outstanding credits arrive, they match the existing obligations. They must not create new sales. Any approved fees, shortage resolution or missing receipt entry goes through the controlled accounting workflow.

**This is the intended progression: report the business → verify its accounting transfer → verify its collections and resolve the differences.**

---
# 9. Shared architecture and data contracts

## 9.1 Extend the application in place

Use the current Domain, Application, Import, Reporting, SQL infrastructure and Desktop boundaries. Keep source parsing out of UI event handlers and keep accounting/settlement rules out of presentation templates. Reuse the current dependency-composition, permissions, configuration, migration, audit and test conventions. [R2][R4][R8]

The design should support an unambiguous path:

```text
Source file/row
  → canonical business fact
  → finalised generation / approved scope
  → expected accounting component
  → export attempt and actual Tally object
  → collection obligation and evidence allocations
  → exception, review and approved correction
```

This is a logical relationship model, not an instruction to rewrite the database or create a new table for every label.

## 9.2 Records that need clear contracts

| Logical record | Minimum purpose |
|---|---|
| Source/import lineage | File identity/hash, profile version, store/date scope, row reference and import outcome. |
| Approved source generation | Frozen fact scope, rule versions, exclusions, controls and approval identity. |
| Accounting plan/component | Intended economic effect, source references, target profile, amounts and unique component identity. |
| Transfer attempt | Exact payload identity, target/environment, attempt history, response and uncertainty state. |
| Tally actual-data snapshot | Retrieval time, verified company identity, actual objects and completeness of query scope. |
| Transfer comparison | Matched/missing/extra/changed objects, differences and evidence-backed status. |
| Collection obligation | Expected amount, source/channel, due-date policy and outstanding balance. |
| Actual receipt/settlement | Bank/provider/cash-count source, reference, dates, amount and validation status. |
| Match allocation | Links between obligation and actual evidence, allocated amount, rule/actor and approval history. |
| Reconciliation case | Difference, explanation/evidence, owner, age, next action and resolution. |
| Adjustment proposal | Approved accounting consequence linked back to the collection case and forward to Stage 2. |

Reuse existing records wherever their semantics fit. Add versioned migrations only for demonstrated gaps. Preserve original evidence and old review versions; do not replace them with a mutable “latest total.”

## 9.3 Cross-stage control rules

Amounts require exact decimal arithmetic and explicit rounding policy. Store monetary values, quantities and rates at their appropriate precision. Preserve currency even when the accepted first profile uses only INR.

Use business dates for period logic and UTC timestamps for system events, displaying operational times in the user's Asia/Kolkata context. Keep bank/settlement dates distinct. Do not derive a historical business date from the date a file happened to be uploaded.

Reopening a finalised source scope must identify downstream impact: affected report generations, export batches, Tally reconciliation and collection obligations. Keep the earlier accepted result as history while marking current reliance stale or requiring re-audit.

Prevent concurrent double finalisation, duplicate submission and over-allocation of the same receipt through persistent controls, not only disabled buttons. A crashed or disconnected client must not turn an uncertain operation into a false success.

---

# 10. Roles, operating rhythm and evidence

## 10.1 Proposed responsibilities, respecting existing permissions

The role names below describe responsibilities. They do not authorise new application roles or broader permissions automatically. In a small team, one person may perform multiple operational tasks, but the recorded approval and authority boundaries must remain intact.

| Responsibility | Manager / permitted operator | Owner/Admin | Accounting reviewer where designated | Software |
|---|---|---|---|---|
| Import ETP and inspect report exceptions | Perform within current permission. | Oversight and exceptional authority. | Review accounting-relevant issues. | Validate, retain lineage and expose blockers. |
| Approve mappings/control rules | Propose only unless explicitly authorised otherwise. | Approve under existing policy. | Supply accounting/tax review. | Version, enforce and audit the approved rule. |
| Verify cash and collection evidence | Prepare and review operational records. | Review escalated exceptions. | Support bank/ledger interpretation. | Match, calculate and retain residuals. |
| Approve material adjustments/write-offs | Request and attach evidence. | Approve under the configured policy. | Validate the accounting treatment. | Do not invent approval; enforce it. |
| Submit to Tally | Only with granted posting rights. | Authorise production use and exceptional actions. | Operate/review where assigned. | Validate, submit, read back and reconcile. |
| Close a daily review | Sign the operational review within scope. | Review overrides/escalations. | Confirm period/accounting closure where required. | Preserve the reviewed version and open items. |

Only Owner/Admin approval of mapping/control-rule changes is already recorded in the repository; do not quietly broaden it for convenience. [R7]

## 10.2 Proposed operating cadence

**At the beginning of the operating day:** Review carried-forward unmatched collections, overdue settlements, pending deposits and unresolved transfer failures. Confirm the cash opening position and responsibility for exceptions.

**At daily reporting close:** Import and validate ETP, complete approved manual inputs, review the reports and finalise the appropriate generation. Record cash counts and the manager's collection review without pretending future bank credits have already arrived.

**When bank/provider evidence becomes available:** Import it, review matches and update residuals. A newly settled prior-day transaction updates its obligation and review history; it does not rewrite that prior day's original sale.

**Weekly:** Review aged and repeatedly recurring exceptions, unmatched bank credits, duplicate problems, unresolved Tally outcomes and evidence quality. Identify a cause and responsible action instead of only repeating totals.

**Monthly/period close:** Reconcile remaining clearing balances and approved adjustments with the accounting reviewer. Preserve open items across the period boundary and protect closed periods according to approved policy.

These are proposed rhythms, not inherited clock times or payment-provider service levels. Configure actual deadlines, escalation thresholds and delegation with the owner.

## 10.3 Evidence storage

Keep SQL as the authoritative state store. Keep supporting files in a controlled, configured evidence root with clear access rules. A suitable proposed structure is:

```text
<evidence-root>/
  Reports/<store>/<business-date>/<generation-id>/
  Tally/<environment>/<company-key>/<batch-id>/
  Collections/<store>/<business-date>/<review-id>/
  BankSources/<account-key>/<statement-scope>/<import-id>/
  SettlementSources/<provider-key>/<settlement-scope>/<import-id>/
  Exceptions/<case-id>/
  Acceptance/<candidate-id>/<test-run-id>/
```

Use safe identifiers rather than customer names or full bank account numbers in paths. Record source hashes, evidence relationships and actor/timestamp information. SHA-256 hashes help detect changes, but are not tamper-proof protection if an attacker can rewrite both evidence and its stored hash; permissions and protected history still matter.

An approved OneDrive or other shared evidence folder can support document access, but must not become the live SQL/Tally database or a transaction-locking mechanism. The application must handle delayed/unavailable evidence synchronisation explicitly.

---

# 11. Delivery plan and release gates

## 11.1 First action: establish an evidence-based baseline

Before new feature work, Claude should inspect the actual checkout and produce a stage-aligned capability matrix. This is shared intake work, not a fourth business stage.

Read repository instructions and selective knowledge first, then inspect implementation and relevant tests. Follow available Graphify/code-review-graph guidance for relationships and verify it against source. If those tools are genuinely unavailable, record the limitation and use the accessible repository material without pretending a graph query ran. [R8][R9]

The matrix must distinguish working code, incomplete code, unverified implementation, missing policy, missing source data and an actual external test blocker. A source-search miss is not proof of absence. Historical test counts are not fresh results.

## 11.2 Milestone sequence

| Milestone | Work package | Evidence required to close |
|---|---|---|
| **S1-01** | Reconcile required report catalogue, source coverage and approved/open business rules. | Report-to-source-to-test matrix; named decisions and exclusions; actual branch/source identity. |
| **S1-02** | Verify/fix canonical imports, calculations, source controls, finalisation and report output consistency. | Reproducible fixtures, independent control totals, regression results and saved-output checks. |
| **S1-03** | Complete current installed workflow/UI acceptance and repair established defects. | Actual failing/retested journeys, permissions, saved exports, device limits and candidate identity. |
| **S1-04** | Accept the report-ready release for the agreed scope. | Manager/owner acceptance, installer/recovery evidence, clear remaining limitations and operating runbook. |
| **S2-01** | Audit existing Tally module and approve accounting/stock scope and mapping contracts. | Reuse/gap matrix, target-company profile, supported document types and source/policy inputs. |
| **S2-02** | Deliver one safe invoice from ETP to actual Tally read-back. | Approved plan, exact payload, target records, comparison and duplicate-replay result. |
| **S2-03** | Add required return/inventory profiles and local TDL audit. | Profile-specific live evidence, expected-manifest completeness, quantities and ledger/tax checks. |
| **S2-04** | Prove failure recovery and accept the accounting transfer scope. | Partial/unknown-outcome tests, controlled correction, company safeguards and owner-approved production gate. |
| **S3-01** | Establish actual-collection inputs and approved channel/cash policies. | Supported bank/provider examples, duplicate rules, amount/date meanings and mapping decisions. |
| **S3-02** | Build/reuse cash, settlement, bank matching and residual-balance controls. | Independent evidence, grouped/partial match results, no over-allocation and carry-forward checks. |
| **S3-03** | Complete manager review, evidence, exceptions, ageing and escalation. | Usable daily workflow, approvals, reopening history and visible outstanding obligations. |
| **S3-04** | Prove multi-day operation and the adjustment loop back into Stage 2. | Operational and accounting closure results, period-boundary cases and owner/manager acceptance. |

Each milestone should be a bounded change set with explicit ownership and relevant regressions. Do not combine a UI rewrite, database redesign, Tally import and bank connector into one unreviewable change.

## 11.3 Shared definition of done

A milestone is done when its requirements are identifiable, implementation is integrated into the actual application, appropriate positive/negative tests ran, evidence points to the exact source/candidate, operators can perform the required journey, and its remaining limitations are explicit.

A simulation can close an offline test requirement, not a live-Tally requirement. A unit test can close a unit requirement, not an installed-touch requirement. “Implemented — verification blocked” remains a useful honest status, but is not full acceptance.

Production enablement is a separate recorded decision. Do not infer it from a successful build, repository push, presence of an installer or old execution instruction.

## 11.4 Acceptance scope changes

If the owner approves a narrower first release, record the precise reports, stores, sources, voucher profiles or payment channels included. Keep exclusions visible in the application and prevent unsupported data from slipping through a supposedly complete workflow.

Do not silently reduce scope to make a stage green. Do not block all safe engineering because one real source file or external environment is unavailable; complete the independent work and name the exact remaining dependency.

---

# 12. Test programme and success measures

## 12.1 Cross-stage acceptance cases

Use synthetic data first and then approved, sanitised representative samples. Expected results must come from explicit worked examples or an independently reviewed calculation, not merely from the implementation under test.

| Case | Required outcome |
|---|---|
| Normal single-line and multi-line sale | Accurate report, intended posting and traceable expected tender. |
| Signed-negative return | No double sign reversal or duplicate refund subtraction. |
| Repeated identical ETP export | No new canonical facts or repeated accounting effect. |
| Same business invoice in another file | Business-key duplicate protection, not only file-hash protection. |
| Corrected finalised source | Controlled restatement and downstream re-audit; old evidence retained. |
| Missing R022/source coverage | Explicit incompleteness; not a zero control total. |
| Unknown tender/transaction code | Quarantine or block; no guessed posting. |
| Brand Segment/category distinction | Correct labels and no invented category result. |
| LY/financial-year boundary | Correct comparable period and visible missing coverage. |
| Screen versus saved Excel/PDF | Same accepted values, full content and readable output. |
| Wrong target company | Controlled submission blocked or manual-import mismatch detected; never reconciled. |
| Missing/incorrect target master | Clear validation outcome and controlled master correction. |
| Inventory-bearing invoice | Actual quantity and accounting effects agree; stock not posted twice. |
| Partial Tally import | Successful components retained; missing/rejected components identified. |
| Timeout after possible Tally write | Outcome unknown until read-back; no blind resubmission. |
| Same total but wrong ledger/item/date | Semantic mismatch detected despite balanced totals. |
| Missing expected voucher or extra duplicate | Manifest-to-actual completeness check fails. |
| Subsequent Tally edit/deletion | Prior result retained; affected scope requires re-audit. |
| Cash short/excess with deposit in transit | Till variance distinct from bank timing; evidence preserved. |
| UPI pending then settled next day | Correct carry-forward and closure; no extra sale. |
| Card batch across days with deductions | Supported settlement bridge and correct residual allocation. |
| Partial/grouped bank credit | Many-to-many matches with no over-allocation. |
| Repeated bank/provider statement | No duplicate actual receipt or settlement. |
| Refund/chargeback/fee | Linked evidence, no double deduction and controlled accounting action. |
| Equal-amount ambiguous bank credits | Ambiguity visible; no arbitrary auto-match. |
| Manager review with open settlements | Daily review may close under policy; outstanding obligations remain open. |
| Unauthorised mapping/write-off/posting attempt | Permission enforced and attempted action recorded appropriately. |
| Crash, restart, restore and period reopen | Durable state and evidence, no duplicate postings or lost residuals. |

## 12.2 Measures to track

Track reporting coverage against accepted scope, unexplained source-control differences, installed journey completion, source-to-output traceability and time spent preparing/reviewing a report.

For Stage 2, track verified versus pending/failed/unknown batches, duplicate-posting incidents, missing/extra target components and recovery outcomes. Do not count exported files as verified imports.

For Stage 3, track independently matched value, outstanding value by age/channel, unidentified bank receipts, cash differences, evidence completeness, overdue actions and time from exception creation to resolution. Show gross amounts and residuals, not just a match percentage that hides a small number of high-value unresolved cases.

Numeric materiality, time and service-level targets require an owner-approved baseline. Do not invent promises such as a fixed auto-match rate or a fixed number of hours saved. Correctness, absence of silent loss and no duplicate economic posting are design requirements, not marketing estimates.

---

# 13. Open decisions, deferred work and risks

## 13.1 Inputs to resolve through existing registers

The inspected pending-input register is dated 28 August 2026. Its entries must be reconciled with later decisions and current code before treating them as still unresolved. Preserve resolved definitions and existing input IDs. [R7]

| Decision area | Safe interim behaviour |
|---|---|
| `TC`/unknown tenders and unsupported transaction types | Quarantine, quantify impact and withhold affected acceptance/posting. |
| Product category and stock-movement definitions | Preserve known source meaning; keep unsupported metrics unavailable. |
| DSR/staff/ABV/ASP denominators | Retain separately labelled approved measures; do not invent replacements. |
| Service source and scope | Continue the approved controlled workflow until a replacement is accepted. |
| Sales voucher granularity, party policy, tax and inventory accounting | Keep the affected Tally profile disabled until its policy is approved. |
| Bank/provider sample layouts and identifiers | Build generic contracts and synthetic tests; do not claim a real connector works. |
| Settlement timelines, shortage tolerances and escalation thresholds | Keep values explicitly unset or use approved existing policy, not universal guesses. |
| Role delegation, adjustment and write-off authority | Apply current least-privilege permissions; require explicit approval for changes. |
| Store/account/company routing and scope | Use confirmed configuration; never route solely from a display name. |
| Evidence roots, access and recovery environment | Preserve current controls and isolate tests; do not broaden access silently. |

Missing policy is not a licence to stop unrelated work. Complete validation, visible blocking states, test scaffolding and other independently safe tasks.

## 13.2 Explicitly deferred or outside the current roadmap

Keep Account TDL distribution, commercial licence enforcement, cloud migration, direct bank APIs and broad new-business expansion outside the critical path unless separately authorised. They are possible later projects, not hidden fourth stages.

The user has previously ruled out an unaffordable code-signing purchase for now. Do not reintroduce certificate procurement as a new mandatory dependency for this roadmap. Preserve the unsigned-artifact warning, hashes, controlled distribution and any organisational acceptance requirements; unsigned does not mean risk-free. The older pending register's signing item should not override the owner's later cost decision.

Do not automatically delete business history or backups to manage storage. Do not change known financial rules to simplify test data. Do not create another knowledge vault or work tracker that competes with the existing repository records.

## 13.3 Highest-priority risks

| Risk | Control |
|---|---|
| Reading stale `main`/handoff evidence | Recheck branch/HEAD, follow supersession notices and state the evidence date. |
| Calling the reporting release complete from test counts | Require actual installed report and manager journeys. |
| Treating one Journal exporter as full sales/inventory integration | Maintain document/profile-specific scope and actual target checks. |
| Duplicate posting after timeout or format fallback | Persist attempt identity and read back before retrying. |
| Same batch total hides wrong accounting | Compare voucher/line semantics and actual inventory effects. |
| Bank timing becomes a false cash/revenue shortage | Separate obligation, custody, settlement and bank dates. |
| Settlement posting creates revenue twice | Link the settlement to its existing clearing obligation. |
| Manager sign-off hides unresolved amounts | Separate daily review from residual settlement/accounting status. |
| Missing fields are silently guessed | Block the affected path and keep an actionable input register. |
| New stage breaks prior accepted behaviour | Run cross-stage regressions and preserve immutable evidence. |

---

# 14. Claude's working instructions and expected outputs

## 14.1 Your role

Understand the business first. You are extending an existing retail product, not proposing a blank-slate platform. Treat the owner's three stages as the product structure and use the current repository to determine what is already built, what needs verification and what genuinely remains missing.

Follow `AGENTS.md`, `knowledge/AI-CONTEXT.md` and the relevant route in `knowledge/AI-ROUTER.md`. Do not load the entire vault by default. Read the actual code/tests after graph-assisted discovery where available. Report differences between documented intent and implementation rather than silently choosing whichever is convenient. [R4][R8][R9]

Do not assume direct access to a Windows VM, real Tally, banking credentials, provider APIs, physical touch hardware or an accounting reviewer. Confirm the actual available tools/environment. Record an unmet external test precisely and continue independently useful work.

## 14.2 Initial response Claude should produce

First give a plain-language restatement of the vision, mission and three stages. Then report the actual branch/HEAD inspected, recent handoff and source references. Provide an existing-capability/gap matrix grouped by stage and distinguish source evidence from fresh executable results.

Identify which Stage 1 acceptance work should finish next. Identify a single smallest Stage 2 vertical slice and the source/accounting inputs it needs. Identify Stage 3 data contracts and evidence samples that can be prepared without premature production automation.

Produce a prioritised backlog with requirement ID, stage, owning module, current state, next action, dependency, acceptance criterion and evidence link. Reuse the existing ledgers and decision registers rather than duplicating them without a reason.

## 14.3 When implementation is separately authorised

Make the smallest safe changes in the owning modules. Preserve unrelated edits, working imports, financial rules, permissions, finalised records and frozen release artifacts. Do not reset branches, overwrite another agent's work or rebuild solely to change an embedded commit label.

Use bounded commits and stage-appropriate tests. Before sharing a completion report, identify actual files changed, migrations introduced, tests executed, results, failed/blocked checks and exact next actions. A fake adapter or demo response must be visibly labelled and cannot pass the real integration gate.

No production data modification, live Tally posting, credential bypass, cloud purchase, software purchase, external message, push, merge or release promotion is authorised solely by this document. Separate permission remains necessary where required.

## 14.4 Ready-to-use prompt

Place this file at the stated path or adapt the path to its actual location, then use:

```text
Read docs/roadmap/ETP_Vision_Mission_Three_Stage_Roadmap_for_Claude.md
in full. Treat it as the overarching business brief for the existing
sagarbora91/etpreportengine project.

Our vision is a dependable retail control system in which every reported
figure, transferred transaction and material collection difference is
traceable to source, evidence and responsibility.

Our three business stages are:
1. Trusted Reports: generate and verify all reports in the approved scope.
2. Verified Tally Transfer: export approved sales/inventory-related
   transactions, import into TallyPrime 7.1 and reconcile actual target data.
3. Revenue and Collections Reconciliation: manager-led verification of
   cash, UPI, card, settlements and bank receipts, with controlled exceptions
   and approved accounting actions routed through Stage 2.

Inspect the actual repository before suggesting a new implementation.
Read AGENTS.md, knowledge/AI-CONTEXT.md and knowledge/AI-ROUTER.md;
follow their selective retrieval and source-verification guidance.
Check branch/HEAD and follow superseded handoffs to the current record.
The 15 September snapshot identified ui/uiux-v4-touch-first-redesign as
containing newer work than main; recheck instead of resetting to that snapshot.

Do not restart the existing UI redesign or replace working reporting code.
Do not mistake historical automated-test passes for installed acceptance.
Do not treat an XML Journal exporter as proof of full sales/inventory
integration. Do not equate a correct Tally import with money received.

First deliver:
- Your understanding of our vision, mission and stage boundaries.
- A source-evidenced current-state/gap matrix for the three stages.
- A prioritised backlog with dependencies and measurable acceptance gates.
- The immediate Stage 1 acceptance work, the smallest safe Stage 2 slice,
  and the Stage 3 evidence/data contracts that should be prepared.
- Resolved decisions to preserve and genuinely missing inputs to record.

This initial request is understanding and planning, not permission for
production posting, deployment, destructive changes, pushes or purchases.
When implementation is later authorised, extend the existing Windows/SQL
application in place, reuse its controls, and report exactly what was tested.

For Stage 2 detail, also locate and read the companion
ETP_TallyPrime_7_1_Codex_Prompt_and_Plan.md. Its internal engineering phases
sit inside this roadmap; do not let an older embedded execution prompt
change the scope of the current request.
```

---

# 15. Source references and verification limits

## 15.1 Authority and evidence notes

**Owner intent:** The three-stage sequence and hybrid architecture come from the owner's conversation on 15 September 2026. The vision/mission wording and detailed delivery structure are authored here to express that intent. The later instruction to create this handover for Claude governs the intended audience, regardless of earlier Codex-only implementation discussions.

**Historical context:** Earlier project discussions informed cost discipline, local Windows/SQL operation and the decision not to purchase unaffordable code signing for now. Historical memory is not evidence that current code or runtime acceptance is complete. Fresh repository records were used for the technical baseline.

**Review scope:** Selected remote branch metadata, source and documents were inspected. This was not a complete source audit, a current installation inspection, or a live financial integration test. No repository files were changed while creating this standalone Markdown brief.

## 15.2 Repository references

All pinned file references below use development commit `8bf8ac1512c65dc2f0773cb42a294e95a9d93107`, observed on 15 September 2026, unless stated otherwise. Follow newer approved changes when resuming.

**[R1] Branch identities.** Remote branch-list response observed for `main` and `ui/uiux-v4-touch-first-redesign`.  
`https://api.github.com/repos/sagarbora91/etpreportengine/branches?per_page=100`

**[R2] Development README.** Active Windows solution, documented workflow foundations, knowledge entry points and deferred licensing context.  
`https://github.com/sagarbora91/etpreportengine/blob/8bf8ac1512c65dc2f0773cb42a294e95a9d93107/README.md`

**[R3] Current inspected resumption record.** 14 September handoff: last recorded r11 candidate, historical 669-test result, outstanding installed acceptance, reported Settings/caption issues and scope limits.  
`https://github.com/sagarbora91/etpreportengine/blob/8bf8ac1512c65dc2f0773cb42a294e95a9d93107/docs/audit/ETP-SESSION-HANDOFF-2026-09-14-UI-REDESIGN.md`

**[R4] AI Context.** Product architecture, R025/R022 meanings, signs, Brand Segment, privacy and missing-data boundaries.  
`https://github.com/sagarbora91/etpreportengine/blob/8bf8ac1512c65dc2f0773cb42a294e95a9d93107/knowledge/AI-CONTEXT.md`

**[R5] Report-to-Source Matrix.** Source relationships, manual inputs, report controls, business-date contract, immutable generations and restatements.  
`https://github.com/sagarbora91/etpreportengine/blob/8bf8ac1512c65dc2f0773cb42a294e95a9d93107/docs/22_REPORT_TO_SOURCE_MATRIX.md`

**[R6] Inspected accounting/export source.** `AccountingBatchComposer` and `TallyXmlExportService`; balance check, single Journal-voucher XML, temporary-file output and SHA-256 hash.  
`https://github.com/sagarbora91/etpreportengine/blob/8bf8ac1512c65dc2f0773cb42a294e95a9d93107/src/Etp.Reporting.Infrastructure.SqlServer/AccountingAndSharingServices.cs`

**[R7] Pending Input and Deferment Register.** Dated 28 August; unresolved/resolved decisions and recorded deferments, to be reconciled with later authority.  
`https://github.com/sagarbora91/etpreportengine/blob/8bf8ac1512c65dc2f0773cb42a294e95a9d93107/docs/PENDING_INPUT_AND_DEFERMENT_REGISTER.md`

**[R8] Repository instructions.** Selective knowledge retrieval, Graphify/code-review-graph guidance, source verification and conflict handling.  
`https://github.com/sagarbora91/etpreportengine/blob/8bf8ac1512c65dc2f0773cb42a294e95a9d93107/AGENTS.md`

**[R9] Knowledge router.** Domain-specific reading and current UI-handoff pointer.  
`https://github.com/sagarbora91/etpreportengine/blob/8bf8ac1512c65dc2f0773cb42a294e95a9d93107/knowledge/AI-ROUTER.md`

## 15.3 Companion planning document

**[C1] `ETP_TallyPrime_7_1_Codex_Prompt_and_Plan.md`.** Prepared earlier on 15 September 2026 in this conversation. Relevant retrieved sections establish the agreed hybrid structure, actual-data reconciliation, expected manifest, local TDL deployment and controlled production safeguards. It is a requirements document, not proof that its requested implementation has been completed. Locate the actual file; do not assume it is already committed in the repository.

## 15.4 Official Tally references checked for this brief

These sources support limited platform claims, not the correctness of our implementation. Validate exact schema, build behaviour and test evidence at implementation time.

**[T1] TallyHelp — Integration using JSON.** Native versus custom JSON and native support from TallyPrime 7.0 onward.  
`https://help.tallysolutions.com/tally-prime-integration-using-json-1/`

**[T2] TallyHelp — Import Data from JSON or XML.** Supported file import and referenced-master preparation.  
`https://help.tallysolutions.com/import-data-from-xml-or-json/`

**[T3] TallyHelp — Configure TDLs and Add-Ons.** Local and Account TDL deployment are distinct supported deployment methods.  
`https://help.tallysolutions.com/deploy-tdls-and-add-ons-tally/`

**[T4] TallyHelp — Sample XML.** Voucher prerequisites, sample errors/outcome fields and guidance to inspect exported target-voucher structure.  
`https://help.tallysolutions.com/sample-xml/`

---

## Final product direction

**Complete and verify the reporting foundation. Extend it into controlled, verified Tally transfer. Then build the manager's evidence-backed collections workflow on those same facts and controls.**

Do not build three disconnected applications. Build three progressively stronger capabilities inside the ETP Reporting Engine, with clear stage boundaries, shared evidence and honest acceptance results.
