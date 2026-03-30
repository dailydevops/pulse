-- ============================================================================
-- 001_CreateOutboxTable.sql
-- ============================================================================
-- Purpose: Creates the OutboxMessage table for the SQLite outbox pattern.
-- Compatible with: NetEvolve.Pulse.SQLite (ADO.NET)
--
-- Configuration:
--   Replace the table name below if you use a custom SQLiteOutboxOptions.TableName.
--   Default table name: OutboxMessage
--
-- Usage:
--   Execute this script once against your SQLite database file before starting
--   the application. Example:
--     sqlite3 outbox.db < 001_CreateOutboxTable.sql
-- ============================================================================

-- Enable WAL mode for concurrent read access during writes
PRAGMA journal_mode=WAL;

-- Create outbox messages table
CREATE TABLE IF NOT EXISTS "OutboxMessage"
(
    "Id"            TEXT        NOT NULL,
    "EventType"     TEXT        NOT NULL,
    "Payload"       TEXT        NOT NULL,
    "CorrelationId" TEXT        NULL,
    "CreatedAt"     TEXT        NOT NULL,
    "UpdatedAt"     TEXT        NOT NULL,
    "ProcessedAt"   TEXT        NULL,
    "NextRetryAt"   TEXT        NULL,
    "RetryCount"    INTEGER     NOT NULL DEFAULT 0,
    "Error"         TEXT        NULL,
    "Status"        INTEGER     NOT NULL DEFAULT 0,
    CONSTRAINT "PK_OutboxMessage" PRIMARY KEY ("Id")
);

-- Index for efficient polling of pending and failed messages
CREATE INDEX IF NOT EXISTS "IX_OutboxMessage_Status_CreatedAt"
    ON "OutboxMessage" ("Status", "CreatedAt")
    WHERE "Status" IN (0, 3); -- Pending and Failed

-- Index for efficient cleanup of completed messages
CREATE INDEX IF NOT EXISTS "IX_OutboxMessage_Status_ProcessedAt"
    ON "OutboxMessage" ("Status", "ProcessedAt")
    WHERE "Status" = 2; -- Completed
