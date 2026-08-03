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
--   Run this script in the target MySQL database once before deploying the application:
--     mysql -u <user> -p <database> < AuditEntry.sql
--
--   If you need a custom table name, replace all occurrences of `AuditEntry`
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
CREATE INDEX `IX_AuditEntry_OccurredAt`
    ON `AuditEntry` (`OccurredAt`);

-- Index to efficiently filter entries by request type
CREATE INDEX `IX_AuditEntry_CommandType`
    ON `AuditEntry` (`CommandType`);
