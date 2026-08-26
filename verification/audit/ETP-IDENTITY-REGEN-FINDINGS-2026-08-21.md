# ETP identity regeneration — findings

**Branch:** `agent/etp-l10n-identity-regen`, from `b5538d2`.
**Purpose:** attempt ETP-4 step 3 (identity regeneration) off the shared branch.

## Correction to the previous checkpoint

`ETP-L10N-IMPORT-CHECKPOINT-2026-08-21.md` implied the 19 red identity assertions followed the
localization import. **That attribution was wrong.** Measured at `099d6a4`, the commit immediately
before the import, using a clean worktree:

| Suite | At `099d6a4` (pre-import) | After import |
|---|---|---|
| `test:modular` | 74 pass, **12 fail** | 74 pass, 12 fail |
| `test:mah3` | 3 pass, **4 fail** | 15 pass, 4 fail |
| `test:mah4` | 5 pass, **3 fail** | 43 pass, 3 fail |
| `tests/mh1-modular-inventory` | 2 pass, 0 fail | — |

**The localization import added zero failures.** Every one of them was already red, introduced by
the ETP-1..ETP-3 work, which took `www/` from 100 to 103 files at `b0904bc` without updating the
identity profiles and inventory constants.

The ETP module checkpoint reported "Final focused ETP suite: 155/155 pass. Final focused manifest
suite: 17/17 pass." Both true, but neither suite covers MAH-3, MAH-4, MH1 or the capability ledger,
which is why the drift went unrecorded.

## MAH-4 — fixed, now 46/46

Root cause: hard-coded inventory constants that ETP-1's three new files invalidated.

1. `refresh-mah4-baseline.mjs` regenerated `MAH4-MESSAGE-LIFECYCLE-BASELINE-PROFILE.json`
   (moduleCount 11 to 12, etp message types added, tree hash, byte total). Verified the regenerated
   profile is deep-equal to `createMah4Profile(root)` — 0 differences.
2. Updated stale constants in `tests/mah4-message-lifecycle-baseline.test.mjs`:

| Constant | Was | Now | Why |
|---|---:|---:|---|
| `currentWwwFileCount` | 100 | 103 | ETP-1 added 3 files |
| `currentWwwTotalBytes` | 7,972,494 | 8,064,569 | the deliberate byte-total bump of spec 6.3 |
| `directEntryScriptAssetCount` | 57 | 58 | etp module gateway script |
| `uniqueDirectAssetTotals.eventListeners` | 41 | 42 | etp module |
| qualified bucket `eventListeners` | 47 | 57 | etp module |
| aggregate lifecycle totals | 141/33/274/11 | 151/36/305/12 | etp module |

All are inventory counters describing the tree, not behavioural assertions.

## MAH-3 — NOT fixed, and deliberately not forced

`scripts/mah3-visual-review-server.mjs:100` requires `minimumVisualCases === 168`, where
168 = 14 surfaces x 3 languages x 4 viewports. The manifest now carries 12 modules, so the contract
becomes 15 x 3 x 4 = **180**.

**This must not be fixed by editing constants.** The profile records the gate as
`"satisfied by identity-bound 168-case evidence and owner confirmation"`. Raising the number to 180
without capturing the 12 new `etp` cases would assert owner-confirmed visual evidence that does not
exist — a false green on an owner gate.

Closing MAH-3 properly requires capturing 12 new visual cases for the `etp` surface and obtaining a
fresh owner confirmation. That is ETP-4 step 4 and it is an owner gate.

## MH1 — one drift, one real defect

**Drift:** `MH1-MODULAR-PROTECTION-PROFILE.json` still lists 11 modules; the manifest has 12. The
profile needs an `etp` entry. No generator script exists for this file, so it is hand-maintained.

**Defect:** `MH1 every module keeps offline responsive, language, and shell-bridge protection` fails
with `etp must contain one st-v5-module-audit-bridge, actual: 0, expected: 1`. Verified directly:

| Modules with `st-v5-module-audit-bridge` | Count |
|---|---|
| cro_audit, dsr, expense, grooming, leave, payroll, planning, qms, service, stock, tax | 1 each |
| **etp** | **0** |

**Eleven of twelve modules carry this bridge and the ETP module does not.** This is an
implementation gap in ETP-1, not identity drift, and it is not fixable by regenerating anything. The
ETP module is missing a protection every other module has.

This is exactly the case the ETP checkpoint's do-not-repeat boundary allows reopening for: "Do not
modify or retest ETP phases 1-3 unless a focused test exposes a defect." A focused test has exposed
one. It was left for the owning workstream rather than patched here, because wiring a bridge into
their module is their design call.

## Capability ledger — owner gate

Three `test:modular` failures remain:

