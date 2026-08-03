-- ============================================================================
-- CommandDeadLetter Table Schema (PostgreSQL)
-- ============================================================================
-- Purpose: Stores commands that failed processing for later inspection, replay,
--          or dismissal.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET)
--
-- Configuration:
--   Adjust schema_name and table_name variables below before executing.
--   Run this script using psql or any PostgreSQL-compatible client.
--
-- Usage:
--   psql -h your-host -d your-database -f CommandDeadLetter.sql
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
\set schema_name 'pulse'
\set table_name 'CommandDeadLetter'

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS :schema_name;

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS ":schema_name".":table_name" (
    "Id"               UUID                      NOT NULL,
    "CommandType"      VARCHAR(500)              NOT NULL,
    "Payload"          TEXT                      NOT NULL,
    "ExceptionType"    VARCHAR(500)              NULL,
    "ExceptionMessage" TEXT                      NULL,
    "OccurredAt"       TIMESTAMP WITH TIME ZONE  NOT NULL,
    "AttemptCount"     INTEGER                   NOT NULL DEFAULT 1,
    "Status"           SMALLINT                  NOT NULL DEFAULT 0,
    CONSTRAINT "PK_:schema_name_:table_name" PRIMARY KEY ("Id")
);

-- Index for efficient filtering of pending (Status = 0) entries
CREATE INDEX IF NOT EXISTS "IX_:schema_name_:table_name_Status"
ON ":schema_name".":table_name" ("Status");

-- Index for ordering entries by occurrence time
CREATE INDEX IF NOT EXISTS "IX_:schema_name_:table_name_OccurredAt"
ON ":schema_name".":table_name" ("OccurredAt");
