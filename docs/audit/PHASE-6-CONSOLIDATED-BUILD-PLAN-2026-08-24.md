# Phase 6 — Consolidated build, UI architecture and ETP completion plan

**Prepared:** 2026-08-24 (Asia/Kolkata)
**Repository:** `saagar-control-centre-phase1-clone`
**Branch at planning time:** `agent/modular-phase1-shared-spine-v2`
**Planning baseline:** `59d36e768b1ef72050dfc678052a80a53a3df381`
**Programme rule:** build first; formal testing, named UAT, approvals, production signing and external publication are consolidated in the final phase.
**Status:** approved planning direction, not a release approval and not authority to publish externally.

This is the single running plan for Phase 6. It consolidates:

- completion of the postponed ETP programme, including the bounded four-report product and roadmap E2-E6;
- the Stage 2 responsive UI and architecture programme across all twelve modules;
- API-23/Chrome-44 compatibility as a first-class design constraint;
- build/release governance repairs;
- final device, resilience, production-data, staff-UAT, audit, signing and release gates.

The owner confirmed the Stock mobile/desktop direction and the build-first ordering on
2026-08-24. That confirms the design direction; it does not pre-approve the final rendered
product identity.

---

## 1. Current truth at Phase 6 entry

### 1.1 Banked work that is not to be repeated without cause

- Production signing and exact Phase-5 APK verification are banked.
- The production-signed Phase-5 APK was installed and verified on the physical Samsung
  SM-T875/API 33 tablet.
- The release TEST-badge defect is fixed.
- Phase-5 offline tests passed.
- Phase-5 WLMHW cashier UAT and the Shadul/Sagar reviews are banked against their named
  Phase-5 identity.
- The free Firebase project `saagar-phase-5-test-lab` already exists. Do not create another
  project or enable billing.
- API-23 emulator evidence is banked. It is not physical-OEM evidence.
- The live Firebase catalog found no API-23 physical device and the result is recorded as an
  evidence-backed carried exception, not a pass.

These records remain valid historical evidence. They do **not** approve newly built Phase-6
behaviour or the future Phase-6 APK.

### 1.2 Product work already present

The current ETP foundation already provides:

- four-XLSX recognition and parsing for R003/R013/R022/R025;
- reconciliation and atomic publication;
- encrypted native generation storage;
- shell-owned authorization and a narrow module gateway;
- owner import/confirmation, sanitized status/history and verified aggregate cards;
- synthetic API-33 publication, cold-relaunch and readback evidence;
- production WLMHW parsing evidence.

It is **not** the full E1-E6 product. In particular, the current “Verified Reports” area is an
aggregate presentation, not four real paged report screens; Home/DSR/Planning/Leave/Payroll do
not consume ETP verified views; targets, CRO reconciliation, monitoring, incentives and
clawbacks remain unbuilt.

### 1.3 Worktree entry condition

The current tree contains the bounded multi-financial-year ETP correction and API-23 layout
corrections plus unrelated/user-owned changes. The first implementation checkpoint must:

- isolate and validate only the Phase-6 foundation corrections;
- preserve unrelated modifications;
- preserve `.tmp-etp-synthetic-fixtures-device/` exactly;
- avoid regenerating formal audit evidence or building a new APK before the product plan is
  integrated;
- record the pre-Phase-6 source and test baseline.

### 1.4 Critical release-governance defect

`.github/workflows/build-apk.yml` currently runs on every branch push, has
`contents: write`, builds `app-debug.apk`, and publishes it as the mutable GitHub `latest`
release with stale version labels. No Phase-6 branch may be pushed until Phase 6A contains this
path. A normal build push must never publish a debug release.

---

## 2. Scope and explicit exclusions

### 2.1 In scope

1. Freeze the existing API-23 and multi-FY corrections as the Phase-6 foundation.
2. Contain automatic debug publication and create a canonical, secret-free clean release recipe.
3. Stabilize control identities before markup changes.
4. Extract the eleven legacy modules' duplicated 189-rule mobile block.
5. Build an API-23-safe shared responsive foundation, tokens and components.
6. Migrate all legacy modules in the proven order and complete the shell/dashboard/nav layer.
7. Complete the bounded ETP report product.
8. Build roadmap E2, E3, E4, E6 and finally E5.
9. Update the twelve-module architecture, storage, access-context and Graphify records.
10. Freeze one candidate and run final testing, approvals, signing and controlled release.

### 2.2 Outside Phase 6 unless separately authorized

