-- ============================================================================
-- OutboxMessage Table Schema (PostgreSQL)
-- ============================================================================
-- Purpose: Stores events for reliable delivery using the outbox pattern.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET)
--                  NetEvolve.Pulse.EntityFramework (EF Core)
--
-- Configuration:
--   Adjust schema_name and table_name variables below before executing.
--   Run this script using psql or any PostgreSQL-compatible client.
--
-- Usage:
--   psql -h your-host -d your-database -f OutboxMessage.sql
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
\set schema_name 'pulse'
\set table_name 'OutboxMessage'

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS :schema_name;

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS ":schema_name".":table_name" (
    "Id"            UUID            NOT NULL,
    "EventType"     VARCHAR(500)    NOT NULL,
    "Payload"       TEXT            NOT NULL,
    "CorrelationId" VARCHAR(100)    NULL,
    "CreatedAt"     TIMESTAMPTZ     NOT NULL,
    "UpdatedAt"     TIMESTAMPTZ     NOT NULL,
    "ProcessedAt"   TIMESTAMPTZ     NULL,
    "NextRetryAt"   TIMESTAMPTZ     NULL,
    "RetryCount"    INTEGER         NOT NULL DEFAULT 0,
    "Error"         TEXT            NULL,
    "Status"        INTEGER         NOT NULL DEFAULT 0,
    CONSTRAINT "PK_:schema_name" PRIMARY KEY ("Id")
);

-- Index for efficient polling of pending messages
CREATE INDEX IF NOT EXISTS "IX_:schema_name_Status_CreatedAt"
ON ":schema_name".":table_name" ("Status", "CreatedAt")
WHERE "Status" IN (0, 3);

-- Index for cleanup of completed messages
CREATE INDEX IF NOT EXISTS "IX_:schema_name_Status_ProcessedAt"
ON ":schema_name".":table_name" ("Status", "ProcessedAt")
WHERE "Status" = 2;

-- ============================================================================
-- Stored Functions
-- ============================================================================

