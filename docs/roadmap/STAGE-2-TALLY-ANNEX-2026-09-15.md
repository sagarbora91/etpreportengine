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

# ETP Report Engine → TallyPrime 7.1
## Detailed Codex Prompt and Phased Implementation Plan

**Prepared:** 15 September 2026  
**Project:** Existing ETP Report Engine, `sagarbora91/etpreportengine`  
**Target:** TallyPrime Release 7.1 — not legacy Tally 7.2 or Tally.ERP 9  
**Architecture:** Existing Windows engine + native JSON/XML exchange + local TDL audit  
**Document type:** Implementation instructions and acceptance criteria, not a completed implementation or live-integration certificate.

> **Business objective:** Extend the existing Tally module so that approved ETP transactions can be mapped, validated, imported into the correct Tally company, and reconciled against the vouchers actually stored in Tally. The engine owns the workflow; the local TDL provides visibility and independent checks inside Tally.

---

## How to use this document

Place this file in the ETP Report Engine workspace, preferably under `docs/tally-integration/`, and give Codex the following instruction. Adapt the file path only to its actual location.

```text
Read docs/tally-integration/ETP_TallyPrime_7_1_Codex_Prompt_and_Plan.md in full.
Treat it as the implementation brief for extending the existing Windows
ETP Report Engine's Tally module.

First follow the repository's AGENTS.md instructions and inspect the actual
checkout. Produce an evidence-based existing-capability and gap analysis.
Then implement the missing or incomplete capabilities in the phases below.
Do not stop after producing a plan, and do not build a second application.

Preserve working functionality, existing accounting policies, SQL authority,
source lineage, permissions, immutable generations and locked-date controls.
Target TallyPrime 7.1 and verify integration details against official docs.

Work in the current development workspace. Do not post to production Tally,
run production migrations, install paid tools, or change live accounting data
without the required explicit owner approval. Use synthetic data or an
approved isolated test company for integration tests.

Do not claim a live Tally test, TDL load test, or Windows build passed unless
it actually ran. Continue useful offline implementation when a runtime is
unavailable, recording the exact blocked tests and reproducible next steps.
Start with Phase 0 and proceed to the first safe end-to-end vertical slice.
```

The remainder of this document is the detailed prompt and execution plan for Codex.

---

## Document map