- E7 Service ETP verification.
- F-series net-new product features.
- PHP/server/cloud sync, multi-device live sync or remote revocation.
- A storage-engine replacement or MAH-4 protocol redesign.
- PAYMENTTYPE25 guessed mapping. Continued quarantine is the current authority.
- A new UI framework or a new visual brand; navy/gold remains the product language.
- Production XLSX files, raw rows, filenames, PII, key material or passwords in Git/evidence.
- Paid Firebase services, Firebase billing, a new Firebase project, or any Firebase upload without
  immediate owner confirmation.
- GitHub Release, store upload, production distribution or other external publication without
  immediate owner confirmation.

---

## 3. Build-first operating contract

“Testing last” means **formal acceptance is last**. It does not mean implementation waves may
accumulate known breakage.

Every build slice still performs focused developer checks for its own contract, calculations,
API-23 output and unchanged business behaviour. These checks are merge safeguards, not UAT,
release approval, a formal re-anchor or a claim that Phase 6 passed.

Only Phase 6J may perform or claim:

- the final full regression and rendered matrix;
- named Akash/Shadul/Sagar UAT;
- capability, language and visual approvals;
- final controlled audit/re-anchor/rebind;
- production signing and release registration;
- production ETP publication acceptance;
- release approval or external publication.

Any product-byte change after the Phase 6J source freeze invalidates downstream APK, device,
UAT, approval and audit evidence.

---

## 4. Agent deployment model

### 4.1 Capacity and ownership

Use a maximum of four active agents: one **Lead Integrator** plus no more than three specialists.
Simultaneous writers must use isolated Git worktrees. Agents sharing the main checkout may only
work in parallel when all but one are read-only.

The Lead Integrator is the sole writer for:

- `www/index.html` and shell wiring;
- `www/module-manifest.js` and shared asset registration/order;
- `www/shared/module-runtime.js`;
- shared cross-module contracts;
- governed/golden build identities and MAH profiles;
- integration commits, conflict resolution, Graphify refresh and final evidence binding.

A module specialist owns only:

- its assigned `www/modules/<id>/` files;
- its new module-specific stylesheet;
- its focused tests.

An ETP specialist may own an explicitly assigned ETP policy/query/render file and focused tests,
but may not move or duplicate the parser, worker, encryption, native store, receipt registry or
publication lifecycle into the iframe.

### 4.2 Standard agent packet

Before deployment, every agent receives a written packet containing:

1. phase and objective;
2. required predecessor gate;
3. allowed files and forbidden files;
4. contract/behaviour that must remain unchanged;
5. focused checks to run;
6. required handoff: commit, changed-file list, test result, risks and unresolved questions;
7. explicit statement that the agent cannot approve its own work or claim formal acceptance.

### 4.3 Deployment/stand-down matrix

| Agent role | Deploy when | Stand down when |
|---|---|---|
| Lead Integrator / Repository Steward | Phase 6A start | After Phase 6J closure register |
| CI/Build Governance Agent | 6A only | Push/release containment tests and canonical recipe land |
| Identity Annotator | Immediately before assigned module markup work | Stable identity patch merges without visual change |
| ETP Domain/Query Agent | After foundation checkpoint | Versioned bounded contract and focused tests freeze |
| Shared UI Foundation Agent | After Planning extraction proof | Stock pilot freezes component API |
| Module Migration Agent | After component API freezes; one module per agent | Module patch merges and focused checks pass |
| API-23 Compatibility Reviewer | At foundation, Stock, ETP and high-risk module gates | Generated Chrome-44 path is independently checked |
| Security/Privacy Adversarial Agent | At ETP contract/report/integration boundaries | Fail-closed tests and threat findings resolve |
| Cross-module Integration Agent | Only after both producer and consumer modules freeze | One named integration merges; never multiple target modules at once |
| Money-Control Agent | Only after E2/E3/E4/E6 and Payroll migration | Golden money cases and idempotency pass |
| Documentation/Graphify Agent | After feature writers stop in 6I | Architecture/graph accurately match frozen source |
| Release Controller | Phase 6J only | Closure/release decision is recorded |
| Final Test/Rendered/Device/UAT agents | Exact frozen source/APK exists | Their identity-bound evidence is delivered |

### 4.4 Work that must remain serial

- release-workflow containment and any branch push;
- Planning extraction proof before the other ten legacy extractions;
- Planning proof before Stock, and Stock before any other component adoption;
- token/tier/component API changes after Stock adoption begins;
- Expense's JS-render helper proof before later Family-B migrations;
- QMS view extraction before QMS responsive adoption;
- any two writers touching shell/runtime/shared CSS;
- manifest/golden/MAH refreshes and integration conflict resolution;
- ETP wiring into a module whose UI migration is not frozen;
- E5 incentive and any Payroll calculation/persistence change;
- final audit, approval rebind, production build/signing and release publication.

