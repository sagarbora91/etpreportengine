# V6 ETP completion wave 9 — governed E5 incentive and Payroll path

**Date:** 2026-08-25

**Engineering status:** complete

**Production activation:** blocked pending independently supplied Owner-approved scheme, CRO-to-Payroll mapping and policy

Wave 9 completes the money-last E5 engineering path without inventing an incentive scheme or Payroll authority. Three exact source packages are canonicalized and hashed before a separate Owner approval can activate them: the scheme and bands, the CRO-to-Payroll employee map, and the policy governing Unassigned sales, close timing and clawback periods.

Calculations accept only a complete verified ETP period bound to the exact store, scope, generation and receipt. Golden cases cover contiguous band edges, half-up rounding, approved Unassigned behavior, close-plus-days, idempotent finalization, next-open-period restatement clawbacks and the total identity. Declarations never form the payment basis.

The durable operational chain persists revision-checked provisional, final, clawback and Payroll attachment state. Every money mutation requires fresh reauthentication and exact authority/binding identity. Restart, optimistic concurrency, portable restore fencing, verified rebind and authority drift fail closed.

Payroll integration is one-way and bounded. E5 emits only provenance-bound, non-editable controlled earning lines; it exposes no generic Payroll mutation, manual recreation or declaration-based line. The mounted ETP screen deliberately omits the internal Payroll attachment capability and exposes only governed calculation, publication and clawback actions.

The responsive presentation shows honest `BLOCKED` and `READY` states, approved hash, verified binding, eligibility blockers, totals and clawbacks. Synthetic authorities and calculations are test fixtures only. Production remains blocked until the real Owner-approved scheme, employee mapping and policy are supplied and hash-bound.
