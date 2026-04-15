-- ============================================================================
-- IdempotencyKey Table Schema (PostgreSQL)
-- ============================================================================
-- Purpose: Stores idempotency keys for at-most-once command processing.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET)
--
-- Configuration:
--   Adjust schema_name and table_name variables below before executing.
--   Run this script using psql or any PostgreSQL-compatible client.
--
-- Usage:
--   psql -h your-host -d your-database -f IdempotencyKey.sql
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
\set schema_name 'pulse'
\set table_name 'IdempotencyKey'

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS :schema_name;

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS ":schema_name".":table_name" (
    "idempotency_key" VARCHAR(500)              NOT NULL,
    "created_at"      TIMESTAMP WITH TIME ZONE  NOT NULL,
    CONSTRAINT "PK_:schema_name_:table_name" PRIMARY KEY ("idempotency_key")
);

-- Index for TTL-based queries (efficient filtering by created_at)
CREATE INDEX IF NOT EXISTS "IX_:schema_name_:table_name_created_at"
ON ":schema_name".":table_name" ("created_at");

-- ============================================================================
-- Stored Functions
-- ============================================================================

-- fn_exists_idempotency_key: Checks if an idempotency key exists and is still valid
CREATE OR REPLACE FUNCTION ":schema_name".fn_exists_idempotency_key(
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
            FROM ":schema_name".":table_name"
            WHERE "idempotency_key" = p_idempotency_key
        );
    ELSE
        -- TTL filtering: only return true if key exists and is not expired
        RETURN EXISTS (
            SELECT 1
            FROM ":schema_name".":table_name"
            WHERE "idempotency_key" = p_idempotency_key
              AND "created_at" >= p_valid_from
        );
    END IF;
END;
$$;

-- fn_insert_idempotency_key: Inserts an idempotency key (idempotent operation)
CREATE OR REPLACE FUNCTION ":schema_name".fn_insert_idempotency_key(
    p_idempotency_key VARCHAR(500),
    p_created_at      TIMESTAMP WITH TIME ZONE
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO ":schema_name".":table_name" ("idempotency_key", "created_at")
    VALUES (p_idempotency_key, p_created_at)
    ON CONFLICT DO NOTHING;
END;
$$;

-- fn_delete_expired_idempotency_keys: Removes expired idempotency keys (cleanup maintenance)
CREATE OR REPLACE FUNCTION ":schema_name".fn_delete_expired_idempotency_keys(
    p_valid_from TIMESTAMP WITH TIME ZONE
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    DELETE FROM ":schema_name".":table_name"
    WHERE "created_at" < p_valid_from;

    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$$;
