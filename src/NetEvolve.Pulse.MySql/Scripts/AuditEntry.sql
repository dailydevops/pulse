-- ============================================================================
-- AuditEntry Table Schema (MySQL)
-- ============================================================================
--
-- Purpose:   Stores audit trail records describing processed requests, so
--            operators can inspect who did what, when, and with what outcome.
-- Provider:  NetEvolve.Pulse.MySql (ADO.NET)
--            NetEvolve.Pulse.EntityFramework with MySql.EntityFrameworkCore
--
-- Prerequisites:
--   MySQL 8.0 or later
--
-- Column types:
--   Id               BINARY(16)    -- Guid.ToByteArray(), primary key
--   CommandType      VARCHAR(500)  -- runtime type name of the processed request
--   UserId           VARCHAR(256)  -- identifier of the user who issued the request (nullable)
--   CorrelationId    VARCHAR(100)  -- correlation identifier associated with the request (nullable)
--   OccurredAt       BIGINT        -- UTC ticks (use dto.UtcTicks / new DateTimeOffset(ticks, TimeSpan.Zero))
--   DurationMs       DOUBLE        -- elapsed time, in milliseconds, of the handler invocation
--   Result           TINYINT       -- AuditResult enum value (0 = Success, 1 = Failure)
--   Payload          LONGTEXT      -- JSON serialized request payload (nullable)
--   ExceptionMessage LONGTEXT      -- message of the exception that caused the failure (nullable)
--
-- Usage:
--   Run this script in the target MySQL database before deploying the application:
--     mysql -u <user> -p <database> < AuditEntry.sql
--
--   The script is safe to re-run. MySQL 8.0 has no CREATE INDEX IF NOT EXISTS, so every index
--   is guarded by an information_schema.statistics lookup executed through PREPARE / EXECUTE.
--   Re-run the script after upgrading the package to apply indexes added in later releases.
--   Existing tables and indexes are left unchanged. When executing it through MySql.Data
--   instead of the mysql client, set AllowUserVariables=True (the guards use @pulse_sql).
--
--   If you need a custom table name, replace every table reference to AuditEntry
--   (CREATE TABLE IF NOT EXISTS `AuditEntry`, ON `AuditEntry` and TABLE_NAME = 'AuditEntry')
--   and update AuditStoreOptions.TableName in your application configuration accordingly.
--
-- Note on schema:
--   MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
--   Tables are created in whichever database is active when this script runs.
--   Pass the desired database in the connection string (Database=<dbname>).
-- ============================================================================

CREATE TABLE IF NOT EXISTS `AuditEntry` (
    `Id`               BINARY(16)   NOT NULL,
    `CommandType`      VARCHAR(500) NOT NULL,
    `UserId`           VARCHAR(256)     NULL,
    `CorrelationId`    VARCHAR(100)     NULL,
    `OccurredAt`       BIGINT       NOT NULL,
    `DurationMs`       DOUBLE       NOT NULL,
    `Result`           TINYINT      NOT NULL,
    `Payload`          LONGTEXT         NULL,
    `ExceptionMessage` LONGTEXT         NULL,
    CONSTRAINT `PK_AuditEntry` PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Index to efficiently order and range-filter entries by occurrence time
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AuditEntry' AND INDEX_NAME = 'IX_AuditEntry_OccurredAt') = 0,
    'CREATE INDEX `IX_AuditEntry_OccurredAt` ON `AuditEntry` (`OccurredAt`)',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;

-- Index to efficiently filter entries by request type
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AuditEntry' AND INDEX_NAME = 'IX_AuditEntry_CommandType') = 0,
    'CREATE INDEX `IX_AuditEntry_CommandType` ON `AuditEntry` (`CommandType`)',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;
