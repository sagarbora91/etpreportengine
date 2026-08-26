# ETP capability delta — for owner approval

**Prepared:** 2026-08-21 (Asia/Kolkata) · Phase 5 item **5B-2**
**Status:** awaiting owner decision. Nothing here is approved.

This is the analogue of the 107-row capability approval, for the twelfth module. The capability
inventory moved **660 → 680**; of the difference, **17 deltas are unclassified** and need a decision.

## How this was produced

`scripts/analyze-modular-capability-delta.mjs` is fail-closed: it throws on the first delta it cannot
classify. A copy patched **only to collect instead of throw** was run once from the repo and deleted;
the committed analyzer is untouched. No classification rule was added, because inventing one would
defeat the gate this document exists to satisfy.

## The 17 deltas

### A. Genuinely new ETP capabilities — 14, these are the decision

| Capability ID | What it is |
|---|---|
| `etp:action:etpimportform:1953c5a58d` | the four-file import form |
| `etp:action:etpvalidate:b07d709f31` | validate the selected report set |
| `etp:action:etpconfirm:4d548862d9` | **confirm and publish a generation** |
| `etp:action:etpcoverageconfirmed:696a80d1fc` | owner confirms four-report coverage |
| `etp:action:etphistoryrefresh:04874c0f68` | refresh bounded scope history |
| `etp:action:tab-import:072653f2e1` | Import tab |
| `etp:action:tab-coverage:3ecf856d2c` | Coverage tab |
| `etp:action:tab-reconciliation:2b8b192693` | Reconciliation tab |
| `etp:action:tab-verified:0711690845` | Verified Reports tab |
| `etp:action:refresh-verified-views:df04d92f87` | refresh verified paged reads |
| `etp:action:refresh-exceptions:47fdf2b89f` | refresh R003/R013 exception counts |
| `etp:action:loading-published-scopes:10e05bdcbf` | published-scope loading control |
| `etp:action:loading-published-scopes:f833acd821` | second binding of the same control |
| `etp:route:entry` | the module's route entry |

**`etpconfirm` is the one to read twice.** It is the control that publishes a financial generation.
Approving this delta approves that capability existing in the shipped product.

The two `loading-published-scopes` rows share a name but carry different binding hashes — worth
confirming that is intended and not a duplicated control.

### B. Shell route swap — 2, a consequence of ETP-1

| Capability ID | Change |
|---|---|
| `shell:action:open-etp-import:8cb1d3b022` | **removed** — the old direct "Open ETP import" button |
| `shell:action:open-retail-etp:60a39c8e5d` | **added** — the Reports-owned route |

This is the intended relocation: ETP moved from a shell button to a Reports module route. The old
bypass being gone is the point.

### C. Structural — 1, mechanical

| Capability ID | Note |
|---|---|
| `etp:action:st-v5-home-fab:c05eb3ddb3` | the back-to-home control added when ETP's missing shared-runtime protections were restored |

The hash `c05eb3ddb3` is **identical** to the home-FAB already listed in
`EXPECTED_STRUCTURAL_ACTION_IDS` for `cro_audit` and `dsr`, so this is the same stable binding, not a
new capability. It belongs in that allowlist rather than in an owner decision.

## What approval unlocks

Three `test:modular` failures, and with them the last engineering blocker in Phase 5 stage 5B:

- `modular capability delta ledger exactly matches the frozen A3 comparison`
- `capability review remains fail-closed until the owner explicitly approves it`
- `Phase 1 audit exit closes owned authority gates and records exact capability review`

## What still has to happen after approval

1. Add `etp:action:st-v5-home-fab:c05eb3ddb3` to `EXPECTED_STRUCTURAL_ACTION_IDS`.
2. Add classification rules for the approved section-A capabilities, citing this approval.
3. Move the analyzer's expected inventory total 660 → 680.
4. Regenerate the ledger with `--write`.
5. Re-run `test:modular` to green.

Steps 1–5 are mechanical once the decision exists. **They must not be done first** — the constants
are what the gate is made of.

## Decision

```text
I, Sagar (sagarbora91), reviewed the 17 capability deltas enumerated in
verification/audit/ETP-CAPABILITY-DELTA-FOR-APPROVAL-2026-08-21.md, covering the
capability inventory moving from 660 to 680 when Retail ETP became the twelfth
modular module. I confirm the 14 new ETP capabilities in section A are intended
and belong in the shipped product, including etp:action:etpconfirm which
publishes a generation; that the section B shell route swap is the intended
relocation of ETP from a shell button to a Reports-owned route; and that the
section C home-FAB binding is structural.
Decision: APPROVE
```

If any row is not intended, list it by capability ID instead of approving; it stays unclassified and
the gate stays closed.
