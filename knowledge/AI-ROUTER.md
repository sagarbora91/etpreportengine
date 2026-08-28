---
type: ai-router
status: active
last_verified: 2026-08-28
---

# AI Router

Always read [[AI-CONTEXT]] first. Then choose one route; do not load the entire vault.

| Task domain | Read next | Suggested Graphify question | Verify in |
|---|---|---|---|
| Import failure or new layout | [[Import Architecture]], [[Mapping Knowledge]] | Where is this report profile, preflight, normalization or orchestrator implemented? | `src/Etp.Reporting.Import`, SQL import orchestrators, import/SQL tests |
| Mapping or terminology change | [[Mapping Knowledge]], [[Data Dictionary]], [[Business Rules Register]] | Which profiles, projections and reports use this field? | Profile/config source, migrations, affected tests |
| Report/formula/export | [[Report Catalog]], [[Business Rules Register]], [[Desktop Architecture]] | Where is this report defined, calculated, presented, exported and tested? | Reporting model/exporters, report Application/SQL adapters, `Modules/Reports`, focused tests |
| DSR or daily workflow | [[Report Catalog]], [[Business Rules Register]], [[Desktop Architecture]] | How does DailySalesReport flow from SQL query through its workspace to the production PDF? | Daily-workflow/report contracts, DSR document/exporter, `Modules/DailyWorkflow`/`Modules/Reports`, focused tests |
| Database or migration | [[Data Architecture]], relevant ADR | Which repositories and reports depend on this entity or migration? | `database/migrations`, SQL infrastructure/tests |
| UI/navigation/help | [[Desktop Architecture]], specific report definition if applicable | Which ownership-registry entry and extracted workspace own this screen? | `WorkspaceModuleOwnershipRegistry`, owning `Modules/` folder, Desktop tests and UI smoke |
| Operations, backup or installer | [[System Architecture]], [[ADR-001 - SQL Server Express]] | Which bootstrap, health, backup and installer components are involved? | `installer/`, `scripts/`, SQL operational services/tests |
| Architecture change | [[Desktop Architecture]], [[System Architecture]], [[Data Architecture]], [[Decision Register]] | Which shell route, workspace, Application contract, adapter and dependency paths are affected? | Composition root, ownership registry, owning module/contracts/adapters/tests; create/supersede ADR if accepted |
| Bug investigation | Most specific note for the symptom | What calls the failing symbol and which tests cover it? | Actual failing path and nearest tests |

## Conflict format

When documentation and code disagree, state:

```text
Documentation expects: ...
Implementation currently does: ...
Potential issue: ...
Recommended resolution: ...
```

Do not update permanent knowledge for a trivial fix unless it reveals a durable rule, contract or limitation.
