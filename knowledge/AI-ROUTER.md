---
type: ai-router
status: active
last_verified: 2026-08-27
---

# AI Router

Always read [[AI-CONTEXT]] first. Then choose one route; do not load the entire vault.

| Task domain | Read next | Suggested Graphify question | Verify in |
|---|---|---|---|
| Import failure or new layout | [[Import Architecture]], [[Mapping Knowledge]] | Where is this report profile, preflight, normalization or orchestrator implemented? | `src/Etp.Reporting.Import`, SQL import orchestrators, import/SQL tests |
| Mapping or terminology change | [[Mapping Knowledge]], [[Data Dictionary]], [[Business Rules Register]] | Which profiles, projections and reports use this field? | Profile/config source, migrations, affected tests |
| Report/formula/export | [[Report Catalog]], [[Business Rules Register]] | Where is this report defined, calculated, rendered and tested? | Reporting project, SQL query repository, Desktop, reporting tests |
| DSR or daily workflow | [[Report Catalog]], [[Business Rules Register]], [[System Architecture]] | How does DailySalesReport flow from SQL query to WPF and PDF? | DSR document/exporter/workspace and focused tests |
| Database or migration | [[Data Architecture]], relevant ADR | Which repositories and reports depend on this entity or migration? | `database/migrations`, SQL infrastructure/tests |
| UI/navigation/help | Specific report definition if applicable | Which WPF view, workspace or navigation class owns this screen? | Desktop project/tests and UI smoke tool |
| Operations, backup or installer | [[System Architecture]], [[ADR-001 - SQL Server Express]] | Which bootstrap, health, backup and installer components are involved? | `installer/`, `scripts/`, SQL operational services/tests |
| Architecture change | [[System Architecture]], [[Data Architecture]], [[Decision Register]] | What components and dependency paths are affected? | Source, migrations, tests; create/supersede ADR if accepted |
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