- `modular capability delta ledger exactly matches the frozen A3 comparison`
- `capability review remains fail-closed until the owner explicitly approves it`
- `Phase 1 audit exit closes owned authority gates and records exact capability review`

The 107-delta capability approval is bound to the pre-ETP identity. A new owner capability approval
is required for the regenerated delta, exactly as the 107-row approval was. Not self-approvable.

## State of this branch

| Suite | Result |
|---|---|
| `test:etp` | 155/155 pass |
| `test:language` | 10/10 pass |
| `test:manifest` | 8/8 pass |
| `test:mah4` | **46/46 pass** (was 43/3) |
| `test:mah3` | 15 pass, 4 fail — needs visual evidence capture and owner confirmation |
| `test:modular` | 12 fail — 4 MAH-3, 2 MH1 (1 drift + 1 defect), 3 capability, 3 MAH-4 now fixed |

## Recommended order for whoever finishes this

1. **Fix the missing `st-v5-module-audit-bridge` in `www/modules/etp/index.html`.** Product defect,
   blocks MH1, and should be settled before any evidence is captured against the module.
2. Add the `etp` entry to `MH1-MODULAR-PROTECTION-PROFILE.json`.
3. Capture the 12 new `etp` visual cases; raise the MAH-3 contract to 180 **together with** the
   evidence and a fresh owner confirmation, never ahead of it.
4. Regenerate the capability delta and obtain a new owner capability approval.
5. Only then: rendered-language matrix, seeded APK, physical acceptance, controlled audit.

---

# Update — ETP module protection gap fixed

The `st-v5-module-audit-bridge` defect turned out to be one symptom of a wider gap. Measured across
all twelve modules, ETP was missing **four of six** shared-runtime canaries, plus the home affordance
and any responsive containment.

## What was wrong

| Requirement | Eleven other modules | ETP before |
|---|---|---|
| `st-v5-iframe-shim`, `st-v5-safety-net` (head) | present | present |
| `st-v5-mobile-boot` (head) | present | **missing** |
| `st-v5-back-script` (body) | present | **missing** |
| `st-v5-emp-assist-script` (body) | present | missing |
| `st-v5-module-audit-bridge` (body) | present | **missing** |
| `st-v5-home-fab` back-to-home button | present | **missing** |
| a selector in `www/mobile-layout.css` | present | **none at all** |

Two of these are user- or compliance-visible, not cosmetic:

- **`st-v5-module-audit-bridge`** wraps `localStorage.setItem` and `removeItem` and emits
  `SaagarMah4.audit(action, key, before, after)`. Without it, ETP's storage writes produced **no
  audit trail** — in the module that handles financial reconciliation and publication.
- **`st-v5-home-fab` with `st-v5-back-script`** is the back-to-home control and the Escape / Ctrl+H
  handler. ETP had no way back to home other than shell chrome.

## What was changed

`www/modules/etp/index.html`

- `st-v5-mobile-boot` added in `<head>` directly after `st-v5-safety-net`, matching all eleven.
- `st-v5-back-style` link, `st-v5-home-fab` button, `st-v5-next-chips` div and `st-v5-back-script`
  added in `<body>`, then `st-v5-module-audit-bridge`, in the canonical order.
- Config copied verbatim from ETP's existing two canaries:
  `{schemaVersion:1,moduleId:'etp',nextSteps:[],customerSelectors:[],accessContext:false}`.

`www/mobile-layout.css`

- `.etp-tabs` added to the three tab-rail selector groups beside `.q-tabs`, giving ETP the same
  horizontal scroll rail every other module's tab nav has.

`verification/MH1-MODULAR-PROTECTION-PROFILE.json`

- Twelfth entry: `etp`, `risk: "high"`, `responsiveSelectors: [".etp-tabs"]`,
  `reviewStates: ["import", "coverage", "reconciliation", "verified"]`.

`www/module-manifest.js`, `verification/module-build-golden-hashes.json`,
`MAH4-MESSAGE-LIFECYCLE-BASELINE-PROFILE.json` and the MAH-4 / MH1 test constants were re-synced to
the changed bytes.

## Two deliberate judgement calls

**`st-v5-emp-assist-script` was NOT added.** It is not in `commonModuleMarkers`, nothing requires it,
and the `employees` stage reads employee-master and customer localStorage keys. That surface has no
place in a financial reports module. This is the one respect in which ETP still differs from the
other eleven, and it is intentional. Overrule it if uniformity is preferred.

**ETP is recorded as `risk: "high"`, not `medium`.** The test hard-coded the high-risk list without
`etp`, so marking it `medium` would have made the suite pass with no edit. That would misstate the
risk of the module that performs financial reconciliation and publication, so the test constant was
updated instead.

## Suite state on this branch

