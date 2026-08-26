# ETP localization import checkpoint — 2026-08-21

**Status (updated after merge):** ETP-4 steps 1 and 2 are complete, and the engineering half of step 3 is now done and merged — **`test:modular` improved from 74 pass / 12 fail to 79 pass / 7 fail, and `test:mah4` is 46/46.** The **7 remaining failures are all owner gates**: 4 MAH-3 needing captured `etp` visual evidence plus owner confirmation, and 3 capability-ledger checks needing a new owner capability approval. **No engineering remains before those gates.**

Still: do not build an APK or run the controlled audit until the owner gates are cleared.

> ## Correction — attribution, added 2026-08-21
>
> An earlier revision of this document attributed the 19 red identity assertions to the
> localization import. **That was wrong.** Measured in a clean worktree at `099d6a4`, the commit
> immediately before the import:
>
> | Suite | At `099d6a4` (pre-import) | After import |
> |---|---|---|
> | `test:modular` | 74 pass, **12 fail** | 74 pass, 12 fail |
> | `test:mah3` | 3 pass, **4 fail** | 15 pass, 4 fail |
> | `test:mah4` | 5 pass, **3 fail** | 43 pass, 3 fail |
>
> **The localization import added zero failures.** The identical failures were already present. The
> drift was introduced by the ETP-1..ETP-3 work, which took `www/` from 100 to 103 files at
> `b0904bc` without updating the identity profiles and inventory constants.
>
> `ETP-MODULE-CRASH-CHECKPOINT-2026-08-21.md` reports "Final focused ETP suite: 155/155 pass. Final
> focused manifest suite: 17/17 pass." Both are true, but neither suite covers MAH-3, MAH-4, MH1 or
> the capability ledger, which is why the drift went unrecorded.
>
> Work continues on branch `agent/etp-l10n-identity-regen`. See
> `ETP-IDENTITY-REGEN-FINDINGS-2026-08-21.md` on that branch. Progress there: **MAH-4 is green at
> 46/46**, and a product defect was found — `www/modules/etp/index.html` contains **zero**
> `st-v5-module-audit-bridge` while all eleven other modules contain exactly one.

## What was done

**Owner approval:** Sagar approved all 119 rows on 2026-08-21. Record:
`verification/audit/approvals/ETP-LOCALIZATION-APPROVAL-2026-08-21.json`, reviewed CSV SHA-256
`f073bf322427996c6bbb44828dc46693ade97333b83f52b47fa588a55c1b7bb1`.

### Step 1 — wording imported

`www/app-i18n.js` gained **102** phrase triples, inserted at the head of `PHRASES` under a labelled
banner naming the approved CSV hash. Localization here is a global exact-phrase table
(`['english','marathi','hindi']`), and every module including `etp` loads it via
`src="../../app-i18n.js"`, so the ETP module picks the wording up without further wiring.

Row disposition, all 119 now carry `reviewer = Sagar (sagarbora91)`:

| Decision | Rows | Meaning |
|---|---:|---|
| `translate` | 104 | 102 newly added, 2 already present with identical wording |
| `preserve-literal` | 12 | code-only strings, correctly not translated |
| `approved-not-imported-shared-phrase` | 3 | see below |

CSV after marking: SHA-256 `fb24951db077b49c3f75e03bceda219edd91eb16ae22d9b9ef353f446fed24c7`.
All 119 `english_sha256` values still verify, all rows keep 14 columns, `row_sha256` untouched.

### The three not imported — this needs an owner decision

The dictionary is **global**. These three English phrases already carry Phase 4A owner-approved
wording, and the ETP package proposed different wording. Importing it would silently re-word other
surfaces, which is outside what was reviewed:

| Row | English | Existing (kept) | ETP package proposed | Also used by |
|---|---|---|---|---|
| ETP-L10N-006 | Import | mr `आयात` / hi `आयात` | mr `आयात करा` | shell |
| ETP-L10N-016 | Store | mr `दुकान` / hi `स्टोर` | mr `स्टोअर` | shell, grooming, stock |
| ETP-L10N-022 | To | mr `पर्यंत` / hi `तक` | mr `ला` / hi `को` | cro_audit |

Existing wording was kept. ETP renders these using the Phase 4A translations. If the ETP wording is
preferred, that is a **cross-module wording change** and should be approved as such.

This is a structural limit worth recording: a global exact-phrase table cannot express
module-specific wording for a shared word. Scoped overrides would be a design change, not a fix.

### Step 2 — focused tests green

- `npm run test:etp` — **155/155 pass**
- `npm run test:language` — **10/10 pass**

`tests/language-localization.test.mjs` pins the dictionary size; the golden moved `2009 -> 2111`
(+102, exactly the import). `www/app-i18n.js` passes `node --check`.

## Step 3 — NOT done. What is red and why

**These failures pre-date this import** — see the correction at the top. They were introduced when
ETP-1 added three files to `www/` without updating the identity profiles. The import neither caused
nor worsened them; it inherits them.

