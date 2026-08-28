# ETP Reporting Engine — Pending Input and Deferment Register

Status date: 28 August 2026
Companion authority: `docs/PROJECT_CLOSURE_TRACEABILITY.md`

## Purpose

This register isolates the work that cannot be truthfully closed through autonomous engineering alone. It is not a parking place for difficult engineering. A row may appear here only when an exact Owner decision, populated source export, external environment or explicit deferment is genuinely required.

All autonomous preparation, validation, safe defaults and fail-closed behavior must be completed before a row is treated as blocked.

## Decision rules

- Do not guess a business definition.
- Do not fabricate source data or convert missing values to zero.
- Do not use an external-validation blocker to defer code that can be tested locally.
- Record the precise affected requirement IDs.
- Record who can unblock the item and the exact evidence needed.
- Reopen affected verification if a later decision changes calculations, mappings, permissions or outputs.

## A. Owner business decisions

| Input ID | Related requirements | Decision required | Safe behavior until decided | Owner/unblock evidence | Impact |
|---|---|---|---|---|---|
| `IN-OWN-001` | `RULE-005`, `REP-004`, `OPS-006` | Meaning and accounting/control treatment of `TC` and every unknown tender code | Quarantine as unknown; show diagnostic; exclude from approved accounting mapping | Owner-approved tender definition with examples | Tender reconciliation and Tally export cannot receive final business acceptance. |
| `IN-OWN-002` | `RULE-004`, `DSR-003` | Approve the separate DSR invoice denominator and staff-attributed transaction denominator, or define a replacement | Keep separately labelled and expose exact variance | Written denominator decision with at least one worked example | Final DSR/staff KPI wording and controls. |
| `IN-OWN-003` | `RULE-007` | Decide whether physical stock equals display + backstock + defective + Y-location | Keep components separate; do not infer a total | Approved composition formula and missing-component policy | Closing-stock reconciliation. |
| `IN-OWN-004` | `RULE-007` | Approve stock-movement categories and signs beyond preserving ETP source signs | Preserve source signs and label unknown movement types | Approved movement dictionary with samples | Stock movement/reconciliation classifications. |
| `IN-OWN-005` | `RULE-006` | Customer output policy: none, display name, masked identity or non-PII reference | Exclude customer PII from canonical reports and support packages | Written privacy policy and role/output matrix | Customer-oriented reports and exports. |
| `IN-OWN-006` | `RULE-009` | Define ABV/ASP denominators and treatment of returns, zero-value transactions and cancellations | Do not present a new ABV/ASP metric as approved | Formula decision with worked invoice/return cases | Management KPI catalogue. |
| `IN-OWN-007` | `RULE-010` | Decide whether Service remains controlled Manual Entry or moves to an approved ETP Service export | Continue audited Manual Entry and show source type | Owner decision; if ETP, provide populated export | Service automation and source completeness. |
| `IN-OWN-008` | `DATA-006`, `RULE-009` | Define exchange, cancellation, credit-note and zero-value transaction treatment beyond confirmed `INV` and signed-negative `SR` | Preserve known source behavior; quarantine/diagnose unknown types | Approved transaction dictionary with worked examples | Sales, tender, staff and accounting totals. |
| `IN-OWN-009` | `DSR-003`, `REP-006` | Decide whether DSR needs a specialized Excel layout distinct from the general workbook | Retain deterministic supported Excel export | Approved workbook mockup/acceptance criteria | Presentation only; calculations remain unchanged. |

## B. Required populated ETP/source-system evidence

| Input ID | Related requirements | Source required | Minimum useful sample | Safe behavior until received | Impact |
|---|---|---|---|---|---|
| `IN-SRC-001` | `IMP-010` | AdvanceOrder Collection populated export | Normal row, return/correction if supported, zero/missing case | Profile remains unavailable/fail-closed | Cannot activate deterministic import/profile-dependent report. |
| `IN-SRC-002` | `IMP-010` | AdvanceOrder Sales populated export | Same coverage as above | Same | Same. |
| `IN-SRC-003` | `IMP-010` | Encircle Redemption populated export | Normal redemption plus correction/zero case | Same | Same. |
| `IN-SRC-004` | `IMP-010` | GC Wise Redemption populated export | Normal redemption plus correction/zero case | Same | Same. |
| `IN-SRC-005` | `IMP-010` | PRP SALES populated export | Normal sale plus return/correction | Same | Same. |
| `IN-SRC-006` | `IMP-010` | PRP STM populated export | Normal movement plus reversal/correction | Same | Same. |
| `IN-SRC-007` | `IMP-010` | Missing cross-store SOR and Transactionwise Bank samples | Both stores, overlapping dates and conflict case | Limit profile claims to verified layouts | Cross-store profile completeness. |
| `IN-SRC-008` | `RULE-010` | Populated ETP Service export, only if Service is moved from Manual Entry | Normal service, cancellation/return, tender fields and date/store evidence | Manual Entry remains authoritative | Automated Service profile. |
| `IN-SRC-009` | `RULE-008` | Approved product/category master or export | SKU-to-category mapping, validity dates and unknown/unmapped examples | Do not use `CLUSTER` as category | Category sales/stock, sell-through, stock turn and cover. |
| `IN-SRC-010` | `DSR-005` | Authoritative LY MTD source coverage | Same stores/period and TY-compatible definitions | Show `— / —` and `LY MTD source required` | LY MTD and derived growth remain unavailable. |

