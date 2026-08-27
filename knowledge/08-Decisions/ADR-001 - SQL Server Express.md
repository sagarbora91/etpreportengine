---
type: adr
status: accepted
date: 2026-08-27
last_verified: 2026-08-27
---

# ADR-001 - SQL Server Express

## Context

The Windows engine requires durable, centrally queryable, multi-period facts, transactional imports, migrations, audit history, backup and recovery.

## Decision

Use local SQL Server Express as the authoritative reporting database after successful transactional import.

## Reason

It supports relational constraints, controlled migrations, transactional restatement, reporting queries, operational health and native Windows deployment.

## Alternatives considered

- Browser local storage/sql.js: retained only as legacy Android reference; unsuitable for the Windows reporting authority.
- Spreadsheet-only storage: rejected because it cannot provide the required audit, concurrency and deterministic history controls.

## Consequences

The installer/bootstrap must configure SQL safely; migrations are checksum-controlled; backups are retained without deleting business data; restore drills and health warnings are operational requirements.

## Affected components

`database/migrations`, SQL infrastructure, installer, backup/restore scripts and SQL integration tests.
