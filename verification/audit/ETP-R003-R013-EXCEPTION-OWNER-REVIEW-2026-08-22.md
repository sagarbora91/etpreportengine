# ETP R003/R013 exception presentation — owner review package

**Gate:** `GATE-ETP-EXCEPTIONS` / Phase 5D-2  
**State:** `AWAITING_OWNER_REVIEW` — this package does not close the gate  
**Product-code identity:** `519c3e1de8e0843f2fff13909084e00b1e2712ad`
**Review date:** 2026-08-22

## Exact review scope

This review is limited to the read-only R003 and R013 exception cards in the
Retail ETP **Reconciliation & Exceptions** view. It does not approve source
workbooks, revenue, production publication, PAYMENTTYPE25, UAT or release.

For each accepted published scope the view presents:

| Card | Exact heading | Exact value pattern | Intended meaning |
|---|---|---|---|
| R003 | `R003 enrichment exceptions` | `<PASS/FAIL> · <0–250,000> difference(s) · does not change revenue` | Differences affecting discount-line enrichment only |
| R013 | `R013 enrichment exceptions` | `<PASS/FAIL> · <0–250,000> difference(s) · does not change revenue` | Differences affecting CRO-attribution enrichment only |

The parent card also displays the accepted receipt's blocking reconciliation
status and rule version. A published receipt is valid only when blocking
reconciliation is `PASS`; an R003/R013 `FAIL` means an enrichment difference,
not failure or recalculation of the verified revenue result.

## Evidence supporting the review

1. **Bounded and validated counts.** `www/etp-core-contract.js` accepts only
   integer R003/R013 difference counts from 0 through 250,000 and only `PASS`
   or `FAIL`. Invalid receipt enrichments are rejected before the gateway can
   present them.
2. **Sanitized receipt only.** `www/etp-module-gateway.js` exposes the two
   status/count pairs from a core-validated receipt. It does not expose source
   workbook rows or unrestricted native facts to the module.
3. **Explicit non-revenue copy.** `www/etp-verified-presentation.js` renders
   `does not change revenue` on both cards. The focused legacy-contract check
   independently requires the fuller explanation `These checks do not change
   revenue or sales totals` and identifies the analyses as CRO attribution and
   discount analysis.
4. **Scope and refresh behavior.** The module uses the selected accepted
   published scope, provides a dedicated `Refresh exceptions` action and queues
   the latest scope when it changes during a refresh.
5. **No raw-row presentation.** Verified pages are limited to 200 rows per
   request and 20,000 rows/100 pages per refresh, aggregated incrementally and
   discarded. Exception cards themselves use receipt metadata only.

## Identity binding

| File | SHA-256 |
|---|---|
| `www/etp-verified-presentation.js` | `031fe0d969062cab8319d23111a4e83c92111b1bf1e90f53238c80666acb2cc6` |
| `www/etp-module-gateway.js` | `50105e28e3fc64ed13f36ac5c83cab15c39010f6cef544c4ec0f62a4e7ad62af` |
| `www/modules/etp/index.html` | `c3e0669a108a4575dc79d167626327fc2e55ac7bae1267d7ef9839e255c0b717` |
| `tests/etp-verified-presentation.test.mjs` | `f64323b3e07d8180625f176626ae14895be3f87296583e2fbd8181d56a0dd838` |
| `tests/etp-module-import-ui.test.mjs` | `785c41b9d93df6964c192b243bb752f2a7c659b428aca805d9787983a2fb143b` |
| `tests/etp-import-ui.test.mjs` | `e8195aa72c6b2ece20dc5f09097c02d0efce89c54a64138c93ce5103e775959d` |

## Focused verification

Command run at the identity above:

```text
node --test tests/etp-verified-presentation.test.mjs tests/etp-module-import-ui.test.mjs tests/etp-import-ui.test.mjs
```

Result: **24 passed, 0 failed**. No full audit, APK build, identity regeneration
or unrelated suite was run.

Automation proves the bounds and contract behavior. The owner must still decide
whether the headings, status/count presentation and non-revenue explanation are
clear enough for operational use.

## Exact owner approval requested

> I, Sagar (sagarbora91), reviewed the current Retail ETP R003 and R013 exception presentation bound to product-code identity 519c3e1de8e0843f2fff13909084e00b1e2712ad, etp-verified-presentation.js SHA-256 031fe0d969062cab8319d23111a4e83c92111b1bf1e90f53238c80666acb2cc6, etp-module-gateway.js SHA-256 50105e28e3fc64ed13f36ac5c83cab15c39010f6cef544c4ec0f62a4e7ad62af, and ETP module index SHA-256 c3e0669a108a4575dc79d167626327fc2e55ac7bae1267d7ef9839e255c0b717. I confirm that the R003 discount-enrichment and R013 CRO-attribution exception cards are intelligible, that their bounded status and difference counts are suitable for operational review, and that I understand these enrichment exceptions do not change verified revenue or sales totals. I approve closure of GATE-ETP-EXCEPTIONS for this exact identity. This approval does not approve source workbooks, PAYMENTTYPE25 classification, production publication, UAT, production signing or release.
