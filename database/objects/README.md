# database/objects - views, procedures and functions (1.12)

From 1.12 every view, stored procedure and function of the `dbo` and `diag` schemas lives here, **one file per object**.
Setup applies a file again whenever its text changes. Tables, columns, constraints, indexes, triggers, table types, roles,
column DENYs and seed data stay in `database/migrations` (append-only and checksummed, as before).

The runner is `SchemaObjectRunner` (`src/Etp.Reporting.Infrastructure.SqlServer/SchemaObjects.cs`). `SqlServerDatabaseBootstrapper`
calls it right after the migrations, and it finds this folder next to `database/migrations`.

## File name

`<layer>-<schema>.<name>.sql`, for example `20-dbo.v_cash_deposits_sent.sql`.

| Layer | Holds |
|---|---|
| 10-19 | functions that read only tables |
| 20-29 | views that read only tables |
| 30-39 | views or functions that read other views or functions (31, 32 ... for deeper chains) |
| 40-49 | procedures |

Objects apply in file-name order, so layer first, then name. An object must come after every view or function it reads. An
integration test checks this on a built database (`sys.sql_expression_dependencies`). There is no manifest file.

## File body

```sql
-- Optional comments: why the object exists, which lane or decision. They are part of the definition SQL Server stores.
CREATE OR ALTER VIEW dbo.v_example AS
SELECT ...;
-- >>> GRANTS
GRANT SELECT ON dbo.v_example TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_example TO etp_store_manager,etp_viewer;
```

- Above the `-- >>> GRANTS` line is exactly one `CREATE OR ALTER VIEW|PROCEDURE|FUNCTION <schema>.<name>`, with the same name as
  the file. `CREATE OR ALTER` keeps the object's existing permissions. Comment lines above it are allowed, and they are stored
  as part of the definition. A line holding only `GO` splits batches. Normally a file has no `GO`.
- Below the line, every non-comment line is one complete `GRANT`, `DENY` or `REVOKE` statement on **this** object (the column
  form `ON dbo.x (col)` is allowed). Each statement must be idempotent. Nothing else is allowed there. The block runs after
  every apply. An adopt does not run it.
- The `GRANT`/`DENY` rows must equal the object's rows in `ExpectedRolePermissions.json`, in both directions (guard test). The
  integrator regenerates the JSON at each gate.
- Never put a `CREATE`/`ALTER` of a view, procedure or function in a migration numbered 0062 or later. A migration must not
  call a procedure that has a file here either: objects apply after the migrations (guard tests).

## What the runner does

For each file, in order:

1. **Skip** when `dbo.schema_objects` has this file's hash and the live `OBJECT_DEFINITION` still has the recorded hash.
2. **Adopt** (record only, run nothing) when the live definition already equals the file. Only `CREATE` vs `CREATE OR ALTER`,
   `PROC` vs `PROCEDURE`, line endings and leading or trailing white space are ignored. This is how a fresh database (built
   by 0001-0061) and every upgraded database take the files over without changing an object.
3. **Apply** otherwise. The batches run, then the GRANTS block. Every non-schema-bound view that reads a changed view or
   function is refreshed (`sp_refreshview`).

All changes of one run share **one transaction**, together with their journal rows (`dbo.schema_objects`) and history rows
(`dbo.schema_object_history`, append-only). If anything fails, every object is rolled back and setup stops with "Database
objects are not up to date; run setup again." The migrations that already ran stay applied.

Health check **DB-07** "Database objects match this release" (Warning) reports objects changed by hand (their definition
hash drifted), missing or pending objects, and journal rows with no file. DB-02 also counts the object files.

## Retiring an object

Delete its file and add a comment-only stub `retired/<schema>.<name>.sql` that says why and when. Setup drops the object if
it is still there, by its live type. It refuses when a view or function still reads it. Never keep a file and a stub for
the same name.

## What stays out

See `EXCLUDED.md`. These stay out: objects that a CHECK or DEFAULT constraint, a computed column, a security policy or a
schema-bound object uses (for example 0059's `application_users_visible_row`), plus triggers and table types. The runner
refuses a file for such an object.

## The baseline (integration step)

`scripts/Export-EtpSchemaObjects.ps1` is read-only. The integrator runs it once against a fresh scratch database migrated
by the test fixture, never live (see the script's help), and commits the files as "objects baseline". It never overwrites
a file that already exists, so lane files such as D2's collections objects stay as written.

## Release check

`Etp.Reporting.Desktop.exe --plan-database-changes` prints `{"pendingMigrations":[...],"pendingObjects":[...]}`. Setup
takes the verified pre-migration backup when either list is non-empty. After the bootstrap it requires both lists to be
empty.