---

## 5. API-23 responsive architecture contract

The older Stage-2 documents are design inputs, not code that may be copied verbatim.

Phase 6 guarantees Chrome 44/API 23 through:

- media queries and explicit width-tier classes as the primary tier mechanism;
- physical `margin-*`/`padding-*` properties where logical properties are unsupported;
- explicit selectors instead of relying on `:is(...)`;
- flex/block fallbacks under `.saagar-api23`;
- generated CSS-variable resolution through the existing API-23 asset pipeline;
- no CSS Grid, container query, `clip-path`, sticky-column or other modern feature as the only
  working path.

Modern CSS may be progressive enhancement only after the API-23 path is complete and tested.

The UI system retains:

- navy/gold brand palette;
- four tiers: mobile `<640`, compact `640-899`, tablet/laptop `900-1199`, desktop `>=1200`;
- `auto` as the default width-driven mode while respecting explicit mobile/desktop preferences;
- 12px minimum persistent UI text, except an explicitly bounded 11px desktop-dense table token;
- 44px primary touch targets;
- pinch zoom by removing `user-scalable=no`;
- cards, priority columns and true grid as three explicit table strategies;
- mobile workflow parity and dense desktop operation.

ETP is not one of the eleven legacy copied-block consumers. It must not inherit the extracted
189-rule legacy file.

---

## 6. Detailed implementation phases

### Phase 6A — Release containment and recoverable foundation checkpoint

**Purpose:** make Phase-6 development safe to push, then freeze the existing corrections.

**Deploy:** Lead Integrator + CI/Build Governance Agent + read-only Worktree/Identity Auditor.

**Build work:**

1. Change normal branch CI to `contents: read`; it may test and upload a clearly named debug
   workflow artifact but must never call a Release action.
2. Remove mutable `latest` publication from any push-triggered job.
3. Add `tests/release-workflow-policy.test.mjs` to reject push-triggered write permission,
   release actions, debug APK release inputs, mutable `latest` tags and unapproved production
   workflows.
4. Keep production release local under the signing custodian unless a separate, manually
   dispatched, protected-environment design is later authorized.
5. Add one tracked, secret-free canonical production recipe that requires a clean/frozen commit
   and runs: Capacitor sync → Android overrides → API-23 preparation → `clean assembleRelease`.
   Credentials enter only through process environment and are cleared on exit.
6. Review and isolate the pending API-23 layout and multi-FY import corrections.
7. Commit the Phase-6 foundation separately while preserving unrelated changes and
   `.tmp-etp-synthetic-fixtures-device/`.
8. Record the current twelve-module source/test baseline and refresh Graphify.

**Engineering exit:**

- a non-release branch push cannot change GitHub Releases;
- a debug artifact cannot enter a release job;
- the canonical recipe is reviewable and has no credentials;
- multi-FY rows outside the explicitly selected store/FY/date scope cannot enter reconciliation
  or staging;
- API-23 flex/iframe fallback tests pass;
- the Phase-6 base commit and dirty-file ownership are explicit.

**Evidence target:** `verification/audit/PHASE-6-CI-RELEASE-CONTAINMENT-<date>.md`.

Do not push Phase-6 work until the workflow containment part of this phase is merged.

### Phase 6B — Stable identities and ETP domain/query contracts

**Purpose:** protect the capability oracle and freeze ETP data contracts before visual work.

**Deploy in parallel:**

- up to two Identity Annotators on disjoint modules;
- one ETP Domain/Query Agent;
- Lead Integrator merges serially and owns shared wiring.

**Build work — identity lane:**

- add stable `data-action` to every existing control that Phase 6 will move, including all ETP
  controls;
- reuse the existing resolved capability identity wherever possible so the ledger meaning does
  not change;
- add safe table handles and predeclare only the Details actions required by a selected table
  strategy;
- make no visual/layout/business change in these patches.

**Build work — ETP lane:**

- record the current implementation as `ETP Foundation v1`, not E1-E6 complete;
- freeze versioned `READY`, `READY_WITH_WARNINGS` and `NOT_READY` semantics;
- freeze bounded filter, sort, page/cursor, scope and generation contracts;
- make cursor state opaque and bind it to store, FY, date scope, generation, report and filter
  signature;
- complete sanitized import-batch history and the versioned tender dictionary;
- keep unknown tenders `Unmapped` and PAYMENTTYPE25 quarantined;
- treat each store/FY/date scope as a separate publication generation;
- keep HEMW semantics evidence-gated until an untouched sample set is authorized and checked.

**Engineering exit:**

