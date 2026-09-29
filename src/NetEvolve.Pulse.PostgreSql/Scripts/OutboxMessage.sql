-- ============================================================================
-- OutboxMessage Table Schema (PostgreSQL)
-- ============================================================================
-- Purpose: Stores events for reliable delivery using the outbox pattern.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET)
--                  NetEvolve.Pulse.EntityFramework (EF Core)
--
-- Usage (psql 10 or later):
--   psql -v ON_ERROR_STOP=1 -h your-host -d your-database \
--        -v schema_name=pulse -v table_name=OutboxMessage -f OutboxMessage.sql
--
--   schema_name and table_name are optional and default to 'pulse' and
--   'OutboxMessage'. Both are used as quoted identifiers, so they are
--   case-sensitive and must match the Schema and TableName options.
--   The script relies on psql meta-commands (\if, \set, \gexec) and has to be
--   run with psql; GUI query tools such as pgAdmin or DBeaver cannot execute it.
--   It is idempotent: re-running it is the supported upgrade path.
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
\set ON_ERROR_STOP on

\if :{?schema_name}
\else
    \set schema_name pulse
\endif
\if :{?table_name}
\else
    \set table_name OutboxMessage
\endif

-- Key and index names include schema and table, so several tables can share one schema.
\set pk_name 'PK_' :schema_name '_' :table_name
\set ix_Status_CreatedAt 'IX_' :schema_name '_' :table_name '_Status_CreatedAt'
\set ix_Status_ProcessedAt 'IX_' :schema_name '_' :table_name '_Status_ProcessedAt'

-- PostgreSQL truncates identifiers to 63 bytes. Truncated index names can collide, and
-- CREATE INDEX IF NOT EXISTS would then silently skip an index, so stop before creating anything.
SELECT format('DO $guard$ BEGIN RAISE EXCEPTION %L; END $guard$',
              'Key or index names exceed 63 bytes; use a shorter schema_name or table_name.')
WHERE greatest(octet_length(:'pk_name'), octet_length(:'ix_Status_CreatedAt'), octet_length(:'ix_Status_ProcessedAt')) > 63 \gexec

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS :"schema_name";

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS :"schema_name".:"table_name" (
    "Id"            UUID            NOT NULL,
    "EventType"     VARCHAR(500)    NOT NULL,
    "Payload"       TEXT            NOT NULL,
    "CorrelationId" VARCHAR(100)    NULL,
    "CausationId"   VARCHAR(100)    NULL,
    "CreatedAt"     TIMESTAMPTZ     NOT NULL,
    "UpdatedAt"     TIMESTAMPTZ     NOT NULL,
    "ProcessedAt"   TIMESTAMPTZ     NULL,
    "NextRetryAt"   TIMESTAMPTZ     NULL,
    "RetryCount"    INTEGER         NOT NULL DEFAULT 0,
    "Error"         TEXT            NULL,
    "Status"        INTEGER         NOT NULL DEFAULT 0,
    CONSTRAINT :"pk_name" PRIMARY KEY ("Id")
);

-- Rename the key and indexes of tables created by earlier versions of this script, whose names
-- did not include the table name, so the CREATE INDEX IF NOT EXISTS below does not add duplicates.
SELECT format('ALTER INDEX %I.%I RENAME TO %I', i.schemaname, i.indexname, legacy.new_name)
FROM pg_indexes i
JOIN (
    VALUES
        ('PK_' || :'schema_name', :'pk_name'),
        ('IX_' || :'schema_name' || '_Status_CreatedAt', :'ix_Status_CreatedAt'),
        ('IX_' || :'schema_name' || '_Status_ProcessedAt', :'ix_Status_ProcessedAt')
) AS legacy (old_name, new_name) ON i.indexname = legacy.old_name
WHERE i.schemaname = :'schema_name'
  AND i.tablename = :'table_name' \gexec

-- Index for efficient polling of pending messages
CREATE INDEX IF NOT EXISTS :"ix_Status_CreatedAt"
ON :"schema_name".:"table_name" ("Status", "CreatedAt")
WHERE "Status" IN (0, 3);

-- Index for cleanup of completed messages
CREATE INDEX IF NOT EXISTS :"ix_Status_ProcessedAt"
ON :"schema_name".:"table_name" ("Status", "ProcessedAt")
WHERE "Status" = 2;

-- ============================================================================
-- Stored Functions
-- ============================================================================

