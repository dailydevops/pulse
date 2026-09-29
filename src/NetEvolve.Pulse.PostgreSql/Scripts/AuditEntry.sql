-- ============================================================================
-- AuditEntry Table Schema (PostgreSQL)
-- ============================================================================
-- Purpose: Stores audit trail records for processed requests, capturing the
--          command type, user, correlation, timing, result, and optional
--          payload/exception details.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET)
--
-- Usage (psql 10 or later):
--   psql -v ON_ERROR_STOP=1 -h your-host -d your-database \
--        -v schema_name=pulse -v table_name=AuditEntry -f AuditEntry.sql
--
--   schema_name and table_name are optional and default to 'pulse' and
--   'AuditEntry'. Both are used as quoted identifiers, so they are
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
    \set table_name AuditEntry
\endif

-- Key and index names include schema and table, so several tables can share one schema.
\set pk_name 'PK_' :schema_name '_' :table_name
\set ix_OccurredAt 'IX_' :schema_name '_' :table_name '_OccurredAt'
\set ix_CommandType 'IX_' :schema_name '_' :table_name '_CommandType'

-- PostgreSQL truncates identifiers to 63 bytes, so truncated names could collide and
-- CREATE INDEX IF NOT EXISTS would silently skip an index. Names that do not fit use an MD5 hash
-- of schema and table instead.
SELECT CASE WHEN octet_length(:'pk_name') > 63 THEN 'PK_' || md5(:'schema_name' || '.' || :'table_name') ELSE :'pk_name' END AS "pk_name",
       CASE WHEN octet_length(:'ix_OccurredAt') > 63 THEN 'IX_' || md5(:'schema_name' || '.' || :'table_name') || '_OccurredAt' ELSE :'ix_OccurredAt' END AS "ix_OccurredAt",
       CASE WHEN octet_length(:'ix_CommandType') > 63 THEN 'IX_' || md5(:'schema_name' || '.' || :'table_name') || '_CommandType' ELSE :'ix_CommandType' END AS "ix_CommandType" \gset

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS :"schema_name";

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS :"schema_name".:"table_name" (
    "Id"               UUID                      NOT NULL,
    "CommandType"      VARCHAR(500)              NOT NULL,
    "UserId"           VARCHAR(256)              NULL,
    "CorrelationId"    VARCHAR(100)              NULL,
    "OccurredAt"       TIMESTAMP WITH TIME ZONE  NOT NULL,
    "DurationMs"       DOUBLE PRECISION          NOT NULL,
    "Result"           SMALLINT                  NOT NULL,
    "Payload"          TEXT                      NULL,
    "ExceptionMessage" TEXT                      NULL,
    CONSTRAINT :"pk_name" PRIMARY KEY ("Id")
);

-- Index for ordering/range-filtering entries by occurrence time
CREATE INDEX IF NOT EXISTS :"ix_OccurredAt"
ON :"schema_name".:"table_name" ("OccurredAt");

-- Index for efficient filtering by command type
CREATE INDEX IF NOT EXISTS :"ix_CommandType"
ON :"schema_name".:"table_name" ("CommandType");