- identity patches cause no visual change and no unapproved capability change;
- all query projections and filters are allowlisted;
- no iframe receives raw facts, workbook bytes, filenames or PII;
- cross-store/FY/generation isolation and hostile-cursor tests pass;
- opening or querying verified data remains read-only.

### Phase 6C — Legacy shared-block extraction and Planning proof

**Purpose:** remove duplication without redesigning module behaviour.

**Deploy:** one Shared CSS Writer + one Planning Specialist + one read-only Visual/Source
Comparator. Lead Integrator owns shared imports and identities.

**Build work:**

1. Extract the current 191-rule, 24,977-byte canonical legacy mobile authority into
   `www/shared/module-mobile-legacy.css`. Eight modules currently carry those exact bytes;
   Service, QMS and Payroll carry the same authority plus bounded local deltas that must remain
   after the shared import. The earlier 189-rule measurement is historical, not a slicing rule.
2. Prove the extraction on Planning first.
3. After Planning passes, remove the duplicate block mechanically from the other ten legacy
   modules.
4. Keep the existing six-rule common asset genuinely common.
5. Do not add the extracted legacy asset to ETP.

**Engineering exit:**

- Planning matches its before state at governed widths;
- eleven inline authorities reduce to one legacy shared copy plus the bounded Service, QMS and
  Payroll delta rules;
- every module keeps its own delta rules;
- ETP product bytes/layout remain unaffected;
- there is still no responsive redesign in this phase.

### Phase 6D — API-23-safe shared UI foundation and access-context decision

**Purpose:** publish the opt-in component system without changing module screens yet.

**Deploy after token names are frozen:**

- Tokens/Tiers Agent;
- Table Component Agent;
- Forms/Buttons/Cards/Modal/Nav/States Agent;
- Lead Integrator owns imports, width-mode engine, API-23 generation and identity refresh.

A Security/Access reviewer performs a read-only access-context decision matrix at the phase gate.

**Build work:**

- consolidate brand tokens and add spacing, typography and metrics tokens;
- add four explicit width tiers and `auto` mode;
- implement table cards, priority columns and true grids for both static and JS-generated markup;
- implement shared form, button, card/tile, modal/sheet, toolbar, nav and state components;
- add API-23 fallbacks and boundary checks at 639/640, 899/900 and 1199/1200;
- remove `user-scalable=no` and enforce the new-component type floor;
- decide access context module by module. Do not blanket-enable the seven original false modules;
- keep ETP `accessContext:false`, because its shell-owned gateway is the authorization boundary.

**Engineering exit:**

- components are opt-in visual no-ops before adoption;
- generated API-23 assets contain resolved variables and explicit fallback paths;
- no modern CSS feature is the sole path;
- JS helper escapes ordinary data and permits audited markup only for explicit controls;
- every enabled access adapter has a named consumer and fail-closed tests.

### Phase 6E — Stock pilot and component freeze

**Purpose:** prove the entire component contract on one well-covered real module.

**Deploy:** one Stock Implementer. Alongside it, use a read-only Business Behaviour Reviewer and
an API-23 Visual Reviewer. No other module adoption writes during this phase.

**Build work:**

- migrate Stock to the new tiers/tokens/components;
- use cards for opening/closing counts and theft-log workflows;
- use priority columns for summaries;
- retain a true data grid only where movement reconciliation requires comparison;
- extract remaining Stock-only CSS into its module stylesheet;
- preserve all stock calculations, persistence and business actions exactly.

**Engineering exit:**

- all Stock-focused tests pass with no calculation/storage change;
- six tables have an explicit, reviewed strategy;
- mobile data entry does not require general sideways scrolling;
- the reconciliation grid remains usable on mobile and dense on desktop;
- API-23 layout is functional;
- component API is frozen after pilot findings.

No module agent may redefine the shared component API after this gate. Required changes return to
the Lead Integrator and reopen the Stock pilot check.

### Phase 6F — Bounded ETP reports and Family-A migration

**Purpose:** introduce real verified reports using the proven table system while migrating the
markup-rendered module family.

**Wave 1 deployment:** ETP Reporting Agent + Payroll Agent + Grooming Agent.
**Wave 2 deployment:** Service Agent + ETP Security/Adversarial Reviewer.
All writers use disjoint worktrees; the Lead Integrator merges in the stated order.

**ETP build work:**

- build parent-owned allowlisted filtering and paging;
- build iframe-owned pure DOM presentation against mocked query responses;
- wire only after both contract sides pass independently;
- deliver real paged R022 Revenue/Tender, R025 Sales Detail, R013 CRO Attribution and R003
  Discount views;
