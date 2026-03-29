-- ============================================================================
-- OutboxMessage Table Schema for PostgreSQL
-- ============================================================================
-- Purpose: Stores events for reliable delivery using the outbox pattern.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET / Npgsql)
--                  NetEvolve.Pulse.EntityFramework (EF Core)
--
-- Configuration:
--   Adjust the schema_name and table_name variables below before executing.
--   Execute this script using psql, pgAdmin, or any compatible PostgreSQL client:
--     psql -U postgres -d mydb -f OutboxMessage.sql
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
-- To change schema/table, replace the literals 'pulse' and 'OutboxMessage'
-- throughout this script with your preferred values.
-- ============================================================================

-- Create schema if it does not exist
CREATE SCHEMA IF NOT EXISTS pulse;

-- Create table if it does not exist
CREATE TABLE IF NOT EXISTS pulse."OutboxMessage"
(
    "Id"            UUID                     NOT NULL,
    "EventType"     CHARACTER VARYING(500)   NOT NULL,
    "Payload"       TEXT                     NOT NULL,
    "CorrelationId" CHARACTER VARYING(100)   NULL,
    "CreatedAt"     TIMESTAMP WITH TIME ZONE NOT NULL,
    "UpdatedAt"     TIMESTAMP WITH TIME ZONE NOT NULL,
    "ProcessedAt"   TIMESTAMP WITH TIME ZONE NULL,
    "RetryCount"    INTEGER                  NOT NULL DEFAULT 0,
    "Error"         TEXT                     NULL,
    "Status"        INTEGER                  NOT NULL DEFAULT 0,
    CONSTRAINT "PK_OutboxMessage" PRIMARY KEY ("Id")
);

-- Index for efficient polling of pending and failed messages
CREATE INDEX IF NOT EXISTS "IX_OutboxMessage_Status_CreatedAt"
    ON pulse."OutboxMessage" ("Status", "CreatedAt")
    WHERE "Status" IN (0, 3);

-- Index for cleanup of completed messages
CREATE INDEX IF NOT EXISTS "IX_OutboxMessage_Status_ProcessedAt"
    ON pulse."OutboxMessage" ("Status", "ProcessedAt")
    WHERE "Status" = 2;

-- ============================================================================
-- Stored Functions
-- ============================================================================

-- get_pending_outbox_messages: Retrieves and locks pending messages for processing.
-- Uses FOR UPDATE SKIP LOCKED to prevent concurrent processing of the same message.
CREATE OR REPLACE FUNCTION pulse.get_pending_outbox_messages(p_batch_size INTEGER)
    RETURNS TABLE
            (
                "Id"            UUID,
                "EventType"     CHARACTER VARYING(500),
                "Payload"       TEXT,
                "CorrelationId" CHARACTER VARYING(100),
                "CreatedAt"     TIMESTAMP WITH TIME ZONE,
                "UpdatedAt"     TIMESTAMP WITH TIME ZONE,
                "ProcessedAt"   TIMESTAMP WITH TIME ZONE,
                "RetryCount"    INTEGER,
                "Error"         TEXT,
                "Status"        INTEGER
            )
    LANGUAGE SQL
AS
$$
WITH cte AS (
    SELECT m."Id"
    FROM pulse."OutboxMessage" AS m
    WHERE m."Status" = 0 -- Pending
    ORDER BY m."CreatedAt"
    LIMIT p_batch_size
    FOR UPDATE SKIP LOCKED
)
UPDATE pulse."OutboxMessage" AS m
SET "Status"    = 1, -- Processing
    "UpdatedAt" = NOW()
FROM cte
WHERE m."Id" = cte."Id"
RETURNING m."Id",
          m."EventType",
          m."Payload",
          m."CorrelationId",
          m."CreatedAt",
          m."UpdatedAt",
          m."ProcessedAt",
          m."RetryCount",
          m."Error",
          m."Status";
$$;

-- get_failed_outbox_messages_for_retry: Retrieves failed messages eligible for retry.
-- Uses FOR UPDATE SKIP LOCKED to prevent concurrent retry of the same message.
CREATE OR REPLACE FUNCTION pulse.get_failed_outbox_messages_for_retry(
    p_max_retry_count INTEGER,
    p_batch_size INTEGER
)
    RETURNS TABLE
            (
                "Id"            UUID,
                "EventType"     CHARACTER VARYING(500),
                "Payload"       TEXT,
                "CorrelationId" CHARACTER VARYING(100),
                "CreatedAt"     TIMESTAMP WITH TIME ZONE,
                "UpdatedAt"     TIMESTAMP WITH TIME ZONE,
                "ProcessedAt"   TIMESTAMP WITH TIME ZONE,
                "RetryCount"    INTEGER,
                "Error"         TEXT,
                "Status"        INTEGER
            )
    LANGUAGE SQL
