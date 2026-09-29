-- ============================================================================
-- CommandDeadLetter Table Schema (PostgreSQL)
-- ============================================================================
-- Purpose: Stores commands that failed processing for later inspection, replay,
--          or dismissal.
-- Compatible with: NetEvolve.Pulse.PostgreSql (ADO.NET)
--
-- Usage (psql 10 or later):
--   psql -v ON_ERROR_STOP=1 -h your-host -d your-database \
--        -v schema_name=pulse -v table_name=CommandDeadLetter -f CommandDeadLetter.sql
--
--   schema_name and table_name are optional and default to 'pulse' and
--   'CommandDeadLetter'. Both are used as quoted identifiers, so they are
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
    \set table_name CommandDeadLetter
\endif

-- Key and index names include schema and table, so several tables can share one schema.
\set pk_name 'PK_' :schema_name '_' :table_name
\set ix_Status 'IX_' :schema_name '_' :table_name '_Status'
\set ix_OccurredAt 'IX_' :schema_name '_' :table_name '_OccurredAt'

-- PostgreSQL truncates identifiers to 63 bytes, so truncated names could collide and
-- CREATE INDEX IF NOT EXISTS would silently skip an index. Names that do not fit use an MD5 hash
-- of schema and table instead.
SELECT CASE WHEN octet_length(:'pk_name') > 63 THEN 'PK_' || md5(:'schema_name' || '.' || :'table_name') ELSE :'pk_name' END AS "pk_name",
       CASE WHEN octet_length(:'ix_Status') > 63 THEN 'IX_' || md5(:'schema_name' || '.' || :'table_name') || '_Status' ELSE :'ix_Status' END AS "ix_Status",
       CASE WHEN octet_length(:'ix_OccurredAt') > 63 THEN 'IX_' || md5(:'schema_name' || '.' || :'table_name') || '_OccurredAt' ELSE :'ix_OccurredAt' END AS "ix_OccurredAt" \gset

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS :"schema_name";

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS :"schema_name".:"table_name" (
    "Id"               UUID                      NOT NULL,
    "CommandType"      VARCHAR(500)              NOT NULL,
    "Payload"          TEXT                      NOT NULL,
    "ExceptionType"    VARCHAR(500)              NULL,
    "ExceptionMessage" TEXT                      NULL,
    "OccurredAt"       TIMESTAMP WITH TIME ZONE  NOT NULL,
    "AttemptCount"     INTEGER                   NOT NULL DEFAULT 1,
    "Status"           SMALLINT                  NOT NULL DEFAULT 0,
    CONSTRAINT :"pk_name" PRIMARY KEY ("Id")
);

-- Index for efficient filtering of pending (Status = 0) entries
CREATE INDEX IF NOT EXISTS :"ix_Status"
ON :"schema_name".:"table_name" ("Status");

-- Index for ordering entries by occurrence time
CREATE INDEX IF NOT EXISTS :"ix_OccurredAt"
ON :"schema_name".:"table_name" ("OccurredAt");