- deliver coverage, reconciliation, readiness and PAYMENTTYPE25-warning surfaces;
- replace the current 20,000-row iframe aggregation approach for report browsing with bounded
  pages;
- provide no report export in this bounded release.

**Family-A build work:**

- migrate Payroll, then Grooming, then Service;
- preserve Payroll financial goldens exactly;
- preserve Service photo/evidence/custody paths;
- apply declared table strategies to every static table.

**Engineering exit:**

- pagination/filtering cannot mix scope or generation and performs no writes;
- REC-002 failure cannot render apparently complete totals;
- R003/R013 never add revenue;
- no raw/native access crosses into the ETP iframe;
- Payroll goldens and Service evidence workflows are unchanged;
- each migrated module is usable at all four tiers on API 23.

### Phase 6G — Family-B JS-rendered migration and shell

**Purpose:** migrate the dynamically rendered modules, then complete shell-level responsiveness.

**Deployment order:**

1. Expense alone proves the JS-rendered component helper.
2. Leave and CRO Audit may prepare in parallel; integrate serially.
3. Tax and DSR may prepare in parallel; integrate serially.
4. QMS receives a dedicated writer: view-layer extraction first, responsive adoption second.
5. After every module freezes, one Shell Owner modernizes shell/dashboard/navigation. Other agents
   are read-only during shell writes.

**Module order remains:** Expense → Leave → CRO Audit → Tax → DSR → QMS.

**Build work:**

- make every generated table/button test-visible in rendered DOM checks;
- keep calculations, allocation engines, close-day state and persistence untouched;
- migrate shell header, sidebar, bottom navigation, dashboard cards/states and full `auto` mode;
- retain the existing shell/module message protocol;
- retain Reports ownership of ETP and keep ETP absent from Settings.

**Engineering exit:**

- Expense proves safe escaping and explicit audited control markup;
- DSR EOD/close-day and QMS allocation suites remain green;
- all twelve modules route and restore correctly;
- no root overflow, unreachable action or lost inline-handler behaviour exists;
- shell and all modules have functional API-23 primary paths.

### Phase 6H — ETP operational programme: E2 → E3 → E4 → E6 → E5

**Purpose:** build the postponed business programme only after its consumer modules and shell are
stable. E5 is the final build because it affects money.

#### Phase 6H.1 — Roadmap E2 verified analytics

**Deploy:** ETP Analytics Agent + Home/DSR Integration Agent + Lead Integrator.

Deliver day/MTD/YTD/LY views; net sale, bills, units, ATV, UPT and ASP; brand/CRO/tender mix;
returns and manual-discount visibility; an honest verified-through banner; and a Home tile.

Exit requires declared period-end semantics, honest missing-coverage `—`, partial-period labels,
cross-store isolation, and the permanent `store net = CRO achievement + Unassigned` test.

#### Phase 6H.2 — Roadmap E3 CRO reconciliation

**Deploy:** CRO Reconciliation Agent + DSR Workflow Integration Agent + Security/Audit Reviewer.

Deliver invoice-grain declarations; `OPEN → CLOSED → IMPORTED → RECONCILED/VARIANCE → LOCKED`;
Matched/Misattributed/Unclaimed/Phantom outcomes; unassigned queue; 24-hour freeze; owner-only
post-freeze correction; manager disposition and attribution audit.

Exit requires immutable source facts, complete before/after audit, locked-day protection,
backup/restorable operational overlays and the store-net identity at every state.

#### Phase 6H.3 — Roadmap E4 targets, Planning and Leave

**Deploy:** Targets/Planning Domain Agent + Leave Integration Agent + Planning Presentation Agent.

Deliver immutable target versions; day-0 CRO allocation lock; new-version reallocation; LY
day-weight curve; versioned festive overrides; Leave pro-rating and explicit Coverage Shortfall;
target, pace, verified actual, rupee gap, run-rate and projected landing.

Exit requires that target version 1 is never edited, allocations reconcile to the store target,
Leave cannot rewrite locked versions, and declarations never become achievement.

#### Phase 6H.4 — Roadmap E6 exceptions before money

**Deploy:** Exception Policy Agent + Home Feed Agent + Audit-State Agent.

Deliver late attribution, unassigned trend, near-target final-week, final-48-hour concentration,
early-next-month movement, declared-versus-actual variance and restated-period exceptions. Every
exception has owner, age, status, evidence and closure reason.

Exit requires durable restatement events consumable by E5, no silent self-closure, versioned
thresholds and sanitized role-filtered Home summaries.

#### Phase 6H.5 — Roadmap E5 incentive and Payroll, last build

**Deploy:** Money-Control Domain Agent + Payroll Integration Agent + independent Golden-Case
Agent. The Lead Integrator owns final wiring.