## C. External production and human-validation inputs

| Input ID | Related requirements | External item | Preparation Codex must finish first | Unblock evidence | Impact |
|---|---|---|---|---|---|
| `IN-EXT-001` | `REL-001`, `REL-004` | Clean Windows VM/PC matrix with and without supported SQL Server | Produce deterministic bootstrap and installer test scripts | Recorded fresh/install/upgrade/repair/uninstall results and artifact hash | Production installer acceptance. |
| `IN-EXT-002` | `REL-007` | Windows code-signing certificate and final publisher identity | Finalize unsigned artifact, signing instructions and hash flow | Signed executable/installer passes Windows signature verification | Removes Unknown Publisher warning. |
| `IN-EXT-003` | `REL-009` | Owner, Store Manager and Viewer users on target PC | Produce role-specific UAT scripts and expected results | Signed/date-stamped UAT record | Final production acceptance. |
| `IN-EXT-004` | `REL-010` | Actual store printer and Microsoft Excel | Produce exact PDF/Excel samples and acceptance checklist | Printed result and Excel-open verification | Printer/Office compatibility. |
| `IN-EXT-005` | `OPS-009`, `OPS-010`, `REL-002` | Real backup destination, scheduled task and service reboot cycle | Finish capacity warnings, scheduler and recovery scripts | Observed backup, checksum, isolated restore and post-reboot evidence | Operational readiness and recovery confidence. |
| `IN-EXT-006` | `UI-009` | Target touch/display devices, Narrator and supported scaling/resolutions | Finish automation and accessibility labels | Manual accessibility/touch matrix | Accessibility production acceptance. |
| `IN-EXT-007` | `OPS-005` | Installed/default WhatsApp/email applications | Finish safe launch/preparation behavior | Target-PC integration test; audit does not claim delivery | Sharing usability. |
| `IN-EXT-008` | `REL-011` | GitHub authentication and explicit release promotion | Complete reviewed commits, tag candidate and release evidence | Remote commit/tag/release URL matches local SHA/hash | Publication. |

## D. Owner-approved deferments

| Deferment ID | Related requirements | Deferred work | Owner instruction | Conditions that end deferment | Current safety position |
|---|---|---|---|---|---|
| `DEF-001` | `LIC-002` | Runtime licence validation, activation screen/import and startup enforcement | Complete licensing engineering now; implement only when the rest of the software is complete | Closure audit shows no autonomous functional/modular work remains and Owner explicitly authorizes licensing implementation | No licensing startup enforcement is present; current application behavior is unchanged. |
| `DEF-002` | `LIC-003` | Microsoft app registration and approved Owner `tid`/`oid` allowlist | Same licensing deferment | Owner authorizes licensing phase and completes app registration | No Microsoft identity secrets or IDs are stored. |
| `DEF-003` | `LIC-004` | Production signing-key ceremony and owner-only licence administration utility | Same licensing deferment | Owner authorizes licensing phase and controlled key ceremony | No production private key has been created or stored. |

## E. Decisions already resolved

These are not pending and must not be asked again unless the Owner changes them.

| Decision ID | Approved decision | Related requirements |
|---|---|---|
| `DEC-001` | `NETVALUE` is the primary GST-inclusive sales value. | `DATA-004` |
| `DEC-002` | Revenue Report/R022 controls final invoice/tender totals. | `DATA-005` |
| `DEC-003` | `CLUSTER` is Brand Segment; example `GAUTO` = Titan Automatic. | `DATA-007` |
| `DEC-004` | `INV` means invoice; `SR` means sales return and its negative signs are preserved. | `DATA-006` |
| `DEC-005` | Store Manager may import; Owner/Admin always has all rights. | `OPS-001`, `OPS-002` |
| `DEC-006` | Only Owner/Admin approves mapping and control-rule changes. | `OPS-003` |
| `DEC-007` | Business data and backups must not be automatically deleted; audit history target is two years. | `OPS-009`, `OPS-012` |
| `DEC-008` | SQL Server Express should start automatically; the application should warn about insufficient backup space. | `REL-002`, `OPS-009` |
| `DEC-009` | ETP files have a dedicated path and remain importable manually in the application. | `IMP-002`, `OPS-007` |
| `DEC-010` | Licensing runtime implementation waits until other software work is complete. | `LIC-002`–`LIC-004` |

## F. Explicitly invalid blocker reasons

The following may not be used to stop autonomous engineering:

- `MainWindow` is large or the refactor is risky;
- a phase was designed to be incremental;
- a feature would benefit from Owner visual review but has an authoritative mockup/specification;
- automated verification is time-consuming;
- an old audit already called the feature complete;
- external UAT will happen later;
- licensing is deferred;
- Graphify/CRG output is dirty or generated;
- another module is also incomplete.

If safe engineering and automated verification can continue, the row remains `NOT_STARTED`, `IN_PROGRESS` or `IMPLEMENTED_NOT_VERIFIED`—not blocked.

## Register update protocol

1. Add a row as soon as an exact external dependency is discovered.
2. Link every row to closure-ledger requirement IDs.
3. Complete all safe preparatory work before declaring the related requirement blocked.
4. When input arrives, record the date and authority, move the related ledger row to `IN_PROGRESS`, implement, and independently verify.
5. Preserve historical decisions in the decision log/ADR when they change durable rules; do not silently overwrite prior authority.
