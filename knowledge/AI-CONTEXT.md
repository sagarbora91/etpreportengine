---
type: ai-context
status: active
last_verified: 2026-08-28
---

# ETP AI Context

## Project identity

- This repository is **ETP Reporting Engine**, remote `https://github.com/sagarbora91/etpreportengine.git`.
- Its historical folder name is `SaagarCC-V6-ETP-Source-and-Report-Engine-605002f`; do not infer project identity from the `SaagarCC` prefix or retained legacy Android material.
- The user's ETP Obsidian vault is `C:\Codex\Reporting Manger\ETP`. Its `Project Knowledge` folder is a reading copy; repository knowledge remains authoritative.
- **Saagar Control Centre** is a separate project under `C:\Codex\Saagar Control Centre`. Do not mix its files, vault, graph or work status with ETP. Verify repository path and remote before changes or publishing.

## Purpose

ETP Reporting Engine is a rules-driven Windows retail reporting application. It imports changing ETP XLSX/ZIP exports, validates and normalizes them, stores auditable facts in SQL Server Express, and produces deterministic on-screen, Excel and PDF reports.

## Essential boundaries

- The active product is the .NET 10/WPF solution in `src/`, `tests-dotnet/`, `database/`, `installer/` and `scripts/`.
- The JavaScript/Capacitor material in `www/` and `tests/` is legacy/reference evidence unless a task explicitly targets it.
- Financial and reporting calculations must remain deterministic and auditable.
- `R025`/SDB-VariantwiseSales is the canonical item-level sales source. `NETVALUE` is the primary GST-inclusive sales value.
- `R022`/Revenue Report is the invoice/tender control source.
- `INV` means completed invoice. `SR` means sales return; ETP already supplies negative quantity and value, so preserve its sign.
- `CLUSTER` is brand segment, not product category. Example: `GAUTO` means Titan Automatic.
- Customer names/contact numbers are restricted PII and are excluded from canonical reporting facts.
- Missing data is not zero. Show or propagate an unavailable/blocking state unless an approved rule says otherwise.
- Desktop feature work starts at `WorkspaceModuleOwnershipRegistry` and the owning `Modules/` workspace; `MainWindow` is a shell host and `DesktopCompositionRoot` is the construction boundary.

## Source-of-truth order

1. Current implementation: source code, database migrations and executable configuration.
2. Code relationships: Graphify discovery, verified against source.
3. Business intent: approved requirements and business-rule notes.
4. Architecture decisions: accepted ADRs unless superseded.
5. Work status: Git history, issues and release evidence; do not duplicate task tracking here.

If intent and implementation differ, report both; do not silently choose one.

## Retrieval workflow

1. Classify the task with [[AI-ROUTER]].
2. Read only the 1–5 notes identified for that domain.
3. Query Graphify for affected implementation and relationships.
4. Inspect the returned source and relevant tests.
5. Make and verify the smallest appropriate change.
6. Update knowledge only when architecture, rules, mappings, report definitions, contracts or major limitations change.

Start at [[ETP Knowledge Home]].
