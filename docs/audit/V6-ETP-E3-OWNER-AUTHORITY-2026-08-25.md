# V6 ETP E3 owner authority — 2026-08-25

Status: **OWNER CONFIRMED**

Confirmed by Sagar in the programme task on 2026-08-25. This authority applies to E3 CRO
reconciliation only and does not approve E4, E6, E5, E7, HEMW profile production or release.

## Controlled decisions

- Staff, Store Manager and Owner may declare invoices while the day is open.
- Store Manager or Owner may check, close and import a day.
- Store Manager or Owner may correct attribution during the first 24 hours after verified import.
- After 24 hours and before lock, only Owner may correct attribution.
- Owner may correct at any time before lock, with an audited reason.
- Store Manager or Owner may dispose a variance and lock the day.
- A locked day is immutable for every role, including Owner.
- A locked-period change requires a verified source restatement and a new reconciliation cycle.

## Security interpretation

The role selector is not authority. Operational calls must pass the shared ETP authority boundary;
privileged actions require fresh reauthentication. All corrections and dispositions preserve actor,
role, time, reason and before/after state. Declarations never become verified facts or a payment
basis.