- [Part A — Detailed implementation prompt](#part-a--detailed-implementation-prompt): requirements, architecture, safety controls, TDL and testing.
- [Part B — Phased implementation plan](#part-b--phased-implementation-plan): discovery through production-readiness gates.
- [Part C — Acceptance, required inputs and handoff](#part-c--acceptance-required-inputs-and-handoff): live checklist, missing-input handling and definition of done.
- [Reference notes](#reference-notes): official documentation and the repository context inspected for this brief.

---

# Part A — Detailed implementation prompt

## 1. Your role and delivery responsibility

You are extending an existing retail reporting application as an implementation engineer. Your responsibilities include repository discovery, design, incremental code changes, migrations, automated tests, a local TDL package, documentation, and an honest completion report.

Do not deliver only an architectural proposal or disconnected sample code. Integrate the work into the application's actual domain, persistence, services, screens, configuration, packaging and tests. Reuse working implementations wherever possible.

Build a system that answers these questions for every batch:

1. What source transactions were selected, and were any excluded?
2. What accounting entries did the approved mapping intend to create?
3. What exact payload was generated and submitted?
4. What did Tally report about the import?
5. What vouchers and allocations actually exist in the intended Tally company?
6. Do the source, submitted representation and actual vouchers reconcile?
7. What remains unresolved, who must act, and what evidence supports the result?

**Generating a file, receiving HTTP 200, obtaining an import count, and completing reconciliation are four different events. Never label them as interchangeable successes.**

## 2. Repository context and boundaries

### 2.1 What is known

The repository README identifies a SQL Server-backed Windows reporting application, the solution `Etp.Reporting.slnx`, a Windows release script, and separate legacy Android reference material. Its root `AGENTS.md` contains repository-navigation and graph-maintenance instructions. These were inspected while preparing this brief. [R1][R2]

A historical completion audit dated 26 August 2026 documents existing reporting, lineage, duplicate protection, finalisation, restatement and policy boundaries. Treat that document as historical evidence, not proof that today's checkout builds or that a live Tally connector already exists. [R3]

The owner has stated that a Tally module already exists. Locate and inspect it. **An empty code-search result is not evidence that the module is absent.** It may be labelled accounting, voucher export, integration, reconciliation or another related term.

### 2.2 Preserve the existing application

- Extend the Windows application. Do not implement this feature in the legacy Android shell or create a parallel importer application.
- Read root and applicable nested `AGENTS.md` files before changes. Where the repository's graph tooling is available, follow its navigation/update instructions. If a required tool is absent, record this and use scoped source inspection rather than pretending it ran.
- Discover the actual framework, project references, database schema and dependency versions. Do not upgrade the framework or replace the UI stack merely to implement this integration.
- Reuse the existing SQL Server/SQL Server Express configuration, Windows-integrated access model, migration runner, logging, audit trail, source profiles, master administration and application shell where present.
- Preserve existing report calculations, source-import atomicity, immutable report generations, approved restatement behaviour and locked business dates.
- Treat `UIUX` files, if present, as design references. Do not copy mocked prototype logic over functioning business services.
- Keep accounting deterministic. Do not use an LLM to decide ledger postings, tax treatments or financial differences at runtime.
- Do not embed credentials, Tally serial numbers, real customer data or private production company details in source control.

Do not confuse **an atomic ETP-to-SQL import** with **an atomic SQL-to-Tally operation**. They are different boundaries.

## 3. Mandatory audit before implementation

Create `docs/tally-integration/00_CURRENT_STATE_AND_GAPS.md`, or an equivalent path consistent with repository conventions, before substantive feature changes.

Record the checkout's branch, commit, dirty-file state, relevant instructions, discovered build/test commands and available runtimes. Do not overwrite unrelated local changes.

Inspect at least:

| Area | What to establish |
|---|---|
| Existing Tally/accounting module | Screens, services, voucher generators, mapping tables, exports, reconciliation and unfinished code. |
| ETP inputs | Existing profiles, invoice and line detail, tender facts, returns, source lineage and approved policy definitions. |
| Canonical storage | Which SQL facts are authoritative; how versions, corrections and finalisation are represented. |
| Tally integration | Existing XML/JSON formats, transport, company selection, master lookup, response parsing and TDL files. |
| Reliability | Duplicate prevention, source keys, outbox or job infrastructure, crash recovery and application concurrency. |
| Audit and security | Roles, approvals, evidence storage, log redaction, locks, migrations and recovery tools. |
| UI and packaging | Navigation, view models, shared controls, release packaging and deployment conventions. |
| Tests | Actual executable tests, fixtures, integration harnesses and tests that depend on Windows, SQL or Tally. |

For each requirement assign exactly one initial status:

`EXISTS_AND_VERIFIED`, `EXISTS_NEEDS_VERIFICATION`, `PARTIAL`, `MISSING`, `BLOCKED_BY_EXTERNAL_INPUT`, or `OUT_OF_SCOPE`.

Include source paths/symbols, observed behaviour, test evidence, proposed reuse or change, dependencies and risk. Do not mark a feature complete because its name appears in a document or menu.

Run the available baseline tests. Preserve pre-existing failures separately from failures introduced by this work. Then implement the missing pieces; do not remain indefinitely in discovery.

## 4. Target compatibility and protocol decision

### 4.1 Required target

Target **TallyPrime Release 7.1** explicitly. Record the precise installed build during live verification. Do not silently substitute TallyPrime 7.2, an earlier release, or legacy Tally 7.2.

Verify features against official Tally documentation and an isolated 7.1 installation. Documentation supports native JSON from TallyPrime 7.0 onward and also describes XML integration. This makes both relevant to the requested 7.1 target, but it does not prove that our particular payload or TDL is correct. [S1][S2]

### 4.2 Preferred implementation

**Preferred new implementation: native Tally JSON for the 7.1 adapter, with XML supported as an explicit alternative.**

However, if the repository already has a working, tested XML implementation, preserve and extend it for the first vertical slice. Do not throw it away or delay essential safety controls just to change syntax. Add JSON through the same domain contracts after the existing path is secured.

Record the choice in `01_COMPATIBILITY_AND_PROTOCOL_DECISION.md`, including:

- What already exists and the evidence for reusing it.
- Supported voucher types, custom metadata and read-back capabilities per format.
- Which format is enabled by default and why.
- What has been verified offline versus in live TallyPrime 7.1.
- Any unsupported combinations, with the affected UI options disabled.

**Native JSON is not arbitrary application JSON.** Keep the internal canonical schema, native Tally payload, HTTP request envelope and reconciliation export schema distinct. Use Tally's documented object structure and format settings; do not assume changing an XML filename to `.json` or wrapping a generic object creates an importable file. [S1]

### 4.3 File mode and connected mode

Support both workflows through the same validated posting plan:

| Mode | Intended operation | Required status behaviour |
|---|---|---|
| File export/import | Engine generates the supported native file; operator imports it through Tally and returns actual-data evidence. | File creation remains `EXPORTED_AWAITING_IMPORT`, not imported or reconciled. |
| Connected import | Engine submits the validated request to the configured local/private Tally endpoint and then reads actual vouchers back. | Response acknowledgement alone is insufficient; actual-data reconciliation is required. |

Choose format and delivery mode **before** dispatch. Never automatically retry XML after an uncertain JSON submission, or JSON after an uncertain XML submission. A timed-out request may already have created vouchers.

Do not require both formats to be enabled before the first verified slice. At final delivery, any format advertised as supported must pass its own tests; an interface or stub is not support.

## 5. Architecture and ownership

Use this logical flow without creating unnecessary projects or microservices:

```text
Existing ETP import and source lineage
                |
        Frozen canonical source selection
                |
        Versioned accounting mapping
                |
        Expected posting plan + validation
                |
        Immutable native JSON/XML artifact
                |
        File workflow OR controlled HTTP submission
                |
            TallyPrime 7.1
                |
      Actual vouchers / masters / allocations
                |
        Local TDL audit + actual-data extraction
                |
        Engine three-way reconciliation
                |
        Exceptions, approval history and evidence
```

The **ETP engine** owns normalization, mapping, validation, batch orchestration, retry decisions, expected values, reconciliation, evidence and final status.

**TallyPrime** stores the accounting vouchers and provides the actual-data side of reconciliation.

The **local TDL** identifies engine-originated vouchers, reads their actual values, displays audit results and exposes a documented read-only export/report contract. It must not become a second ETP parsing engine or silently repair accounting entries.

Prefer engine-initiated retrieval of a named TDL report/collection. A TDL callback server is not required for the first release.

Useful conceptual boundaries include source selection, posting-plan construction, payload serialization, delivery, actual-data reading, reconciliation and evidence storage. Reuse equivalent existing abstractions instead of introducing interfaces for their own sake.

## 6. Transaction scope and business-policy controls

Start with one ordinary, owner-approved ETP sales invoice and its correct Tally representation. Prove the full path before expanding voucher categories.

Use source store identifiers such as `WLMHW` and `HEMW` only through configuration. A store label is not automatically a legal entity, GST registration or Tally company.

| Category | Required treatment |
|---|---|
| Ordinary sales | First vertical slice, with approved ledger/tax/tender mapping. |
| Returns / credit notes | Add only after source signs, original-invoice references and accounting treatment are verified. |
| Split tender | Preserve all components and use one approved settlement policy without double counting. |
| Service income | Keep its source and approved accounting separate from merchandise sales. |
| Receipts and settlements | Add only when the source and owner-approved posting model require them. |
| Purchases, stock transfers, journals, opening balances | Discover existing support; do not invent or expand scope automatically. |
| Exchanges, cancellations, unusual tender codes and zero-value documents | Explicit policy required; otherwise quarantine and explain the block. |

The historical project audit identified unresolved policies including `TC` and some exchange/cancellation cases. Re-check the current policy store and code before assuming these remain unresolved, and never invent their meanings. [R3]

Decide and document whether a posting profile is accounting-only or inventory-integrated. Do not silently downgrade inventory requirements when the source lacks line detail. Missing required HSN, tax, item, unit, party or allocation data must block the affected transaction or a clearly unsupported profile.

Do not derive an unobserved tax split from a gross amount merely to make a voucher balance. Do not treat missing values as zero.

## 7. Canonical source and posting-plan contracts

Reuse existing entities and identifiers. Add only what is missing.

Each source document must retain enough information to reconstruct its origin and intended accounting:

| Group | Minimum information where applicable |
|---|---|
| Source identity | Source system, source document ID, document type, store/entity, financial year and stable source revision. |
| Provenance | Import/generation ID, original file hash, workbook/sheet/row references or equivalent existing lineage. |
| Dates | Source business date, invoice date, posting date and separately recorded processing timestamps. |
| Accounting context | Destination profile, legal entity, intended Tally company, currency and approved posting mode. |
| Parties and ledgers | Approved party reference, sales/service accounts, tax ledgers and settlement accounts. |
| Items | Stable line identity, item code, quantity, unit, rate, discount, taxable amount and allocations when required. |
| Taxes and charges | Source tax components, approved treatment, charges, discounts and explicit round-off. |
| Tender | Tender components and references, with approved treatment for clearing, bank, cash or receivables. |
| Control metadata | Mapping version, policy version, schema version, source hash and expected-posting hash. |

Use decimal/fixed-point monetary arithmetic with explicit scale and rounding policy. Quantities and rates may need a different scale from money. Preserve original precision and compare at the approved accounting precision.

Keep source facts immutable. Corrections or restatements must create traceable revisions through existing controls rather than overwrite the evidence used by an already-posted batch.

Separate source business identity from file location: row numbers are provenance, not the sole transaction identity. Reordering rows or receiving the same invoice in a different workbook must not create a new business event.

Use the application's established time handling. Where a new operational display timezone is required, use `Asia/Kolkata`; keep business dates distinct from timestamp conversions.

## 8. Destination-company and environment safety

Create or extend a destination profile containing environment, endpoint, expected company identity, company name, applicable registration/entity identifiers, posting-period rules, supported build, TDL version and enabled capabilities.

For connected operation, verify that Tally is reachable and that the intended company is available before writes. Tally's integration prerequisites include a running endpoint and a loaded company. [S3]

Always send explicit company selection rather than relying on whichever company is active on screen. Tally's JSON documentation specifically warns that omitted company selection can direct exchange to the active company. [S1]

Validate the strongest documented identity fields available. Do not rely on company name alone; do not claim a GUID alone differentiates restored or copied companies. Test companies may retain production identifiers after copying, so also bind approvals to the explicit environment/instance profile.

Before dispatch, revalidate company, environment, date range, mapping revision and artifact hash. Invalidate approval when any of them changes.

Use a conspicuous `TEST` or `PRODUCTION` label. Production posting remains disabled until the release gate is satisfied and an authorised operator explicitly enables the approved profile.

For manual import, record the intended target in the manifest and require company-confirmed read-back. Explain that the engine cannot physically prevent an operator from importing a file into another company outside its controlled workflow.

## 9. Mapping configuration and financial validation

### 9.1 Versioned mapping

Provide controlled mappings for document/voucher type, company/store, party, revenue ledger, tax ledger, tender/clearing account, stock item, unit, godown, cost centre and bill reference as required by the selected profile.

Each mapping should have scope, effective dates, version, enabled state, audit history and approval status. Detect overlapping or ambiguous rules. Unknown mappings block the affected records.

A fuzzy name match may be presented as a suggestion for review; it must not silently approve a financial master mapping.

### 9.2 Tender policy

Select one explicit accounting model per profile. Examples are direct tender allocations within the sale, or a receivable/clearing entry followed by separately linked settlement vouchers.

Whichever policy is selected, demonstrate that revenue and receipts are not posted twice. Where a source invoice creates multiple Tally vouchers, assign each target voucher a stable component role and reconcile the complete expected group.

### 9.3 Financial rules

Validate debit/credit balance, taxable amounts, tax components, discounts, charges, round-off, net amount, tender totals, quantities and allocation totals as applicable.

Treat tax rules as date-effective, owner/accountant-approved configuration. This project is not authority to change tax rates, infer legal classifications, file returns or generate statutory submissions.

Use independently specified expected accounting fixtures. Comparing three representations produced by the same incorrect mapping is not enough to prove the mapping is right.

## 10. Master readiness and controlled master creation

Before vouchers, read the required Tally masters through documented APIs or a controlled export. Capture company, retrieval time and available master identifiers/version information.

Check not only existence but relevant attributes: ledger group/nature, currency, tax setup, units, stock-item configuration, godowns, cost centres, voucher type and bill-wise settings.

Default behaviour is **validate existing masters, do not create or alter them silently**.

Where missing-master creation is approved:

1. Generate a separate master-change plan and preview.
2. Resolve dependency order and validate required attributes.
3. Require authorised approval bound to the exact changes.
4. Create only approved missing masters in the intended company.
5. Read them back and verify their properties before dependent vouchers.
6. Record partial master success separately from voucher outcomes.

Do not merge or alter an existing master solely because its name appears similar. Unexpected alteration or combination during a create-only operation is an exception requiring investigation.

Invalidate readiness when relevant masters, mappings, company context or source revisions change. A stale master snapshot must not justify an unattended write.

## 11. Pre-import validation and approval gate

Produce structured validation findings containing rule ID/version, severity, source document/line, observed value, expected value, explanation and corrective action.

Use `PASS`, `WARN` and `FAIL` consistently. A blocking failure prevents posting. Warnings requiring approval remain unapproved until a named authorised user accepts them with a reason.

At minimum, check:

- Source selection completeness, supported profile, required fields, source lineage and finalized/restated status.
- Date validity, period restrictions, company/entity mapping and environment identity.
- Approved mappings and compatible masters.
- Invoice arithmetic, tax splits, tender totals, accounting balance and applicable inventory allocations.
- Duplicate/conflicting source keys, overlapping batches and existing Tally matches.
- Payload integrity, required TDL metadata support, transport capability and destination readiness.

If a selected source set contains blocked records, default to blocking the batch. An authorised exclusion creates a visible, versioned selection with reasons; it must not silently drop rows or count excluded records as imported.

Provide a preview showing source invoices, proposed voucher count, target company, posting dates, totals, required masters, warnings and exclusions.

## 12. Batch identity, idempotency and lineage

### 12.1 Separate identities

Maintain separate concepts for:

- Business document key: the stable source event being accounted for.
- Target component key: the stable role of each expected voucher generated from that event.
- Source revision and expected-content hash.
- Batch ID: one approved source selection/posting plan.
- Artifact ID/hash: the exact generated file or request representation.
- Attempt ID: one dispatch or read-back attempt.
- Actual Tally identity: documented voucher identifiers observed in the intended company.

An illustrative business key is:

```text
ETP | legal-entity | store | source-financial-year |
source-document-type | immutable-source-document-id
```

This is a design example, not a claim that every field already exists in ETP exports. Verify the actual source uniqueness rules. If reliable identity cannot be established, block automated posting of the ambiguous records.

Do not put batch ID, mapping version, file name or payload hash into the stable business identity in a way that makes the same invoice look new after regeneration.

### 12.2 Required duplicate behaviour

| Situation | Required outcome |
|---|---|
| Same business key and same verified posted content | Do not create again; link to the existing verified outcome. |
| Same business key with changed source or mapping result | Conflict/revision workflow; no silent second voucher or alteration. |
| Same invoice appears in another file or batch | Detect by business identity, not merely file hash. |
| Earlier outcome is uncertain | Read back and resolve before any retry. |
| Multiple Tally vouchers match one expected component | Duplicate exception; no automatic selection or deletion. |
| A previously posted voucher is no longer found | Missing-after-posting exception; no automatic recreation. |

Use database uniqueness constraints and coordinated reservations/locks across application processes, not only an in-memory flag. Scope locks to the correct destination and business keys.

Where supported and tested, store stable origin/batch/component metadata in namespaced Tally UDFs. Use documented identifiers and verified field lengths. A narration tag is a human aid, not the sole identity mechanism.

Do not claim universal exactly-once posting: manual imports, other integrations, company copies and ambiguous external outcomes create boundaries. Document what the implementation guarantees and where it must stop for investigation.

### 12.3 Integrity, not exaggerated security claims

Hash source snapshots, approved posting plans and exact artifact bytes. Protect evidence through permissions and append-only application behaviour. Hashes detect differences relative to a trusted record; they are not, by themselves, proof against a privileged administrator rewriting all records.

Do not describe the implementation as tamper-proof. Use stronger signatures only where an existing, approved key-management mechanism supports them.

## 13. Artifact package and controlled submission

Create an immutable evidence package for each approved batch. Reuse existing archive conventions where possible.

```text
<configured-evidence-root>/Tally/<environment>/<company-key>/<year>/<batch-id>/
  manifest.json
  source-snapshot.json
  expected-postings.json
  validation-results.json
  payload/
    masters.<enabled-format>       # Only when separately approved
    vouchers.<enabled-format>
  attempts/
    <attempt-id>/request-metadata.json
    <attempt-id>/request-body.bin
    <attempt-id>/response-metadata.json
    <attempt-id>/response-body.bin
  actuals/
    <readback-run-id>/raw-export.<format>
    <readback-run-id>/actual-vouchers.json
  reconciliation/
    <run-id>/summary.json
    <run-id>/differences.csv
  approvals-and-events.json
```

The tree is illustrative. Do not create empty files that imply an operation occurred. File-mode batches may have no HTTP artifacts.

The manifest should identify the company/environment, source selection, exclusions, expected document/component counts, control totals, mapping/policy/schema versions, enabled format and hashes. Define a non-circular hashing scheme and distinguish content hashes from timestamps or attempt metadata.

Serialize with real JSON/XML libraries, not string concatenation. Preserve Unicode, leading-zero references and documented date/decimal/sign conventions. Respect required encoding and BOM behaviour, and test response decoding independently from request encoding. [S1][S3]

Re-parse the final on-disk file or exact outgoing body independently and compare it to the expected posting plan. Verify the stored artifact hash immediately before submission. Any byte change requires a new validation/approval path.

Keep artifacts in permission-controlled storage, not executable/code locations. Do not depend on cloud synchronization for transactional correctness. Never place live SQL or Tally company data under a casual file-sync workflow as an integration shortcut.

## 14. Import orchestration and uncertain outcomes

### 14.1 Durable operation

Before sending a write, durably record the approved plan, reserved business keys, immutable artifact and attempt state in the existing database. Do not hold a SQL transaction open for the duration of a network request.

Use the existing job/outbox infrastructure where available. Recover jobs after process termination by inspecting the recorded state and actual Tally data.

Do not let a generic HTTP retry handler automatically replay accounting writes. A timeout, disconnect, malformed response or application crash after dispatch may leave an unknown outcome.

Tally imports must be treated as potentially partially applied unless a specific tested operation documents stronger guarantees. A local SQL rollback cannot undo vouchers already accepted by Tally.

### 14.2 Status model

Use separate posting and reconciliation states, or an equally explicit existing model. Recommended meanings are:

| Status | Meaning |
|---|---|
| `DRAFT` | Selection or mapping is still editable. |
| `BLOCKED` | Validation, policy, capability or approval prevents dispatch. |
| `APPROVED_READY` | Exact source/plan/artifact/destination combination is authorised. |
| `EXPORTED_AWAITING_IMPORT` | File created; import is not yet established. |
| `SUBMITTED_AWAITING_RESULT` | Write may be in progress; do not send it again. |
| `OUTCOME_UNKNOWN` | Evidence is insufficient to determine which writes occurred. |
| `PARTIALLY_APPLIED` | Some expected changes exist; others are rejected, absent or unresolved. |
| `IMPORT_REPORTED_AWAITING_RECONCILIATION` | Tally reported an outcome, but actual-data checks are unfinished. |
| `RECONCILIATION_INCOMPLETE` | Required actual data, manifest, TDL verification or coverage is unavailable. |
| `FAILED_RECONCILIATION` | Actual data is available and does not meet the approved plan. |
| `RECONCILED` | All mandatory checks pass with complete evidence. |
| `RECONCILED_WITH_ACCEPTED_WARNINGS` | No blocking mismatch; approved warning exceptions remain explicitly visible. |
| `RE_AUDIT_REQUIRED` | Later changes invalidate reliance on the previous current-state result. |

Persist transition history, actor, reason and evidence. Derive batch status from document/component outcomes; do not overwrite a partial failure with a successful HTTP request status.

### 14.3 Retry rules

Read-only requests may use bounded retries. A write retry is allowed only for specifically identified components with an established safe outcome and the necessary approval.

After an uncertain attempt, perform company-bound, complete read-back using stable origin keys and actual identifiers. Confirm no delayed/in-flight operation remains before declaring absence safe to retry. Ambiguity remains `OUTCOME_UNKNOWN` rather than becoming an automatic retry.

After partial application, create a recovery plan for unresolved components. Do not resend the entire original batch.

Cancellation stops future dispatches. It must not claim that already-submitted vouchers were undone.

## 15. Three-way reconciliation

### 15.1 The three independent evidence sets

**A — Source and expected accounting:** frozen ETP canonical facts, original-source lineage and the owner-approved expected posting plan.

**B — Generated/submitted representation:** the independently re-parsed native file and/or exact submitted request body.

**C — Actual Tally state:** vouchers and relevant allocations freshly fetched from the identified Tally company or obtained through a controlled actual-data export.

Compare A↔B, B↔C and A↔C. Retain the distinction between raw source facts and their accounting transformation so that a mapping error is not hidden by a payload-to-payload comparison.

Add a preceding **source-coverage check**: selected source documents, approved exclusions and expected target components must account for the entire selected set. Three-way checks cannot detect source rows that were silently discarded before A was built.

### 15.2 Required checks

| Level | Comparisons |
|---|---|
| Coverage | Every expected component is located exactly once; no unexpected engine-tagged components in the defined scope. |
| Header | Company/entity, document identity, dates, voucher type, references and relevant status flags. |
| Accounting | Ledger identities, debit/credit direction, amounts, party balance and bill-wise allocations. |
| Tax | Taxable bases, component amounts, mapped tax ledgers and supported tax attributes. |
| Tender | Cash/bank/clearing/receivable allocations and any separately expected settlement vouchers. |
| Inventory | Item, quantity, unit, rate, discounts, valuation and required godown/batch allocations. |
| Aggregate | Document/component counts, gross/net/tax/discount/round-off totals, ledgers, tenders and quantities. |
| Integrity | Batch/component metadata, source revision, payload hash and relevant actual-voucher change indicators. |

Compare repeated line items using stable source-line correspondence where available, otherwise an explicitly documented multiset/grouping approach. Do not match only by display row position, amount, date or invoice number. Two equal-value rows are still two rows.

Keep identifier mismatches exact. Use explicit, versioned numeric tolerances; begin with exact expected monetary values at the approved precision, allowing only documented rounding rules. Never use a broad percentage tolerance to conceal a difference.

Distinguish `MISSING`, `EXTRA`, `DUPLICATE`, `WRONG_COMPANY`, `AMOUNT_MISMATCH`, `TAX_MISMATCH`, `LEDGER_MISMATCH`, `QUANTITY_MISMATCH`, `STATUS_MISMATCH`, `SOURCE_CHANGED`, `ACTUAL_CHANGED`, `AMBIGUOUS_MATCH` and `NOT_VERIFIABLE`.

Each difference must retain the values from A, B and C, numeric delta where meaningful, rule/version, matching rationale, evidence location and action required.

### 15.3 Actual-data completeness and freshness

Fetch all required fields and all pages/partitions in the requested scope. Detect truncation, filters, permissions and unsupported fields. Missing actual fields are not equal to expected zero.

Record company identity, acquisition time, request scope, adapter/TDL version and relevant documented voucher identifiers. Candidate fields such as voucher GUID, master ID or alteration ID must be validated against the actual Tally 7.1 behaviour before use.

If the extraction cannot be a consistent snapshot, use a controlled quiet window or recheck change indicators and restart when necessary. Do not certify a batch from a mixture of pre-edit and post-edit values.

Import counters and last-imported IDs may assist diagnostics but cannot substitute for complete voucher-level evidence. Tally exposes import counts and per-object event/error information; use them as supporting observations. [S4]

### 15.4 Later re-audit

Preserve the original reconciliation run and its timestamp. Allow a later run to detect edited, cancelled, deleted, duplicated or retagged vouchers.

When a later state differs, mark the batch as requiring review without rewriting its historical result. Where data access cannot distinguish deletion from filtering or lost permissions, report the uncertainty rather than asserting deletion.

## 16. Local TDL audit package

### 16.1 Scope and deployment

Create or extend a namespaced local TDL project within the repository. Supply readable `.tdl` source, a version manifest, installation instructions, compatibility notes, a collision register for UDF identifiers and test steps.

Local TDL and centrally deployed Account TDL are documented deployment mechanisms. This release requires **local TDL**. Account TDL distribution is a later deployment option, not a reason to build cloud infrastructure now. [S5]

A compiled `.tcp` is optional and must be produced only through the appropriate available Tally tooling. Do not rename a text file to `.tcp` or claim compilation without evidence. Do not introduce a paid tool dependency without approval.

### 16.2 Metadata contract

Prefer a small, versioned origin contract with fields conceptually equivalent to source system, source document key, component key, batch ID, source revision/hash and integration schema version.

These are proposed custom fields, **not built-in Tally field names**. Choose actual UDF definitions after inspecting existing customizations and verify persistence and export for every supported import format.

The TDL must identify its installed contract/version through a health report. The engine must detect missing or incompatible TDL before treating its audit as available.

### 16.3 Audit reports inside Tally

Provide a menu/report entry such as **ETP Import Audit**, reusing established menu conventions.

At minimum include:

- Batch summary: environment/company, batch, dates, expected and observed counts/totals, coverage and last check time.
- Voucher detail: source identity, actual Tally identity, voucher status, ledgers, taxes, inventory and tender details.
- Exceptions: duplicates, missing expected components, unexpected tagged components and field-level discrepancies.
- Drill-down to the actual voucher, respecting the logged-in user's Tally permissions.
- A documented machine-readable export/report for the engine.

Compute observed amounts from actual voucher/ledger/inventory data, not from an imported `ExpectedAmount` field. Imported origin metadata establishes correspondence; it does not prove financial correctness.

### 16.4 Expected-manifest requirement

A TDL that only lists imported vouchers cannot know which source vouchers are missing. For in-Tally completeness checks, provide a read-only expected manifest from the approved engine batch and compare it with the actual collection.

The manifest must carry company, batch, component identities, required expected values, schema version and integrity references. Validate it in the engine and implement only integrity checks actually supported by the TDL runtime; do not invent cryptographic functions.

Where the engine and Tally run on different computers, explicitly configure where the manifest is available to the Tally process. Do not assume an engine-local file path exists on the Tally machine.

If the expected manifest is unavailable, display **observed vouchers only — completeness not verified**. Do not show an all-clear result.

Keep expected manifests out of the accounting vouchers themselves except for the minimal approved origin metadata. Reading an audit manifest must not create, alter or cancel vouchers.

### 16.5 Read-only return path and coexistence

Use a named report/collection that the engine can request, with controlled file export as the alternative. Include contract version, company identity, scope, observation time, coverage information and actual values.

No arbitrary remote code loading, external callback URL or embedded credential is required. Avoid global event modifications affecting unrelated imports. If import events are used, scope them to this integration and test coexistence with other TDLs.

The audit must not automatically balance entries, delete duplicates, change dates or fix tax allocations. Corrections belong to a separately approved workflow.

Do not mark required TDL checks passed when only an ordinary Tally export was available. Ordinary read-back may support useful engine-side checks, but missing mandatory TDL evidence remains explicit.

## 17. UI integration within the existing Tally module

Extend existing views and view models instead of creating a second application shell.

The operator workflow should support:

```text
Select source → Confirm company → Review mappings/masters → Validate
→ Preview/approve → Export or import → Read back → Reconcile → Resolve exceptions
```

Provide connection/environment status, mapping/master readiness, batch preview, import progress, reconciliation summary, document-level differences, evidence access and retry/recovery actions governed by permissions.

Always show the intended company, environment, selected dates, source count, expected voucher count, actual located count and unresolved count. Keep missing, zero and not-tested visually distinct.

Disable posting for blocking conditions, stale approval, changed artifacts, incompatible TDL or unresolved earlier outcomes. Do not bury the reason in a log file.

Use actionable messages such as “Two invoices are blocked because their tender code is unmapped” rather than “Import failed.” Preserve detailed technical diagnostics behind the plain-language explanation.

## 18. Security, backups and correction workflow

### 18.1 Permissions and endpoint safety

Reuse existing application roles. Separate preparation, approval/posting, configuration and recovery rights where the existing model permits.

Default to loopback or an explicitly approved private endpoint. Do not expose Tally's integration port to the public internet or invent an OAuth/TLS/authentication capability that has not been verified. Document necessary network and host restrictions.

Reject untrusted redirects and unsafe destination changes. Sanitize paths, restrict evidence export roots, use parameterized SQL, and disable unsafe XML external-entity/DTD processing in application parsers. Apply input-size and extraction limits without silently truncating accounting data.

Do not bypass Tally permissions or assume every collection export enforces them identically. Test with the intended restricted operating role and document any limitation.

### 18.2 Before production posting

Require current recoverable Tally company backup evidence and the corresponding engine database/evidence backup according to the owner's recovery policy. A file path alone is not proof of recoverability.

Use documented backup procedures; do not assume copying open company files is a safe backup. Where backup cannot be triggered or validated through an available API, provide a truthful operator-controlled checklist and capture the approval evidence.

Demonstrate restoration in an isolated environment before release. Never restore production automatically as a response to an import error.

### 18.3 Corrections are not blanket rollback

A backup restore may remove legitimate work performed after the backup. Do not describe it as harmless per-batch rollback.

Default recovery is: stop writes, establish actual state, isolate affected vouchers, prepare an accountant/owner-approved correction or reversal plan, apply only approved changes, and reconcile again.

Do not silently delete or alter production vouchers. Even integration-created vouchers may have been edited or referenced later. Preserve the complete before/after evidence and link corrections to the original source and batch.

## 19. Testing requirements

### 19.1 Baseline and test layers

Use the repository's actual test framework. Add unit, serialization/contract, SQL integration, transport simulation, Windows UI and live Tally tests as appropriate.

Label evidence accurately:

`UNIT_VERIFIED`, `SQL_VERIFIED`, `SIMULATED_TRANSPORT_VERIFIED`, `WINDOWS_VERIFIED`, `LIVE_TALLY_7_1_VERIFIED`, `TDL_LOAD_VERIFIED`, or `NOT_RUN` with a reason.

Mocks do not establish that Tally accepted a voucher. Loading a TDL does not establish that its report reads the correct data. An official example is a starting fixture, not a successful local test.

### 19.2 Golden dataset: 20 core scenarios

Create synthetic, reproducible fixtures with independently reviewed expected accounting values. Do not publish real customer identities. Transaction scenarios outside an approved posting profile must demonstrate an explicit block rather than pretend support.

| ID | Scenario | Required result |
|---|---|---|
| G01 | Ordinary approved sale | Correct target voucher and complete three-way reconciliation. |
| G02 | Multiple invoice lines, including a repeated item | Correct multiplicity, amounts and line/aggregate checks. |
| G03 | Split cash and digital tender | Components reconcile without duplicate settlement/revenue. |
| G04 | Discount | Source, payload and actual discount treatment agree. |
| G05 | Permitted round-off | Explicit approved rounding only; no hidden balancing entry. |
| G06 | Approved different tax-component pattern | Correct configured ledgers/components; unsupported policy blocks. |
| G07 | Approved sales return | Correct sign, references and voucher type; otherwise explicit block. |
| G08 | Missing required master | No dependent voucher posted before approved master resolution. |
| G09 | Unknown tender or mapping | Actionable blocking validation; no guessed ledger. |
| G10 | Same source in a new file or batch | No duplicate posting despite different file/batch identity. |
| G11 | Same source key with changed content | Revision/conflict workflow, not an extra voucher. |
| G12 | Wrong company or cloned test/prod identity | Destination safety prevents dispatch or flags unverifiable manual import. |
| G13 | Invalid or locked posting date | Block without changing existing lock rules. |
| G14 | One rejected voucher among successful vouchers | Partial outcome preserved; only unresolved components recoverable. |
| G15 | Timeout after Tally accepts a write | Read-back resolves it without blind reposting. |
| G16 | Manual edit after import | Later re-audit detects actual-state drift. |
| G17 | Missing, cancelled or duplicated actual voucher | Distinct exception; no automatic repair. |
| G18 | Unicode and special characters | Correct serialization, persistence and read-back. |
| G19 | Missing TDL, incompatible TDL or absent manifest | Incomplete audit clearly shown; no false reconciliation. |
| G20 | File-mode import and controlled actual-data export | File generation alone is not success; same reconciliation rules apply. |

For numeric fixture examples, label rates and amounts as test inputs, not recommended tax treatment.

### 19.3 Additional failure and regression tests

Test application termination before send, during send and after response but before local persistence; simultaneous overlapping submissions from two processes; read-back truncation; stale snapshots; changed approved payload; lost permissions; duplicate UDF values; company switch; source restatement after approval; mapping changes after approval; unexpected master alteration; malformed/HTML error responses; and blocked cross-format retry.

Test at least one zero-versus-missing mismatch and one case where aggregate totals match but individual vouchers differ. Both must be detected correctly.

Add serializer parity tests for every advertised format and end-to-end tests for both advertised delivery modes. Check UDF round-trip persistence and report coexistence with other local customizations.

Run the existing ETP/reporting tests to establish no regression. Record performance on a deterministic larger dataset sized to the target workstation; publish measured timings, memory and query behaviour rather than invented performance claims.

## 20. Documentation and deliverables

Adapt names to existing conventions, but provide equivalent maintained artifacts:

| Deliverable | Required content |
|---|---|
| Current-state/gap analysis | Actual checkout, evidence, reused code and missing capabilities. |
| Compatibility/protocol decision | Tally 7.1 build, format/delivery matrix and verified limitations. |
| Architecture and contracts | Domain boundaries, schemas, identifiers and state transitions. |
| Mapping/policy register | Approved rules, unresolved policies and transaction support matrix. |
| Engine implementation | Integrated services, persistence, UI, validation and reconciliation. |
| Database changes | Additive, tested migrations using the existing runner and permissions. |
| Local TDL package | Source, version/UDF manifest, installation and verification instructions. |
| Test assets | Synthetic fixtures, independently expected results and automated tests. |
| Operations runbook | Setup, preflight, file/connected import, read-back and exception handling. |
| Recovery runbook | Uncertain outcomes, partial import, approved corrections and restore drill. |
| Completion matrix | Implemented, verified, blocked, deferred and not-run items with evidence. |

Store a concise progress/resume note in the repository, listing changed files, completed tests, unresolved decisions and the exact next task. Do not scatter unrelated TODO files or leave a non-building half-refactor as the handoff.

---

# Part B — Phased implementation plan

## 21. Execution strategy and phase gates

Implement in reviewable increments. The phases below are delivery gates, not permission to ignore safety until the end. Every live-test write needs the appropriate isolated environment, stable identity, target verification and uncertainty handling from the first slice.

If a required external runtime or business policy is unavailable, continue independent offline work and explicitly keep the affected release gate closed. Do not fabricate a policy, a successful test, or access to a Windows/Tally installation.

### Phase 0 — Discover, baseline and map gaps

**Work:** Read repository instructions; identify the Windows solution and existing Tally module; inspect source profiles, mapping, SQL, UI and tests; run available baseline checks; produce the evidence-based gap matrix.

**Deliver:** `00_CURRENT_STATE_AND_GAPS.md`, baseline test record and a scoped change plan with concrete paths/symbols.

**Exit gate:** Existing functionality and actual gaps are distinguishable. The plan extends the current application rather than creating parallel infrastructure.

### Phase 1 — Lock the contract and verify capabilities

**Work:** Record target TallyPrime 7.1 build expectations; inspect any existing XML/JSON code; choose the first adapter; define stable identities, source-to-voucher cardinality, company binding, approved first-invoice mapping, metadata and reconciliation fields. Build read-only connection/master/TDL capability probes where possible.

**Deliver:** Compatibility matrix, protocol decision, draft contracts and a policy/input register.

**Exit gate:** The first test invoice has an explicit expected accounting result. Unsupported business rules are blocked. The system does not depend on a guessed API or custom field.

### Phase 2 — Prove one complete vertical slice

**Work:** Reuse the existing normalized source path; freeze one synthetic or approved anonymized sales invoice; validate masters and mapping; generate and independently parse its payload; import into an isolated test company; retrieve actual data; run a minimal local TDL audit and three-way reconciliation.

Build the minimum durable batch/attempt record and source identity controls needed to make this slice safe. Exercise a duplicate attempt and an uncertain-response scenario through simulation before expanding live writes.

**Deliver:** Integrated working slice, minimal TDL source, fixture/expected result and actual evidence for every test that ran.

**Exit gate:** One real TallyPrime 7.1 round trip and TDL observation are proven, or the live gate is explicitly `NOT_RUN/BLOCKED` with a runnable harness. Offline completion must not be described as a completed live slice.

### Phase 3 — Complete durable batches and import controls

**Work:** Extend existing SQL/job infrastructure for business-key uniqueness, overlap reservations, approval binding, immutable artifacts, attempt history, crash recovery, partial outcomes and safe retry planning. Integrate environment/company safeguards and production-disabled defaults.

**Deliver:** Migrations, orchestration services, recovery logic and failure-injection tests.

**Exit gate:** The system survives replay, concurrent submission and interruption without blind duplicate posting or false success states.

### Phase 4 — Expand approved mappings and master readiness

**Work:** Implement mapping administration, attribute-level master validation, approved missing-master creation and additional supported sales/tender/return cases. Reuse current policies and source profiles; quarantine unsupported cases.

**Deliver:** Mapping/master UI, documented support matrix and expanded golden fixtures.

**Exit gate:** Every enabled transaction type has explicit policy, required source data, expected accounting and passing tests. Existing unapproved tender meanings remain unresolved rather than guessed.

### Phase 5 — Complete the local TDL audit

**Work:** Finalize namespaced metadata, collision checks, TDL health/version contract, expected-manifest ingestion, batch/voucher/exception reports, drill-down and engine-readable actual-data export. Test actual values rather than copied expected values.

**Deliver:** Installable `.tdl` package, source/version manifest, operator guide and live load/report evidence where available.

**Exit gate:** Missing expected vouchers, duplicated metadata and financial mismatches are visible. Absent manifests or incompatible TDLs produce incomplete status, not an all-clear.

### Phase 6 — Complete reconciliation and operator workflow

**Work:** Build all required A↔B↔C comparisons, source coverage, component matching, line/aggregate checks, freshness controls, re-audit and evidence-linked exception screens. Integrate them into the existing Tally module.

**Deliver:** Reconciliation engine, difference reports, UI and audit history.

**Exit gate:** A user can trace a difference from batch summary to actual voucher and original source evidence. Equal batch totals cannot hide missing or incorrect individual vouchers.

### Phase 7 — Finish supported format and delivery alternatives

**Work:** Complete native JSON and XML adapters to the extent advertised, preserve existing working support, verify format-specific metadata/encoding and implement controlled file mode plus connected mode through the same posting/reconciliation contracts.

Do not perform a second accounting posting merely to compare formats. Use separate isolated fixtures/companies or verified read-only representation comparisons.

**Deliver:** Explicit format × delivery × voucher-type capability matrix and parity/round-trip tests.

**Exit gate:** Unsupported combinations are disabled; supported combinations have evidence. No automatic cross-format write fallback exists after an uncertain dispatch.

### Phase 8 — Hardening, regression and recovery rehearsal

**Work:** Run fault injection, restricted-role tests, parser/path safety checks, coexistence checks, performance measurements, source/report regressions and a controlled recovery/restore drill.

**Deliver:** Test report, measured limitations, operational failure guide and unresolved-risk register.

**Exit gate:** No unexplained accounting/data-integrity regression. Remaining limitations are explicit and correctly block affected production use.

### Phase 9 — Windows/Tally UAT and production gate

**Work:** Execute the checklist in Section 23 on the intended Windows/Tally environment. Obtain mapping/accounting acceptance and verify backup/recovery evidence. Package through the existing application release process.

**Deliver:** UAT record, exact build/TDL versions, operator instructions and production-readiness matrix.

**Exit gate:** The owner authorises production use for the tested company/profile and transaction scope. Do not enable unsupported scope through a general “integration complete” flag.

### Future option — Account TDL distribution

After the local version is stable, document how the same compatible TDL could be centrally distributed through Account TDL, subject to actual account access and deployment permissions. Keep code and distribution concerns separate. This is not a Phase 0–9 dependency and does not authorize publishing or buying anything. [S5]

## 22. Work-package reporting and session continuation

At the end of each meaningful implementation increment, report:

```text
Phase / work package:
Checkout / relevant commit:
Existing capability reused:
Files changed:
Behaviour added or corrected:
Tests actually run and their results:
Live Windows/Tally/TDL evidence, if any:
Unresolved policy or runtime dependency:
Known limitations / production gates still closed:
Next concrete task:
```

Keep the gap matrix current. Distinguish implemented code from verified behaviour and released functionality.

If the session ends, leave a buildable, reviewable increment and a resume note. Do not state that work will continue in the background. A later session must be able to inspect the repository and resume without reconstructing undocumented decisions.

---

# Part C — Acceptance, required inputs and handoff

## 23. Live Windows and TallyPrime 7.1 verification checklist

This checklist must be executed on an appropriate test machine; it is not satisfied by checking boxes in documentation.

- [ ] Record Windows environment, application build/commit, SQL environment, TallyPrime 7.1 build and TDL version.
- [ ] Use an isolated, clearly named test company and explicitly bound environment profile.
- [ ] Verify the endpoint, intended company, read permissions and required master attributes.
- [ ] Load the local TDL and confirm its health/version report and namespaced metadata contract.
- [ ] Approve the first fixture's ledger, tax, tender and inventory treatment.
- [ ] Generate and independently parse the exact payload; verify its manifest/hash.
- [ ] Import one invoice; retrieve its actual voucher and required allocations; reconcile completely.
- [ ] Repeat the same source through another batch/file and demonstrate duplicate prevention.
- [ ] Verify split tender and every other transaction type advertised as supported.
- [ ] Demonstrate the expected-manifest completeness check inside Tally.
- [ ] Demonstrate a genuine mismatch and its source-to-voucher drill-down.
- [ ] Demonstrate partial/uncertain outcome handling without blind full-batch retry.
- [ ] Test both supported delivery modes and every advertised format.
- [ ] Test restricted operator permissions and coexistence with the intended other TDLs.
- [ ] Edit/cancel an eligible test voucher and demonstrate later re-audit without rewriting historical evidence.
- [ ] Run the isolated backup/restore or approved recovery rehearsal and retain evidence.
- [ ] Re-run the existing Windows/reporting regression suite and record results.
- [ ] Obtain owner/accountant acceptance for the tested posting profiles.
- [ ] Keep production disabled until the required approval and recovery gates are satisfied.

## 24. Inputs to discover first, then request only if unresolved

Do not ask the owner for information already available in the repository, current configuration, supplied source files or a permitted read-only probe.

| Input | Preferred discovery route | Behaviour if unresolved |
|---|---|---|
| Actual existing Tally module | Repository search, navigation graph and source inspection. | Document search evidence; do not assume absence from one query. |
| Installed TallyPrime build and endpoint | Approved local environment/read-only probe. | Implement offline contracts; mark live tests not run. |
| Company/entity/store mapping | Existing configuration and accountant-approved records. | Block production target selection/posting. |
| Representative source invoice and line/tender data | Existing source fixtures and canonical lineage. | Use labelled synthetic data; do not claim ETP production coverage. |
| Correct Tally voucher example | An approved manually prepared test voucher exported from 7.1. | Keep uncertain mapping/schema cases blocked. |
| Ledger, tax, tender and inventory policies | Existing policy register and approved masters. | Require only the missing material decision; no guessed treatment. |
| Meaning of unusual tender/document codes | Current project policy and owner/accountant confirmation. | Quarantine affected records. |
| Required UDF identifiers / other TDLs | Existing customization inventory. | Avoid guessed identifiers; block conflicting metadata deployment. |
| Production permissions and backup practice | Existing roles/runbooks and owner-controlled setup. | Do not enable production writes. |

Tax and statutory interpretation should be confirmed by the responsible accountant or owner. Codex must implement the approved rule, not decide the business's legal position.

## 25. Final definition of done

### Engineering complete

- [ ] Current-state/gap analysis identifies what was reused versus added, with actual paths and tests.
- [ ] Changes are integrated into the existing Windows Tally module, with no duplicate application.
- [ ] Required migrations, configuration, UI, diagnostics and packaging are complete and tested where available.
- [ ] Source coverage, mapping, validation, master readiness and immutable artifacts are implemented.
- [ ] Business-key idempotency, overlap controls, durable attempts and uncertain/partial outcome handling are implemented.
- [ ] Three-way reconciliation checks actual Tally data, not merely request/response counts.
- [ ] Local TDL source, manifest-dependent completeness checks and the return contract are implemented.
- [ ] All advertised format/delivery combinations have appropriate tests; unsupported combinations are disabled.
- [ ] Golden fixtures, failure tests and existing regression results are recorded honestly.
- [ ] Documentation and resume/completion notes reflect the actual code and remaining limitations.

### Live integration verified

- [ ] The end-to-end workflow has actually run in TallyPrime 7.1.
- [ ] The local TDL actually loaded and its actual-data and completeness checks were exercised.
- [ ] Duplicate, mismatch, partial/uncertain outcome and later-edit cases were demonstrated.
- [ ] Test evidence identifies the application/TDL build and intended company/environment.

### Production authorised

- [ ] The owner/accountant has approved the specific mappings and supported transaction scope.
- [ ] Backup/recovery evidence and operator permissions are satisfactory.
- [ ] The production company/environment has been deliberately configured and authorised.
- [ ] Unresolved business rules and unsupported cases remain blocked.

**Engineering completion does not imply live verification. Live verification does not imply permission to change production books.**

## 26. Required final response from Codex

Finish with a concise, evidence-based implementation report containing:

1. Existing functionality found and reused.
2. Missing/incomplete capabilities implemented, with important file paths.
3. Protocol and architecture decisions, including preserved existing behaviour.
4. Test results separated into unit, SQL, simulated transport, Windows, live Tally and TDL evidence.
5. Instructions to load the local TDL and execute the first test batch.
6. Known limitations, missing inputs and any production gate still closed.
7. The next concrete owner action, only where one is genuinely required.

Never write “fully working,” “fully reconciled,” “Tally-compatible,” “rollback supported,” or “production ready” without stating the scope and evidence supporting that claim.

---

# Reference notes

The engineering controls in this brief are requirements and design recommendations. They are not claims that Tally provides those controls automatically. Official documentation was checked on 15 September 2026; Codex must re-check relevant details against the target installation and record the evidence it actually uses.

## Official product and developer references

**[S1] TallyHelp — Integration using JSON.** Native JSON support, native versus custom JSON, HTTP exchange, required company/format variables and encoding details. Use the target-release examples rather than old custom-JSON assumptions.  
`https://help.tallysolutions.com/tally-prime-integration-using-json-1/`

**[S2] TallyHelp — Integration using XML.** Built-in XML exchange and request/response structure. Verify the exact native voucher representation against an approved exported test voucher.  
`https://help.tallysolutions.com/xml-integration/`

**[S3] TallyHelp — Pre-requisites for Integrations.** Running endpoint, loaded company and HTTP/XML integration setup. Confirm the actual 7.1 configuration screens rather than copying an older menu path blindly.  
`https://help.tallysolutions.com/pre-requisites-for-integrations/`

**[S4] TallyHelp — How to use TDL Functions for Integrations?** Import observations including `$$ImportInfo`, `$$LastImportError` and `$$ImportAction`, and per-object import-event examples. These support diagnostics, not independent financial certification.  
`https://help.tallysolutions.com/how-to-use-tdl-functions-for-integrations/`

**[S5] TallyHelp — Configure/Deploy TDLs and Add-Ons in TallyPrime.** Distinguishes local TDL, Account TDL and other deployment modes, including local loading and central configuration.  
`https://help.tallysolutions.com/deploy-tdls-and-add-ons-tally/`

**[S6] TallyHelp — Integration Methods and Technologies.** Broader native/file/HTTP integration capability reference.  
`https://help.tallysolutions.com/integration-methods-and-technologies/`

**[S7] OpenAI — Custom instructions with AGENTS.md.** Repository and scoped instruction handling. Preserve the actual project's applicable instructions.  
`https://developers.openai.com/codex/guides/agents-md`

## Repository references inspected for this brief

These references establish context, not a full current-code audit. No repository code was changed and no Windows/Tally integration was executed while preparing this document.

**[R1] Repository README.** Identifies the Windows solution and distinguishes it from legacy Android material. Retrieved from `main`; file blob SHA `431613d78643ea3706e5f75da683382e9cb8c0d2`.  
`https://github.com/sagarbora91/etpreportengine/blob/main/README.md`

**[R2] Root AGENTS.md.** Contains repository graph-navigation/update guidance. Retrieved from `main`; file blob SHA `6511cd1dd5bda01b13d607ffb3ab76b865b77fc7`.  
`https://github.com/sagarbora91/etpreportengine/blob/main/AGENTS.md`

**[R3] Historical Master Prompt Completion Audit.** Dated 26 August 2026; documents earlier implementation claims and explicitly deferred owner policies. Retrieved from `main`; file blob SHA `755dd13ba2d4941407da5419acb7541b37dbe953`. Re-run relevant tests rather than inheriting its pass claims.  
`https://github.com/sagarbora91/etpreportengine/blob/main/docs/23_MASTER_PROMPT_COMPLETION_AUDIT.md`

---

**Execution instruction:** Start with Phase 0, inspect the actual checkout, and proceed through the first safe vertical slice. Preserve working systems, make missing information visible, and treat financial reconciliation as an evidence-based result rather than a success label.