AS
$$
WITH cte AS (
    SELECT m."Id"
    FROM pulse."OutboxMessage" AS m
    WHERE m."Status" = 3 -- Failed
      AND m."RetryCount" < p_max_retry_count
    ORDER BY m."UpdatedAt"
    LIMIT p_batch_size
    FOR UPDATE SKIP LOCKED
)
UPDATE pulse."OutboxMessage" AS m
SET "Status"    = 1, -- Processing
    "UpdatedAt" = NOW()
FROM cte
WHERE m."Id" = cte."Id"
RETURNING m."Id",
          m."EventType",
          m."Payload",
          m."CorrelationId",
          m."CreatedAt",
          m."UpdatedAt",
          m."ProcessedAt",
          m."RetryCount",
          m."Error",
          m."Status";
$$;

-- mark_outbox_message_completed: Marks a message as successfully processed.
CREATE OR REPLACE FUNCTION pulse.mark_outbox_message_completed(p_message_id UUID)
    RETURNS VOID
    LANGUAGE SQL
AS
$$
UPDATE pulse."OutboxMessage"
SET "Status"      = 2, -- Completed
    "ProcessedAt" = NOW(),
    "UpdatedAt"   = NOW()
WHERE "Id" = p_message_id
  AND "Status" = 1; -- Processing
$$;

-- mark_outbox_message_failed: Marks a message as failed with error details.
CREATE OR REPLACE FUNCTION pulse.mark_outbox_message_failed(p_message_id UUID, p_error TEXT)
    RETURNS VOID
    LANGUAGE SQL
AS
$$
UPDATE pulse."OutboxMessage"
SET "Status"     = 3, -- Failed
    "RetryCount" = "RetryCount" + 1,
    "Error"      = p_error,
    "UpdatedAt"  = NOW()
WHERE "Id" = p_message_id
  AND "Status" = 1; -- Processing
$$;

-- mark_outbox_message_dead_letter: Moves a message to dead letter status.
CREATE OR REPLACE FUNCTION pulse.mark_outbox_message_dead_letter(p_message_id UUID, p_error TEXT)
    RETURNS VOID
    LANGUAGE SQL
AS
$$
UPDATE pulse."OutboxMessage"
SET "Status"    = 4, -- DeadLetter
    "Error"     = p_error,
    "UpdatedAt" = NOW()
WHERE "Id" = p_message_id
  AND "Status" = 1; -- Processing
$$;

-- delete_completed_outbox_messages: Removes old completed messages.
-- Returns the number of deleted rows.
CREATE OR REPLACE FUNCTION pulse.delete_completed_outbox_messages(p_older_than_utc TIMESTAMP WITH TIME ZONE)
    RETURNS INTEGER
    LANGUAGE PLPGSQL
AS
$$
DECLARE
    v_deleted_count INTEGER;
BEGIN
    DELETE
    FROM pulse."OutboxMessage"
    WHERE "Status" = 2 -- Completed
      AND "ProcessedAt" < p_older_than_utc;

    GET DIAGNOSTICS v_deleted_count = ROW_COUNT;
    RETURN v_deleted_count;
END;
$$;

-- ============================================================================
-- Management Functions
-- ============================================================================

-- get_dead_letter_outbox_messages: Returns a paginated list of dead-letter messages.
CREATE OR REPLACE FUNCTION pulse.get_dead_letter_outbox_messages(p_page_size INTEGER, p_page INTEGER)
    RETURNS TABLE
            (
                "Id"            UUID,
                "EventType"     CHARACTER VARYING(500),
                "Payload"       TEXT,
                "CorrelationId" CHARACTER VARYING(100),
                "CreatedAt"     TIMESTAMP WITH TIME ZONE,
                "UpdatedAt"     TIMESTAMP WITH TIME ZONE,
                "ProcessedAt"   TIMESTAMP WITH TIME ZONE,
                "RetryCount"    INTEGER,
                "Error"         TEXT,
                "Status"        INTEGER
            )
    LANGUAGE SQL
