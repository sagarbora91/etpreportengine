# V6 ETP operational foundation — 2026-08-25

Status: **IMPLEMENTED FAIL-CLOSED — BUSINESS ACTIVATION NOT AUTHORIZED**

This is the first engineering increment after Gate 0. It supplies the shared authority and
portability boundary required before E3–E7 can become operational. It does not approve any Gate 0
source, activate HEMW, enable Service ETP, or enable an incentive/Payroll path.

## Implemented boundary

- exact domain/action/role/store grants for E3, E4, E6, E5 and E7;
- an active domain authority requires a source SHA-256 and explicit Owner approval identity;
- privileged actions require reauthentication no more than five minutes before the action;
- portable backups admit bounded human-action overlays only;
- raw verified facts, workbook rows and named PII fields are refused from overlays;
- restore is tamper-evident and fences every restored scope;
- a scope becomes readable only after exact verified generation and receipt rebind;
- the asset is ordered before every E3–E7 domain contract and byte-bound in MAH-2.

## Deliberate exclusions

- no browser persistence implementation is granted to an iframe;
- no native ETP fact-store access is added;
- no Gate 0 pending authority is inferred;
- no E3 screen or action wiring is activated yet;
- no monetary calculation or Payroll mutation is permitted.

## Verification

The focused suite is `npm run test:v6-foundation`. The full Phase 6H suite, Gate 0 freeze suite,
historical source reconstruction, MAH-2 manifest and API-23 compatibility checks must remain green.

The next engineering increment is E3 CRO reconciliation integration through this foundation. Its
live roles, correction window, close authority and lock authority remain blocked until the Gate 0
owner inputs are approved.
