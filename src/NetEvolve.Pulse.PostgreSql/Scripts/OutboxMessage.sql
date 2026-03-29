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
DO $$
DECLARE
    schema_name TEXT := 'pulse';
    table_name  TEXT := 'OutboxMessage';
BEGIN

-- Create schema if it doesn't exist
EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I', schema_name);

-- Create table if it doesn't exist
IF NOT EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE table_schema = schema_name
      AND table_name = table_name
) THEN
    EXECUTE format('
        CREATE TABLE %I.%I (
            "Id"            UUID            NOT NULL,
            "EventType"     VARCHAR(500)    NOT NULL,
            "Payload"       TEXT            NOT NULL,
            "CorrelationId" VARCHAR(100)    NULL,
            "CreatedAt"     TIMESTAMPTZ     NOT NULL,
            "UpdatedAt"     TIMESTAMPTZ     NOT NULL,
            "ProcessedAt"   TIMESTAMPTZ     NULL,
            "RetryCount"    INTEGER         NOT NULL DEFAULT 0,
            "Error"         TEXT            NULL,
            "Status"        INTEGER         NOT NULL DEFAULT 0,
            CONSTRAINT "PK_%s" PRIMARY KEY ("Id")
        )', schema_name, table_name, table_name);

    -- Index for efficient polling of pending messages
    EXECUTE format('
        CREATE INDEX "IX_%s_Status_CreatedAt"
        ON %I.%I ("Status", "CreatedAt")
        WHERE "Status" IN (0, 3)
    ', table_name, schema_name, table_name);

    -- Index for cleanup of completed messages
    EXECUTE format('
        CREATE INDEX "IX_%s_Status_ProcessedAt"
        ON %I.%I ("Status", "ProcessedAt")
        WHERE "Status" = 2
    ', table_name, schema_name, table_name);
END IF;

END $$;

-- ============================================================================
-- Stored Functions
-- ============================================================================

-- get_pending_outbox_messages: Retrieves and locks pending messages for processing
-- Uses FOR UPDATE SKIP LOCKED for concurrent polling safety
CREATE OR REPLACE FUNCTION "pulse".get_pending_outbox_messages(
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
        FROM "pulse"."OutboxMessage" om
        WHERE om."Status" = 0 -- Pending
        ORDER BY om."CreatedAt"
        LIMIT batch_size
        FOR UPDATE SKIP LOCKED
    )
    UPDATE "pulse"."OutboxMessage" msg
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
        msg."RetryCount",
        msg."Error",
        msg."Status";
END;
$$;

-- get_failed_outbox_messages_for_retry: Retrieves failed messages eligible for retry
CREATE OR REPLACE FUNCTION "pulse".get_failed_outbox_messages_for_retry(
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
        FROM "pulse"."OutboxMessage" om
        WHERE om."Status" = 3 -- Failed
          AND om."RetryCount" < max_retry_count
        ORDER BY om."UpdatedAt"
        LIMIT batch_size
        FOR UPDATE SKIP LOCKED
    )
    UPDATE "pulse"."OutboxMessage" msg
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
        msg."RetryCount",
        msg."Error",
        msg."Status";
END;
$$;

-- mark_outbox_message_completed: Marks a message as successfully processed
CREATE OR REPLACE FUNCTION "pulse".mark_outbox_message_completed(
    message_id UUID
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE "pulse"."OutboxMessage"
    SET
        "Status" = 2, -- Completed
        "ProcessedAt" = NOW(),
        "UpdatedAt" = NOW()
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$;

-- mark_outbox_message_failed: Marks a message as failed with error details
CREATE OR REPLACE FUNCTION "pulse".mark_outbox_message_failed(
    message_id UUID,
    error TEXT
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE "pulse"."OutboxMessage"
    SET
        "Status" = 3, -- Failed
        "RetryCount" = "RetryCount" + 1,
        "Error" = error,
        "UpdatedAt" = NOW()
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$;

-- mark_outbox_message_dead_letter: Moves a message to dead letter status
CREATE OR REPLACE FUNCTION "pulse".mark_outbox_message_dead_letter(
    message_id UUID,
    error TEXT
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE "pulse"."OutboxMessage"
    SET
        "Status" = 4, -- DeadLetter
        "Error" = error,
        "UpdatedAt" = NOW()
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$;

-- delete_completed_outbox_messages: Removes old completed messages
CREATE OR REPLACE FUNCTION "pulse".delete_completed_outbox_messages(
    older_than_utc TIMESTAMPTZ
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    DELETE FROM "pulse"."OutboxMessage"
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
CREATE OR REPLACE FUNCTION "pulse".get_dead_letter_outbox_messages(
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
        om."RetryCount",
        om."Error",
        om."Status"
    FROM "pulse"."OutboxMessage" om
    WHERE om."Status" = 4 -- DeadLetter
    ORDER BY om."UpdatedAt" DESC
    LIMIT page_size
    OFFSET (page * page_size);
END;
$$;

-- get_dead_letter_outbox_message: Returns a single dead-letter message by Id
CREATE OR REPLACE FUNCTION "pulse".get_dead_letter_outbox_message(
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
        om."RetryCount",
        om."Error",
        om."Status"
    FROM "pulse"."OutboxMessage" om
    WHERE om."Id" = message_id
      AND om."Status" = 4; -- DeadLetter
END;
$$;

-- get_dead_letter_outbox_message_count: Returns the count of dead-letter messages
CREATE OR REPLACE FUNCTION "pulse".get_dead_letter_outbox_message_count()
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN (
        SELECT COUNT(*)
        FROM "pulse"."OutboxMessage"
        WHERE "Status" = 4 -- DeadLetter
    );
END;
$$;

-- replay_outbox_message: Resets a dead-letter message to Pending for reprocessing
CREATE OR REPLACE FUNCTION "pulse".replay_outbox_message(
    message_id UUID
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE "pulse"."OutboxMessage"
    SET
        "Status"     = 0, -- Pending
        "RetryCount" = 0,
        "Error"      = NULL,
        "UpdatedAt"  = NOW()
    WHERE "Id" = message_id
      AND "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RETURN updated_count;
END;
$$;

-- replay_all_dead_letter_outbox_messages: Resets all dead-letter messages to Pending
CREATE OR REPLACE FUNCTION "pulse".replay_all_dead_letter_outbox_messages()
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE "pulse"."OutboxMessage"
    SET
        "Status"     = 0, -- Pending
        "RetryCount" = 0,
        "Error"      = NULL,
        "UpdatedAt"  = NOW()
    WHERE "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RETURN updated_count;
END;
$$;

-- get_outbox_statistics: Returns message counts grouped by status
CREATE OR REPLACE FUNCTION "pulse".get_outbox_statistics()
RETURNS TABLE (
    "Pending"    BIGINT,
    "Processing" BIGINT,
    "Completed"  BIGINT,
    "Failed"     BIGINT,
    "DeadLetter" BIGINT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT
        SUM(CASE WHEN "Status" = 0 THEN 1::BIGINT ELSE 0 END) AS "Pending",
        SUM(CASE WHEN "Status" = 1 THEN 1::BIGINT ELSE 0 END) AS "Processing",
        SUM(CASE WHEN "Status" = 2 THEN 1::BIGINT ELSE 0 END) AS "Completed",
        SUM(CASE WHEN "Status" = 3 THEN 1::BIGINT ELSE 0 END) AS "Failed",
        SUM(CASE WHEN "Status" = 4 THEN 1::BIGINT ELSE 0 END) AS "DeadLetter"
    FROM "pulse"."OutboxMessage";
END;
$$;
