-- ============================================================================
-- AuditEntry Table Schema (PostgreSQL)
-- ============================================================================
-- Purpose: Stores audit trail records for processed requests, capturing the
--          command type, user, correlation, timing, result, and optional
--          payload/exception details.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET)
--
-- Configuration:
--   Adjust schema_name and table_name variables below before executing.
--   Run this script using psql or any PostgreSQL-compatible client.
--
-- Usage:
--   psql -h your-host -d your-database -f AuditEntry.sql
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
\set schema_name 'pulse'
\set table_name 'AuditEntry'

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS :schema_name;

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS ":schema_name".":table_name" (
    "Id"               UUID                      NOT NULL,
    "CommandType"      VARCHAR(500)              NOT NULL,
    "UserId"           VARCHAR(256)              NULL,
    "CorrelationId"    VARCHAR(100)              NULL,
    "OccurredAt"       TIMESTAMP WITH TIME ZONE  NOT NULL,
    "DurationMs"       DOUBLE PRECISION          NOT NULL,
    "Result"           SMALLINT                  NOT NULL,
    "Payload"          TEXT                      NULL,
    "ExceptionMessage" TEXT                      NULL,
    CONSTRAINT "PK_:schema_name_:table_name" PRIMARY KEY ("Id")
);

-- Index for ordering/range-filtering entries by occurrence time
CREATE INDEX IF NOT EXISTS "IX_:schema_name_:table_name_OccurredAt"
ON ":schema_name".":table_name" ("OccurredAt");

-- Index for efficient filtering by command type
CREATE INDEX IF NOT EXISTS "IX_:schema_name_:table_name_CommandType"
ON ":schema_name".":table_name" ("CommandType");
