---
type: adr
status: accepted-implementation-external-validation
date: 2026-08-29
last_verified: 2026-08-29
---

# ADR-008 - Health-Gated Database Upgrade

## Context

Checksum-controlled migrations protect script identity but do not by themselves protect an existing store database from installer, storage or post-migration failure. Pretending to reverse already committed migrations is unsafe.

## Decision

Before an existing database with pending bundled migrations is changed, require compatible SQL/database state, adequate backup space and a unique SQL `COPY_ONLY, CHECKSUM` backup verified with `RESTORE VERIFYONLY`. Publish and independently verify a receipt containing target identity, backup path, length and SHA-256.

After migration, require online/read-write state, exact bundled journal count and `DBCC CHECKDB` before setup continues to operational-task installation or success. On failure, stop and retain the database, logs, receipt and backup. Never automatically restore, reverse committed migrations or delete business data.

## Consequences

- Offline/manual deployment remains supported through preinstalled prerequisites.
- Restore is an explicit operator decision after diagnosis.
- Source implementation is not operational acceptance: compiled installer, live clean/upgrade/failure, backup/restore and preserved-data evidence remain external gates.

## Affected components

SQL migration runner, installer, prerequisite/bootstrap and backup scripts, installer lifecycle contracts/tests and operational acceptance evidence.
