-- ============================================================================
-- IdempotencyKey Table Schema (MySQL)
-- ============================================================================
--
-- Purpose:   Stores idempotency keys to ensure at-most-once command processing.
-- Provider:  NetEvolve.Pulse.MySql (ADO.NET)
--            NetEvolve.Pulse.EntityFramework with MySql.EntityFrameworkCore
--
-- Prerequisites:
--   MySQL 8.0 or later
--
-- Column types:
--   IdempotencyKey VARCHAR(500) — the idempotency key (primary key)
--   CreatedAt      BIGINT       — UTC ticks (use dto.UtcTicks / new DateTimeOffset(ticks, TimeSpan.Zero))
--
-- Usage:
--   Run this script in the target MySQL database once before deploying the application:
--     mysql -u <user> -p <database> < IdempotencyKey.sql
--
--   If you need a custom table name, replace all occurrences of `IdempotencyKey`
--   and update IdempotencyKeyOptions.TableName in your application configuration accordingly.
--
-- Note on schema:
--   MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
--   Tables are created in whichever database is active when this script runs.
--   Pass the desired database in the connection string (Database=<dbname>).
-- ============================================================================

CREATE TABLE IF NOT EXISTS `IdempotencyKey` (
    `IdempotencyKey` VARCHAR(500) NOT NULL,
    `CreatedAt`      BIGINT       NOT NULL,
    CONSTRAINT `PK_IdempotencyKey` PRIMARY KEY (`IdempotencyKey`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Index to efficiently filter keys by creation time (for TTL-based existence checks)
CREATE INDEX `IX_IdempotencyKey_CreatedAt`
    ON `IdempotencyKey` (`CreatedAt`);
