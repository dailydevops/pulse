-- ============================================================================
-- OutboxMessage Table Schema (MySQL)
-- ============================================================================
--
-- Purpose:   Stores domain events for reliable delivery using the outbox pattern.
-- Provider:  NetEvolve.Pulse.MySql (ADO.NET)
--            NetEvolve.Pulse.EntityFramework with MySql.EntityFrameworkCore
--
-- Prerequisites:
--   MySQL 8.0 or later (requires FOR UPDATE SKIP LOCKED support)
--
-- Column types:
--   Id             BINARY(16)   — raw 16-byte UUID (use Guid.ToByteArray() / new Guid(bytes))
--   DateTimeOffset BIGINT       — UTC ticks (use dto.UtcTicks / new DateTimeOffset(ticks, TimeSpan.Zero))
--   EventType      VARCHAR(500) — assembly-qualified type name
--   Payload        LONGTEXT     — serialised event payload (typically JSON)
--   CorrelationId  VARCHAR(100) — optional correlation identifier
--   Error          LONGTEXT     — error message for failed/dead-letter messages
--   Status values:
--     0 = Pending    1 = Processing    2 = Completed
--     3 = Failed     4 = DeadLetter
--
-- Claim lease reclaim:
--   Each claim sets UpdatedAt to the claim timestamp. The pending-poll query (see
--   NetEvolve.Pulse.Outbox.MySqlOutboxRepository.GetPendingAsync) also reclaims rows still in the
--   Processing status whose UpdatedAt is older than (now - OutboxOptions.ProcessingLeaseTimeout),
--   so messages are not lost forever when a worker crashes, is cancelled, or throws an unhandled
--   exception during dispatch. No additional column is required — UpdatedAt is reused.
--
-- Usage:
--   Run this script in the target MySQL database before deploying the application:
--     mysql -u <user> -p <database> < OutboxMessage.sql
--
--   The script is safe to re-run. MySQL 8.0 has no CREATE INDEX IF NOT EXISTS, so every index
--   is guarded by an information_schema.statistics lookup executed through PREPARE / EXECUTE.
--   Re-run the script after upgrading the package to apply indexes added in later releases.
--   Existing tables and indexes are left unchanged. When executing it through MySql.Data
--   instead of the mysql client, set AllowUserVariables=True (the guards use @pulse_sql).
--
--   If you need a custom table name, replace every table reference to OutboxMessage
--   (CREATE TABLE IF NOT EXISTS `OutboxMessage`, ON `OutboxMessage` and TABLE_NAME = 'OutboxMessage')
--   and update OutboxOptions.TableName in your application configuration accordingly.
--
-- Note on schema:
--   MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
--   Tables are created in whichever database is active when this script runs.
--   Pass the desired database in the connection string (Database=<dbname>).
-- ============================================================================

CREATE TABLE IF NOT EXISTS `OutboxMessage` (
    `Id`            BINARY(16)   NOT NULL,
    `EventType`     VARCHAR(500) NOT NULL,
    `Payload`       LONGTEXT     NOT NULL,
    `CorrelationId` VARCHAR(100) NULL,
    `CausationId`   VARCHAR(100) NULL,
    `CreatedAt`     BIGINT       NOT NULL,
    `UpdatedAt`     BIGINT       NOT NULL,
    `ProcessedAt`   BIGINT       NULL,
    `NextRetryAt`   BIGINT       NULL,
    `RetryCount`    INT          NOT NULL DEFAULT 0,
    `Error`         LONGTEXT     NULL,
    `Status`        INT          NOT NULL DEFAULT 0,
    CONSTRAINT `PK_OutboxMessage` PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Index for pending/failed message polling (queried by the outbox processor)
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OutboxMessage' AND INDEX_NAME = 'IX_OutboxMessage_Status_CreatedAt') = 0,
    'CREATE INDEX `IX_OutboxMessage_Status_CreatedAt` ON `OutboxMessage` (`Status`, `CreatedAt`)',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;

-- Index for retry-scheduled message polling (exponential backoff)
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OutboxMessage' AND INDEX_NAME = 'IX_OutboxMessage_Status_NextRetryAt') = 0,
    'CREATE INDEX `IX_OutboxMessage_Status_NextRetryAt` ON `OutboxMessage` (`Status`, `NextRetryAt`)',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;

-- Index for completed message cleanup
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OutboxMessage' AND INDEX_NAME = 'IX_OutboxMessage_Status_ProcessedAt') = 0,
    'CREATE INDEX `IX_OutboxMessage_Status_ProcessedAt` ON `OutboxMessage` (`Status`, `ProcessedAt`)',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;

-- Index for reclaiming Processing messages whose claim lease has expired
SET @pulse_sql := IF(
    (SELECT COUNT(*) FROM information_schema.statistics
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OutboxMessage' AND INDEX_NAME = 'IX_OutboxMessage_Status_UpdatedAt') = 0,
    'CREATE INDEX `IX_OutboxMessage_Status_UpdatedAt` ON `OutboxMessage` (`Status`, `UpdatedAt`)',
    'DO 0'
);
PREPARE pulse_stmt FROM @pulse_sql;
EXECUTE pulse_stmt;
DEALLOCATE PREPARE pulse_stmt;
