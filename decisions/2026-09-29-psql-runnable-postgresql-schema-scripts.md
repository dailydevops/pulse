---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.PostgreSql/Scripts/*.sql"

created: 2026-09-29

lastModified: 2026-09-29

state: accepted

instructions: |
  MUST keep every PostgreSQL provider script runnable unchanged with `psql -v ON_ERROR_STOP=1 -v schema_name=<schema> -v table_name=<table> -f <script>.sql` (psql 10 or later), and idempotent so re-running it is the upgrade path.
  MUST default schema_name and table_name with `\if :{?var} \else \set var <default> \endif`, never with a plain `\set` that would override `-v`.
  MUST reference identifiers in plain DDL as `:"schema_name"` / `:"table_name"`, and derived key/index names as `\set` variables named `PK_<schema>_<table>` / `IX_<schema>_<table>_<columns>`.
  MUST generate every statement whose table reference sits inside a `$$` body with `SELECT format($ddl$ ... %1$I.%2$I ... $ddl$, :'schema_name', :'table_name') \gexec`; a literal `%` inside such a body must be written as `%%`.
  MUST NOT write `":schema_name"` or other placeholders inside quoted identifiers or string literals; psql does not interpolate there.
  MUST run the scripts in integration tests through real psql (PostgreSqlContainerFixture.RunScriptAsync) instead of textual substitution.
---

# Decision: psql-Runnable PostgreSQL Schema Scripts

The `NetEvolve.Pulse.PostgreSql` schema scripts are psql scripts. They run unchanged through `psql -f`, take the schema and table names as psql variables, and are safe to re-run.

## Context

The scripts wrote `":schema_name".":table_name"` inside quoted identifiers and inside `$$` function bodies. psql does not interpolate variables in quoted identifiers or literals, so the documented `psql -f` run created an empty schema and a list of errors (#852). A plain `\set schema_name 'pulse'` also overrode any `-v schema_name=...`. The integration tests did not notice, because they removed the `\set` lines and replaced the placeholders with `string.Replace`. The outbox key and index names also left out the table name, so a second table with the same schema name collided on its key and index names.

## Decision

* The scripts target psql 10 or later and use `\if`, `\set` and `\gexec`.
* `schema_name` and `table_name` default through `\if :{?var}`, so `-v` wins.
* Plain DDL uses `:"schema_name"` and `:"table_name"`, which psql quotes as identifiers. Quoting also keeps a mixed-case schema name.
* Key and index names are built with `\set` concatenation as `PK_<schema>_<table>` and `IX_<schema>_<table>_<columns>`.
* A key or index name longer than 63 bytes is replaced by `PK_<md5(schema.table)>` or `IX_<md5(schema.table)>_<columns>`.
* Each function is created by `format(...)` with `%I` and executed by `\gexec`, because psql cannot reach into `$$` bodies.
* `OutboxMessage.sql` renames the `PK_<schema>` and `IX_<schema>_Status_*` names of earlier deployments before `CREATE INDEX IF NOT EXISTS`, so re-running it does not add duplicate indexes.
* Integration tests execute the scripts with the psql binary inside the PostgreSQL Testcontainer.

## Consequences

* The documented psql command works, including custom and mixed-case schema names.
* pgAdmin, DBeaver and ADO.NET runners cannot execute the scripts unchanged. The README no longer offers them.
* Function bodies sit inside `format()` literals, so a literal `%` in a body must be escaped as `%%`.
* PostgreSQL truncates identifiers to 63 bytes. Truncated index names could collide, and `CREATE INDEX IF NOT EXISTS` would skip the second index silently, so names that do not fit use the MD5 form. A deployment whose truncated index names were created by hand earlier keeps those indexes and gets the hashed ones added on re-run; drop the truncated ones manually.
* Functions are created per schema and bound to the last `table_name` the script ran with, so each outbox or idempotency table still needs its own schema. `AuditEntry.sql` and `CommandDeadLetter.sql` create no functions, so their tables can share a schema.

## Alternatives Considered

* **Placeholder templates with manual substitution**: keeps GUI clients usable, but users would edit every script by hand, and the scripts could never be tested unchanged.
* **Dynamic SQL inside the functions (`EXECUTE format(...)` at call time)**: moves the table name resolution to every call and changes the runtime behaviour and plan caching of the hot outbox functions.
* **A C# psql emulator in the tests**: less faithful than the real client that users run.

## Related Decisions (Optional)

* [Re-runnable MySQL Schema Scripts](2026-09-28-rerunnable-mysql-schema-scripts.md)
