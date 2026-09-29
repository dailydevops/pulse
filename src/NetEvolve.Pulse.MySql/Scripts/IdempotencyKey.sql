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
--   IdempotencyKey VARCHAR(500) — the idempotency key (primary key), binary collation utf8mb4_bin
--   CreatedAt      BIGINT       — UTC ticks (use dto.UtcTicks / new DateTimeOffset(ticks, TimeSpan.Zero))
--
-- Idempotency keys:
--   Keys are compared by code point (utf8mb4_bin), so keys that differ only by case are distinct.
--   utf8mb4_bin is a PAD SPACE collation, so trailing spaces are not significant.
--   The application rejects keys longer than 450 characters (IdempotencyKeySchema.MaxLengths.IdempotencyKey).
--   The column stays VARCHAR(500), so tables created by earlier releases need no data change.
--
-- Usage:
--   Run this script in the target MySQL database before deploying the application:
--     mysql -u <user> -p <database> < IdempotencyKey.sql
--
--   The script is safe to re-run. MySQL 8.0 has no CREATE INDEX IF NOT EXISTS, so every index
--   is guarded by an information_schema.statistics lookup executed through PREPARE / EXECUTE.
--   Re-run the script after upgrading the package to apply indexes added in later releases.
--   Existing tables and indexes are left unchanged, except that a case-insensitive key column
--   created by an earlier release is switched to utf8mb4_bin through ALTER TABLE ... MODIFY. When executing it through MySql.Data
--   instead of the mysql client, set AllowUserVariables=True (the guards use @pulse_sql).
--
--   If you need a custom table name, replace every table reference to IdempotencyKey
--   (CREATE TABLE IF NOT EXISTS `IdempotencyKey`, ALTER TABLE `IdempotencyKey`, ON `IdempotencyKey`
--   and TABLE_NAME = 'IdempotencyKey')
--   and update IdempotencyKeyOptions.TableName in your application configuration accordingly.
--
-- Note on schema:
--   MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
--   Tables are created in whichever database is active when this script runs.
--   Pass the desired database in the connection string (Database=<dbname>).
-- ============================================================================

CREATE TABLE IF NOT EXISTS `IdempotencyKey` (
    `IdempotencyKey` VARCHAR(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `CreatedAt`      BIGINT       NOT NULL,
    CONSTRAINT `PK_IdempotencyKey` PRIMARY KEY (`IdempotencyKey`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Switch the key column of a table created by an earlier release to the binary collation
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.columns
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'IdempotencyKey' AND COLUMN_NAME = 'IdempotencyKey'
          AND COLLATION_NAME <> 'utf8mb4_bin') > 0,
    'ALTER TABLE `IdempotencyKey` MODIFY `IdempotencyKey` VARCHAR(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;

-- Index to efficiently filter keys by creation time (for TTL-based existence checks)
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'IdempotencyKey' AND INDEX_NAME = 'IX_IdempotencyKey_CreatedAt') = 0,
    'CREATE INDEX `IX_IdempotencyKey_CreatedAt` ON `IdempotencyKey` (`CreatedAt`)',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;
