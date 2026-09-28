# NetEvolve.Pulse.MySql

MySQL persistence provider for the [Pulse](https://github.com/dailydevops/pulse) outbox and idempotency patterns using plain ADO.NET.

## Overview

`NetEvolve.Pulse.MySql` provides MySQL-backed implementations of the outbox and idempotency patterns:

- **`MySqlOutboxRepository`** — Implements `IOutboxRepository` with optimized MySQL queries and schema scripts for table creation.
- **`MySqlOutboxManagement`** — Implements `IOutboxManagement` for dead-letter inspection, replay, and statistics.
- **`MySqlIdempotencyKeyRepository`** — Implements `IIdempotencyKeyRepository` for at-most-once command processing.

## Requirements

- **MySQL 8.0 or later** — Required for `SELECT … FOR UPDATE SKIP LOCKED` support (concurrent polling safety).
- **`MySql.Data`** — Oracle MySQL Connector/NET.

## Getting Started

### 1. Create the database schema

Run the SQL scripts from the `Scripts/` folder against your MySQL database:

```bash
mysql -u <user> -p <database> < OutboxMessage.sql
mysql -u <user> -p <database> < IdempotencyKey.sql
```

### Upgrading an existing schema

The scripts are safe to re-run. Every index is guarded by an `information_schema.statistics` check, because MySQL 8.0 has no `CREATE INDEX IF NOT EXISTS`. Re-running a script after upgrading the package is the supported way to apply indexes added in later releases. Existing tables and indexes are left unchanged.

For example, deployments created before `IX_OutboxMessage_Status_UpdatedAt` existed can re-run `OutboxMessage.sql`. They can also apply the index on its own:

```sql
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OutboxMessage' AND INDEX_NAME = 'IX_OutboxMessage_Status_UpdatedAt') = 0,
    'CREATE INDEX `IX_OutboxMessage_Status_UpdatedAt` ON `OutboxMessage` (`Status`, `UpdatedAt`)',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;
```

The scripts use a session user variable (`@pulse_sql`). To execute them through MySql.Data instead of the `mysql` client, set `AllowUserVariables=True` in the connection string.

### 2. Register services

**Outbox:**

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .AddMySqlOutbox("Server=localhost;Database=mydb;User Id=root;Password=secret;")
);
```

**Idempotency:**

```csharp
services.AddPulse(config => config
    .AddMySqlIdempotencyStore("Server=localhost;Database=mydb;User Id=root;Password=secret;")
);
```

## Data Types

| C# type | MySQL column type | Encoding |
|---|---|---|
| `Guid` | `BINARY(16)` | `Guid.ToByteArray()` / `new Guid(byte[])` |
| `DateTimeOffset` | `BIGINT` | UTC ticks |

This encoding is interchangeable with the `NetEvolve.Pulse.EntityFramework` MySQL provider.

## Notes

MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL. The `OutboxOptions.Schema` and `IdempotencyKeyOptions.Schema` properties are **not used**; tables are always created in the active database specified by the connection string.
