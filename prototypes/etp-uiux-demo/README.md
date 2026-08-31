# ETP UI and workflow prototype

This is an interactive design prototype for the ETP Reporting Engine. It demonstrates the proposed task-first navigation, all authoritative screen destinations, all 29 reports, role behavior, shared screen states and full function coverage using safe dummy data.

It does not connect to SQL Server, modify production data, run imports or generate real business reports.

## Review paths

- `#dashboard` — task-first Today workspace
- `#report-dsr` — representative report with filters, dummy data and lineage drawer
- `#manual-entry` — governed entry form and audit preview
- `#import-overview` — import workflow
- `#accounting-overview` — accounting workflow
- `#open-items` — exceptions and approvals
- `#prototype-coverage` — 244-function mapping and deferred-function review

Use the top-right role and state selectors to preview Viewer, Store Manager and Owner behavior plus Ready, Loading, Empty, Error and Locked states.

## Validation status

- Production build: passing
- Active function mapping: 244 / 244
- Production reports represented: 29 / 29
- Deferred functions represented: 7 / 7
- Responsive checks: 1366×768 and 960×600
- Browser interaction checks: navigation, report detail drawer, role restriction, locked reason and state switching
