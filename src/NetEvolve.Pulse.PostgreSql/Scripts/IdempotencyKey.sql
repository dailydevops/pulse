-- ============================================================================
-- IdempotencyKey Table Schema (PostgreSQL)
-- ============================================================================
-- Purpose: Stores idempotency keys for at-most-once command processing.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET)
--
-- Usage (psql 10 or later):
--   psql -v ON_ERROR_STOP=1 -h your-host -d your-database \
--        -v schema_name=pulse -v table_name=IdempotencyKey -f IdempotencyKey.sql
--
--   schema_name and table_name are optional and default to 'pulse' and
--   'IdempotencyKey'. Both are used as quoted identifiers, so they are
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
    \set table_name IdempotencyKey
\endif

-- Key and index names include schema and table. The functions below are named per schema only,
-- so each table created by this script needs its own schema.
\set pk_name 'PK_' :schema_name '_' :table_name
\set ix_created_at 'IX_' :schema_name '_' :table_name '_created_at'

-- PostgreSQL truncates identifiers to 63 bytes. Truncated index names can collide, and
-- CREATE INDEX IF NOT EXISTS would then silently skip an index, so stop before creating anything.
SELECT format('DO $guard$ BEGIN RAISE EXCEPTION %L; END $guard$',
              'Key or index names exceed 63 bytes; use a shorter schema_name or table_name.')
WHERE greatest(octet_length(:'pk_name'), octet_length(:'ix_created_at')) > 63 \gexec

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS :"schema_name";

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS :"schema_name".:"table_name" (
    "idempotency_key" VARCHAR(500)              NOT NULL,
    "created_at"      TIMESTAMP WITH TIME ZONE  NOT NULL,
    CONSTRAINT :"pk_name" PRIMARY KEY ("idempotency_key")
);

-- Index for TTL-based queries (efficient filtering by created_at)
CREATE INDEX IF NOT EXISTS :"ix_created_at"
ON :"schema_name".:"table_name" ("created_at");

-- ============================================================================
-- Stored Functions
-- ============================================================================

-- fn_exists_idempotency_key: Checks if an idempotency key exists and is still valid
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.fn_exists_idempotency_key(
    p_idempotency_key VARCHAR(500),
    p_valid_from      TIMESTAMP WITH TIME ZONE DEFAULT NULL
)
RETURNS BOOLEAN
LANGUAGE plpgsql
AS $$
BEGIN
    IF p_valid_from IS NULL THEN
        -- No TTL filtering: check if key exists regardless of age
        RETURN EXISTS (
            SELECT 1
            FROM %1$I.%2$I
            WHERE "idempotency_key" = p_idempotency_key
        );
    ELSE
        -- TTL filtering: only return true if key exists and is not expired
        RETURN EXISTS (
            SELECT 1
            FROM %1$I.%2$I
            WHERE "idempotency_key" = p_idempotency_key
              AND "created_at" >= p_valid_from
        );
    END IF;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- fn_insert_idempotency_key: Inserts an idempotency key (idempotent operation)
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.fn_insert_idempotency_key(
    p_idempotency_key VARCHAR(500),
    p_created_at      TIMESTAMP WITH TIME ZONE
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO %1$I.%2$I ("idempotency_key", "created_at")
    VALUES (p_idempotency_key, p_created_at)
    ON CONFLICT DO NOTHING;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- fn_reserve_idempotency_key: Atomically inserts an idempotency key or refreshes an expired one.
-- Returns TRUE when the key was inserted or refreshed, FALSE when a key that has not expired already exists.
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.fn_reserve_idempotency_key(
    p_idempotency_key VARCHAR(500),
    p_created_at      TIMESTAMP WITH TIME ZONE,
    p_valid_from      TIMESTAMP WITH TIME ZONE DEFAULT NULL
)
RETURNS BOOLEAN
LANGUAGE plpgsql
AS $$
DECLARE
    affected_count INTEGER;
BEGIN
    INSERT INTO %1$I.%2$I AS t ("idempotency_key", "created_at")
    VALUES (p_idempotency_key, p_created_at)
    ON CONFLICT ("idempotency_key") DO UPDATE
        SET "created_at" = EXCLUDED."created_at"
        WHERE p_valid_from IS NOT NULL AND t."created_at" < p_valid_from;

    GET DIAGNOSTICS affected_count = ROW_COUNT;
    RETURN affected_count > 0;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec

-- fn_delete_expired_idempotency_keys: Removes expired idempotency keys (cleanup maintenance)
SELECT format($ddl$
CREATE OR REPLACE FUNCTION %1$I.fn_delete_expired_idempotency_keys(
    p_valid_from TIMESTAMP WITH TIME ZONE
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    DELETE FROM %1$I.%2$I
    WHERE "created_at" < p_valid_from;

    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$$
$ddl$, :'schema_name', :'table_name') \gexec