Deliver versioned scheme bands; provisional and close+15 final computation; gap/reconciliation
hard blockers; ETP-only calculation basis; durable clawbacks; controlled Payroll earning-line
provenance and pre-lock gate. Keep the engine disabled without an approved active scheme version.

Exit requires boundary/gap/restatement/clawback/sum-identity goldens, idempotent finalization,
no declaration-based payment, and no manual edit/recreation of the controlled Payroll line.

### Phase 6I — Integration cleanup, documentation and source freeze

**Purpose:** stop feature writes, remove compatibility debt and create one reviewable source
candidate. This is still a build phase, not formal approval.

**Deploy:** Lead Integrator + Cleanup Reviewer + Documentation/Graphify Agent. All feature agents
stand down.

**Build work:**

- resolve integration conflicts serially;
- remove migrated modules' legacy links and delete the temporary legacy shared asset only when
  there are zero consumers;
- retire dead `.bcc-mobile` patches and reduce obsolete `!important` rules;
- update `ARCHITECTURE.md` to twelve iframe modules and ETP's hybrid parent-gateway model;
- update module/access/storage/schema/role/operator documentation;
- regenerate manifest, golden hashes and MAH identities from final merged bytes;
- run Graphify update and verify the graph contains all twelve modules and new integration paths;
- confirm every Phase-6 scope row has implementation and focused-test ownership;
- produce a clean source-freeze candidate commit and WWW-tree hash.

**Engineering exit:**

- zero feature-agent writes remain;
- no legacy asset is referenced or left dead;
- no placeholder Phase-6 implementation remains;
- generated identities match final bytes;
- E7 remains explicitly deferred;
- source tree is clean and ready for final acceptance.

---

## 7. Phase 6J — Final testing, approvals, production signing and release

This is the **last phase**. Nothing in Sections 6A-6I is formal approval.

### Phase 6J.0 — Freeze controller and approved release identity

**Deploy:** Release Controller only.

- obtain owner-approved version/versionCode; do not assume the next number;
- verify the clean source commit, Graphify state, product fingerprint and WWW-tree hash;
- lock feature writers out of the candidate;
- stop if any planned build row, generated identity or focused check is unresolved.

### Phase 6J.1 — One production-signed candidate

**Deploy in order:** Production Build Agent supervised by Sagar as key custodian, then an
independent Artifact Verification Agent.

- use the tracked clean release recipe;
- never print/store signing credentials;
- build exactly one candidate;
- verify package/version/minSdk/targetSdk, `android:debuggable`, v2/v3 signatures and production
  certificate;
- compute byte count/SHA-256 and register the exact artifact;
- reject debug certificate, stale files or mismatched source identity.

Evidence targets:

- `verification/audit/PHASE-6-PRODUCTION-SIGNING-<date>.md`
- `verification/audit/PHASE-6-PRODUCTION-RELEASE-REGISTER-<date>.json`

### Phase 6J.2 — Parallel technical acceptance

With the exact APK hash fixed, deploy three specialists alongside the Release Controller:

1. **Automated Regression Agent** — `npm ci`, registered Phase-0/security/report/offline/ETP/
   modular/MAH/language/manifest suites, Phase-6 components, API-23 compatibility and audit-runner
   self-tests; zero fail/skip/todo/cancel.
2. **Rendered UI/Accessibility Agent** — every governed surface at 360×800, 412×915, 800×600 and
   1365×768 in English/Marathi/Hindi, plus tier-boundary checks at 639/640, 899/900 and 1199/1200.
3. **Android Lab Agent** — exact APK on API-23 emulator, current-platform emulator and physical
   SM-T875/API 33, including update-in-place, state preservation, installed `base.apk` readback,
   rotation/resume/process-restoration and full smoke.

Rendered acceptance covers blank states, overflow, table strategy, 44px targets, readable text,
contrast, focus/Back/Escape, pinch zoom, empty/loading/warning/error states, long translations and
mobile/desktop workflow parity.

### Phase 6J.3 — Physical API-23/OEM, interruption and low-storage gate

**Deploy:** OEM Device Evidence Agent only when a qualifying physical device is available and the
exact APK is ready.

Required physical cases include OEM document selection, import, cold relaunch/readback, provider
interruption, rotation/background/force-stop around staging/activation, write failure/disk-full,
corruption refusal, safe low-storage refusal, previous-generation retention and no partial
activation.

Low-storage work uses synthetic data, a verified backup, a bounded disposable filler, an explicit
recovery floor and immediate cleanup on any device-health limit.

If the live provider catalog still has no physical API-23 device, record a refreshed,
evidence-backed carried exception. Never substitute the emulator and never claim the gate passed.