-- get_pending_outbox_messages: Retrieves and locks pending messages for processing
-- Uses FOR UPDATE SKIP LOCKED for concurrent polling safety
CREATE OR REPLACE FUNCTION ":schema_name".get_pending_outbox_messages(
    batch_size INTEGER
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CreatedAt"     TIMESTAMPTZ,
    "UpdatedAt"     TIMESTAMPTZ,
    "ProcessedAt"   TIMESTAMPTZ,
    "NextRetryAt"   TIMESTAMPTZ,
    "RetryCount"    INTEGER,
    "Error"         TEXT,
    "Status"        INTEGER
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH cte AS (
        SELECT om."Id"
        FROM ":schema_name".":table_name" om
        WHERE om."Status" = 0 -- Pending
        ORDER BY om."CreatedAt"
        LIMIT batch_size
        FOR UPDATE SKIP LOCKED
    )
    UPDATE ":schema_name".":table_name" msg
    SET
        "Status" = 1, -- Processing
        "UpdatedAt" = NOW()
    FROM cte
    WHERE msg."Id" = cte."Id"
    RETURNING
        msg."Id",
        msg."EventType",
        msg."Payload",
        msg."CorrelationId",
        msg."CreatedAt",
        msg."UpdatedAt",
        msg."ProcessedAt",
        msg."NextRetryAt",
        msg."RetryCount",
        msg."Error",
        msg."Status";
END;
$$;

-- get_failed_outbox_messages_for_retry: Retrieves failed messages eligible for retry
CREATE OR REPLACE FUNCTION ":schema_name".get_failed_outbox_messages_for_retry(
    max_retry_count INTEGER,
    batch_size INTEGER
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CreatedAt"     TIMESTAMPTZ,
    "UpdatedAt"     TIMESTAMPTZ,
    "ProcessedAt"   TIMESTAMPTZ,
    "NextRetryAt"   TIMESTAMPTZ,
    "RetryCount"    INTEGER,
    "Error"         TEXT,
    "Status"        INTEGER
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH cte AS (
        SELECT om."Id"
        FROM ":schema_name".":table_name" om
        WHERE om."Status" = 3 -- Failed
          AND om."RetryCount" < max_retry_count
        ORDER BY om."UpdatedAt"
        LIMIT batch_size
        FOR UPDATE SKIP LOCKED
    )
    UPDATE ":schema_name".":table_name" msg
    SET
        "Status" = 1, -- Processing
        "UpdatedAt" = NOW()
    FROM cte
    WHERE msg."Id" = cte."Id"
    RETURNING
        msg."Id",
        msg."EventType",
        msg."Payload",
        msg."CorrelationId",
        msg."CreatedAt",
        msg."UpdatedAt",
        msg."ProcessedAt",
        msg."NextRetryAt",
        msg."RetryCount",
        msg."Error",
        msg."Status";
END;
$$;

-- mark_outbox_message_completed: Marks a message as successfully processed
CREATE OR REPLACE FUNCTION ":schema_name".mark_outbox_message_completed(
    message_id UUID
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE ":schema_name".":table_name"
    SET
        "Status" = 2, -- Completed
        "ProcessedAt" = NOW(),
        "UpdatedAt" = NOW()
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$;

-- mark_outbox_message_failed: Marks a message as failed with error details
CREATE OR REPLACE FUNCTION ":schema_name".mark_outbox_message_failed(
    message_id UUID,
    error TEXT,
    next_retry_at TIMESTAMPTZ
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE ":schema_name".":table_name"
    SET
        "Status" = 3, -- Failed
        "RetryCount" = "RetryCount" + 1,
        "Error" = error,
        "NextRetryAt" = next_retry_at,
        "UpdatedAt" = NOW()
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$;

-- mark_outbox_message_dead_letter: Moves a message to dead letter status
CREATE OR REPLACE FUNCTION ":schema_name".mark_outbox_message_dead_letter(
    message_id UUID,
    error TEXT
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE ":schema_name".":table_name"
    SET
        "Status" = 4, -- DeadLetter
        "Error" = error,
        "UpdatedAt" = NOW()
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$;

-- delete_completed_outbox_messages: Removes old completed messages
CREATE OR REPLACE FUNCTION ":schema_name".delete_completed_outbox_messages(
    older_than_utc TIMESTAMPTZ
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    DELETE FROM ":schema_name".":table_name"
    WHERE "Status" = 2 -- Completed
      AND "ProcessedAt" < older_than_utc;

    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$$;

-- ============================================================================
-- Management Functions
-- ============================================================================

-- get_dead_letter_outbox_messages: Returns a paginated list of dead-letter messages
CREATE OR REPLACE FUNCTION ":schema_name".get_dead_letter_outbox_messages(
    page_size INTEGER,
    page INTEGER
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CreatedAt"     TIMESTAMPTZ,
    "UpdatedAt"     TIMESTAMPTZ,
    "ProcessedAt"   TIMESTAMPTZ,
    "NextRetryAt"   TIMESTAMPTZ,
    "RetryCount"    INTEGER,
    "Error"         TEXT,
    "Status"        INTEGER
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT
        om."Id",
        om."EventType",
        om."Payload",
        om."CorrelationId",
        om."CreatedAt",
        om."UpdatedAt",
        om."ProcessedAt",
        om."NextRetryAt",
        om."RetryCount",
        om."Error",
        om."Status"
    FROM ":schema_name".":table_name" om
    WHERE om."Status" = 4 -- DeadLetter
    ORDER BY om."UpdatedAt" DESC
    LIMIT page_size
    OFFSET (page * page_size);
END;
$$;

-- get_dead_letter_outbox_message: Returns a single dead-letter message by Id
CREATE OR REPLACE FUNCTION ":schema_name".get_dead_letter_outbox_message(
    message_id UUID
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CreatedAt"     TIMESTAMPTZ,
    "UpdatedAt"     TIMESTAMPTZ,
    "ProcessedAt"   TIMESTAMPTZ,
    "NextRetryAt"   TIMESTAMPTZ,
    "RetryCount"    INTEGER,
    "Error"         TEXT,
    "Status"        INTEGER
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT
        om."Id",
        om."EventType",
        om."Payload",
        om."CorrelationId",
        om."CreatedAt",
        om."UpdatedAt",
        om."ProcessedAt",
        om."NextRetryAt",
        om."RetryCount",
        om."Error",
        om."Status"
    FROM ":schema_name".":table_name" om
    WHERE om."Id" = message_id
      AND om."Status" = 4; -- DeadLetter
END;
$$;

-- get_dead_letter_outbox_message_count: Returns the count of dead-letter messages
CREATE OR REPLACE FUNCTION ":schema_name".get_dead_letter_outbox_message_count()
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN (
        SELECT COUNT(*)
        FROM ":schema_name".":table_name"
        WHERE "Status" = 4 -- DeadLetter
    );
END;
$$;

-- replay_outbox_message: Resets a dead-letter message to Pending for reprocessing
CREATE OR REPLACE FUNCTION ":schema_name".replay_outbox_message(
    message_id UUID
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE ":schema_name".":table_name"
    SET
        "Status"     = 0, -- Pending
        "RetryCount" = 0,
        "Error"      = NULL,
        "NextRetryAt" = NULL,
        "UpdatedAt"  = NOW()
    WHERE "Id" = message_id
      AND "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RETURN updated_count;
END;
$$;

-- replay_all_dead_letter_outbox_messages: Resets all dead-letter messages to Pending
CREATE OR REPLACE FUNCTION ":schema_name".replay_all_dead_letter_outbox_messages()
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE ":schema_name".":table_name"
    SET
        "Status"     = 0, -- Pending
        "RetryCount" = 0,
        "Error"      = NULL,
        "NextRetryAt" = NULL,
        "UpdatedAt"  = NOW()
    WHERE "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RETURN updated_count;
END;
$$;

-- get_outbox_statistics: Returns message counts grouped by status
CREATE OR REPLACE FUNCTION ":schema_name".get_outbox_statistics()
RETURNS TABLE (
    "Pending"    BIGINT,
    "Processing" BIGINT,
    "Completed"  BIGINT,
    "Failed"     BIGINT,
    "DeadLetter" BIGINT
)
LANGUAGE plpgsql
STABLE
AS $$
BEGIN
    RETURN QUERY
    SELECT
        COALESCE(SUM(CASE WHEN "Status" = 0 THEN 1::BIGINT ELSE 0 END), 0::BIGINT)::BIGINT AS "Pending",
        COALESCE(SUM(CASE WHEN "Status" = 1 THEN 1::BIGINT ELSE 0 END), 0::BIGINT)::BIGINT AS "Processing",
        COALESCE(SUM(CASE WHEN "Status" = 2 THEN 1::BIGINT ELSE 0 END), 0::BIGINT)::BIGINT AS "Completed",
        COALESCE(SUM(CASE WHEN "Status" = 3 THEN 1::BIGINT ELSE 0 END), 0::BIGINT)::BIGINT AS "Failed",
        COALESCE(SUM(CASE WHEN "Status" = 4 THEN 1::BIGINT ELSE 0 END), 0::BIGINT)::BIGINT AS "DeadLetter"
    FROM ":schema_name".":table_name";
END;
$$;