-- get_pending_outbox_messages: Retrieves and locks pending messages for processing.
-- Also reclaims messages stuck in Processing whose lease (based on UpdatedAt) has expired,
-- e.g. after a worker crash, cancellation, or unhandled exception during dispatch.
-- Uses FOR UPDATE SKIP LOCKED for concurrent polling safety.
--
-- All timestamps are passed in by the caller (from the application's TimeProvider) instead of
-- being taken from the database clock, so the lease comparison and UpdatedAt share one clock.
-- Overloads from earlier versions of this script are dropped first: PostgreSQL overloads
-- functions by argument types, so CREATE OR REPLACE with changed parameters would otherwise
-- leave the old signatures behind and make calls ambiguous.
DROP FUNCTION IF EXISTS :"schema_name".get_pending_outbox_messages(INTEGER);
DROP FUNCTION IF EXISTS :"schema_name".get_pending_outbox_messages(INTEGER, TIMESTAMPTZ);
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.get_pending_outbox_messages(
    batch_size INTEGER,
    lease_expired_before TIMESTAMPTZ,
    now_utc TIMESTAMPTZ
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CausationId"   VARCHAR(100),
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
        FROM %1$I.%2$I om
        WHERE om."Status" = 0 -- Pending
           OR ( -- Processing, but the claim lease has expired (stuck after a crash/cancellation)
                om."Status" = 1
                AND lease_expired_before IS NOT NULL
                AND om."UpdatedAt" <= lease_expired_before
              )
        ORDER BY om."CreatedAt"
        LIMIT batch_size
        FOR UPDATE SKIP LOCKED
    )
    UPDATE %1$I.%2$I msg
    SET
        "Status" = 1, -- Processing
        "UpdatedAt" = now_utc
    FROM cte
    WHERE msg."Id" = cte."Id"
    RETURNING
        msg."Id",
        msg."EventType",
        msg."Payload",
        msg."CorrelationId",
        msg."CausationId",
        msg."CreatedAt",
        msg."UpdatedAt",
        msg."ProcessedAt",
        msg."NextRetryAt",
        msg."RetryCount",
        msg."Error",
        msg."Status";
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- get_failed_outbox_messages_for_retry: Retrieves failed messages eligible for retry
DROP FUNCTION IF EXISTS :"schema_name".get_failed_outbox_messages_for_retry(INTEGER, INTEGER);
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.get_failed_outbox_messages_for_retry(
    max_retry_count INTEGER,
    batch_size INTEGER,
    now_utc TIMESTAMPTZ
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CausationId"   VARCHAR(100),
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
        FROM %1$I.%2$I om
        WHERE om."Status" = 3 -- Failed
          AND om."RetryCount" < max_retry_count
          AND (om."NextRetryAt" IS NULL OR om."NextRetryAt" <= now_utc)
        ORDER BY om."UpdatedAt"
        LIMIT batch_size
        FOR UPDATE SKIP LOCKED
    )
    UPDATE %1$I.%2$I msg
    SET
        "Status" = 1, -- Processing
        "UpdatedAt" = now_utc
    FROM cte
    WHERE msg."Id" = cte."Id"
    RETURNING
        msg."Id",
        msg."EventType",
        msg."Payload",
        msg."CorrelationId",
        msg."CausationId",
        msg."CreatedAt",
        msg."UpdatedAt",
        msg."ProcessedAt",
        msg."NextRetryAt",
        msg."RetryCount",
        msg."Error",
        msg."Status";
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- mark_outbox_message_completed: Marks a message as successfully processed
DROP FUNCTION IF EXISTS :"schema_name".mark_outbox_message_completed(UUID);
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.mark_outbox_message_completed(
    message_id UUID,
    processed_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE %1$I.%2$I
    SET
        "Status" = 2, -- Completed
        "ProcessedAt" = processed_at,
        "UpdatedAt" = updated_at
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- mark_outbox_message_failed: Marks a message as failed with error details
DROP FUNCTION IF EXISTS :"schema_name".mark_outbox_message_failed(UUID, TEXT, TIMESTAMPTZ);
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.mark_outbox_message_failed(
    message_id UUID,
    error TEXT,
    next_retry_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE %1$I.%2$I
    SET
        "Status" = 3, -- Failed
        "RetryCount" = "RetryCount" + 1,
        "Error" = error,
        "NextRetryAt" = next_retry_at,
        "UpdatedAt" = updated_at
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- mark_outbox_message_dead_letter: Moves a message to dead letter status
DROP FUNCTION IF EXISTS :"schema_name".mark_outbox_message_dead_letter(UUID, TEXT);
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.mark_outbox_message_dead_letter(
    message_id UUID,
    error TEXT,
    updated_at TIMESTAMPTZ
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE %1$I.%2$I
    SET
        "Status" = 4, -- DeadLetter
        "Error" = error,
        "UpdatedAt" = updated_at
    WHERE "Id" = message_id
      AND "Status" = 1; -- Processing
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- delete_completed_outbox_messages: Removes old completed messages
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.delete_completed_outbox_messages(
    older_than_utc TIMESTAMPTZ
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    DELETE FROM %1$I.%2$I
    WHERE "Status" = 2 -- Completed
      AND "ProcessedAt" < older_than_utc;

    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- ============================================================================
-- Management Functions
-- ============================================================================

-- get_dead_letter_outbox_messages: Returns a paginated list of dead-letter messages
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.get_dead_letter_outbox_messages(
    page_size INTEGER,
    page INTEGER
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CausationId"   VARCHAR(100),
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
        om."CausationId",
        om."CreatedAt",
        om."UpdatedAt",
        om."ProcessedAt",
        om."NextRetryAt",
        om."RetryCount",
        om."Error",
        om."Status"
    FROM %1$I.%2$I om
    WHERE om."Status" = 4 -- DeadLetter
    ORDER BY om."UpdatedAt" DESC, om."Id" DESC
    LIMIT page_size
    OFFSET (page * page_size);
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- get_dead_letter_outbox_message: Returns a single dead-letter message by Id
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.get_dead_letter_outbox_message(
    message_id UUID
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CausationId"   VARCHAR(100),
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
        om."CausationId",
        om."CreatedAt",
        om."UpdatedAt",
        om."ProcessedAt",
        om."NextRetryAt",
        om."RetryCount",
        om."Error",
        om."Status"
    FROM %1$I.%2$I om
    WHERE om."Id" = message_id
      AND om."Status" = 4; -- DeadLetter
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- get_dead_letter_outbox_message_count: Returns the count of dead-letter messages
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.get_dead_letter_outbox_message_count()
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN (
        SELECT COUNT(*)
        FROM %1$I.%2$I
        WHERE "Status" = 4 -- DeadLetter
    );
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- replay_outbox_message: Resets a dead-letter message to Pending for reprocessing
DROP FUNCTION IF EXISTS :"schema_name".replay_outbox_message(UUID);
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.replay_outbox_message(
    message_id UUID,
    updated_at TIMESTAMPTZ
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE %1$I.%2$I
    SET
        "Status"     = 0, -- Pending
        "RetryCount" = 0,
        "Error"      = NULL,
        "NextRetryAt" = NULL,
        "UpdatedAt"  = updated_at
    WHERE "Id" = message_id
      AND "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RETURN updated_count;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- replay_all_dead_letter_outbox_messages: Resets all dead-letter messages to Pending
DROP FUNCTION IF EXISTS :"schema_name".replay_all_dead_letter_outbox_messages();
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.replay_all_dead_letter_outbox_messages(
    updated_at TIMESTAMPTZ
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    updated_count INTEGER;
BEGIN
    UPDATE %1$I.%2$I
    SET
        "Status"     = 0, -- Pending
        "RetryCount" = 0,
        "Error"      = NULL,
        "NextRetryAt" = NULL,
        "UpdatedAt"  = updated_at
    WHERE "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    RETURN updated_count;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- get_outbox_messages: Returns a paginated, read-only list of messages, optionally filtered by status
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.get_outbox_messages(
    page_size INTEGER,
    page INTEGER,
    message_status INTEGER
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CausationId"   VARCHAR(100),
    "CreatedAt"     TIMESTAMPTZ,
    "UpdatedAt"     TIMESTAMPTZ,
    "ProcessedAt"   TIMESTAMPTZ,
    "NextRetryAt"   TIMESTAMPTZ,
    "RetryCount"    INTEGER,
    "Error"         TEXT,
    "Status"        INTEGER
)
LANGUAGE plpgsql
STABLE
AS $$
BEGIN
    RETURN QUERY
    SELECT
        om."Id",
        om."EventType",
        om."Payload",
        om."CorrelationId",
        om."CausationId",
        om."CreatedAt",
        om."UpdatedAt",
        om."ProcessedAt",
        om."NextRetryAt",
        om."RetryCount",
        om."Error",
        om."Status"
    FROM %1$I.%2$I om
    WHERE message_status IS NULL OR om."Status" = message_status
    ORDER BY om."UpdatedAt" DESC, om."Id" DESC
    LIMIT page_size
    OFFSET (page * page_size);
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- get_outbox_message: Returns a single message by Id, regardless of its status
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.get_outbox_message(
    message_id UUID
)
RETURNS TABLE (
    "Id"            UUID,
    "EventType"     VARCHAR(500),
    "Payload"       TEXT,
    "CorrelationId" VARCHAR(100),
    "CausationId"   VARCHAR(100),
    "CreatedAt"     TIMESTAMPTZ,
    "UpdatedAt"     TIMESTAMPTZ,
    "ProcessedAt"   TIMESTAMPTZ,
    "NextRetryAt"   TIMESTAMPTZ,
    "RetryCount"    INTEGER,
    "Error"         TEXT,
    "Status"        INTEGER
)
LANGUAGE plpgsql
STABLE
AS $$
BEGIN
    RETURN QUERY
    SELECT
        om."Id",
        om."EventType",
        om."Payload",
        om."CorrelationId",
        om."CausationId",
        om."CreatedAt",
        om."UpdatedAt",
        om."ProcessedAt",
        om."NextRetryAt",
        om."RetryCount",
        om."Error",
        om."Status"
    FROM %1$I.%2$I om
    WHERE om."Id" = message_id;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- dismiss_outbox_message: Permanently deletes a single dead-letter message
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.dismiss_outbox_message(
    message_id UUID
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    DELETE FROM %1$I.%2$I
    WHERE "Id" = message_id
      AND "Status" = 4; -- DeadLetter

    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- get_outbox_statistics: Returns message counts grouped by status
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.get_outbox_statistics()
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
    FROM %1$I.%2$I;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec
