# Reporting and import logic audit — 2026-09-11

Scope: bounded review of non-Desktop reporting controls, import retry/cancellation, source sign handling, SQL persistence and access contracts. This is not evidence of complete live SQL or Windows workflow acceptance. Shared builds and tests are run by the coordinating agent; results belong in the integrated acceptance evidence.

## Issue register

| ID | Severity | Function / reproduction | Expected / original actual | Cause and evidence | Fix / retest |
|---|---|---|---|---|---|
| LOGIC-001 | High | Tender reconciliation: run with empty invoice and tender collections, as an empty query scope can return. | Unavailable control evidence must block; original result was Passed with no documents and zero totals. | `InvoiceTenderReconciliationService.Reconcile` used `documents.All(...)` on an empty array. `Empty_invoice_and_tender_evidence_cannot_pass_control` defines regression. | Explicit Blocked result and explanatory message. Automated execution pending coordinated suite at time of writing. |
| LOGIC-002 | High | Stock reconciliation: run with no positions and no movements. | Missing opening/closing evidence must block; original result was Passed with no items. | `StockReconciliationService.Reconcile` used `items.All(...)` on an empty array. `Empty_stock_position_evidence_cannot_pass_control` defines regression. | Explicit Blocked result. Real positions with no movements still reconcile. Automated execution pending coordinated suite. |
| LOGIC-003 | Medium | Batch import: first processor call cancels the supplied token and throws a transient IOException. | No subsequent processor call; original coordinator invoked processor again before observing cancellation itself. | Retry loop lacked its own cancellation boundary and depended on every processor observing the token. `CoordinatorDoesNotStartRetryAfterCancellationDuringTransientFailure` asserts one call, two cancelled files and attempts 1/0. | Check token before each invocation, including retries; count only invoked attempts. Automated execution pending coordinated suite. |

These reproduction outcomes before the fix follow directly from the inspected code and regression inputs; no separate old-binary runtime reproduction was performed in this bounded review.

## Independently defined expected values and neighbouring checks

- Sale quantity 2/value 200 plus source-signed return quantity -1/value -75 gives quantity 1/value 125. Existing `Sales_summaries_preserve_source_signs` covers five dimensions; return-only output remains -1/-75.
- Invoice 100 and return -20 matched to card 60 + cash 40 and refund -20 gives invoice/tender totals 80. Existing reconciliation regression retains these independent expectations.
- A supplied zero invoice and supplied zero tender is valid evidence and still passes. Added explicit-zero regression.
- Stock opening 10 with receipt +5 and issue -3 gives closing 12. Existing signed movement test retained. Opening/closing 10 with no movements still passes; added neighbour regression.
- Cash formula inspected: opening + retail + service - expense - deposit + adjustment. Missing nullable manual inputs produce null calculation/variance and Blocked.

## Coverage classification

| Area | Review status | Evidence / limits |
|---|---|---|
| Tender/stock empty controls | Static inspection + new executable regressions | Live SQL empty-scope/UI retest requires integrated workflow run. |
| Sales/returns | Source and existing regression inspection | No sign transformation; approved NETVALUE mapping retains missing-amount blocking. |
| Import cancellation/retry | Source + new executable regression | Existing transient success and initial cancellation tests retained; shared runner executes. |
| R025/R022 persistence | Static inspection | Staged values preserve signs; scope guard verifies selected store/report date; transaction rollback uses uncancelled token to attempt cleanup. No new SQL mutation performed. |
| Repeated import handling | Static inspection | SQL hash lookup and row outcomes exist; live overlap/restatement/concurrent import acceptance not established here. |
| Permission contracts | Static inspection | Known roles define effective capabilities; SQL role adapter defaults unknown infrastructure roles to None; connection tests reject SQL credentials. This does not prove live restricted-user journeys. |
| Other non-Desktop workflows | Not comprehensively reviewed by this subtask | Accounting/registers/archive/backup administration require integrated owner coverage. |

## Limitation requiring care

The R022 projection deliberately omits zero and blank tenders (`R022PersistenceProjectionTests.Omits_zero_and_blank_tenders_without_changing_invoice_control`). Tender diagnostics also depend on unmatched nonzero documents to explain MissingTender/TenderWithoutInvoice. Therefore this change does not blanket-block unmatched keys or invent zero tender records. The persisted projection cannot distinguish an omitted explicit zero from absent tender evidence at that point. Any redesign requires retaining source-presence provenance and neighbouring import/diagnostic verification; it is not silently treated as an approved new business rule.

## Retrieval and verification notes

Read repository AGENTS, AI-CONTEXT, AI-ROUTER, Business Rules Register and Report Catalog. Graphify query located control and signed-return tests. Code-review-graph was queried before precise source inspection; its index reported a different HEAD and some absent tests, so results were treated as navigation only. The coordinator owns one final graph refresh to avoid competing shared-index writes.

`git diff --check` completed without whitespace errors. No builds/tests were started by this subtask to preserve sequential shared build ownership. Coordinating commands: `dotnet test tests-dotnet/Etp.Reporting.Reporting.Tests` and `dotnet test tests-dotnet/Etp.Reporting.Import.Tests`, or the integrated solution suite. Record actual results before claiming verification.
