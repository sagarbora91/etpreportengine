# Objects that stay in the migrations

These views, procedures and functions do **not** get a file in `database/objects`. `CREATE OR ALTER` on them would fail, or
it would break what depends on them. They stay where their migration created them. A change to one needs a new migration,
and the guard tests allow a migration numbered 0062 or later to create or alter only the names listed here.

The runner refuses an object file for any object that a CHECK or DEFAULT constraint, a computed column, a security policy or
a schema-bound object uses (`LiveObjectsSql` in `SchemaObjects.cs`). Triggers and table types are never objects here.

## Always excluded (by rule)

| Object | Why | Created in |
|---|---|---|
| `dbo.application_users_visible_row` | security-policy predicate of `application_users` (1.11.2 L1); schema-bound | 0059 |

Expected in the generated list below (confirm at the baseline export): the `diag` helper functions that CHECK constraints of
the `diag` tables use (0054, 0055: for example `diag.is_token`, `diag.is_check_id`), and `dbo.is_blank_text` (0044), which
CHECK constraints use (it is on the live database at 0048).

## Found by the export (generated)

`scripts/Export-EtpSchemaObjects.ps1` rewrites the section between the markers. Do not edit it by hand.

<!-- BEGIN GENERATED EXCLUSIONS -->
(not generated yet: the objects baseline is exported at integration)
<!-- END GENERATED EXCLUSIONS -->
