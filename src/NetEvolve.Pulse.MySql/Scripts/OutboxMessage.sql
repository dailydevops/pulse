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
-- Usage:
--   Run this script in the target MySQL database once before deploying the application:
--     mysql -u <user> -p <database> < OutboxMessage.sql
--
--   If you need a custom table name, replace all occurrences of `OutboxMessage`
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
CREATE INDEX `IX_OutboxMessage_Status_CreatedAt`
    ON `OutboxMessage` (`Status`, `CreatedAt`);

-- Index for retry-scheduled message polling (exponential backoff)
CREATE INDEX `IX_OutboxMessage_Status_NextRetryAt`
    ON `OutboxMessage` (`Status`, `NextRetryAt`);

-- Index for completed message cleanup
CREATE INDEX `IX_OutboxMessage_Status_ProcessedAt`
    ON `OutboxMessage` (`Status`, `ProcessedAt`);