No Firebase upload, billing change or paid service is permitted without immediate owner
confirmation. Ask immediately before any upload if qualifying hardware becomes available.

### Phase 6J.4 — Authorized production ETP publication

**Deploy:** ETP Publication Evidence Agent only after technical acceptance and explicit source-set
authorization.

For each authorized store/FY/period, hash the untouched R003/R013/R022/R025 sources outside Git;
record bounded counts and scope; require owner/manager reauthentication; require REC-002 PASS;
record non-revenue exception counts; activate exactly one generation; confirm previous-generation
retention; cold-launch/read back; and record metadata-only receipts.

Raw workbooks, rows, customer data and report screenshots do not enter Git. Preparation by an
agent is not publication authority.

### Phase 6J.5 — Named Phase-6 UAT

**Deploy:** UAT Coordinator only after technical and production-publication gates are resolved.
The coordinator records decisions; it cannot make them.

- Akash: cashier/maker workflows.
- Shadul: manager/checker, exceptions and approval workflows.
- Sagar: owner/admin, Payroll, access/PIN, backup/restore, ETP publication and audit trail.

Where a workflow is dependent, run Akash maker → Shadul checker → Sagar owner. Cover both stores
where applicable and all Phase-6-changed modules. Previous Phase-5 UAT is not final Phase-6 UAT.
No unresolved P0/P1 is permitted and no human decision may be inferred from automation.

### Phase 6J.6 — Approval rebind, controlled audit and integrity review

**Deploy in order:** Approval Rebind Agent → Controlled Audit Agent → Evidence Integrity Agent.

- obtain exact capability-delta approval;
- obtain fresh visual/language approval for materially changed UI;
- rebind older business approvals only when byte/delta evidence proves applicability;
- bind every approval to commit, product fingerprint, WWW tree and APK hash;
- run the final comparison audit once in a clean detached linked worktree;
- stop on new P0/P1, unapproved capability change, an unexpectedly unmeasured gate, storage gap,
  critical-control regression or unexplained coupling/performance regression.

The audit is engineering evidence; it cannot replace physical, UAT, privacy, signing or release
authority.

### Phase 6J.7 — Closure register and release decision

**Deploy:** Closure Documentation Agent and Release Controller.

Update:

- the final Phase-6 closure register;
- `docs/audit/HANDOFF.md`;
- architecture/module inventory;
- exact test counts, commit/fingerprint/tree/APK/certificate hashes;
- the immutable evidence manifest.

Every gate row must be one of:

1. closed with identity-bound evidence;
2. pending external authority; or
3. open owner-accepted exception with risk, prohibited claims and reopen condition.

A carried exception never counts as a passed gate. The independent release approver must be
different from the signing custodian where possible. If Sagar remains both, explicitly carry the
independence exception rather than claim a clean independent pass.

Ask for confirmation immediately before any GitHub Release, store/Firebase upload or other
external publication.

---

## 8. Dependency map and safe parallel lanes

```text
6A release containment + foundation freeze
 ├─> 6B identity lane ─> 6C Planning extraction ─> 6D UI foundation ─> 6E Stock
 │                                                                  ├─> 6F ETP reports + Family A
 │                                                                  └─> 6G Family B + shell
 └─> 6B ETP domain/query contracts ──────────────────────────────────────┘
                                                                         │
                                                                         v
                                6H E2 ─> E3 ─> E4 ─> E6 ─> E5 (money last)
                                                                         │
                                                                         v
                                      6I cleanup/docs/Graphify/source freeze
                                                                         │
                                                                         v
                                     6J signed candidate/test/UAT/audit/release
```

Permitted parallelism:

- ETP pure domain/query work may run while legacy UI extraction/foundation work proceeds.
- After the component API freezes, disjoint module migrations may be prepared in parallel in
  isolated worktrees, but integration remains serial.
- ETP parent query and iframe renderer may be built in parallel against the same frozen contract.
- Pure ETP engines may be prepared while a target module migrates, but cross-module wiring waits
  until the consumer module freezes.

Integration locks:

- Home/DSR wiring waits for shell and DSR migration.
- E4 wiring waits for Planning and Leave migration.
- E5 wiring waits for Payroll migration and E2/E3/E4/E6 completion.
- No integration agent edits two consumer modules in one patch.

---

## 9. ETP and architecture invariants

- Preserve the versioned `retail-etp-core-v1` parser/sign/reconciliation meanings unless a
  separately approved contract replaces them.