AS
$$
SELECT "Id",
       "EventType",
       "Payload",
       "CorrelationId",
       "CreatedAt",
       "UpdatedAt",
       "ProcessedAt",
       "RetryCount",
       "Error",
       "Status"
FROM pulse."OutboxMessage"
WHERE "Status" = 4 -- DeadLetter
ORDER BY "UpdatedAt" DESC
OFFSET (p_page * p_page_size) ROWS
FETCH NEXT p_page_size ROWS ONLY;
$$;

-- get_dead_letter_outbox_message: Returns a single dead-letter message by Id.
CREATE OR REPLACE FUNCTION pulse.get_dead_letter_outbox_message(p_message_id UUID)
    RETURNS TABLE
            (
                "Id"            UUID,
                "EventType"     CHARACTER VARYING(500),
                "Payload"       TEXT,
                "CorrelationId" CHARACTER VARYING(100),
                "CreatedAt"     TIMESTAMP WITH TIME ZONE,
                "UpdatedAt"     TIMESTAMP WITH TIME ZONE,
                "ProcessedAt"   TIMESTAMP WITH TIME ZONE,
                "RetryCount"    INTEGER,
                "Error"         TEXT,
                "Status"        INTEGER
            )
    LANGUAGE SQL
AS
$$
SELECT "Id",
       "EventType",
       "Payload",
       "CorrelationId",
       "CreatedAt",
       "UpdatedAt",
       "ProcessedAt",
       "RetryCount",
       "Error",
       "Status"
FROM pulse."OutboxMessage"
WHERE "Id" = p_message_id
  AND "Status" = 4; -- DeadLetter
$$;

-- get_dead_letter_outbox_message_count: Returns the count of dead-letter messages.
CREATE OR REPLACE FUNCTION pulse.get_dead_letter_outbox_message_count()
    RETURNS BIGINT
    LANGUAGE SQL
AS
$$
SELECT COUNT(1)
FROM pulse."OutboxMessage"
WHERE "Status" = 4; -- DeadLetter
$$;

-- replay_outbox_message: Resets a dead-letter message to Pending for reprocessing.
-- Returns the number of messages reset (0 or 1).
CREATE OR REPLACE FUNCTION pulse.replay_outbox_message(p_message_id UUID)
    RETURNS INTEGER
    LANGUAGE PLPGSQL
AS
$$
DECLARE
    v_updated_count INTEGER;
BEGIN
    UPDATE pulse."OutboxMessage"
    SET "Status"     = 0, -- Pending
        "RetryCount" = 0,
        "Error"      = NULL,
        "UpdatedAt"  = NOW()
    WHERE "Id" = p_message_id
      AND "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS v_updated_count = ROW_COUNT;
    RETURN v_updated_count;
END;
$$;

-- replay_all_dead_letter_outbox_messages: Resets all dead-letter messages to Pending.
-- Returns the number of messages reset.
CREATE OR REPLACE FUNCTION pulse.replay_all_dead_letter_outbox_messages()
    RETURNS INTEGER
    LANGUAGE PLPGSQL
AS
$$
DECLARE
    v_updated_count INTEGER;
BEGIN
    UPDATE pulse."OutboxMessage"
    SET "Status"     = 0, -- Pending
        "RetryCount" = 0,
        "Error"      = NULL,
        "UpdatedAt"  = NOW()
    WHERE "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS v_updated_count = ROW_COUNT;
    RETURN v_updated_count;
END;
$$;

-- get_outbox_statistics: Returns message counts grouped by status.
CREATE OR REPLACE FUNCTION pulse.get_outbox_statistics()
    RETURNS TABLE
            (
                "Pending"    BIGINT,
                "Processing" BIGINT,
                "Completed"  BIGINT,
                "Failed"     BIGINT,
                "DeadLetter" BIGINT
            )
    LANGUAGE SQL
AS
$$
SELECT SUM(CASE WHEN "Status" = 0 THEN 1 ELSE 0 END) AS "Pending",
       SUM(CASE WHEN "Status" = 1 THEN 1 ELSE 0 END) AS "Processing",
       SUM(CASE WHEN "Status" = 2 THEN 1 ELSE 0 END) AS "Completed",
       SUM(CASE WHEN "Status" = 3 THEN 1 ELSE 0 END) AS "Failed",
       SUM(CASE WHEN "Status" = 4 THEN 1 ELSE 0 END) AS "DeadLetter"
FROM pulse."OutboxMessage";
$$;
