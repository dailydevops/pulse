---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.MySql/Scripts/*.sql"

created: 2026-09-28

lastModified: 2026-09-28

state: proposed

instructions: |
  MUST keep every MySQL provider script safe to run repeatedly against the same database; re-running the script is the supported upgrade path.
  MUST create tables with CREATE TABLE IF NOT EXISTS and guard every CREATE INDEX with an information_schema.statistics lookup (TABLE_SCHEMA = DATABASE(), TABLE_NAME, INDEX_NAME) executed through SET @pulse_sql / PREPARE / EXECUTE / DEALLOCATE PREPARE.
  MUST keep the scripts runnable through the plain mysql client with the default ';' delimiter: no DELIMITER, no stored procedures, no ';' inside string literals.
  MUST guard a column added in a later release the same way, with an information_schema.columns lookup and ALTER TABLE ... ADD COLUMN.
---

# Decision: Re-runnable MySQL Schema Scripts

The `NetEvolve.Pulse.MySql` schema scripts are idempotent. Operators re-run them to pick up indexes and columns that later releases add.

## Context

MySQL 8.0 has no `CREATE INDEX IF NOT EXISTS`. A second `CREATE INDEX` with an existing name fails with `ER_DUP_KEYNAME` (1061). The scripts used `CREATE TABLE IF NOT EXISTS` but plain `CREATE INDEX`. Because of that, a re-run aborted at the first index. New indexes such as `IX_OutboxMessage_Status_UpdatedAt` (#647) therefore never reached existing deployments (#853). The package ships no migration tooling and no separate upgrade scripts.

## Decision

* Each script stays the single source of the schema, and re-running it is the upgrade path.
* Tables use `CREATE TABLE IF NOT EXISTS`.
* Each index is created through a guard that checks `information_schema.statistics` and executes either the `CREATE INDEX` or `DO 0` through `PREPARE` / `EXECUTE`. The MySQL 8.0 manual lists both `CREATE INDEX` and `DO` as SQL permitted in prepared statements.
* A column added in a later release gets the same guard, with `information_schema.columns` and `ALTER TABLE ... ADD COLUMN`.
* The scripts contain no `DELIMITER` changes and no `;` inside string literals. As a result they run unchanged through `mysql < script.sql` and through statement-splitting runners.

## Consequences

* Existing deployments get new indexes by re-running the shipped script.
* The scripts are more verbose than plain DDL.
* The scripts use a session user variable (`@pulse_sql`). ADO.NET runners based on MySql.Data must enable `AllowUserVariables=True` to execute them.
* A guard checks only the index name. It does not repair an index that has the right name but the wrong columns.

## Alternatives Considered

* **Stored procedure helper**: needs `DELIMITER` changes, so it breaks the plain-client and statement-splitting requirements, and it leaves a procedure behind in the database.
* **Separate numbered upgrade scripts**: operators would need to track which scripts they have applied, and nothing in the package records that.
* **`CREATE INDEX IF NOT EXISTS`**: only MariaDB supports it, MySQL 8.0 does not.

## Related Decisions (Optional)

None at this time.