- Keep ETP engines loaded once in the parent shell; the iframe is presentation only.
- Keep trust decisions shell-owned. The iframe cannot assert its own role, store or scope.
- Keep all gateway projections allowlisted and bounded.
- Keep re-derivable facts in the sealed ETP store; declarations, dispositions, target versions,
  allocations, incentive/clawback state, tender mappings and import-control logs belong to
  operational storage and its backup rules.
- Never implicitly merge stores, FYs, generations or date scopes.
- R022/R025 are economic facts; R003/R013 are non-revenue exception/attribution sources.
- REC-002 failure is `NOT_READY` and blocks apparently complete totals.
- PAYMENTTYPE25 remains visibly quarantined until a separately approved mapping exists.
- Achievement and incentive come from verified ETP facts, never declarations.
- Money paths remain disabled without approved versioned authorities.
- No UI task changes native storage, export control, reauth/PIN or the MAH-4 protocol for
  convenience.

---

## 10. Global stop rules

Stop the active phase and return to its owner if:

- the unsafe automatic debug-release workflow is still active when a push is proposed;
- two agents would edit the same file/shared contract concurrently;
- source changes after final freeze;
- a P0/P1 exists;
- a business calculation, storage, permission or role result changes unexpectedly;
- API-23 relies solely on an unsupported CSS/JS feature;
- an APK has the wrong package/version/certificate/debug flag or TEST/DEMO badge;
- a registered suite fails, skips or becomes unexpectedly unmeasured;
- UI has a blank screen, inaccessible control, critical overflow or unreadable translation;
- ETP scope is ambiguous, REC-002 fails, coverage is incomplete or publication becomes partial;
- previous-generation retention fails;
- raw production data, credentials, private keys or PII enter logs/evidence/source control;
- a human approval is inferred rather than personally stated;
- an emulator is offered as a physical-device substitute;
- a low-storage recovery floor or device-health boundary is crossed;
- an agent attempts billing, paid services or external publication without authority.

---

## 11. Phase 6 definition of done

Phase 6 is complete only when all of the following are true:

1. Release workflow containment and canonical build provenance are in place.
2. All twelve modules use the approved responsive architecture and documented API-23 fallback.
3. The bounded four-report ETP product and roadmap E2/E3/E4/E6/E5 scope are implemented.
4. The exact clean source commit, product fingerprint, WWW tree and production APK hash are frozen.
5. Every registered automated test passes with zero fail/skip/todo/cancel.
6. Every governed surface/viewport/language case is captured and fluently reviewed.
7. The exact production-signed APK is verified and read back from the physical SM-T875.
8. Physical API-23/OEM and low-storage gates pass on qualifying hardware or are explicitly carried
   with evidence and owner acceptance; emulator evidence is never substituted.
9. Authorized production ETP publication passes or is explicitly carried, never silently omitted.
10. Akash, Shadul and Sagar personally complete final-identity Phase-6 UAT.
11. Capability, language, visual, architecture and applicable business approvals bind to the exact
    final identity.
12. The controlled comparison audit has no unauthorized regression.
13. Every closure-register row has a truthful decision and every carried risk is acknowledged.
14. Release approval names the exact production APK and certificate.
15. External publication occurs only after immediate, explicit owner confirmation.

Do not state that Phase 6 is closed merely because all code is built. It is closed only when each
required gate passes or is formally recorded and accepted as a carried exception.

---

## 12. Authoritative inputs

Repository inputs:

- `docs/audit/ETP-COMPLETION-PHASES-2026-08-21.md`
- `docs/V6-IMPROVEMENT-ROAD-PLAN.md`
- `docs/audit/PHASE-5-CONSOLIDATED-CLOSURE-2026-08-21.md`
- `docs/audit/AUDIT-PROGRAM-v1.md`
- `verification/audit/PHASE-5-CURRENT-REMAINING-INVENTORY-2026-08-23.md`
- `verification/audit/PHASE-5-FIREBASE-API23-PHYSICAL-CATALOG-EXCEPTION-2026-08-24.md`

External Stage-2 design inputs retained in the Retail workspace:

- `V:\Co work\Projects\Retail\docs\design\SCC-STAGE-2-FOUNDATION-SPEC-2026-08-16.md`
- `V:\Co work\Projects\Retail\docs\design\SCC-MODULE-SURVEY-COMPLETE-2026-08-17.md`
- `V:\Co work\Projects\Retail\docs\design\SCC-STAGE-2-PLAN-AND-NEXT-STEPS-2026-08-21.md`
- `V:\Co work\Projects\Retail\docs\design\SCC-TABLE-COMPONENT-API-2026-08-17.md`

Where an older document assumes eleven modules, a modern browser, container-query-only layout or
pre-Phase-5 ETP state, this consolidated Phase-6 plan is the controlling correction.
