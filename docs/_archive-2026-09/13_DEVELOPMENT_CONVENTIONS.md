# Development Conventions

## Layer ownership

- Domain contains business concepts and policy contracts only.
- Import reads and maps source files but does not persist or calculate reports.
- Application coordinates use cases and transaction boundaries.
- SQL Server infrastructure implements persistence and query ports.
- Reporting defines fixed report contracts and result models.
- Desktop displays application results and never owns formulas or SQL.

References must point inward: Desktop/Infrastructure/Import/Reporting may depend on Application or Domain; Domain depends on no other project.

## Change gate

Every meaningful change must build with warnings treated as errors and include proportionate tests. Unknown business semantics must be added to `11_DECISION_LOG.md`, not guessed in code. Real ETP files, credentials, backups and customer data must never be committed.

## Agent handoff

Each workstream reports changed files, tests executed, assumptions avoided, blockers and any requested contract change. The Lead Integrator alone resolves cross-project contract conflicts during an active parallel wave.