| Suite | Result |
|---|---|
| `test:manifest` | 8/8 pass |
| `test:mah3` | 15 pass, **4 fail** |
| `test:mah4` | 43 pass, **3 fail** |
| `test:modular` | 74 pass, **12 fail** |

`app-i18n.js` is pinned in `MH1-MODULAR-PROTECTION-PROFILE.json`,
`MAH3-SHARED-RUNTIME-BASELINE-PROFILE.json` and `MAH4-MESSAGE-LIFECYCLE-BASELINE-PROFILE.json`.

**These are identity and inventory drift failures, not functional defects.** The product parses and
its focused behavioural suites pass.

### A wrong turn, recorded so it is not repeated

`refresh-mah3-stage-b-identity.mjs`, `refresh-mah4-baseline.mjs`,
`refresh-mah4-runtime-identities.mjs` and `refresh-module-golden-identities.mjs` were run. They

- did **not** fix any of the 19 failures;
- broke MAH-3 with `MAH-3 evidence matrix must match the exact 168-case contract`; and
- rewrote three historical canary evidence files dated 2026-08-07
  (`MAH3-DSR/PLANNING/QMS-CANARY-EVIDENCE`).

All of that was reverted with `git checkout`. The working tree contains only the three intended
changes. **Running those four scripts is not the regeneration path for a shared-asset change.**

Per `SCC-STAGE-2-FOUNDATION-SPEC-2026-08-16.md` §6.3 the real path is: regenerate
`module-manifest.js`, `module-build-golden-hashes.json`, MAH-3 and MAH-4 profiles; **bump the MAH-4
`www` byte total deliberately**; run the full offline suite and the audit self-test; re-anchor;
re-baseline. The byte-total bump is a deliberate manual step and was not attempted.

## Two owner gates ahead, neither self-approvable

Among the 12 `test:modular` failures:

- `modular capability delta ledger exactly matches the frozen A3 comparison`
- `capability review remains fail-closed until the owner explicitly approves it`
- `Phase 1 audit exit closes owned authority gates and records exact capability review`

The 107-delta capability approval is bound to the pre-import identity. A **new owner capability
approval** is required, exactly as the 107-row approval was. Separately, ETP-4 step 4 needs a fresh
rendered-language matrix and an identity-bound visual approval, which the wording import invalidated
by changing product bytes.

## Resume point

Items 1 to 3 are **merged into this branch** from `agent/etp-l10n-identity-regen` (`8b2cecb`).
Full detail: `ETP-IDENTITY-REGEN-FINDINGS-2026-08-21.md`.

| # | Work | State |
|---|---|---|
| 1 | **MAH-4 regeneration** — profile refresh plus six stale inventory constants, including the deliberate byte-total bump | **DONE — 46/46** |
| 2 | **ETP module shared-runtime protections** — audit bridge, home-fab, back-script, mobile-boot, `.etp-tabs` responsive containment | **DONE** |
| 3 | `etp` entry added to `MH1-MODULAR-PROTECTION-PROFILE.json` | **DONE — MH1 8/8** |
| 4 | Capture 12 new `etp` visual cases; raise the MAH-3 contract 168 to 180 **with** the evidence and a fresh owner confirmation | OPEN — owner gate |
| 5 | Regenerate the capability delta; obtain a new owner capability approval | OPEN — owner gate |
| 6 | Rendered-language matrix and identity-bound visual approval | OPEN — owner gate |
| 7 | One seeded APK; physical ETP fixture acceptance | OPEN — needs a device |
| 8 | Final controlled audit or comparison; update the handoff | OPEN |

**Items 4 to 8 are the only work left, and 4, 5 and 6 are owner gates — no engineering remains
before them.**

Item 2 turned out to be wider than the reported single defect: ETP was missing four of six
shared-runtime canaries, the home affordance, and any responsive containment. Its storage writes
emitted no audit trail, and it had no back-to-home control. One deliberate deviation remains:
`st-v5-emp-assist-script` was **not** added, because nothing requires it and the `employees` stage
reads employee-master and customer storage keys, which has no place in a financial reports module.

### Suite state on this branch after the merge

| Suite | Result |
|---|---|
| `test:etp` | 155/155 |
| `test:language` | 10/10 |
| `test:manifest` | 8/8 |
| `test:mah4` | **46/46** |
| `test:mobile` | 6/6 |
| `test:mah3` | 15 pass, **4 fail** — owner gate |
| `test:modular` | 79 pass, **7 fail** — 4 MAH-3 + 3 capability, both owner gates |

`test:modular` improved from 74/12 to 79/7. Every remaining failure is an owner gate.

**Never raise the MAH-3 case count ahead of the captured evidence.** The profile records that gate
as satisfied by identity-bound evidence and owner confirmation; moving the number alone manufactures
a false green on an owner gate.

**Do not** build an APK, run the controlled audit, or claim any gate closure from the current red
state.
