---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Extensibility/Idempotency/*.cs"
  - "src/NetEvolve.Pulse/Idempotency/*.cs"
  - "src/**/Idempotency/*.cs"
  - "src/**/Scripts/IdempotencyKey.sql"
  - "src/NetEvolve.Pulse.EntityFramework/Configurations/*IdempotencyKeyConfiguration*.cs"

created: 2026-09-29

lastModified: 2026-09-29

state: proposed

instructions: |
  IdempotencyKeySchema.MaxLengths.IdempotencyKey is 450 characters, so NVARCHAR(450) (900 bytes) fits the SQL Server clustered index key limit.
  IdempotencyStore and the SQL Server, MySQL and Entity Framework repositories MUST reject a key longer than MaxLengths.IdempotencyKey with an ArgumentException before any database call. A key MUST never be truncated.
  Key columns MUST use a binary collation: Latin1_General_100_BIN2 on SQL Server and utf8mb4_bin on MySQL, in the provider scripts and in the EF Core configurations. Keys are case-sensitive and accent-sensitive on every provider.
  Provider code MUST NOT use statements that turn data errors into warnings (MySQL INSERT IGNORE). Duplicate keys are detected by catching the provider's duplicate-key error.
---

# Decision: Store and Compare Idempotency Keys Exactly

Idempotency keys are stored and compared exactly as the client sent them. The maximum length is 450 characters, keys that are too long are rejected instead of truncated, and the key columns use binary collations.

## Context

* `IdempotencyKeySchema.MaxLengths.IdempotencyKey` was 500. On SQL Server the key column was `NVARCHAR(500)`, and it is also the clustered primary key. [CREATE INDEX, Index key size](https://learn.microsoft.com/sql/t-sql/statements/create-index-transact-sql#index-key-size) states: "The maximum size for an index key is 900 bytes for a clustered index and 1,700 bytes for a nonclustered index." `NVARCHAR` needs 2 bytes per character, so keys of 451 to 500 characters failed with Msg 1946 (#850).
* SqlClient silently cuts a parameter value to its `Size`. MySQL `INSERT IGNORE` turns `ER_DATA_TOO_LONG` into a warning and stores a prefix ([MySQL 8.0, INSERT](https://dev.mysql.com/doc/refman/8.0/en/insert.html): "With `IGNORE`, invalid values are adjusted to the closest values and inserted"). Two keys that share a long prefix collided, or a stored key was never found again (#858).
* The MySQL column used `utf8mb4_unicode_ci`, and the SQL Server column used the database default collation. Both are usually case-insensitive, so `aBc123` and `ABC123` counted as the same key. Base62 and base64 client keys differ only by case all the time. PostgreSQL, SQLite, Redis and EF InMemory already compare keys exactly.

## Decision

* `MaxLengths.IdempotencyKey` is 450 for every provider. The SQL Server column and stored procedure parameters are `NVARCHAR(450)`.
* `IdempotencyStore` (`ExistsAsync`, `StoreAsync`, `TryReserveAsync`) and the SQL Server, MySQL and Entity Framework repositories reject a longer key with `ArgumentOutOfRangeException` (an `ArgumentException`) before any database call.
* SQL Server uses `Latin1_General_100_BIN2`. [Collation and Unicode support](https://learn.microsoft.com/sql/relational-databases/collations/collation-and-unicode-support) describes BIN2 as "a pure code-point comparison". MySQL uses `utf8mb4_bin`. The [MySQL 8.0 manual](https://dev.mysql.com/doc/refman/8.0/en/charset-binary-collations.html) says that for `_bin` collations "ordering is based on numeric character code values". The EF Core configurations set the same collations with `UseCollation`.
* MySQL stores keys with a plain `INSERT` and treats error 1062 (`ER_DUP_ENTRY`) as a duplicate. `INSERT ... ON DUPLICATE KEY UPDATE` is not used, because MySql.Data reports found rows by default, so a duplicate would also report one affected row and `TryReserveAsync` would always return `true`.
* The provider scripts upgrade existing tables on re-run. SQL Server drops the primary key, alters the column and re-creates the primary key in one transaction. MySQL changes the column collation through the `information_schema` guard pattern of the re-runnable MySQL scripts decision. The MySQL column stays `VARCHAR(500)`, because existing rows may already be longer than 450 characters. The core guard still enforces the 450 limit.

## Consequences

* Every key up to 450 characters works on every provider, and a longer key fails fast with a clear exception instead of a `SqlException` or a silent truncation.
* Keys that differ only by case are distinct on every provider.
* Trailing spaces are still ignored on SQL Server and MySQL. SQL Server pads strings before every `=` comparison, whatever the collation ([= (String comparison)](https://learn.microsoft.com/sql/t-sql/language-elements/string-comparison-assignment)), and `utf8mb4_bin` is a `PAD SPACE` collation. `utf8mb4_0900_bin` (`NO PAD`) was not chosen, because it would still differ from SQL Server and MariaDB does not provide it.
* The public constant drops from 500 to 450. Code that compiled against the old value keeps 500 until it is recompiled.
* EF Core users need a new migration for the column type and collation. ADO.NET users re-run the provider script.

## Alternatives Considered

* **`PRIMARY KEY NONCLUSTERED` with the 1,700-byte limit**: keeps 500 characters on SQL Server, but needs a separate clustered index or a heap, and the limit would differ between providers.
* **Hash the key into a fixed-length column**: allows any length and exact comparison including trailing spaces, but changes the schema contract of every provider and makes stored keys unreadable for operators.
* **Case-sensitive, accent-sensitive linguistic collations** (for example `Latin1_General_100_CS_AS`): still apply linguistic equivalence rules, so they are not an exact comparison.

## Related Decisions (Optional)

* [Refresh Expired Idempotency Keys on Reserve](2026-09-28-idempotency-refresh-expired-keys-on-reserve.md)
* [Re-runnable MySQL Schema Scripts](2026-09-28-rerunnable-mysql-schema-scripts.md)