| Suite | Before | Now |
|---|---|---|
| `test:etp` | 155/155 | **155/155** |
| `test:language` | 10/10 | **10/10** |
| `test:manifest` | 8/8 | **8/8** |
| `test:mah4` | 43 pass, 3 fail | **46/46** |
| `test:mah3` | 15 pass, 4 fail | 15 pass, 4 fail |
| `test:modular` | 74 pass, 12 fail | **79 pass, 7 fail** |
| `test:mobile` / `test:settings` / `test:security` | — | 6/6, 8/8, 100/100 |

**All seven remaining failures are owner gates**, not engineering:

- 4 x MAH-3 — needs 12 new `etp` visual cases captured plus fresh owner confirmation. The count must
  never be raised ahead of the evidence.
- 3 x capability ledger — needs a new owner capability approval for the regenerated delta.

---

# Update — MAH-3 matrix expanded to 180; capture attempted and BLOCKED

## Blocker: the capture harness cannot complete a case on the current shell

The 12 `etp` cases were **not captured**. The MAH-3 review harness loads a case by booting the shell
in an iframe and calling `shellWindow.openModule(surface)`. On the current product the shell stops at
the **role gate** with no session:

```
role: null            roleGateVisible: true
moduleFrameSrc: "modules/etp/index.html"   moduleFrameVisible: true
```

The ETP module itself renders correctly inside the frame ("Retail ETP Reports" is visible), but one
of the harness readiness checks is `blocking-shell-overlays`, and the role gate is exactly that, so
readiness never passes and geometry never runs. `Re-run geometry` stays on `Not run`.

**This is not specific to `etp`.** The harness predates the role-switch gate added in V6 Wave 6, so
**no MAH-3 case can be captured on the current shell** until the harness can establish a session.
That affects any future visual capture, not just the twelfth module.

Geometry is advisory in any case; the gate is a human visual review, which cannot be automated.

## What was done, and why the matrix moved anyway

The contract was raised 168 to 180 **together with** honest bookkeeping, never ahead of it:

| Change | File |
|---|---|
| `etp` added to the surface list; `minimumVisualCases` 168 to 180 | `MAH3-SHARED-RUNTIME-BASELINE-PROFILE.json`, `MH1-MODULAR-PROTECTION-PROFILE.json` |
| contract constant and message 168 to 180 | `scripts/mah3-visual-review-server.mjs` |
| matrix guard 168 to 180 | `verification/mah3-visual-review/review-controller.js` |
| 12 `etp` case rows added, `manualStatus: "not-run"`, `geometry: null` | `MAH3-VISUAL-REVIEW-EVIDENCE-2026-08-07.json` |
| case total and gate 168 to 180 | `scripts/lib/mah4-contract-source.mjs` |

## Every "captured" claim now reports the truth

Raising the matrix while 12 cases are uncaptured briefly made the repository assert a complete
baseline that did not exist. That was corrected in the same change:

| Claim | Was | Now |
|---|---|---|
| `review.visualBaselinesCaptured` | `true` | **`false`** |
| `baseline.visualBaselinesCaptured` | `true` | **`false`** |
| `review.reviewStatus` | `complete-passed` | **`incomplete-pending-etp-capture`** |
| `review.runtimeRefactorGate` | "satisfied by identity-bound 168-case evidence" | **states the gate is NOT satisfied and names what is missing** |
| evidence `summary.captureComplete` | `true` | **`false`** |
| evidence `summary.visualBaselinePassed` | `true` | **`false`** |
| MAH-4 `gates.refactorGateReady` | `true` | **`false`** |
| MAH-4 `gates.mah3RenderedCasesReviewed` | 168 | **0** |

`review.pendingCaseIds` lists the 12 by id. The 168 pre-existing cases keep their original
2026-08-07 review by "Codex / engineering review"; that attribution was not touched, and no reviewer
identity was written for the new cases.

## Suite state

| Suite | Result |
|---|---|
| `test:mah3` | **19/19** |
| `test:mah4` | **46/46** |
| `test:modular` | **83 pass, 3 fail** |
| `test:etp` / `test:manifest` / `test:language` | 155/155, 8/8, 10/10 |

`test:modular` improved 74/12 to 83/3. **The only remaining failures are the three capability-ledger
checks, which need a new owner capability approval.**

## To actually capture the 12

1. Give the harness a way past the role gate — a seeded session on the loopback origin, or a
   harness-only bypass that is not shipped.
2. Run `npm run review:mah3`, open `http://127.0.0.1:8766/`, select cases 169 to 180.
3. A **fluent reviewer** marks each Pass or Defect with an evidence reference.
4. Flip `visualBaselinesCaptured` back to `true` only when all 180 are reviewed, and refresh the
   dependent gates and constants.
